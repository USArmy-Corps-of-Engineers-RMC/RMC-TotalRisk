# System Response Functions

> Technical reference for `RMC.TotalRisk.RiskFunctions.Responses` — `TabularResponse`, `ParametricResponse`, `NonFailResponse`, `CompositeResponse`, and the tree-structured `EventTreeResponse`. Source of the methodology: RMC-TR-2022-XX, [*Quantitative Risk Analysis with RMC-TotalRisk*](https://usace-rmc.github.io/RMC-Software-Documentation/source-documents/desktop-applications/rmc-totalrisk/technical-reference-manual/RMC-TotalRisk-Technical-Reference-Manual.pdf), System Response Functions chapter.

A **system response function** (fragility curve) describes the conditional probability of failure of the system at each hazard level. "Failure" is the general reliability-engineering limit state — the system fails to meet the demand placed on it — not necessarily fracture, breach, or collapse.

## R-S reliability formulation

Engineering risk problems oppose a resistance (capacity) *R* and a load (demand) *S* at hazard level *x*. Failure occurs when the load exceeds the resistance; the annual probability of failure (report Eq. 27–28) is

P(f) = P(R ≤ S) = ∫ F_R(x) · f_S(x) dx

where *F_R* is the conditional CDF of the resistance and *f_S* is the hazard (demand) density. In RMC-TotalRisk the response function **is** the resistance CDF: the system response probability (SRP) at hazard level *x* is P(R ≤ x) = F_R(x) (report Eq. 29). A sampled response is therefore a Numerics `IUnivariateDistribution` whose `CDF(x)` is the failure probability.

**Monotonicity policy:** most responses increase strictly with hazard, but multivariate scenarios exist (e.g., a levee where correlated high tailwater *increases* resistance at extreme river stages), so RMC-TotalRisk does **not** require nonparametric response probabilities to be strictly increasing, nor exhaustive (cumulative 0 → 1). `IsMonotonic()` reports (and validation warns) rather than rejects.

### Tree-structured responses

`EventTreeResponse` and `FaultTreeResponse` are response functions under this exact contract: they produce conditional fragility `P(F|h)`. Hazard functions manage hazard probability/frequency, and the component risk graph plus `RiskAnalysis` connects responses to consequences and computes risk. An event-tree response owns a controlled tree of chance nodes whose sibling branch probabilities come from scalars, aligned uncertain tables, ordinary response functions, or nested event trees; terminal probabilities are conditional path products, and the aggregate failure probability is the compensated sum of the failure-classified terminals, with omitted remainder mass surfaced as a stable implicit non-failure branch. A fault-tree response evaluates exact top-event probability over And/Or/Xor/k-of-n gates with shared-logical and independent-clone repetition through a reduced decision diagram. Full family documentation: [event-trees.md](event-trees.md) and [fault-trees.md](fault-trees.md). The normative [tree-response implementation design](../requirements/EVENT_AND_FAULT_TREE_RESPONSE_DESIGN.md) defines event end-state outputs, exact static fault evaluation, internal/external links, independent clones, controlled authoring operations, graph algorithms, LHS, serialization, hashing, testing, and cost-benefit.

## Contract

```csharp
public interface IResponseFunction : IRiskFunction
{
    ResponseFunctionType FunctionType { get; }                       // runtime discriminator (never serialized/hashed)
    bool SupportsOrderedCurveSampling { get; }                       // can SampleResponseFunction emit a curve?
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

`SupportsOrderedCurveSampling` lets callers discover the ordered-curve capability without using an
exception as feature detection: `ParametricResponse`, `CompositeResponse`, and the `NonFailResponse`
sentinel report false (their `SampleResponseFunction` overloads throw); the tabular and event-tree
responses report true.

## TabularResponse

A tabular relationship of strictly ascending hazard levels and conditional failure probabilities, evaluated by linear interpolation,

```
P(F|x) = pᵢ + (pᵢ₊₁ − pᵢ)·(x − xᵢ)/(xᵢ₊₁ − xᵢ),        xᵢ ≤ x ≤ xᵢ₊₁,
```

Beyond the table the function's `Extrapolation` policy governs; the default holds the end ordinates (the v1.0 behavior [7], bit-identical). Probabilities must lie in [0, 1] (validated per ordinate across each distribution's full range), but need not be ordered or exhaustive.

- `HazardTransform` — optional logarithmic input-axis transform (non-negative hazards).
- `ProbabilityTransform` — optional logarithmic or Normal-Z output-axis transform. **Default `None`** (the hazard cluster defaults Normal-Z; the response cluster deliberately does not).

### Uncertainty

Per-ordinate failure-probability distributions, sampled co-monotonically (report Algorithm 3, one percentile per realization across all ordinates — `UncertainOrderedPairedData.CurveSample(p)`). PERT-Percentile ordinates are coerced to the [0, 1] allowable range during validation. `IsMonotonic()` tests the 10⁻⁵-percentile curve and, when uncertain, also the median and 1 − 10⁻⁵ curves for any decreasing step.

Validation warns when the first ordinate's mean failure probability exceeds 10⁻⁸, worded for the configured policy: under the default hold every hazard below the table carries that probability (which can bias risk at low hazard levels); under a Below-extending policy evaluation follows the extended lower segment; under `Error` it stops the analysis.

### Extrapolation policy

`Extrapolation` — `None` (default), `Below`, `Above`, `Both`, or `Error` — extends the boundary
segments linearly in the configured transform spaces, with the sampled distribution clamping
extended probabilities to [0, 1]; `Error` refuses out-of-range forward (hazard-axis) evaluation
loudly while probability-axis inverse lookups retain the endpoint hold. Serialized by enum name
only when non-default (every existing form, hash, and seed unchanged); a configured policy is
hashed compute content. The raw-curve `SampleResponseFunction()` members are policy-free by
design. The full doctrine lives in the
[hazard-functions chapter](hazard-functions.md#extrapolation-policy). Contrast: the
`BivariateResponse` collapse curve keeps its v1.0 first/last-ordinate `Min/MaxProbability`
semantics, and the joint surface keeps its pinned corner/edge policy — both are excluded from
this per-function policy by design.

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
fragility.SetDistributionParameters(new[] { 10.0, 2.0 });        // LnNormal (base e): real-space mean, sd
fragility.Estimate();

double pf = fragility.SampleFunction().CDF(stage);
UncertaintyAnalysisResults? summary = fragility.ComputeUncertaintyResults(0.90);
```

Note `SetDistributionParameters` on `ParametricResponse` follows the shared parametric surface (`ParentDistribution.SetParameters` with estimate invalidation).

## NonFailResponse

The non-failure sentinel: a `FailureMode` whose response is a `NonFailResponse` is the component's **non-failure branch**. It never fails; its consequence function carries the non-breach (background) consequences the engine subtracts to form incremental risk (see the consequence-functions page). The type is inert — no compute content (every instance hashes identically), curve sampling throws, distribution sampling returns null (the engine never samples it), bounds return zero, `IsMonotonic()` is true.

v1.1 note: v1.0 exposed a `GetInstance()` singleton and identified the non-failure branch by reference equality; v1.1 forbids singletons in the model library, so the type is instantiable and identification is a type test (`ResponseFunction is NonFailResponse`).

## BivariateResponse

A two-way system-response table: primary hazard levels × weighted secondary hazard levels × a probability grid P[i, j], with per-axis interpolation transforms plus a probability transform. The type serves TWO operating modes, selected only by the parent context — it holds no mode state:

- **Collapse mode** (the v1.0 method, fully preserved), under a univariate hazard: the secondary axis collapses through the stored weights, SRP(xᵢ) = Σⱼ P[i, j]·wⱼ, producing an ordinary one-dimensional fragility (interpolated in the primary and probability transform spaces, clamped to its first/last collapse ordinate outside the primary range). Weights are serialized compute content; `EstimateWeights()` re-derives them EXPLICITLY from a stored secondary-hazard link (Voronoi midpoints against the linked hazard's mean CDF, last-bin residual absorption so Σw = 1 exactly) — never as a load or link-set side effect. Weight staleness against the link is a validation Warning. The link itself is provenance metadata: id-first with lenient name fallback, a miss keeps the stored pair verbatim and is never an Error.
- **Joint surface mode**, under a bivariate hazard with a wired secondary input: `SurfaceProbability(x, y)` interpolates the grid bilinearly in the three transform spaces with the interpolator's native edge clamps, then clamps to [0, 1] after back-transform. The engine evaluates the surface at each conditional bin's (x, yⱼ) — see [bivariate-hazards](bivariate-hazards.md) — and the stored weights are compute-inert (though still identity content). Joint mode requires at least two secondary levels and a single response stage.

Monotonicity is advisory on this type: `IsMonotonic()` reports the collapsed curve, and non-monotone rows/columns are validation Warnings (matching the v1.0 posture) — a genuinely non-monotone surface is legal input. The surface is deterministic (D = 0); percentile and index sampling return the mean collapse. The consistency of the two modes — the collapse method and the conditional-bin joint path estimating the same E_Y[P(x, Y)] — is verified by a deliberate cross-anchor in [bivariate-risk](../verification/bivariate-risk.md).

**Choosing the mode is an accuracy decision, not just a modeling-convenience one.** Collapse mode puts the secondary variable on the response's own weighted levels and leaves the primary axis to the engine's exact quadrature; joint mode integrates the secondary through conditional bins. When the secondary variable's frequency curve concentrates its mass where the surface barely varies — the common case for a coincident conditioning variable — the collapse is the *better-conditioned* arrangement: on the verification family's seismic scenario it reproduces the exact answer to 0.241% while the same scenario driven from the other axis carries 35.4% error at the default bin count. Reach for joint mode when the dependence is genuine (a non-independence copula), when both axes vary materially, or when the secondary signal must feed transforms or consequences downstream. See [bivariate-hazards](bivariate-hazards.md) for the measured comparison.

## v1.1 changes vs. the v1.0 report

- The integer sampling overloads take a **realization index** into the pre-allocated percentile matrix (`SetupSampler`, Latin hypercube default); v1.0's `SampleResponseFunction(int)` treated the integer as a PRNG seed.
- Sampling an invalid/un-estimated function throws (v1.0 returned null); out-of-range posterior indices throw (v1.0 returned null); a failed bootstrap propagates (v1.0 swallowed it).
- The parametric full-posterior bounds scan is race-free, and percentile lookup clamps at percentile 1.0 (latent v1.0 defects).
- `ComputeUncertaintyResults` moved the app-layer uncertainty plotting math into the model library.

## Composite response functions

`CompositeResponse` combines a weighted list of child fragilities under one of two rules:

| Combination | Rule | Reading |
|---|---|---|
| **Mixture** (default) | `p(h) = Σ ωᵢ·pᵢ(h)` — a `Numerics.Mixture` | Alternative descriptions of the response; exactly one applies |
| **CompetingRisks** | the **minimum** rule — the weakest link | All mechanisms act; any one can fail the system. Weights are inert. |

The weakest-link rule is the substantive divergence from `CompositeHazard`, which takes the maximum
because the most severe *loading* controls: for a response, any mechanism failing is enough, so the
combination is the union of the child failure events, `1 − ∏(1 − pᵢ(h))` under independence.

The mixture's classic use cases are the composite-response precursor of the combination note [24]:
a response conditional on an uncertain secondary variable (condition on the variable that does
**not** drive consequences, so the consequence function stays one-dimensional) and mutually
exclusive geologic interpretations weighted by elicited likelihood. Sub-scenarios that can be
active simultaneously — multiple erosion initiation points — are *not* exclusive, and the union
(`CompetingRisks`) is then the right combine; sub-scenarios with different consequences are
separate failure modes, never composite children
([failure-mode-combination.md](failure-mode-combination.md) §2).

The mixture is **aleatory** by design, exactly as for the hazard composite — see
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
