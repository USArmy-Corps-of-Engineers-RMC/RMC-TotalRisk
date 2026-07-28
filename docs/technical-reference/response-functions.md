# System Response Functions

> Technical reference for `RMC.TotalRisk.RiskFunctions.Responses` (Phase 2 surface: `TabularResponse`, `ParametricResponse`, `NonFailResponse`). Source of the methodology: RMC-TR-2022-XX, *Quantitative Risk Analysis with RMC-TotalRisk* (docs/reports), System Response Functions chapter.

A **system response function** (fragility curve) describes the conditional probability of failure of the system at each hazard level. "Failure" is the general reliability-engineering limit state — the system fails to meet the demand placed on it — not necessarily fracture, breach, or collapse.

## R-S reliability formulation

Engineering risk problems oppose a resistance (capacity) *R* and a load (demand) *S* at hazard level *x*. Failure occurs when the load exceeds the resistance; the annual probability of failure (report Eq. 27–28) is

P(f) = P(R ≤ S) = ∫ F_R(x) · f_S(x) dx

where *F_R* is the conditional CDF of the resistance and *f_S* is the hazard (demand) density. In RMC-TotalRisk the response function **is** the resistance CDF: the system response probability (SRP) at hazard level *x* is P(R ≤ x) = F_R(x) (report Eq. 29). A sampled response is therefore a Numerics `IUnivariateDistribution` whose `CDF(x)` is the failure probability.

**Monotonicity policy:** most responses increase strictly with hazard, but multivariate scenarios exist (e.g., a levee where correlated high tailwater *increases* resistance at extreme river stages), so RMC-TotalRisk does **not** require nonparametric response probabilities to be strictly increasing, nor exhaustive (cumulative 0 → 1). `IsMonotonic()` reports (and validation warns) rather than rejects.

### EventTreeResponse and planned tree-response specializations

`EventTreeResponse` (Phase 10A) and `FaultTreeResponse` (Phase 10B) remain response functions under this exact contract: they produce conditional fragility `P(F|h)`. Hazard functions manage hazard probability/frequency, and the component risk graph plus `RiskAnalysis` connects responses to consequences and computes risk. The normative [tree-response implementation design](../requirements/EVENT_AND_FAULT_TREE_RESPONSE_DESIGN.md) defines event end-state outputs, exact static fault evaluation, internal/external links, independent clones, controlled authoring operations, graph algorithms, LHS, serialization, hashing, testing, and cost-benefit.

The first five coherent `EventTreeResponse` slices landed 2026-07-28. They include:

- the common immutable branch descriptors/sample contract and tree-reference value object;
- controlled `EventTree` ownership with add, insert, move, delete, search, ancestry,
  reachability, pre/post-order DFS, breadth-first traversal, leaves, structural equality, and
  subtree hashes;
- immutable `TreeFragment` subtree snapshots with fresh persistent IDs on paste and remapped
  fragment-local links, while cross-tree references retain live external targets;
- transactional paste, replace, materialize, `TreeDeletePolicy.MaterializeLinks`, and unreachable
  pruning, with topology, output-port allocation, IDs, canonical hash, and configured samplers
  restored exactly when an operation fails;
- deterministic expanded topological order plus unreachable/internal/external reference queries;
- deterministic scalar, aligned uncertain-tabular, and ordinary response-function probability
  sources;
- mean, co-monotonic percentile, and indexed LHS branch/aggregate samples;
- the legacy sibling normalization, remainder, path-product, and failure-terminal sum rules;
- explicit node/edge XML with self-contained and by-reference nested function modes; and
- projected canonical identity independent of display metadata, persistent IDs, and sibling
  presentation order;
- internal and external `EventTreeLinkNode` references with `IndependentClone` occurrence
  semantics, including separate epistemic sampler occurrences when one live source is reused;
- resolver-backed self-contained and by-reference XML, with lenient function/node name fallback
  and repaired IDs on the next write;
