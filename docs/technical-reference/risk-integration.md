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
  lands on an interval endpoint. Adjacent stratification bins therefore never share a `p`, and the
  recorded risk-point set is duplicate-free. The LEC probability-mass step that sorts risk points by
  `p` (legacy `Curve.ProcessHazardProbabilities`, `Curve.vb:304-316`) relies on that uniqueness.
- **Point placement is denser and differently distributed than Simpson's.** Recorded LECs will differ
  from v1.0 by quadrature resolution alone. The mean converges to the same value — that is the
  verification gate ([../verification.md](../verification.md), v0.13 policy: means are v1.0-parity,
  tails are MC-parity).
- **`MinDepth` defaults to 0.** Set it ≥ 2. Otherwise a region that looks flat to the first Kronrod
  pass (a low-probability shoulder before a steep fragility) can be accepted without subdivision.
- **`StandardError`** is a real error estimate (`√Σ (Kronrod − Gauss)²` accumulated across accepted
  intervals), unlike Simpson's Richardson proxy — surface it on the results container as a diagnostic.

### Interim: probability mass from the weight (Numerics item N7)

The exact LEC construction wants each evaluation's **quadrature weight** (the `dF` mass it represents)
handed to the integrand, exactly as the VEGAS path already receives `wgt`. `AdaptiveGaussKronrod.Function`
is `Func<double, double>` today — it does not pass the Kronrod weight to the callback. Until Numerics
adds a weight-exposing overload (**roadmap item N7**, Phase 8), keep the v1.0 midpoint-trapezoid mass
re-derivation but **deduplicate** the sorted risk points first and assert `Σ mass = 1 ± 1e-9`. See
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

**The heuristic (`Automatic`) is the new design work.** VEGAS already runs a warm-up pass with
`recordOutput = false` that currently discards everything but the importance grid
(legacy `RiskAnalysis.vb:3153-3163`). Instead, accumulate the observed per-component failure
probabilities during the warm-up and set

```
pTarget = clamp( min_i( P̂_f,i ) · Alpha , 1e-12 , 1e-2 )
```

then call `Vegas.ConfigureForRareEvents(pTarget)` before the recording pass. This costs nothing extra,
is fully deterministic (the warm-up seed is fixed), and adapts γ to the system's actual fragility
rather than a guess.

### Two correctness items before enabling γ > 1

- **Jacobian must reach the weight.** In TotalRisk `wgt` *is* the LEC probability mass, so if the
  power-transform Jacobian is not folded into the `wgt` handed to the integrand, every LEC ordinate is
  biased even though the returned integral is correct. `Vegas.cs` appears to apply it — **verify with a
  test** (Numerics item **N9**, Phase 8: integrate a known heavy-tail function at γ ∈ {1, 4, 10} to the
  same value; confirm `Σ wgt` = domain volume at every γ) before making γ > 1 a default.
- **Record more than one final pass.** v1.0 records LEC points from a single pass of `FinalEvaluations`
  (default 10,000) — far too sparse for a tail ordinate in D dimensions. Accumulate across
  `IndependentEvaluations > 1` recording passes and scale `FinalEvaluations` with D in
  `SetIntegrationDefaults`.

See [loss-exceedance-curves.md](loss-exceedance-curves.md) §System aggregation for how the joint path
enumerates real component failure/non-failure combinations (rather than convolving conditional means)
once the per-pathway lists on `ComponentRiskOutput` are activated.
