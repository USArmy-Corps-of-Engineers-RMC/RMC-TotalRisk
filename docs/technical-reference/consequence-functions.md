# Consequence Functions

> Technical reference for `RMC.TotalRisk.RiskFunctions.Consequences` (Phase 2 surface: `TabularConsequence`). Source of the methodology: RMC-TR-2022-XX, *Quantitative Risk Analysis with RMC-TotalRisk* (docs/reports), Consequence Functions chapter.

A **consequence function** (damage function) describes the consequences of failure or non-failure — life loss, economic damages — at each hazard level. All consequence functions in RMC-TotalRisk are nonparametric.

## Contract

```csharp
public interface IConsequenceFunction : IRiskFunction
{
    string SpecifiedConsequence { get; set; }
    string ConsequenceUnit { get; set; }
    IUnivariateFunction SampleFunction();                       // mean consequence function
    IUnivariateFunction SampleFunction(double percentile);      // co-monotonic percentile function
    IUnivariateFunction SampleFunction(int realizationIndex);   // pre-allocated sampler row
    double MinHazard();
    double MaxHazard();
}
```

A sampled consequence function is a Numerics `IUnivariateFunction`: `Function(x)` maps hazard to consequence magnitude.

## TabularConsequence

A tabular relationship of strictly ascending hazard levels and consequence values, evaluated by linear interpolation (report Eq. 45) with flat extrapolation beyond the table. Consequence values need not be ordered. Optional logarithmic transforms on either axis (`HazardTransform`, `ConsequenceTransform`) improve interpolation accuracy; a logarithmic consequence axis requires a non-negative consequence range.

### Uncertainty

Each ordinate's consequence can carry a distribution (the tabular catalog; PERT-Percentile ordinates are coerced to a minimum allowable value of zero during validation). Sampling is co-monotonic (report Algorithm 3): one percentile per realization drives every ordinate; `SamplingDimensions = 1`.

**Negative-consequence clamp:** sampled consequence functions never return negative values — the sampled `TabularFunction` sets `AllowNegativeYValues = false`, so any negative sampled consequence evaluates to zero. Validation warns when any ordinate's sampled range can drop below −10⁻⁵ ("negative consequence values will be set to zero"), and warns when the first ordinate's mean consequence is non-zero (flat extrapolation makes every hazard below the table carry it).

### Incremental consequences and fail/non-fail coupling

Incremental consequences are the losses failure inflicts **over and above** what would occur without failure for the same hazard event (report Eq. 46):

ΔC(x) = C_fail(x) − C_nonfail(x)

In the Monte Carlo simulation, each failure mode samples its failure and non-failure consequence functions with the **same draw** — perfectly correlated — so the incremental difference is coherent per hazard event (v1.0 `SampledFailureMode` behavior, preserved by the v1.1 sampler design; architecture doc question Q-N). Negative incremental consequences computed during simulation are set to zero with a warning.

### API

```csharp
var damages = new TabularConsequence
{
    Name = "Breach Life Loss",
    SpecifiedHazard = "Stage",
    HazardUnit = "ft",
    SpecifiedConsequence = "Life Loss",
    ConsequenceUnit = "lives",
    UncertainOrderedPairedData = new UncertainOrderedPairedData(
        ordinates, strictOnX: true, SortOrder.Ascending, strictOnY: false, SortOrder.None,
        UnivariateDistributionType.TruncatedNormal),
};

var (isValid, messages) = damages.Validate();
IUnivariateFunction mean = damages.SampleFunction();
double lives = mean.Function(stage);
UncertaintyAnalysisResults? summary = damages.ComputeUncertaintyResults(0.90);
```

`ComputeUncertaintyResults(w)` evaluates exact per-ordinate percentile curves (mean, median, (1 ∓ w)/2 bounds), index-aligned with the table ordinates — no simulation.

## Later family members

- **LifeSimConsequence** (Phase 11) — a tabular consequence built from imported LifeSim Monte Carlo results: per hazard level the user selects an alternative/time-of-day result set, and a distribution (truncated normal by default) is auto-fit to all iterations (fit methods per the report: moments for Deterministic/Triangular/Normal/Ln-Normal/Truncated Normal, percentiles for PERT).
- **ParametricConsequenceFunction** (Phase 7) — closed-form power model per ER 1110-2-1156: C(h) = clamp(α·max(h − h₀, 0)^β, 0, U).
- **CompositeConsequence** (Phase 9) — sums a list of consequence functions (report Eq. 47), e.g., aggregating property, industry, and agriculture damages into a total function.

## v1.1 changes vs. the v1.0 report

- Sampling is index-driven through the pre-allocated percentile matrix (`SetupSampler`; Latin hypercube default).
- Sampling an invalid table throws `InvalidOperationException` (v1.0 returned null).
- `ComputeUncertaintyResults` moved the app-layer uncertainty plotting math into the model library (exact percentile evaluation).
