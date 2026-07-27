# Risk Integration

> Technical reference for the numerical integration inside the `RMC.TotalRisk.Analyses.RiskAnalysis`
> engine (Phase 4 / 4b). Covers the 1D adaptive quadrature, the selectable integrand
> (`RiskIntegrand`), and the multi-dimensional VEGAS integration with its power-transform tail focus.
> Companion: [loss-exceedance-curves.md](loss-exceedance-curves.md) (how the integrator's evaluation
> points become LECs and risk measures). Normative spec:
> [../requirements/MODEL_LIBRARY_ARCHITECTURE.md](../requirements/MODEL_LIBRARY_ARCHITECTURE.md)
> §7.3, §7.7, §7.8 (v0.13). Legacy source paths are in the `C:\GIT\RMC-TotalRisk-Dev` reference repo.

## The integrator is an adaptive sampler

The single most important fact for porting the engine: **the risk integral's returned value is
discarded.** In v1.0 the per-component `AdaptiveSimpsonsRule` integrand returns the conditional total
expected consequence at each hazard probability `p`, but the call site
(legacy `RiskAnalysis.vb:2891-2892`) reads only `integrator.FunctionEvaluations` and
`integrator.StandardError`. The reported means, standard deviations, and LECs are built afterward from
the **risk points** the integrand recorded as a side effect (`recordOutput = true`).

So the adaptive integrator is being used as an *importance sampler*: its job is to decide **where**
along `p ∈ (0, 1)` to place evaluations, densely where the integrand varies, sparsely where it is
flat. Each evaluation records a `RiskPoint` — the hazard level, the failure probability, and the
per-branch consequences — and the LEC is assembled from that point cloud
([loss-exceedance-curves.md](loss-exceedance-curves.md)). Two consequences follow:

1. The **choice of integrand changes only where points land**, never what is reported — every
   risk-type LEC and every risk measure is produced regardless (this is why `RiskIntegrand` is a
   refinement-objective enum, not an output selector).
2. The integrator's error estimate and evaluation count are genuine diagnostics worth surfacing on
   the results container.

## 1D quadrature: Adaptive Gauss–Kronrod (replaces Adaptive Simpson)

v1.0 used `Numerics.Mathematics.Integration.AdaptiveSimpsonsRule`. v1.1 uses
`AdaptiveGaussKronrod` (G10K21 — 10-point Gauss with a 21-point Kronrod extension, 21st-order accurate
for smooth integrands, QUADPACK-style). The Numerics type exposes the **same surface** the engine
already drove Simpson through, so both 1D call sites are a drop-in swap:

```csharp
// C:\GIT\Numerics\Numerics\Mathematics\Integration\AdaptiveGuassKronrod.cs
//   ^ note: the FILE name is misspelled "Guass"; the TYPE name AdaptiveGaussKronrod is correct.
var integrator = new AdaptiveGaussKronrod(p => Integrand(p), min: 1e-16, max: 1 - 1e-16)
{
    ReportFailure = false,
    MaxFunctionEvaluations = 1_000_000,   // _maxEvaluations
    MaxDepth = 100,                       // _maxDepth
    RelativeTolerance = 1e-8,             // _tolerance
    MinDepth = 2,                         // NEW: ≥ 2 so a flat low-probability region is not
                                          //      accepted on the first Kronrod pass
};

// Seed the adaptivity with the same 50 hazard-stratified bins v1.0 used:
var bins = Stratify.XValues(new StratificationOptions(
                hazard.InverseCDF(1e-16), hazard.InverseCDF(1 - 1e-16), 50), true);
bins = Stratify.XToProbability(bins, hazard.CDF, false);
integrator.Integrate(bins);
```

### The two 1D call sites

