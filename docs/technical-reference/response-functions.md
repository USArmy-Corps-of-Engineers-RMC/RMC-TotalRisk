# System Response Functions

> Technical reference for `RMC.TotalRisk.RiskFunctions.Responses` (Phase 2 surface: `TabularResponse`, `ParametricResponse`, `NonFailResponse`). Source of the methodology: RMC-TR-2022-XX, *Quantitative Risk Analysis with RMC-TotalRisk* (docs/reports), System Response Functions chapter.

A **system response function** (fragility curve) describes the conditional probability of failure of the system at each hazard level. "Failure" is the general reliability-engineering limit state — the system fails to meet the demand placed on it — not necessarily fracture, breach, or collapse.

## R-S reliability formulation

Engineering risk problems oppose a resistance (capacity) *R* and a load (demand) *S* at hazard level *x*. Failure occurs when the load exceeds the resistance; the annual probability of failure (report Eq. 27–28) is

P(f) = P(R ≤ S) = ∫ F_R(x) · f_S(x) dx

where *F_R* is the conditional CDF of the resistance and *f_S* is the hazard (demand) density. In RMC-TotalRisk the response function **is** the resistance CDF: the system response probability (SRP) at hazard level *x* is P(R ≤ x) = F_R(x) (report Eq. 29). A sampled response is therefore a Numerics `IUnivariateDistribution` whose `CDF(x)` is the failure probability.

**Monotonicity policy:** most responses increase strictly with hazard, but multivariate scenarios exist (e.g., a levee where correlated high tailwater *increases* resistance at extreme river stages), so RMC-TotalRisk does **not** require nonparametric response probabilities to be strictly increasing, nor exhaustive (cumulative 0 → 1). `IsMonotonic()` reports (and validation warns) rather than rejects.

## Contract

```csharp
public interface IResponseFunction : IRiskFunction
{
    OrderedPairedData SampleResponseFunction();                      // mean curve (hazard vs. Pf)
    OrderedPairedData SampleResponseFunction(double percentile);
    OrderedPairedData SampleResponseFunction(int realizationIndex);
    IUnivariateDistribution SampleFunction();                        // resistance CDF form
    IUnivariateDistribution SampleFunction(double percentile);
    IUnivariateDistribution SampleFunction(int realizationIndex);
    bool IsMonotonic();
    double MinHazard();
    double MaxHazard();
    double MinProbability();
    double MaxProbability();
}
```

## TabularResponse

