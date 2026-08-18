# Hazard Functions

> Technical reference for `RMC.TotalRisk.RiskFunctions.Hazards` — `TabularHazard`, `ParametricUnivariateHazard`, and `NonparametricHazard` in detail, plus the composite family. Source of the methodology: RMC-TR-2022-XX [7], [*Quantitative Risk Analysis with RMC-TotalRisk*](https://usace-rmc.github.io/RMC-Software-Documentation/source-documents/desktop-applications/rmc-totalrisk/technical-reference-manual/RMC-TotalRisk-Technical-Reference-Manual.pdf), Hazard Functions chapter. The v1.1 API preserves the v1.0 domain surface; deltas are listed at the end.

A **hazard function** is defined by the exceedance probabilities of hazard levels — annual maximum peak flow, stage, or peak ground acceleration. Hazard functions are commonly called frequency curves; in dam and levee risk assessment they typically describe the annual exceedance probability (AEP) of the governing hazard parameter.

Every hazard function is modeled as a continuous random variable: a sampled hazard function is a Numerics `IUnivariateDistribution` with a PDF *f(x)*, CDF *F(x)*, survival function *S(x) = 1 − F(x)*, and inverse CDF *F⁻¹(p)*. In the risk analysis, hazard functions are numerically integrated between non-exceedance probabilities of 10⁻¹⁶ and 1 − 10⁻¹⁶ — sufficiently close to 0 and 1 to make the risk results collectively exhaustive.

## Contract

```csharp
public interface IHazardFunction : IRiskFunction
{
    HazardFunctionType FunctionType { get; }                        // runtime discriminator (never serialized/hashed)
    IUnivariateDistribution SampleFunction();                       // mean (expected) curve
    IUnivariateDistribution SampleFunction(double percentile);      // co-monotonic percentile curve
    IUnivariateDistribution SampleFunction(int realizationIndex);   // pre-allocated sampler row / posterior index
    double MinHazard(bool meanOnly);
    double MaxHazard(bool meanOnly);
}
```

`SampleFunction()` must return the **expected probability given the hazard level**, E[F(x)] — not the hazard given expected probability — because the mean-risk-only simulation relies on the law of total expectation over statistically independent inputs (report Eq. 104–106): E[C] = Σ E[f(x)]·E[P(F|x)]·E[C(x)]. The distinction matters: E[F⁻¹(p)] ≠ F⁻¹(E[p]) under asymmetric knowledge uncertainty.

## TabularHazard

A tabular (nonparametric) relationship of hazard levels and exceedance probabilities, with one of three knowledge-uncertainty modes (`FunctionUncertainty`):

| Mode | Table | Ordinate content |
|---|---|---|
| `None` | `NoUncertaintyFunction` | descending exceedance probability *pᵢ* → deterministic hazard *xᵢ* |
| `Hazard` | `HazardUncertainFunction` | descending exceedance probability *pᵢ* → hazard **distribution** *Xᵢ* |
| `Probability` | `ProbabilityUncertainFunction` | ascending hazard *xᵢ* → exceedance-probability **distribution** *Pᵢ* |

`TargetFunction` exposes the active table. Probabilities must lie in [0, 1] (validated on the ordinate axis carrying them per mode).

### Interpolation and transforms

The nonparametric survival function and its inverse are computed by linear interpolation over the table (report Eq. 5–6). Values outside the user-defined hazard range are **flat-lined** (clamped to the end ordinates) — no extrapolation. Because integration spans essentially (0, 1), enter tables with sufficient probability coverage to avoid significant flat-lining.

Interpolation accuracy can be improved with axis transforms [7]:

- `HazardTransform` — logarithmic (hazards that grow exponentially in real space interpolate linearly in log space). Requires a non-negative hazard range.
- `ProbabilityTransform` — logarithmic or **Normal-Z** (default): p is mapped through Φ⁻¹, the standard-normal inverse CDF, so frequency-curve tails interpolate accurately.

With hazards x₁ < … < xₙ at descending exceedance probabilities p₁ > … > pₙ, the interpolated
inverse survival function and its transformed variants are

```
S⁻¹(p) = xᵢ + (xᵢ₊₁ − xᵢ)·(p − pᵢ)/(pᵢ₊₁ − pᵢ)                              (real space)
S⁻¹(p) = exp[ ln xᵢ + (ln xᵢ₊₁ − ln xᵢ)·(p − pᵢ)/(pᵢ₊₁ − pᵢ) ]              (log hazard)
S⁻¹(p) = xᵢ + (xᵢ₊₁ − xᵢ)·(Φ⁻¹(p) − Φ⁻¹(pᵢ))/(Φ⁻¹(pᵢ₊₁) − Φ⁻¹(pᵢ))          (normal-Z probability)
```

— the transform pair applying to whichever axes carry it.

### Uncertainty analysis (co-monotonic sampling)

Knowledge uncertainty is sampled with one percentile driving every ordinate simultaneously (report Algorithm 3):

1. Sample a percentile *u* ∈ (0, 1) (Latin hypercube by default in v1.1; the analysis pre-allocates each function's percentile matrix via `SetupSampler`).
2. For each ordinate *i*, evaluate the ordinate distribution at *u*: *xᵢ = X ᵢ⁻¹(u)* (Hazard mode) or *pᵢ = Pᵢ⁻¹(u)* (Probability mode).
3. Assemble the sampled curve; if the sampled ordinates violate monotonicity, they are repaired minimally (`FunctionHelpers.ForceMonotonic`).

This perfect rank correlation across hazard levels is deliberate: it preserves the requirement that confidence intervals increase monotonically with hazard level, and matches HEC-FDA's graphical-relationship uncertainty analysis. Available ordinate-uncertainty distributions: Deterministic, Generalized Beta, Ln-Normal, Normal, PERT, PERT-Percentile, PERT-Percentile-Z (probability mode), Triangular, Truncated Normal, Uniform.

### Mean curve

`SampleFunction()` is mode-dependent:

- **None** — the deterministic table, inverted to hazard-vs-non-exceedance and wrapped as an `EmpiricalDistribution` with the configured transforms.
- **Hazard** — the expected-probability curve: 200 stratified hazard quantiles spanning the full-uncertainty hazard range; 10,000 percentile curves at Weibull plotting positions *uₖ = k/(N+1)*; the expected exceedance probability at each quantile is the average CDF across the curves (`BootstrapAnalysis.ExpectedProbabilities`); ordinates that fail to decrease the exceedance probability by more than 10⁻⁸ are dropped when rebuilding the curve.
- **Probability** — the per-ordinate mean probabilities directly; for PERT-Percentile-Z (whose ordinate mean is not analytic) the expected probability is rebuilt from 10,000 plotting-position percentile curves.

### API

```csharp
var hazard = new TabularHazard
{
    Name = "Stage Frequency",
    SpecifiedHazard = "Stage",
    HazardUnit = "ft",
    UncertaintyValue = FunctionUncertainty.Hazard,
    HazardUncertainFunction = new UncertainOrderedPairedData(
        ordinates, strictOnX: true, SortOrder.Descending, strictOnY: true, SortOrder.Ascending,
        UnivariateDistributionType.Pert),
};

var (isValid, messages) = hazard.Validate();
IUnivariateDistribution mean = hazard.SampleFunction();          // expected curve
IUnivariateDistribution p90  = hazard.SampleFunction(0.90);      // 90th-percentile curve
UncertaintyAnalysisResults? summary = hazard.ComputeUncertaintyResults(0.90);
```

`ComputeUncertaintyResults(w)` evaluates **exact** percentile curves over the active table — mean, median, and the (1 ∓ w)/2 confidence bounds per ordinate, index-aligned with the table — no simulation.

## ParametricUnivariateHazard

A parametric hazard function: a fitted parent distribution (Log-Pearson Type III by default; any Numerics univariate distribution is accepted — the v1.0 catalog lists Exponential, Gamma, GEV, Generalized Logistic, Generalized Normal, Generalized Pareto, Gumbel, Kappa-4, Log-Normal, Log-Pearson III, Pearson III) whose knowledge uncertainty is a posterior parameter-set ensemble.

### Parametric bootstrap (report Algorithm 1)

`Estimate()` quantifies uncertainty with the parametric bootstrap:

1. Sample at random *n* hazard levels from the parent distribution, where *n* = `EffectiveRecordLength` (ERL) — the measure of information content in the fit (equivalent record length / effective sample size). Longer ERL → narrower intervals.
2. Estimate a new distribution from the bootstrap sample with `EstimationMethod` (product moments, linear moments, or maximum likelihood; product moments are rejected for Generalized Normal, Kappa-4, and Weibull, linear moments for Weibull).
3. Record quantiles at the `ProbabilityOrdinates` (entered as **exceedance** probabilities, inverted internally to non-exceedance).
4. Repeat for `Realizations` bootstrap replications (default 10,000; range [100, 100,000] with a warning below 1,000), then derive the mean curve and the two-sided `ConfidenceIntervalWidth` intervals from the bootstrapped quantile ensemble.

The bootstrap PRNG seed (`PRNGSeed`, default 12345, must be positive) is used directly, so the same configuration always reproduces the same posterior.

### Uncertainty propagation (report Algorithm 2)

When simulating risk with full uncertainty, each Monte Carlo realization selects one of the posterior parameter sets and configures a fresh parent clone from it:

- `SampleFunction(int realizationIndex)` — direct posterior lookup (the engine path; `SamplingDimensions = 0`, so the realization index maps straight onto the posterior).
- `SampleFunction(double percentile)` — index ⌊percentile × Realizations⌋, clamped to the last draw (used by composite functions, which drive sub-functions by percentile).
- `SampleFunction()` — the posterior **mean curve** as an `EmpiricalDistribution` (Normal-Z probability transform), satisfying the expected-probability requirement of the mean-only simulation.

### Posterior import (v1.1)

`Estimate(IList<ParameterSet> parameterSets)` imports an externally fitted posterior instead of bootstrapping — the path the future UI importer uses for RMC-BestFit results (univariate Bayesian estimation, Bulletin 17C, point process), which are exported as Numerics artifacts (no `RMC.BestFit.dll` reference). `Realizations` aligns to the ensemble size, the mean curve is the expected quantile across the ensemble, and the confidence bounds are the (1 ∓ w)/2 sample percentiles — identical definitions to the bootstrap, so imported and bootstrapped posteriors summarize identically.

### API

```csharp
var hazard = new ParametricUnivariateHazard
{
    Name = "Peak Flow Frequency",
    SpecifiedHazard = "Peak Flow",
    HazardUnit = "cfs",
    EffectiveRecordLength = 100,
    Realizations = 10000,
    PRNGSeed = 12345,
};
hazard.SetDistributionParameters(new[] { 3.0, 0.2, 0.5 });   // LP3: mean/sd/skew of log
hazard.Estimate();                                            // parametric bootstrap
// — or import a BestFit posterior (UI layer passes parsed Numerics artifacts):
// hazard.Estimate(parameterSets);

IUnivariateDistribution mean = hazard.SampleFunction();
IUnivariateDistribution draw = hazard.SampleFunction(realizationIndex: 42);
UncertaintyAnalysisResults? summary = hazard.ComputeUncertaintyResults(0.90);
```

`ComputeUncertaintyResults(w)` surfaces the stored posterior summary (`Results`); a width other than the estimated `ConfidenceIntervalWidth` re-slices the intervals exactly from the stored parameter sets.

## v1.1 changes vs. the v1.0 report

- **Sampling** is index-driven through pre-allocated per-function percentile matrices (`SetupSampler`, Latin hypercube by default) instead of ad-hoc `Random` draws; parametric functions remain direct posterior lookups ([`MODEL_LIBRARY_ARCHITECTURE.md`](../requirements/MODEL_LIBRARY_ARCHITECTURE.md) §5.8).
- **`NoUncertaintyFunction`** corrects the v1.0 `NoUncertainyFunction` spelling.
- **Posterior injection** (`Estimate(IList<ParameterSet>)`) replaces the dedicated BestFit-import element; the importer lives in the UI layer.
- **Failure paths throw**: sampling an invalid or un-estimated function raises `InvalidOperationException` (v1.0 returned null); a failed bootstrap propagates its exception (v1.0 swallowed it).
- **Bounds fixes**: the hazard-bounds cache is keyed by the `meanOnly` flag, and the full-posterior bounds scan is race-free (both latent v1.0 defects).
- **Uncertainty summaries** (`ComputeUncertaintyResults`) moved from app-layer plotting code into the model library; tabular summaries are exact percentile evaluations.

## NonparametricHazard (the HEC-FDA graphical method)

`NonparametricHazard` is the HEC-FDA "less simple method" [14] preserved for backwards
compatibility with existing flood risk management studies: the user enters only the graphical
AEP-vs-hazard curve plus an `EffectiveRecordLength`, and the per-ordinate knowledge uncertainty is
**derived**, not entered.

The derivation (`UpdateHazardFunction()` — a pure deterministic function of the serialized inputs,
recomputed on load):

1. **Extend** the curve by linear extrapolation on the configured interpolation transforms — to
   `ExtrapolationEP` at the rare end (skipped when the data already reaches it) and to AEP 0.999
   at the frequent end (the HEC-FDA convention).
2. **Quantile standard error** per ordinate from the asymptotic order-statistic variance [7]:

   ```
   Var[x_p] = p·(1 − p) / ( N · f(x_p)² ),
   ```

   with N the effective record length and f the empirical density of the (log-)mean curve by
   central-difference differentiation. SEs are pinned beyond p ∈ [0.01, 0.99] and smoothed
   monotone toward both tails — the guard against unreasonable density estimates in the extremes.
3. **Ordinate distributions**: each ordinate becomes an `LnNormal` with real-space mean x and the
   derived σ (through the base-e log-normal moment mapping under a logarithmic
   `HazardTransform`), with σ repaired so the 1% confidence bound never inverts between adjacent
   ordinates.

Sampling is the standard co-monotonic percentile walk over the derived table, and it gates on
**input** validity: a legitimately derived ladder can cross deep in the tails, and sampled curves
repair minimally through `ForceMonotonic` (matching v1.0). The derived table never serializes —
the identity surface is exactly the user content plus `ExtrapolationEP` and `IsUncertain` — so a
change to the derivation moves loaded results without moving hashes (a documented re-pin event).

The report's restrictiveness caveat carries over: perfect rank correlation with Ln-Normal
ordinates limits the shapes a random realization can take, which can slightly over- or under-state
the variance of risk results — the price of a nonparametric method with derived uncertainty.
`TabularHazard` offers full per-ordinate control when more flexibility is needed [7].

**Improved over v1.0** (results-preserving; the Table 38 pins are the arbiter): the per-ordinate
Brent root find in the 1%-bound repair is replaced by its exact closed-form solution (the legacy
Brent call remains as a guarded fallback); the log-normal moment mappings are inlined closed
forms; and the frequent-end extrapolation is evaluated *before* the ordinate lists are mutated —
v1.0 corrupted its own interpolator there and silently produced a flat extension (inert for inputs
already anchored at AEP 0.999, this type's default).

Verification: the 2024 report's SF-8 scenario against HEC-FDA — all 20 published log₁₀ quantile
pins (Table 38 [8]) — plus an independent re-derivation oracle and the reliability-mode
annualized-failure-probability pins
([closed-form-functions](../verification/closed-form-functions.md)).

## Composite hazard functions

`CompositeHazard` combines a weighted list of child hazard functions under one of two rules:

| Combination | Rule | Reading |
|---|---|---|
| **Mixture** (default, the v1.0 `IsMixture = true`) | `F(x) = Σ ωᵢ·Fᵢ(x)` — a `Numerics.Mixture` | Alternative descriptions of the loading; exactly one applies to any event. Report Equation 49. |
| **CompetingRisks** | the **maximum** rule under the configured `Dependency` | All loading mechanisms occur; the most severe controls. Weights are inert. |

The two rules are the two directions of one extreme-value algebra [27], [28]: a **mixture** is the
weighted average of mutually exclusive alternative descriptions of the same annual maximum,
`F(x) = Σ ωᵢFᵢ(x)` — elicited debris-blockage or gate-availability scenarios, exactly one true per
event. The **maximum of independent drivers** (rainfall- and snowmelt-driven annual maxima, say)
multiplies the CDFs, `F(x) = ∏ Fᵢ(x)` — equivalently, in USACE survival form, the probability of
union of the exceedances, `S(x) = 1 − ∏(1 − Sᵢ(x))` — so the combined curve is always at least as
severe as its most severe member, where a mixture is the weighted average of its members [7].

The mixture is **aleatory** by design: the combination is a single distribution carried through
every realization, `SamplingDimensions` is 0, and no branch is drawn — so a mixture of deterministic
children is itself deterministic. Knowledge uncertainty enters through the children's own posteriors.
The weight semantics, the mixture-versus-competing-risks decision rule, and what is deferred are in
[composite-functions.md](composite-functions.md); verification is in
[../verification/composite-hazard.md](../verification/composite-hazard.md).

v1.1 changes vs. v1.0: percentile sampling is deterministic and RNG-free (v1.0 derived a `Random`
seed from the percentile, colliding percentiles closer than 1e-5); children keep their own
interpolation transforms; the lossy all-empirical union-knot collapse is not ported; empty composites
throw instead of returning `double.MaxValue`; the circular-reference recursion carries a visited set;
and `SetupSampler` rejects a posterior-indexed child whose realization capacity is below the
requested sample size.