| Call site | Legacy | Domain | Integrand | Notes |
|---|---|---|---|---|
| Per-component risk integral | `RiskAnalysis.vb:2852-2889` | `p ∈ [1e-16, 1−1e-16]` | selected by `RiskIntegrand` (below) | 50 `Stratify` hazard bins; tol 1e-8; MaxDepth 100; MaxEvals 1e6 |
| CVaR integral | `Curve.vb:547-552` | `p ∈ [1e-16, α]` (α = `Options.Alpha`, default 0.01) | `LEC.GetXFromY(p, Log, Log)` — the consequence quantile by log-log inverse interpolation | steepest integrand in the engine; **give it explicit tol/eval caps** (legacy used library defaults) |

### Properties to port correctly

- **G10K21 nodes are strictly interior** — the largest abscissa is ≈ 0.99566 < 1, so no evaluation
  lands on an interval endpoint, and adjacent stratification bins never share a `p` from the node
  placement alone. That is *not* enough to make the recorded set duplicate-free: a saturating hazard
  CDF collapses several hazard bins onto the same probability, and `Integrate(List<StratificationBin>)`
  then evaluates a zero-width bin 21 times at one abscissa. The mass ledger keys on the exact abscissa
  and **sums weights per key**, so the degenerate case is credited once at its true (zero) width.
- **Point placement is denser and differently distributed than Simpson's.** Recorded LECs will differ
  from v1.0 by quadrature resolution alone. The mean converges to the same value — that is the
  verification gate ([../verification.md](../verification.md), v0.13 policy: means are v1.0-parity,
  tails are MC-parity).
- **`MinDepth` defaults to 0.** Set it ≥ 2. Otherwise a region that looks flat to the first Kronrod
  pass (a low-probability shoulder before a steep fragility) can be accepted without subdivision.
- **`StandardError`** is a real error estimate (`√Σ (Kronrod − Gauss)²` accumulated across accepted
  intervals), unlike Simpson's Richardson proxy — surface it on the results container as a diagnostic.

### Probability mass from the quadrature weight (N7, adopted Phase 8.5)

The exact LEC construction takes each evaluation's **quadrature weight** — the `dF` mass it represents —
from the integrator, exactly as the VEGAS path takes `wgt`. `AdaptiveGaussKronrod.Recorder` is the
acceptance-aware `(x, weight, f)` ledger: it flushes only for intervals the refinement **accepted**, so
`Σ weights` is the domain width and `Σ w·f ≡ Result`. `RMC.TotalRisk.Results.QuadratureMassLedger`
collects the flush, seals it (sort + coalesce duplicate abscissas), and `Curve.ApplyRecordedMass` sets
each risk point's mass from it, compacting away points the refinement superseded.

Two gates replace the old `Σ mass = 1` assertion, which could not detect a mis-partition (the trapezoid
partition telescoped to one regardless of whether the point set was right):

1. **Domain-partition witness**, once per component integration — `|Σ weights − Σ bin widths|` within
   1e-9 relative, against a Neumaier-compensated total. A double-counted rejected interval overshoots
   by that interval's *width*, which is macroscopic rather than rounding.
2. **Fan-out coverage** per curve — the number of recorded abscissas that match a ledger key must equal
   `DistinctAbscissaCount`, so a break in the record fan-out is caught rather than silently thinning
   the curve.

Measured against a 4,000,000-point dense reference, the mass partition improves from **5.7e-7** relative
(midpoint trapezoid) to **4.0e-11** (ledger). See
[loss-exceedance-curves.md](loss-exceedance-curves.md) §Probability mass.

## `RiskIntegrand` — the adaptive-refinement objective

`RiskIntegrand` lives in `RMC.TotalRisk.Core.Enums` (one file). It is **not** a runtime discriminator —
it is a field on `RiskAnalysisOptions` and is therefore **hashed** with the other options (it changes
seeds only through the options hash, never the function/component identity surface). Its default,
`MeanTotalRisk`, reproduces v1.0 point placement exactly.

All members are functions of the hazard non-exceedance probability `p`; `P_F` = failure probability at
`h = Hazard.InverseCDF(p)`, `C_F` = failure consequence, `C_NF` = non-failure consequence.