A tabular relationship of strictly ascending hazard levels and conditional failure probabilities (report Eq. 30), evaluated by linear interpolation with flat extrapolation beyond the table. Probabilities must lie in [0, 1] (validated per ordinate across each distribution's full range), but need not be ordered or exhaustive.

- `HazardTransform` — optional logarithmic input-axis transform (non-negative hazards).
- `ProbabilityTransform` — optional logarithmic or Normal-Z output-axis transform. **Default `None`** (the hazard cluster defaults Normal-Z; the response cluster deliberately does not).

### Uncertainty

Per-ordinate failure-probability distributions, sampled co-monotonically (report Algorithm 3, one percentile per realization across all ordinates — `UncertainOrderedPairedData.CurveSample(p)`). PERT-Percentile ordinates are coerced to the [0, 1] allowable range during validation. `IsMonotonic()` tests the 10⁻⁵-percentile curve and, when uncertain, also the median and 1 − 10⁻⁵ curves for any decreasing step.

Validation warns when the first ordinate's mean failure probability exceeds 10⁻⁸: flat extrapolation makes every hazard below the table carry that probability, which can bias risk at low hazard levels.

### API

```csharp
var fragility = new TabularResponse
{
    Name = "Overtopping Fragility",
    SpecifiedHazard = "Stage",
    HazardUnit = "ft",
    UncertainOrderedPairedData = new UncertainOrderedPairedData(
        ordinates, strictOnX: true, SortOrder.Ascending, strictOnY: false, SortOrder.None,
        UnivariateDistributionType.Triangular),
};

var (isValid, messages) = fragility.Validate();
OrderedPairedData meanCurve = fragility.SampleResponseFunction();
IUnivariateDistribution srp = fragility.SampleFunction();
double pf = srp.CDF(stage);                                      // failure probability at stage
UncertaintyAnalysisResults? summary = fragility.ComputeUncertaintyResults(0.90);
```

## ParametricResponse

A parametric response: a fitted parent distribution whose CDF is the failure probability (v1.0 catalog: Exponential, Gamma, Logistic, Ln-Normal — the default, Log-Normal base 10, Normal, Weibull). Uncertainty is the parametric bootstrap of the hazard chapter (report Algorithm 1) with the response-specific configuration:

- `ProbabilityOrdinates` are **non-exceedance** probabilities (default 23 ordinates, 0.001–0.999) — no inversion, the deliberate difference from the parametric hazard.
- `PRNGSeed` defaults to 67891 (the hazard defaults 12345), keeping paired hazard/response bootstraps independent.
- Estimation-method rejections: product moments for Weibull; linear moments for Logistic, Weibull, Triangular, and PERT.

Sampling follows report Algorithm 2: `SampleFunction(int realizationIndex)` looks up the posterior directly (D = 0); `SampleFunction(double p)` uses the clamped ⌊p·N⌋ index; `SampleFunction()` returns the posterior mean curve (no reversal — non-exceedance ordinates) as an `EmpiricalDistribution` with a Normal-Z probability transform. `SampleResponseFunction*` throws — parametric responses do not emit ordered-pair curve samples (v1.0 behavior). `IsMonotonic()` is always true (a parametric CDF is monotonic by construction).

`Estimate(IList<ParameterSet>)` imports an externally fitted fragility posterior (same contract as the parametric hazard — the UI importer passes Numerics artifacts).

### API

```csharp
var fragility = new ParametricResponse
{
    Name = "Internal Erosion Fragility",
    SpecifiedHazard = "Stage",
    HazardUnit = "ft",
    EffectiveRecordLength = 30,
};
fragility.SetDistributionParameters(new[] { 10.0, 2.0 });        // LnNormal μ, σ (base e)
fragility.Estimate();

double pf = fragility.SampleFunction().CDF(stage);
UncertaintyAnalysisResults? summary = fragility.ComputeUncertaintyResults(0.90);
```

Note `SetDistributionParameters` on `ParametricResponse` follows the shared parametric surface (`ParentDistribution.SetParameters` with estimate invalidation).

## NonFailResponse

The non-failure sentinel: a `FailureMode` whose response is a `NonFailResponse` is the component's **non-failure branch**. It never fails; its consequence function carries the non-breach (background) consequences the engine subtracts to form incremental risk (see the consequence-functions page). The type is inert — no compute content (every instance hashes identically), curve sampling throws, distribution sampling returns null (the engine never samples it), bounds return zero, `IsMonotonic()` is true.

v1.1 note: v1.0 exposed a `GetInstance()` singleton and identified the non-failure branch by reference equality; v1.1 forbids singletons in the model library, so the type is instantiable and identification is a type test (`ResponseFunction is NonFailResponse`).

## v1.1 changes vs. the v1.0 report

- The integer sampling overloads take a **realization index** into the pre-allocated percentile matrix (`SetupSampler`, Latin hypercube default); v1.0's `SampleResponseFunction(int)` treated the integer as a PRNG seed.
- Sampling an invalid/un-estimated function throws (v1.0 returned null); out-of-range posterior indices throw (v1.0 returned null); a failed bootstrap propagates (v1.0 swallowed it).
- The parametric full-posterior bounds scan is race-free, and percentile lookup clamps at percentile 1.0 (latent v1.0 defects).
- `ComputeUncertaintyResults` moved the app-layer uncertainty plotting math into the model library.

## Composite response functions (Phase 9, landed 2026-07-25)

`CompositeResponse` combines a weighted list of child fragilities under one of two rules:

| Combination | Rule | Reading |
|---|---|---|
| **Mixture** (default) | `p(h) = Σ ωᵢ·pᵢ(h)` — a `Numerics.Mixture` | Alternative descriptions of the response; exactly one applies |
| **CompetingRisks** | the **minimum** rule — the weakest link | All mechanisms act; any one can fail the system. Weights are inert. |

The weakest-link rule is the substantive divergence from `CompositeHazard`, which takes the maximum
because the most severe *loading* controls: for a response, any mechanism failing is enough, so the
combination is the union of the child failure events, `1 − ∏(1 − pᵢ(h))` under independence.

The mixture is **aleatory** (ratified Q-Y), exactly as for the hazard composite — see
[composite-functions.md](composite-functions.md); verification is in
[../verification/composite-response.md](../verification/composite-response.md).

`SampleResponseFunction()` and its overloads **throw**, matching v1.0 and the two sibling response
types. The engine consumes the distribution form exclusively, and a union-knot re-tabulation would
agree with the true combined curve only *at* the knots under the weakest-link rule, where the
combination is nonlinear in the children — that would create a second, subtly wrong response
surface. `IsMonotonic()` is a theorem rather than a probe: a convex combination of non-decreasing
`pᵢ` is non-decreasing, and `1 − ∏(1 − pᵢ)` is non-decreasing in each `pᵢ`, so monotone children
imply a monotone combination under both rules. A `NonFailResponse` child is a validation error — it
emits no distribution to combine.
