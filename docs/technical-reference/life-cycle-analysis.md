# Life-Cycle Analysis

Time enters the risk model in two places: a response that weakens with age, and an evaluation
that walks a system through the years of a planning horizon. This page documents both — the
age-indexed deteriorating response function, and the epoch-sequence life-cycle query that
drives it through a schedule of interventions. The annual risk integral itself is untouched: a
deteriorating response evaluated at a fixed age is an ordinary response function, each epoch
of a life-cycle evaluation is an ordinary mean-only quantification, and every capability of
the engine composes with them unchanged.

## The capacity-shift deterioration law

A deteriorating response wraps a base response (fragility) function together with an owned
tabular deterioration law mapping age in years to a capacity-axis shift Δ(t):

$$P_f(h, t) = F_{\text{base}}\big(h + \Delta(t)\big)$$

A **positive shift weakens** the wrapped response: the capacity that resists the hazard moves
down, the fragility curve moves left, and the same hazard level fails more often. A decreasing
law (repair or strengthening) is legal by the same convention with negative movement.

The law is an `UncertainOrderedPairedData` with strictly ascending non-negative ages and a
shift distribution per ordinate:

- **Interpolation** is linear in age; outside the tabled age range the boundary ordinate holds
  (a single-ordinate law is a constant shift).
- **Uncertainty** follows the house co-monotonic convention: one percentile drives every law
  ordinate — and the same percentile drives the base when a single-percentile sample is
  requested. A realization draws the law's percentile from the wrapper's own one-column
  percentile matrix.
- A law whose mean shift at age zero is non-zero draws a validation warning, because the
  age-zero response then differs from the base.

The tabular law subsumes the constant, linear, and step shapes practitioners reach for first;
parametric trend laws (mirroring the nonstationary trend-function vocabulary of RMC-BestFit)
are a recorded extension.

## The evaluation age is external state

`EvaluationAge` is runtime-only: **never serialized, never part of the canonical hash, and
never an influence on sampling seeds**. One authored function with one content-seeded stream
evaluates at many ages, which is the property real-options reasoning needs — epistemic
realization *i* means the same state of knowledge in year 0 and year 50, so trajectories are
coherent realization for realization.

- The ordinary `IResponseFunction` members evaluate at the current `EvaluationAge` (zero by
  default — the undegraded base). The explicit-age `SampleFunctionAtAge` overloads are pure
  and leave the property untouched.
- A run's isolated component snapshot carries the configured age onto its cloned instances by
  function id, so the age set on the authored wrapper governs the run it precedes. A
  serialization round trip alone resets the age to zero — external state does not persist —
  and a graph-element clone behaves the same way.
- The age must not be mutated while a run is sampling the function.

## Sampling and identity contracts