| Member | Integrand (refinement target) | Concentrates evaluations where… |
|---|---|---|
| `MeanTotalRisk` **(default)** | `P_F·E[C_F] + P_NF·C_NF` | v1.0 behavior; the mean annualized total consequence accumulates |
| `MeanIncrementalRisk` | `P_F·E[(C_F − C_NF)⁺]` (the Excess curve) | the **reducible** risk lives — the quantity most USACE alternatives are judged on |
| `TotalProbabilityOfFailure` | `P_F(p)` | the fragility is steep rather than where consequences are large; the natural pairing for `RiskAnalysisMode.Reliability` (Phase 4c) |
| `TailConditionalRisk` | `P_F·E[C_F]·1{p ≤ α}`, α = `Options.Alpha` | the α-tail — so VaR, CVaR and the F-N tail ordinates converge (the CVaR / expected-shortfall family) |
| `ThresholdExceedanceProbability` | `P(C > ConsequenceThreshold ∣ p)` | the assurance / tolerable-risk compliance decision is made |
| `SecondMoment` | `E[C² ∣ p]` | the LEC variance converges — v1.0's weakest output |
| `Balanced` | normalized `MeanTotalRisk + SecondMoment + TailConditionalRisk` | every measure converges together; the sensible default for headless / API callers that read all measures |

### Discontinuous integrands need a bin boundary

`TailConditionalRisk` and `ThresholdExceedanceProbability` are **discontinuous** in `p` (the indicator
switches at `p = α`, respectively at the consequence-threshold crossing). A high-order rule loses its
accuracy on any interval straddling a discontinuity. Requirement: inject the discontinuity `p` as an
extra **stratification-bin boundary** so it falls on a bin edge — the 50-bin hazard stratification
simply gains one boundary, and `AdaptiveGaussKronrod.Integrate(List<StratificationBin>)` integrates
each bin independently, so the discontinuity never lands inside a Kronrod panel.

### Why the tail members exist

Value-at-Risk marks the boundary of the α-tail but says nothing about the losses beyond it;
conditional VaR / expected shortfall is the coherent tail measure that averages them, and is now the
regulatory standard (Basel III FRTB replaced 99% VaR with 97.5% expected shortfall). For life-safety
consequence LECs the tail *is* the decision-relevant region, so the engine needs an integrand that
refines it directly rather than hoping the mean-total objective happens to place points there. See
[../references.md](../references.md).

## Multi-dimensional integration: VEGAS with power-transform tail focus

For the joint system-risk method (D > 1 components with `SystemRiskMethod = Joint`), the engine
integrates over the D-dimensional unit hypercube `(1e-16, 1−1e-16)^D` of correlated hazard
probabilities using `Numerics.Mathematics.Integration.Vegas` (legacy `RiskAnalysis.vb:2984-3177`).
Correlation enters through the multivariate-normal inverse CDF `_eMVN.InverseCDF(p)`. As in the 1D
path, the VEGAS **weight `wgt` is the LEC probability mass** for each recorded point — this is why the
joint path (unlike the 1D path) needs no mass re-derivation.

### The power transform (γ)

Numerics' VEGAS gained a **power transform** for rare-tail sampling
(`Vegas.cs`: `TailFocusParameter`, `ApplyPowerTransform`, `PowerTransformJacobian`,
`ConfigureForRareEvents`). It maps the sampling probability by

```
p' = 1 − (1 − p)^γ        (γ = TailFocusParameter, default 1.0 = identity = v1.0 behavior)
```

concentrating samples near `p → 1` (the upper hazard tail) when γ > 1, with the weight corrected by
the Jacobian `dp'/dp = γ(1 − p)^(γ−1)` so the integral stays unbiased. `ConfigureForRareEvents(pTarget)`
sets `γ = ln(pTarget)/ln(0.05)` clamped to [1, 20], `NumberOfBins ≥ 100`, and `Alpha = 1.8`.

### Exposing and setting γ in TotalRisk

`RiskAnalysisOptions` gains:

- `VegasTailFocusMode { None, Automatic, Manual }` — default `Automatic`. `None` pins γ = 1 for exact
  v1.0 comparability; `Manual` uses the supplied γ.
