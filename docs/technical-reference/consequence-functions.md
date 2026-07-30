# Consequence Functions

> Technical reference for `RMC.TotalRisk.RiskFunctions.Consequences` (`TabularConsequence`, `ParametricConsequence`, `CompositeConsequence` + `WeightedConsequenceFunction`). Source of the methodology: RMC-TR-2022-XX, [*Quantitative Risk Analysis with RMC-TotalRisk*](https://usace-rmc.github.io/RMC-Software-Documentation/source-documents/desktop-applications/rmc-totalrisk/technical-reference-manual/RMC-TotalRisk-Technical-Reference-Manual.pdf), Consequence Functions chapter; the parametric form follows USACE depth-damage conventions and the depth-damage literature [9]–[14].

A **consequence function** (damage function) describes the consequences of failure or non-failure — life loss, economic damages — at each hazard level.

## Contract

```csharp
public interface IConsequenceFunction : IRiskFunction
{
    ConsequenceFunctionType FunctionType { get; }               // runtime discriminator (never serialized/hashed)
    string SpecifiedConsequence { get; set; }
    string ConsequenceUnit { get; set; }
    IUnivariateFunction SampleFunction();                       // mean consequence function
    IUnivariateFunction SampleFunction(double percentile);      // co-monotonic percentile function
    IUnivariateFunction SampleFunction(int realizationIndex);   // pre-allocated sampler row
    IReadOnlyList<(double Weight, IUnivariateFunction Function)> SampleExposureBranches();
    IReadOnlyList<(double Weight, IUnivariateFunction Function)> SampleExposureBranches(double percentile);
    int CountExposureBranches();
    double MinHazard();
    double MaxHazard();
}
```

A sampled consequence function is a Numerics `IUnivariateFunction`: `Function(x)` maps hazard to consequence magnitude.

The exposure-branch surface exists because mixture weights are **aleatory exposure probabilities**
(which day/night state occurs), not knowledge uncertainty: the risk engine enumerates the weighted
branches at every hazard point — in the mean-only and full Monte Carlo paths alike — instead of
collapsing a mixture to its weighted-mean curve, which would keep the mean but destroy the
loss-exceedance tail (the v1.0 day/night defect). Non-composite functions (and additive/average
composites, which are genuine pointwise combinations rather than exposure states) return a single
unit-weight branch; a mixture composite returns one entry per positively weighted child, nested
mixtures flattened by multiplied weights. The percentile overload samples every branch
co-monotonically at the given percentile. `CountExposureBranches()` feeds the engine's
branch-explosion guardrails (warn above 64 combined branches per failure mode, error above 1024).

## TabularConsequence

A tabular relationship of strictly ascending hazard levels and consequence values, evaluated by linear interpolation (report Eq. 45) with flat extrapolation beyond the table. Consequence values need not be ordered. Optional logarithmic transforms on either axis (`HazardTransform`, `ConsequenceTransform`) improve interpolation accuracy; a logarithmic consequence axis requires a non-negative consequence range.

### Uncertainty

Each ordinate's consequence can carry a distribution (the tabular catalog; PERT-Percentile ordinates are coerced to a minimum allowable value of zero during validation). Sampling is co-monotonic (report Algorithm 3): one percentile per realization drives every ordinate; `SamplingDimensions = 1`.

**Negative-consequence clamp:** sampled consequence functions never return negative values — the sampled `TabularFunction` sets `AllowNegativeYValues = false`, so any negative sampled consequence evaluates to zero. Validation warns when any ordinate's sampled range can drop below −10⁻⁵ ("negative consequence values will be set to zero"), and warns when the first ordinate's mean consequence is non-zero (flat extrapolation makes every hazard below the table carry it).

### Incremental consequences and fail/non-fail coupling

Incremental consequences are the losses failure inflicts **over and above** what would occur without failure for the same hazard event (report Eq. 46):

ΔC(x) = C_fail(x) − C_nonfail(x)

In the Monte Carlo simulation, each failure mode samples its failure and non-failure consequence functions with the **same draw** — perfectly correlated — so the incremental difference is coherent per hazard event (the shared failure/non-failure coupling draw, v1.0 `SampledFailureMode` behavior preserved by the v1.1 sampler design). Negative incremental consequences computed during simulation are set to zero with a warning.

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

## ParametricConsequence

A closed-form power model of the hazard (new in v1.1 — no v1.0 ancestor):

> C(h) = clamp(α · max(h − h₀, 0)^β, 0, U)

- **α** (scale) and **β** (exponent) — positive, finite. The power law is the best-fit parametric form for observed depth-damage data in the built environment (R² 0.68–0.78 on post-flood field surveys [9]).
- **h₀** (damage-initiation threshold) — the hazard at and below which the consequence is exactly zero; the initiation point materially changes risk estimates [10].
- **U** (saturation cap) — the maximum consequence; positive infinity means no cap. Bounded damage at saturation matches practice from the JRC global depth-damage functions [13] and bounded S-curve calibrations on NFIP claims [11], [12].

USACE practice remains tabular percent-damage with per-ordinate uncertainty (EGM 01-03/04-01, HEC-FDA [14]) — the parametric form complements `TabularConsequence` where a closed-form curve is fitted or assumed.

### Uncertainty

When `IsUncertain`, each realization draws independent standard normal deviates Z₁, Z₂ from two sampler dimensions and realizes α_i = α·e^{σ_α·Z₁}, β_i = β·e^{σ_β·Z₂} — multiplicative log-space scatter that preserves coefficient positivity (`SamplingDimensions = 2`). `SampleFunction()` returns the **nominal (median) curve**, not the analytic mean: with exponent scatter and no cap, E[s^{β_i}] is the lognormal moment-generating function at ln s, which diverges for s > 1 — only a finite cap bounds the mean, and `Validate()` warns about the unbounded configuration. `SampleFunction(double p)` is co-monotonic (one deviate drives both coefficients); at the unit offset (h = h₀ + 1) the exponent drops out and C is exactly lognormal in σ_α. Sigma attributes serialize only while `IsUncertain` (hash-recipe literal).

`ComputeUncertaintyResults(w)` runs a deterministic content-seeded internal Monte Carlo (10,000 median-LHS realizations) over `UncertaintySummaryHazards()` — the threshold plus 100 log-spaced offsets in [10⁻², 10²] capped at the saturation crossing.

## CompositeConsequence

Combines a weighted list of child consequence functions (`WeightedConsequenceFunction` entries — live references to stored functions, the BestFit `CompositeAnalysis` pattern) with three methods (`CompositeFunctionType`, default Mixture):

| Method | Per-realization combination | Mean curve | Use case |
|---|---|---|---|
| **Additive** | Σ Cᵢ (weights ignored) | Σ fᵢ | Sector damages (properties, industry, agriculture) aggregated to a total (report Eq. 47) |
| **Average** | Σ wᵢ·Cᵢ, Σw = 1, children drawn independently | Σ wᵢ·fᵢ | Historic day/night exposure practice |
| **Mixture** | One child per realization with probability wᵢ | Σ wᵢ·fᵢ (a mixture's mean is the weighted average of component means) | **Day/night exposure** — two children with weight = P(day) (0.42/0.58 typical); fully captures scenario uncertainty |

Average and Mixture share the same mean but not the same variance: Σw²σ² (Average) vs Σw(σ² + μ²) − (Σwμ)² (Mixture, always at least as large). For the verification scenario the mixture σ is nearly 17× the average σ — treating day/night as a weighted average understates consequence uncertainty. There is no dedicated day/night type; a two-child Mixture composite is the model.

### Sampling

Each child owns its sampler, seeded content-derived per [`MODEL_LIBRARY_ARCHITECTURE.md`](../requirements/MODEL_LIBRARY_ARCHITECTURE.md) §5.8.5 — `HashCombine(seed, child.CanonicalHash(), ordinal)` — so identical-content siblings draw independently and child renames can never move results. In Mixture mode the composite owns one selector dimension (`SamplingDimensions = 1`; otherwise 0). `SampleFunction(double p)` is RNG-free: Additive/Average sample every child co-monotonically at p; Mixture uses single-uniform composition sampling — p selects the cumulative-weight bucket and the child is sampled at the rescaled remainder (p − C_{k−1})/w_k, reproducing the exact mixture ensemble deterministically. Combined consequences clamp at zero. Nested composites are allowed (e.g., Mixture(day, night) over Additive sector sums); circular references are validation errors.

### Serialization and hashing

Under `SelfContained` (the storeless default: headless callers, oracles, failure-mode projection XML) child content serializes inline. Under `ByReference` (the stored form) each entry carries only its weight and a `FunctionReference` marker — **a store never duplicates child function content** — and an `IRiskFunctionResolver` reattaches the live stored instances on load (unresolvable references keep their weighted entries and surface through `Validate()`). `CanonicalHash()` hashes a projected identity form (mode, entry count, effective weights, child content hashes) rather than the persisted form, so the serialization mode, child metadata, and Additive-mode weight edits can never move the hash — the second instance of the `SystemComponent` identity-form exception.

## Later family members

- **LifeSimConsequence** (future work) — a tabular consequence built from imported LifeSim Monte Carlo results: per hazard level the user selects an alternative/time-of-day result set, and a distribution (truncated normal by default) is auto-fit to all iterations (fit methods per the report: moments for Deterministic/Triangular/Normal/Ln-Normal/Truncated Normal, percentiles for PERT). A LifeSim day result and night result wrapped in a Mixture composite is the intended day/night import path.

## v1.1 changes vs. the v1.0 report

- Sampling is index-driven through the pre-allocated percentile matrix (`SetupSampler`; Latin hypercube default).
- Sampling an invalid table throws `InvalidOperationException` (v1.0 returned null).
- `ComputeUncertaintyResults` moved the app-layer uncertainty plotting math into the model library (exact percentile evaluation).
- `ParametricConsequence` is new (v1.0 had no parametric consequence).
- The composite's legacy percentile-reseeded `Random` sampling is replaced by the deterministic sampler contract; the per-realization combine is pointwise-exact (no union-grid tabular snapshot); the legacy child-level transform overrides are dropped (children own their interpolation transforms); a Mixture over multiple reachable branches reports `IsDeterministic = false` even with deterministic children (branch picks are real variability); and an empty composite throws from `MinHazard()`/`MaxHazard()` instead of returning the legacy sentinels.
