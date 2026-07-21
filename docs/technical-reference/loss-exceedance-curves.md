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

**Target state (Numerics item N7):** hand the Kronrod weight to the integrand callback so the 1D path
uses exact weights like the VEGAS path. **Interim (until N7):** keep the midpoint-partition fallback
but (a) **deduplicate** the sorted points first (G10K21's strictly-interior nodes make duplicates rare
but a shared bin edge can still collide), and (b) assert `Σ mass = 1 ± 1e-9`. Do not silently accept a
mass budget that does not sum to 1.

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

### Additive method — strict independence + FFT convolution

The additive method is **redefined (ratified v0.13) to assume the components are strictly
independent.** Validation: `SystemRiskMethod = Additive` with `ComponentHazardDependency ≠ Independent`
(or a non-identity `HazardCorrelationMatrix`) is an **Error**; the correlation matrix applies to the
joint method only. The v1.0 additive path combined only the first two moments — means added, variances
combined through the correlation matrix — and produced **no system LEC at all** (a warning told the user
so). Under independence we can do far better: build the true system LEC by convolution.

**Zero-inflation makes convolution equal to full combination enumeration.** The `Fail`, `Excess`, and
`NonFail` component curves are defective — with probability `1 − TotalProbability` the component did not
fail and contributes **zero** consequence. Make each curve a proper distribution by adding an atom at
consequence 0 with mass `1 − TotalProbability`. Convolving the D zero-inflated distributions is then
**exactly** the enumeration of all `2^D` component failure/non-failure combinations (each convolution
term is "this component failed and contributed `c`, or it didn't and contributed 0"), in `O(n log n)`
instead of `2^D`, and it reproduces the full tail rather than a conditional mean.

```csharp
// Per risk type: build an EmpiricalDistribution per component (X ascending consequence,
// P = 1 − exceedance), zero-inflated for the defective curves, then convolve.
var systemFail = EmpiricalDistribution.Convolve(
    components.Select(c => ZeroInflate(c.Fail)).ToList(),
    numberOfPoints: Options.SystemConvolutionPoints);   // default 4096, min 4096
```

- `Background` is already exhaustive (convolve directly). Convolve the component `Total` curves for the
  system `Total`. Convert `Fail`/`Excess`/`NonFail` results back to defective form (strip the 0-atom
  into `TotalProbability`) afterward. System `pF = Probability.IndependentUnion(pfs)`.
- **Grid caveat.** `EmpiricalDistribution.Convolve` samples the PDFs on a **linear** uniform grid over
  `[Σmin, Σmax]` with `fftPoints = NextPowerOfTwo(max(8·numberOfPoints, 2048))`
  (`EmpiricalDistribution.cs:549`, `:734`). Life-loss consequences span orders of magnitude, so a linear
  grid starves the tail — require `SystemConvolutionPoints ≥ 4096`, and raise a **log-spaced /
  adaptive-grid** convolution as Numerics item **N8** (Phase 8).
- **Free regression gate.** Assert the convolved system mean equals `Σ` component means to 1e-6
  relative — that is exactly the v1.0 additive answer, so it proves the FFT did not disturb the mean
  while adding the tail. Gate the phase on a brute-force Monte Carlo cross-check of the system LEC tail
  ([../verification.md](../verification.md), v0.13 policy).

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