- `VegasTailFocusParameter` (γ) — default 1.0, valid [1, 20].

**The heuristic (`Automatic`) — v0.15 implementation: a deterministic quadrature probe.** The v0.13
plan harvested `pTarget` from the VEGAS warm-up itself; two implementation facts broke that mechanism
(architecture doc v0.15, item 2). First, `ConfigureForRareEvents` raises `NumberOfBins`, whose setter
reallocates the importance-grid arrays — configuring γ *after* the warm-up would wipe the warmed grid,
so γ must be set **before** any VEGAS pass. Second, a γ = 1 Monte Carlo warm-up cannot observe the
rare failure probabilities the target needs — that blindness is the very problem the transform
solves. The engine instead probes each component's annualized failure probability **on the mean
sample with adaptive Gauss–Kronrod**, once per run:

```
AFP_i = ∫ P_F,i(p) dp          (the one call site where the integral's returned value is the product)
pTarget = clamp( min_i AFP_i · Alpha , 1e-12 , 1e-2 )
```

then calls `Vegas.ConfigureForRareEvents(pTarget)` before the warm-up. The probe costs about a
thousand evaluations per component, is fully deterministic (mean sample, fixed stratification),
measures the failure probabilities to quadrature accuracy however rare they are — and the warm-up
itself then adapts under the active γ, strictly better than the post-warm-up ordering the v0.13 text
assumed.

### Driving stream, recording passes, and mass normalization (v0.15)

- **Seeded Mersenne Twister, not Sobol.** Numerics' VEGAS defaults to `UseSobolSequence = true`,
  which would make the driving stream seed-independent and void the §5.5 content-seed contract. The
  engine sets `UseSobolSequence = false` and `Random = new MersenneTwister(vegasSeed)` with
  `vegasSeed = ToPositiveSeed(HashCombine(systemSeed, "VEGAS", realizationIndex))`, where
  `systemSeed` folds the analysis seed with every component's canonical hash and occurrence index in
  canonical-hash order.
- **Five recording passes, self-normalized.** v1.0 recorded a single pass of `FinalEvaluations`
  (default 10,000) — far too sparse for a tail ordinate in D dimensions. The engine records across
  five passes (`Initialize = 1`, `IndependentEvaluations = 5`) with `FinalEvaluations` scaled by D in
  `SetIntegrationDefaults`, then scales every recorded mass by the reciprocal of the realized weight
  sum: per-pass `Σ wgt` equals the domain volume only in expectation, so self-normalization makes the
  exhaustive Total budget exactly one (and stays consistent if the evaluation cap truncates a pass).
- **The Jacobian demonstrably reaches the weight.** In TotalRisk `wgt` *is* the LEC probability mass,
  so a Jacobian missing from the recorded weight would bias every LEC ordinate even with a correct
  returned integral. Source-confirmed (`Vegas.cs:466-472` folds `PowerTransformJacobian` into the
  weight handed to the integrand) and **empirically gated** by the Phase 4b tail-focus audit
  ([../verification/system-risk.md](../verification/system-risk.md)): γ = 1, manual γ = 4, and the
  automatic focus agree on the mean, the failure union, and a deep-tail ordinate, with every recorded
  budget self-normalizing to one. The upstream Numerics unit tests remain item **N9** (Phase 8:
  integrate a known heavy-tail function at γ ∈ {1, 4, 10} to the same value; confirm `Σ wgt` = domain
  volume at every γ).

See [loss-exceedance-curves.md](loss-exceedance-curves.md) §System aggregation for how the joint path
enumerates real component failure/non-failure combinations (rather than convolving conditional means)
through the per-pathway lists on `ComponentRiskOutput`. Reproducibility scope: renaming is bit-inert;
component *reordering* under the joint method is statistically equivalent but not bit-identical — the
VEGAS variates couple the hypercube dimensions, so reordering permutes which coordinate stream drives
which component (architecture doc v0.15, item 4).