- **Dimensions.** The wrapper consumes one knowledge dimension (the law's percentile column).
  The base draws from its own child stream, re-seeded with
  `HashCombine(seed, base.CanonicalHash(), 0)` — the composite forward rule, with ordinals 1
  and above reserved — so renaming the base never changes results while editing its content
  always re-rolls its stream.
- **Sampled products.** Every sampled product is the base's product behind a shifted-axis
  view: `CDF(x) = base.CDF(x + Δ)`, `InverseCDF(p) = base.InverseCDF(p) − Δ`. A zero shift
  reproduces the base bit-for-bit.
- **Identity.** The canonical hash is a projected identity form: the law's serialized content
  plus the base's own canonical hash as a token attribute. The serialization mode and every
  metadata edit (ids, names, labels — the base's included) are inert; any law or base content
  edit moves the hash and re-rolls seeds.
- **Serialization.** The base persists through the shared function-entry contract in both
  serialization modes; an unresolved reference marker is re-written verbatim on save so
  resolver-less round trips stay bit-equal.

## Seat restrictions

The wrapper is legal as **the single response stage of an ordinary failure mode under a
univariate component hazard** — the seat a life-cycle evaluation drives. Every other seat
refuses it at validation, because it would silently evaluate the age-zero response with no age
surface to drive:

| Seat | Rule |
|---|---|
| Single response stage, univariate parent | Legal |
| Composite response child | Error (validation and the sampling gate) |
| Tree probability source | Error |
| Multi-stage response chain | Error per offending stage |
| Under a bivariate component hazard | Error |

The wrapped base is restricted to `TabularResponse` and estimated `ParametricResponse`.
Composite, tree, bivariate, non-failure, and nested deteriorating bases are refused: a
composite hidden inside a wrapper would escape the epistemic discovery walks (shared-variable
and logic-tree axis collection), and the other kinds carry no meaningful capacity axis to
shift. Widening the base allow-list or the seat matrix is a recorded extension that must
extend those walks in the same change.

## The transform-equivalence identity

A deteriorating response at age *t* is exactly its base behind a deterministic unit-slope
`LinearTransform` with intercept Δ(t) and bounds spanning the domain: with slope one the
transform computes Δ + h and the wrapper computes h + Δ, bit-identical in IEEE arithmetic.
The identity anchors the verification family at function level and behind the full engine
(where the re-authored twin binds its consequences to the raw hazard — position 0 — because
the shift is internal to the response path). See
[docs/verification/life-cycle.md](../verification/life-cycle.md).

## API

```csharp
var wrapper = new DeterioratingResponse
{
    Name = "Aging embankment fragility",
    SpecifiedHazard = "Stage",
    HazardUnit = "ft",
    BaseResponse = fragility,                       // TabularResponse or estimated ParametricResponse
    DeteriorationLaw = new UncertainOrderedPairedData(
        new[]
        {
            new UncertainOrdinate(0d, new Deterministic(0d)),
            new UncertainOrdinate(50d, new Deterministic(2d)),   // 2 ft of capacity lost by year 50
        },
        true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
};

wrapper.EvaluationAge = 30d;                        // external state; governs the run it precedes
var pAt30 = wrapper.SampleFunction().CDF(stage);    // mean P_f at the current age
var pure = wrapper.SampleFunctionAtAge(0.5d, 30d);  // explicit-age overloads never touch the property
```

`DeterioratingResponse` registers as `ResponseFunctionType.Deteriorating` and reconstructs
through `RiskFunctionFactory` like every concrete function type.

## The epoch-sequence life-cycle query

`RiskAnalysis.MeasureLifeCycleRisk(definition)` evaluates "fix failure mode A now, replace the
hazard in year 20" as a trajectory over one authored system. The definition — a planning
horizon, a discount rate, optional evaluation years, and an ordered intervention schedule — is
runtime-only input; the result is a runtime-only trajectory. Epoch boundaries are the sorted
distinct union of year zero, the evaluation years, and the intervention years; each epoch runs
the ordinary engine mean-only on throwaway self-contained clones carrying:

1. the **cumulative house-event states** (last-wins across years — re-exercise and reversal
   are legal; the same containment walk as the configuration-risk query, so fault-tree house
   events are reached through element assignments, external transfers, tree-referenced
   sources, and composite children);
2. the **hazard replacements in service** — each hazard element whose live function carries a
   target id is reassigned a factory clone of the already-authored replacement (the seat a
   nonstationary per-epoch hazard fit occupies; same arity only; chained replacements target
   the function in service, so A→B in year 5 followed by B→C in year 20 composes);
3. every deteriorating response's **evaluation age = the epoch's start year** (stepwise-
   constant deterioration; the run snapshot carries the age onto its clones).

Aggregates follow the exposure-period conversions' exact expression shapes, so the stationary
case reproduces `MeasureExposurePeriodRisk`: P(≥ 1 failure by T) accumulates in log space,
present values discount by exact per-epoch annuity segments, and the equivalent-annual amounts
divide by the horizon annuity. The absorbing variants weight each year by the survival of
every earlier year. Unreachable targets, arity mismatches, and failed epoch quantifications
refuse loudly, wrapped with the epoch's start year. Full conventions:
[uncertainty-analysis.md](uncertainty-analysis.md) §6.7; the result surface:
[results-catalog.md](results-catalog.md).

```csharp
var trajectory = analysis.MeasureLifeCycleRisk(new LifeCycleDefinition(
    periodYears: 50,
    discountRate: 0.025d,
    evaluationYears: new[] { 10, 30 },                     // extra epochs refine the aging curve
    interventions: new[]
    {
        new LifeCycleIntervention(0,                        // fix failure mode A now
            new[] { new HouseEventState(spillwayTree.Id, gateOutId, false) }),
        new LifeCycleIntervention(20, null,                 // the year-20 hazard in service
            new[] { new HazardReplacement(stageFrequency.Id, futureStageFrequency) }),
    }));

double pByHorizon = trajectory.FailureProbabilityByHorizon;
double annualized = trajectory.EquivalentAnnualConsequences[0];
foreach (var epoch in trajectory.Epochs)
{
    // epoch.StartYear, epoch.System.FailureProbability, epoch.CumulativeFailureProbability…
}
```

Intervention entries are unconditional exercise plans, so alternatives compare as
deterministic schedules; the condition member gating exercise on observed state (the
decision-rule reading, least-squares Monte Carlo and its relatives) is the named extension
seat on `LifeCycleIntervention`.

## Related chapters

- [response-functions.md](response-functions.md) — the response cluster the wrapper extends.
- [hashing-and-seeding.md](hashing-and-seeding.md) — the projected identity and child-stream
  seeding conventions the wrapper follows.
- [uncertainty-analysis.md](uncertainty-analysis.md) §6.5 — the exposure-period conversions
  whose stationarity caveat this capability answers.