- a link-expanded immutable occurrence plan used by evaluation, sampling, identity, and validation;
  and
- transactional same-tree and cross-function cycle rejection with the complete function/node path
  in the diagnostic.
- direct and multi-level `EventTreeResponse` probability sources evaluated at each caller hazard
  ordinate through the established response-CDF interpolation/extrapolation contract;
- recursive sampler-dimension discovery through nested event trees, ordinary responses, aligned
  uncertain tables, and internal/external independent-clone links;
- isolated deterministic epistemic streams for every canonical nested occurrence, including
  repeated references to the same live response, with indexed, co-monotonic percentile, and LHS
  sampling preserved through arbitrary acyclic nesting depth; and
- transactional recursive compilation/setup plus direct, indirect, mixed source/link, and
  cross-function cycle diagnostics carrying the complete deterministic function/node path;
- an import-only legacy adapter for recursive v1.0 `Node` roots, direct or inside the released
  `EventTreeResponse`/`EventTree` envelopes, including both hazard-attribute and GUID spellings,
  scalar/table/name-only-response probability sources, and the legacy automatic remainder; and
- current-only writes plus deterministic path diagnostics for malformed, excluded, and semantically
  ambiguous legacy input.

The conversion authority is the partial C#
`RMC.TotalRisk.IO/Project/Elements/Response Function/EventTreeResponse.cs`, the released VB
`RMC.TotalRisk/Project/Elements/Response Function/EventTreeResponse.vb` and their event-node
types, and the shipped `RMC-TotalRisk/Resources/TreeTemplates.xml` in the Dev repository. The
committed verification fixtures reproduce the shipped `Basic` and `Concrete Dam Gate Failure`
roots exactly. Name-only legacy `ResponseFunction` sources pass through the existing resolver, so
successful by-reference writes repair stable IDs. Legacy `EventNode` probability references are
not structural subtree links and cannot be mapped faithfully to Phase 10A `IndependentClone`
semantics; they therefore fail explicitly. The commented `SecondaryHazardNode` remains excluded,
and `WeightedHazardLevel` remains Phase 11 bivariate-response work.

For explicit siblings with raw probabilities `q_i` and compensated sum `S`, the conditional rule is:

```
p_i = q_i and p_remainder = 1-S, when S <= 1
p_i = q_i/S and p_remainder = 0, when S > 1
```

A terminal probability is the product of its conditional path. Aggregate `P(F|h)` is the
compensated sum of terminals explicitly classified as failure; omitted remainder mass is surfaced
as a stable implicit non-failure branch.
Referenced responses, including recursively nested event trees, are evaluated at the owning
response's current hazard ordinate through `IResponseFunction.SampleFunction(...).CDF(h)`.
That preserves the existing response interpolation and extrapolation policy; this slice introduces
no alternate interpolator. Setup creates an isolated self-contained sampler occurrence for every
referenced source, derives its seed from the established source-identity/occurrence recipe, and
copies the exact child percentile columns into the owner's flattened sampler. A failed recursive
compile, clone, capacity check, or child setup restores the owner's prior sample size, percentile
matrix, sampler identity, and occurrence bindings exactly.

Nested response content participates in projected canonical identity. Link wrappers distinguish
referenced occurrences from authored inline content, while persistent IDs, display names, sibling
authoring order, serialization mode, and reference wrappers remain projected away. `TreeFragment`
is authoring-only state: it is neither a persistence type nor a hash input, and paste-cloned
subtrees therefore preserve projected canonical identity. Still open in Phase 10A are expanded
per-leaf graph output ports and stale-connection policy; immutable compiled-plan
caching/invalidation; large-tree performance; property-based testing; branch-routing Monte Carlo;
graph-connected per-leaf consequence verification; aggregate LHS variance reduction; and the
remaining thread-count, coverage, and performance exit gates.


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
