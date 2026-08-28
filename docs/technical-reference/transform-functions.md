# Transform Functions

> Technical reference for `RMC.TotalRisk.RiskFunctions.Transforms` — `TabularTransform`, the closed-form `LinearTransform` and `PowerTransform`, and `CompositeTransform`. Source of the methodology: RMC-TR-2022-XX, [*Quantitative Risk Analysis with RMC-TotalRisk*](https://usace-rmc.github.io/RMC-Software-Documentation/source-documents/desktop-applications/rmc-totalrisk/technical-reference-manual/RMC-TotalRisk-Technical-Reference-Manual.pdf), Transform Functions chapter.

A **transform function** converts hazard levels from one domain to another — mathematically, function composition ([7], Transform Functions chapter): given *g* (a frequency function of *x*) and a transform *t*, the composed frequency function of the transformed hazard is *g ∘ t⁻¹*. The canonical example: a peak-flow frequency function becomes a stage frequency function through a flow-to-stage rating curve. Transforms can feed hazard functions, other transform functions (chained), and system response functions.

## Contract

```csharp
public interface ITransformFunction : IRiskFunction
{
    TransformFunctionType FunctionType { get; }                 // runtime discriminator (never serialized/hashed)
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

A tabular (nonparametric) relationship of hazard levels *x₁ < x₂ < … < xₙ* (strictly ascending) and transformed hazard values *yᵢ*, evaluated by linear interpolation,

```
t(x) = yᵢ + (yᵢ₊₁ − yᵢ) · (x − xᵢ)/(xᵢ₊₁ − xᵢ),        xᵢ ≤ x ≤ xᵢ₊₁,
```

Beyond the table the function's `Extrapolation` policy governs; the default holds the end ordinates (the v1.0 behavior, bit-identical). The transformed values need not be monotonic (`OrderY = None`). A rating curve is typically derived by a hydraulic model (e.g., HEC-RAS) and entered as this tabular data [7].

- `HazardTransform` — optional logarithmic transform on the input axis (requires non-negative hazards; validated).
- `TransformTransform` — optional logarithmic transform on the output axis (requires a non-negative transformed range across every ordinate's lower/mean/upper values; unbounded distributions are probed at the ±10⁻⁵ percentiles).

### Uncertainty

Each ordinate's transformed value can carry a distribution (the tabular distribution catalog: Deterministic, Generalized Beta, Ln-Normal, Normal, PERT, PERT-Percentile, Triangular, Truncated Normal, Uniform). Sampling is co-monotonic per report Algorithm 3 — one percentile drives every ordinate — implemented by the Numerics `TabularFunction.ConfidenceLevel`:

- `SampleFunction()` → `TabularFunction` at the per-ordinate **means** (`ConfidenceLevel = −1`).
- `SampleFunction(p)` → `TabularFunction` at percentile *p* across all ordinates.
- `SampleFunction(idx)` → the pre-allocated percentile row for realization *idx* (`SamplingDimensions = 1`).

Uncertainty must be entered so the confidence intervals increase monotonically with hazard level.

### Extrapolation policy

`Extrapolation` — `None` (default), `Below`, `Above`, `Both`, or `Error` — extends the boundary
segments linearly in the configured transform spaces, or refuses out-of-range forward evaluation
loudly under `Error`; the inverse direction retains the endpoint hold. Serialized by enum name
only when non-default (every existing form, hash, and seed unchanged); a configured policy is
hashed compute content. Transformed outputs are physical values and extend unbounded. The full
doctrine, incl. the Error semantics, lives in the
[hazard-functions chapter](hazard-functions.md#extrapolation-policy). Contrast: the
`BivariateTransform` surface keeps its pinned corner/edge extrapolation policy — the two-way
table is excluded from this per-function policy by design.

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

## LinearTransform and PowerTransform

Thin wrappers over the Numerics `LinearFunction`/`PowerFunction` — the wrappers add domain labels, validation, serialization, and hash identity; zero math lives in the classes. Parameters are user-supplied, fitted externally by ordinary least squares [7]:

**Linear.** The transform and its OLS estimators are

```
y = α + βx + ε,                 ε ~ N(0, σₑ)  (normal, independently distributed),
β̂ = Σ(xᵢ − x̄)(yᵢ − ȳ) / Σ(xᵢ − x̄)²,          α̂ = ȳ − β̂·x̄,
σ̂ₑ = √[ Σ(yᵢ − α̂ − β̂·xᵢ)² / (n − 2) ].
```

Knowledge uncertainty is co-monotonic: one percentile sets `LinearFunction.ConfidenceLevel`, shifting every hazard level by the same `Normal(0, σₑ).InverseCDF(p)` offset (the exact v1.0 behavior). Defaults: α = 0, β = 1, σ = 10, uncertain, input range [0, 100].

**Power.** The transform carries a multiplicative log-normal residual,

```
y = α · (x − ξ)^β · ε,          log ε ~ N(0, σ),  α > 0,  β > 0,  x > ξ.
```

Taking logs linearizes it — `log y = log α + β·log(x − ξ) + log ε` — so the same OLS machinery estimates α and β in log space, and σ is the **log-space** standard error. A percentile-p sample multiplies the whole curve by `exp(σ·Φ⁻¹(p))` (e.g., the 0.9-percentile curve is `α(x − ξ)^β · exp(σ·z₀.₉)`). The inverse-form option (`IsInverse`) supports rating curves fitted with stage as the independent variable:

```
y = (x/α · ε)^(1/β) + ξ.
```

Defaults: α = 1, β = 1.5, ξ = 0, σ = 0.1, not inverted, input range [0, 100]. **`Minimum` is wrapper API/hash state only:** Numerics `PowerFunction.Minimum` has always derived from ξ (the v1.0 assignment to it never took effect), so evaluation clamps at ξ — v1.1 preserves that exact behavior and warns in validation when `Minimum` < `Xi`.

Both declare `SamplingDimensions = 1` while `IsUncertain` (else 0), serialize σ only while uncertain (the hash-recipe conditional), and surface `ComputeUncertaintyResults` percentile summaries. Verification: [closed-form-functions](../verification/closed-form-functions.md) — closed-form anchors plus engine-chain ensembles against a flat Monte Carlo oracle.

## BivariateTransform

A deterministic two-way table z = f(x, y) on the Numerics `Bilinear` interpolator (convention `z[i, j] = z(x1[i], x2[j])`), with per-axis transforms (`HazardTransform`, `SecondaryHazardTransform`) and an output transform (`TransformTransform`). In a component graph the element is two-in/two-out: output port 0 carries z, output port 1 passes the secondary signal through unchanged, so bivariate transforms CHAIN — deformation(pga, pool) → warning-time(deformation, pool) — with every element on the path reading the same secondary-chain signal (see [bivariate-hazards](bivariate-hazards.md)).

Two policies are pinned by test and shared with every bivariate table:

- **Extrapolation** is the interpolator's native clamp: both coordinates out of range returns the exact nearest-corner cell; one out of range clamps to the edge row/column and interpolates linearly along the in-range axis. Never a linear extension beyond the grid.
- **Thread discipline**: `CreateInterpolator()` builds one fresh configured instance per sampled realization, sharing the function's arrays (`Bilinear` carries mutable correlated-search state — a shared instance across the parallel realization loop is a data race). Nothing is cached on the function; `Evaluate(x, y)` is the allocating convenience for tests and diagnostics.

The one-argument transform surface throws `NotSupportedException` naming `Evaluate(x, y)` — a fabricated univariate bridge would silently evaluate at a meaningless secondary value, and graph arity plus validation make the throw unreachable in valid models. Composite transforms reject bivariate children.

## v1.1 changes vs. the v1.0 report

- Sampling is index-driven through the pre-allocated percentile matrix (`SetupSampler`; Latin hypercube default).
- Sampling an invalid table throws `InvalidOperationException` (v1.0 returned null).
- `ComputeUncertaintyResults` moved the app-layer uncertainty plotting math into the model library (exact percentile evaluation).

## Composite transform functions

`CompositeTransform` blends candidate transforms — several rating curves with credibility weights —
into a single consensus curve, `Σ ωᵢfᵢ(x)`, riding the Numerics `CompositeFunction` in
`WeightedAverage` mode. It is **new in v1.1**: v1.0 had no composite transform.

**`Average` is the only supported combination.** `Mixture` and `Additive` are validation errors.
Mixture would need per-realization branch selection, and there is no transform analog of the
consequence exposure-branch surface — `SampledFailureMode` chains transforms deterministically — so
a mean-only run would collapse the branch and its loss-exceedance tail would diverge from the mean
of the full-uncertainty ensemble. Additive has no physical reading for a hazard-to-hazard mapping.

The composite's input domain is the **intersection** of its children's, not their union: a weighted
average needs every child evaluable at every input. An empty intersection is an error; differing
domains warn.

Practitioners should know that a weighted average is the aleatory-*mean* reading of a set of
candidate transforms — exact when everything downstream is linear, approximate otherwise — and that
it contributes no spread of its own. [composite-functions.md](composite-functions.md) §4 works
through the Jensen-bias example (an eleven-fold difference in conditional failure probability) and
says when to model alternatives as separate analyses instead. Verification is in
[../verification/composite-transform.md](../verification/composite-transform.md).
