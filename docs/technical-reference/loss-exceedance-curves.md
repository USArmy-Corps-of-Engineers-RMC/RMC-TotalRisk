# Loss Exceedance Curves and Risk Measures

> Technical reference for how the `RMC.TotalRisk` engine builds loss exceedance curves (LECs, a.k.a.
> F-N curves) and the risk measures derived from them (Phase 4 / 4b). Companion:
> [risk-integration.md](risk-integration.md) (how the integrator's evaluation points are produced).
> Normative spec: [../requirements/MODEL_LIBRARY_ARCHITECTURE.md](../requirements/MODEL_LIBRARY_ARCHITECTURE.md)
> §7.7, §7.8 (v0.13). Legacy source paths (`Curve.vb`, `RiskAnalysis.vb`, `SampledComponent.vb`,
> `ComponentRiskOutput.vb`) are in the `C:\GIT\RMC-TotalRisk-Dev` reference repo.

A **loss exceedance curve** plots, for each consequence magnitude `c`, the annual probability that the
realized consequence exceeds `c`. The engine builds five of them per component and per system — one per
`RiskType`: `Excess` (incremental), `Background` (irreducible), `Total`, `Fail`, `NonFail`. Every risk
measure the engine reports is a functional of these curves.

## Input: risk points

The adaptive integrator ([risk-integration.md](risk-integration.md)) records a cloud of `RiskPoint`s.
Each carries the hazard level, the hazard probability mass (`dF`), and — because a failure mode can
have multiple response branches and multiple consequence types — **parallel lists**
`ResponseProbabilities[k]` and `Consequences[k]`. The LEC is built by walking every `(point, k)` pair:

```
mass_k       = point.HazardProbabilityMass * point.ResponseProbabilities[k]
consequence_k = point.Consequences[k]
```

The full set of `(mass, consequence)` pairs is the empirical loss distribution. Everything below is how
to turn that set into an exceedance curve and its moments **accurately** — the v1.0 code does it in a
way that is correct in the mean but wrong in the tail.

## Probability mass

Mass should come from the **quadrature weight** of the evaluation point — the `dF` it represents. The
VEGAS path already does this: it uses `wgt` directly (legacy `RiskAnalysis.vb:3030`, `:3074-3077`). The
1D path does **not**: v1.0's `ProcessHazardProbabilities` (`Curve.vb:304-316`) throws the weights away
and *re-derives* mass by sorting risk points on `p` and midpoint-partitioning the gaps between them —
a trapezoidal `dF` heuristic that is only as good as the point spacing and breaks on any duplicate `p`.

**Current state (N7 adopted, Phase 8.5):** the 1D path takes exact weights like the VEGAS path.
`AdaptiveGaussKronrod.Recorder` flushes `(x, weight, f)` for **accepted** intervals only;
`QuadratureMassLedger` seals that flush into a sorted array keyed on the exact abscissa with a
Neumaier-compensated total, and `Curve.ApplyRecordedMass` credits each risk point from it. Duplicate
abscissas are **summed**, which is what makes the degenerate zero-width bin correct (v1.0 concatenated
the 21 duplicate entries and gave them one full trapezoid mass — a 21× overcount). Points at abscissas
the refinement superseded carry no mass and are compacted away. The mass budget is checked against the
integration domain, not against 1: see [risk-integration.md](risk-integration.md) for the two gates.

The comparison against a 4,000,000-point dense reference: **5.7e-7** relative for the midpoint
trapezoid, **4.0e-11** for the ledger.

## Building the exceedance curve

### The v1.0 histogram (dropped)

v1.0 bins consequences into `LECOutputLength` (default 200) **log10 bins**:

```
idx = floor( (log10(c + offset) − binLower) / binDelta )     // Curve.vb:375
Bins[idx].Weight += mass
```

then forms the exceedance curve as the reverse-cumulative sum of bin weights, plotted at each bin
**midpoint** (`Curve.vb:397-399`). Two defects:

1. The index map (`binOffset`/`binLower`/`binDelta`) and the `Stratify`-built bin edges are **two
   separate constructions** that can disagree about which bin a consequence falls in.
2. Plotting the cumulative probability at the bin **midpoint** biases every ordinate — the exceedance
   probability at the midpoint is not the sum of the mass at-or-above the midpoint.

The histogram also conflates output resolution with compute resolution: `LECOutputLength` controls both
how many ordinates you get *and* how finely mass is discretized before the moments are taken.

### The exact construction (v1.1)

Build the curve **exactly** from the pairs, then thin for output:

```
1. Collect all (mass, consequence) pairs from the risk points.
2. Sort by consequence DESCENDING.
3. Accumulate exactly:  the exceedance probability at consequence c[i]
   is the running sum of mass for all pairs with consequence ≥ c[i].
4. Thin to LECOutputLength ordinates by quantile-preserving selection,
   ALWAYS retaining the extreme-tail points (the largest few consequences).
```

This is `O(n log n)` (the sort), carries no binning bias, and — crucially — makes `LECOutputLength` an
**output-resolution knob only**. The moments and risk measures are computed from the exact accumulation
before thinning, so shrinking the output curve never degrades a reported statistic. This is the single
biggest accuracy win for tail behavior.

Store the result as a Numerics `OrderedPairedData` (X = consequence descending, Y = exceedance
probability ascending), preserving the v1.0 curve orientation so downstream `GetXFromY` / `GetYFromX`
interpolation is unchanged.

## Moments (weighted, numerically stable)

v1.0 computes the mean, standard deviation, skewness, and kurtosis from **raw power sums**
(`Curve.vb:370-389`): `u1 = Σ mass·c`, `u2 = Σ mass·c²`, … then `m2 = sqrt(u2 − u1²)` and a fully
expanded fourth central moment. These **catastrophically cancel** when the mean is large relative to
the spread — the normal case for life-loss consequences, where `u2` and `u1²` agree to many
significant figures and their difference loses most of them.

Use a **weighted streaming (Welford / West) central-moment accumulation** instead: maintain the running
weighted mean and the central sums `M2`, `M3`, `M4` and update them per pair. This keeps full precision
regardless of the mean-to-spread ratio. Document it as an *improve-on-port* case in the type's XML
`<remarks>` (the porting rule in [CLAUDE.md](../../CLAUDE.md) — `ForceMonotonic` is the canonical
precedent for actively fixing a numerically fragile v1.0 body while preserving its reference results).

Central moments then give: mean = weighted mean; standard deviation = `√M2`; skewness = `M3 / M2^{3/2}`;
kurtosis = `M4 / M2²`.

## Mass leakage

`Fail`, `Excess`, and `NonFail` are **defective** distributions (`IsExhaustive = false`,
`TotalProbability < 1`); `Background` and `Total` are exhaustive (`TotalProbability = 1`). v1.0 forces
`totalProbability = If(IsExhaustive, 1, Math.Min(Σ mass, 1))` (`Curve.vb:380`), silently hiding a mass
budget that came out wrong. Keep the clamp for numerical noise, but **raise a validation Warning when
`|Σ mass − 1| > 1e-6` on an exhaustive curve** — a real leak is a bug, not something to round away.

## Risk-measure catalog

Every measure is a functional of the finished LEC. Definitions and the v1.0 fixes:

| Measure | Definition | Notes / fixes |
|---|---|---|
| `TotalProbability` | max exceedance probability of the curve | = annualized P(failure) on the `Fail` curve |
| `Mean` | 1st raw moment `Σ mass·c` | = EAD (expected annual damage) / mean annualized risk; **unchanged by the tail fixes** |
| `ConditionalMean` | `Mean / TotalProbability` | expected consequence **given** the curve's event occurs |
| `StandardDeviation` | `√M2` (weighted Welford) | v1.0 raw-power-sum form cancels — fixed |
| `Skewness` | `M3 / M2^{3/2}` | as above |
| `Kurtosis` | `M4 / M2²` | as above |
| `ConsequenceThresholdProbability` | `LEC.GetYFromX(ConsequenceThreshold, Log, Log)` | assurance: P(consequence > threshold) |
| `HazardThresholdProbability` | from the `HazardFrequency` profile at `HazardThreshold` | |
| `ValueAtRisk` | consequence quantile at level α = `Options.Alpha` | **fix:** return **0** (not the minimum consequence) when `α > TotalProbability` — no loss is exceeded at that level (v1.0 `Curve.vb:544` returns `LEC.Last().X`) |
| `ConditionalValueAtRisk` | `(1/α)·∫₀^α VaR(p) dp` via `AdaptiveGaussKronrod` over the log-log LEC quantile | expected shortfall — the coherent tail measure ([references](../references.md)) |
| `LEC` | the curve itself (X = consequence, Y = exceedance prob) | the F-N curve |
| `HazardFrequency` | hazard level vs cumulative exceedance probability | risk profile (1D path only — needs `HazardLevel` on the points) |
| `HazardvsCEN` | hazard level vs conditional expected consequence | risk profile |

One more v1.0 inconsistency to fix in the uncertainty post-processing: `PostProcessUncertainty`
reconstructs the **Total** percentile curve as `fAEP + nfAEP` (`RiskAnalysis.vb:3369`) rather than
reading the Total LEC. Read the Total LEC and delete the reconstruction — the Total curve is already
built exactly.

## System aggregation

After each component has its five LECs, the system LECs are assembled per `SystemRiskMethod`.

### Additive method — strict independence + exact lattice convolution

The additive method is **redefined (ratified v0.13) to assume the components are strictly
independent.** Validation: `SystemRiskMethod = Additive` with `ComponentHazardDependency ≠ Independent`
is an **Error**; the correlation matrix applies to the joint method only. The v1.0 additive path
combined only the first two moments — means added, variances combined through the correlation matrix —
and produced **no system LEC at all** (a warning told the user so). Under independence we can do far
better: build the true system LEC by convolution.

**Zero-inflation makes convolution equal to full combination enumeration.** The `Fail`, `Excess`, and
`NonFail` component curves are defective — with probability `1 − TotalProbability` the component did not
fail and contributes **zero** consequence. Make each curve a proper distribution by adding an atom at
consequence 0 with mass `1 − TotalProbability`. Convolving the D zero-inflated distributions is then
**exactly** the enumeration of all `2^D` component failure/non-failure combinations (each convolution
term is "this component failed and contributed `c`, or it didn't and contributed 0"), in `O(n log n)`
instead of `2^D`, and it reproduces the full tail rather than a conditional mean.

**The atoms force a lattice (v0.15 implementation).** The v0.13 plan routed this through
`EmpiricalDistribution.Convolve`, which proved unusable at implementation: it samples continuous
`PDF`s on a uniform grid (`EmpiricalDistribution.cs:549`, `:734`), and a distribution-function jump —
the zero atom — has no finite density. Any ramp-width approximation either loses the atom or corrupts
the sampled density and its renormalization, and the per-stage PDF-normalize/regrid chain cannot hold
the 1e-6 mean-parity gate. The engine therefore convolves **exactly on a shared consequence lattice**
(`RMC.TotalRisk.Analyses.SystemConvolution`, public):

```
1. Lattice: step Δ = (Σ component maxima) / (SystemConvolutionPoints − 1); node k ↔ consequence k·Δ.
2. Per component: bin its exact recorded (mass, consequence) pairs — Curve.CollectRecordedPairs() —
   with the moment-preserving two-node split (mass divides between the bracketing nodes so the
   pair's first moment is preserved exactly); add the zero atom 1 − Σmass at node 0.
3. Convolve the lattice mass vectors pairwise by Fourier.FFT (zero-padded, power-of-two complex
   length — the same Numerics primitive Convolve uses internally), in canonical-hash component
   order so declaration order can never move the result by association-rounding.
4. Feed the system lattice into the shared exact construction: Curve.CreateCurve(pairs, LECOutputLength).
```

Mass and first moments are preserved exactly at every step, so **the convolved system mean equals Σ
component means to floating-point roundoff by construction** — the v1.0 additive answer, now with the
full curve. Higher moments carry an O(Δ²) binning quantization that `SystemConvolutionPoints ≥ 4096`
keeps far below sampling error (and under independence the v1.0 σ answer, `√Σσᵢ²`, is reproduced too).

- `Background` is already exhaustive (convolve directly); convolve the component `Total` curves for
  the system `Total`. The lattice zero node is **kept** on exhaustive streams (it is real probability
  at zero consequence, and the recorded budget stays exactly one) and **dropped** on defective streams
  (it is the no-event atom; zero-valued events quantize into it).
- **Stream probabilities keep the v1.0 system-state semantics** after the curve is built: `Fail` and
  `Excess` carry `pF = Probability.IndependentUnion(pfs)`, `NonFail` carries `1 − pF` — the values the
  v1.0 additive summary reported — while the curve mass distribution comes from the convolution.
- **Numerics item N8 is extended** (Phase 8): alongside the log-spaced grid, `Convolve` needs an
  atom-aware (discrete/mixed-distribution) overload; until then the lattice kernel stays in the
  model library.
- **Verified** ([../verification/system-risk.md](../verification/system-risk.md)): mean, σ, failure
  union, tail exceedances, VaR, and CVaR of the convolved system curve against a brute-force
  event-level Monte Carlo oracle at N = 10⁶ — every deviation within ~1.2 oracle standard errors.

### Joint method — real combination enumeration

The joint method integrates over correlated hazards with VEGAS
([risk-integration.md](risk-integration.md) §VEGAS). Its defect is upstream of the integration: the
integrand consumes only **per-component conditional means** (`fC(i) = MeanFailureConsequences`,
`nfC(i)`, `iC(i)` — single scalars, legacy `RiskAnalysis.vb:3030-3047`) and combines those scalars
across component failure/non-failure combinations (`:3089-3104`). So the system F-N curve convolves
conditional means, discarding each component's within-consequence spread — the legacy source even
carries the TODO for it (`ComponentRiskOutput.vb:39`: *"These lists below will need to be used to
compute precise system FN curves in the future"*).

Fix: **activate the parallel `ResponseProbabilities` / `FailureConsequences` / `ExcessConsequences`
lists on `ComponentRiskOutput`** (uncomment the design intent at `ComponentRiskOutput.vb:39-54` and its
producer at `SampledComponent.vb:589-592`) so the VEGAS integrand enumerates the true within-component
consequence distribution across the system combinations. Also fix the double-increment of `tPF`
(`RiskAnalysis.vb:3117` and `:3129`). Because this path is where the Vegas power transform pays off,
land it together with the tail-focus work (Phase 4b).

## The two paths converge

After the v1.1 changes, both the 1D and the multi-D paths reduce to the **same** LEC construction: a set
of `(mass, consequence)` pairs → exact sorted exceedance curve → weighted-Welford moments → risk
measures. The only difference is where the mass comes from — the AGK Kronrod weight (1D) or the VEGAS
`wgt` (multi-D). Keeping the construction in one place (a single `Curve.CreateCurve` that takes weighted
pairs) is the intended shape.
