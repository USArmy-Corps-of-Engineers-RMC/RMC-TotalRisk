# Transform Functions

> Technical reference for `RMC.TotalRisk.Models.TransformFunctions` (Phase 2 surface: `TabularTransform`; the closed-form `LinearTransform`/`PowerTransform` arrive in Phase 7). Source of the methodology: RMC-TR-2022-XX, *Quantitative Risk Analysis with RMC-TotalRisk* (docs/reports), Transform Functions chapter.

A **transform function** converts hazard levels from one domain to another — mathematically, function composition (report Eq. 16): given *g* (a frequency function of *x*) and a transform *t*, the composed frequency function of the transformed hazard is *g ∘ t⁻¹*. The canonical example: a peak-flow frequency function becomes a stage frequency function through a flow-to-stage rating curve. Transforms can feed hazard functions, other transform functions (chained), and system response functions.

## Contract

```csharp
public interface ITransformFunction : IRiskFunction
{
    string TransformedHazard { get; set; }
    string TransformedHazardUnit { get; set; }
    IUnivariateFunction SampleFunction();                       // mean transform
    IUnivariateFunction SampleFunction(double percentile);      // co-monotonic percentile transform
    IUnivariateFunction SampleFunction(int realizationIndex);   // pre-allocated sampler row
    double MinHazard();
    double MaxHazard();
    double MinTransformedHazard(bool meanOnly);
    double MaxTransformedHazard(bool meanOnly);
}
```

Sampled transforms are Numerics `IUnivariateFunction`s: `Function(x)` maps hazard to transformed hazard; `InverseFunction(y)` maps back.

## TabularTransform

A tabular (nonparametric) relationship of hazard levels *x₁ < x₂ < … < xₙ* (strictly ascending) and transformed hazard values *yᵢ*, evaluated by linear interpolation (report Eq. 26) with **flat (clamped) extrapolation** beyond the table. The transformed values need not be monotonic (`OrderY = None`).

- `HazardTransform` — optional logarithmic transform on the input axis (requires non-negative hazards; validated).
- `TransformTransform` — optional logarithmic transform on the output axis (requires a non-negative transformed range across every ordinate's lower/mean/upper values; unbounded distributions are probed at the ±10⁻⁵ percentiles).

### Uncertainty

Each ordinate's transformed value can carry a distribution (the tabular distribution catalog: Deterministic, Generalized Beta, Ln-Normal, Normal, PERT, PERT-Percentile, Triangular, Truncated Normal, Uniform). Sampling is co-monotonic per report Algorithm 3 — one percentile drives every ordinate — implemented by the Numerics `TabularFunction.ConfidenceLevel`:

- `SampleFunction()` → `TabularFunction` at the per-ordinate **means** (`ConfidenceLevel = −1`).
- `SampleFunction(p)` → `TabularFunction` at percentile *p* across all ordinates.
- `SampleFunction(idx)` → the pre-allocated percentile row for realization *idx* (`SamplingDimensions = 1`).

Uncertainty must be entered so the confidence intervals increase monotonically with hazard level.

### Bounds

`MinHazard()`/`MaxHazard()` are the first/last table hazards. `MinTransformedHazard(meanOnly)`/`MaxTransformedHazard(meanOnly)` read the first/last ordinate at its mean (`meanOnly = true`) or at the 10⁻⁵ / 1 − 10⁻⁵ percentiles (full uncertainty) — the span the risk analysis uses to align downstream functions.

### API

```csharp
var rating = new TabularTransform
{
    Name = "Rating Curve",
    SpecifiedHazard = "Peak Flow",
    HazardUnit = "cfs",
    TransformedHazard = "Stage",
    TransformedHazardUnit = "ft",
    UncertainOrderedPairedData = new UncertainOrderedPairedData(
        ordinates, strictOnX: true, SortOrder.Ascending, strictOnY: false, SortOrder.None,
        UnivariateDistributionType.Normal),
};

var (isValid, messages) = rating.Validate();
IUnivariateFunction mean = rating.SampleFunction();
double stage = mean.Function(25000d);                            // flow → stage
UncertaintyAnalysisResults? summary = rating.ComputeUncertaintyResults(0.90);
```

`ComputeUncertaintyResults(w)` evaluates exact per-ordinate percentile curves (mean, median, (1 ∓ w)/2 bounds), index-aligned with the table ordinates — no simulation.

## LinearTransform and PowerTransform (Phase 7)

Documented here for family completeness; they land with the Phase 7 backfill as thin wrappers over the Numerics `LinearFunction`/`PowerFunction`:

- **Linear** (report Eq. 17–20): *y = α + βx + ε*, with residual ε ~ N(0, σₑ) sampled by percentile under full uncertainty; parameters estimable by ordinary least squares.
- **Power** (report Eq. 21–25): *y = α(x − ξ)^β · ε*, with log-normal residual (log-space standard error); estimable by OLS after log-transforming; an inverse-form option supports rating curves fitted with stage as the independent variable.

## v1.1 changes vs. the v1.0 report

- Sampling is index-driven through the pre-allocated percentile matrix (`SetupSampler`; Latin hypercube default).
- Sampling an invalid table throws `InvalidOperationException` (v1.0 returned null).
- `ComputeUncertaintyResults` moved the app-layer uncertainty plotting math into the model library (exact percentile evaluation).
