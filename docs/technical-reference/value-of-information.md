# Value of Information

Every quantitative risk study ends with the same two questions: *is the risk tolerable*, and
*if we are not sure, which study would settle it*. The tolerable-risk confidence surface
answers the first — `P(mean risk > guideline)` across the epistemic ensemble. This chapter is
the second answer: **candidate studies ranked by the epistemic uncertainty they could
resolve, in the measure's own units, with the movement of the confidence statement each study
could produce.** The intent is that a table like the one below appears in routine risk
reporting, next to the tornado chart, so uncertainty-reduction investments are argued in
dollars and lives rather than in correlation coefficients.

## 1. The deliverable

A value-of-information query returns the material for a table of this shape (illustrative
numbers):

| Rank | Candidate study (resolves) | Epistemic uncertainty resolvable | Share of total | Movement of "P(mean risk > 1×10⁻³)" |
|---|---|---|---|---|
| 1 | Rating curve — *Dam / Transform* | $184,000/yr | 62% | ±0.24 expected |
| 2 | Overtopping fragility — *Dam / Response* | $61,000/yr | 21% | ±0.09 expected |
| 3 | Stage–damage — *Dam / Consequence* | $28,000/yr | 9% | ±0.04 expected |
| — | **Perfect information** (all studies) | **$241,000/yr** | 100% | resolves the finding (±0.41) |

Read the first row as: *resolving the rating-curve uncertainty would remove about 62% of
today's epistemic variance in expected annual damages — roughly $184,000/yr of standard
deviation — and would be expected to move the tolerable-risk confidence statement by ±0.24.*
The last row is the perfect-information ceiling: the total epistemic uncertainty, all of which
is removable in principle because a realization's risk measure is a deterministic function of
its knowledge draws.

Each *candidate study* is a model function — a hazard curve, a fragility, a stage–damage
relationship — because that is the unit a study actually resolves. Functions that sample more
than one knowledge dimension list their per-dimension detail beneath the rollup.

## 2. What the numbers are

Let Y be a stored scalar risk measure (any member of the measure catalog, on any risk-type
stream and consequence type) and let θᵢ be one knowledge input — one sampled percentile
column of the uncertainty analysis. Across the stored ensemble, with realization weights wₖ
(unit weights when none are assigned):

**Resolvable variance (the consequence framing).** The epistemic variance of Y attributable
to θᵢ is the variance of the conditional mean,

```
Vᵢ = Var[ E(Y | θᵢ) ],
```

reported alongside its square root √Vᵢ — the resolvable uncertainty in the measure's own
units — and its share Vᵢ / Var(Y). By the law of total variance, Var(Y) = Vᵢ + E[Var(Y | θᵢ)]:
resolving θᵢ removes exactly Vᵢ from the epistemic variance on average. Because the outer
loop's knowledge draws are independent by construction, the Vᵢ are the first-order main
effects of a Sobol decomposition, expressed in output units rather than as ratios.

**The perfect-information row.** Conditioning on *all* inputs leaves nothing: Y is
deterministic given the full knowledge vector, so the ceiling is the total weighted variance
Var(Y) — no estimator involved, and the row every ranked study is compared against.

**Guideline movement (the decision framing).** For each configured tolerable-risk criterion
with published confidence p = P(Y_c > threshold), the expected absolute movement of the
statement under perfect information about θᵢ is

```
Mᵢ = E | P(Y_c > threshold | θᵢ) − p |,
```

with the perfect-information ceiling 2·p·(1−p) — full resolution collapses every
realization's exceedance indicator to 0 or 1, and the expected absolute movement of a
Bernoulli probability under its own resolution is exactly twice its variance. A study with a
large Mᵢ is one that could genuinely change the finding; a study with Mᵢ ≈ 0 refines a number
the decision does not turn on.

**What is deliberately absent: an action model.** Strict decision-theoretic EVPPI prices
information against an explicit act/do-not-act choice with costs. This surface stops one step
earlier — resolvable uncertainty and confidence movement — because the action model (study
costs, remediation alternatives, net value) belongs to the cost–benefit layer, which converts
these quantities into expected value against a decision once alternatives exist.

## 3. The given-data estimator

All quantities are computed from the **stored ensemble** — the same "condition on what the
run already produced" pattern the sensitivity engine uses — with no re-simulation:

1. Pair the input column with the stored per-realization measures, dropping pairs where
   either is NaN.
2. Sort by the input value (stable; the realization index breaks ties).
3. Partition into B = 20 **equal-weight bins**: walking the sorted order, a bin closes at the
   first realization that carries the cumulative weight past k·W/B; the last bin absorbs the
   residual. At unit weights this is the classical equal-frequency partition; under
   realization weights it generalizes it exactly (integer weights reproduce the replicated
   sample bit-for-bit).
4. The between-bin variance of the weighted conditional means estimates Vᵢ; the weighted
   per-bin strict-exceedance fractions against the overall fraction estimate Mᵢ. The
   partition is exact, so the total weighted variance decomposes exactly into the between-bin
   and within-bin parts — a share can never exceed one by more than rounding.

Two bias terms bound the estimates, mirrored from the given-data global-sensitivity
literature: a **discretization bias** of order 1/B² (the conditional mean varies inside a
bin, so the binned Vᵢ slightly understates the continuous one) and a **noise bias** of order
B/n (each bin mean carries sampling error, which inflates the between-bin variance). At the
default 1,000 realizations and 20 bins — 50 realizations per bin — both are small against the
quantities practitioners rank on; an input with fewer valid pairs than bins reports NaN
rather than a noise-dominated number.

**Function rollups.** A study's resolvable variance is the sum of its member columns' main
effects. Because the knowledge draws are independent, the sum equals the group's joint main
effect exactly when the members enter the measure additively, and understates it by the
within-group interaction variance otherwise — a conservative rollup. Guideline movement is
reported per column, not per group: absolute movements do not sum, and a multi-column joint
conditioning would need cell counts the stored ensemble cannot support.

**Weights.** Every reduction — the grand mean, the total variance, the bin means, the
exceedance fractions — uses the ensemble's realization weights, so a posterior re-weighting
(observed-performance updating, scenario credibilities) re-ranks the studies with no new
simulation: the query is answered against the *current* state of knowledge.

## 4. The API

```csharp
// After a full-uncertainty run (and optionally SetRealizationWeights):
ValueOfInformationResults? voi = analysis.MeasureValueOfInformation(
    RiskMeasure.Mean, RiskType.Total);          // + componentIndex, failureModeIndex, consequenceType

foreach (var study in voi.RankedGroups())        // the practitioner table
    Console.WriteLine($"{study.Label}: ±{study.ResolvableStandardDeviation:G4} ({study.VarianceShare:P0})");

double ceiling = voi.TotalStandardDeviation;     // the perfect-information row
var movement = voi.CriterionMovements[0];        // per configured TolerableRiskCriterion
```

- The query shares the sensitivity engine's scope surface (system, component, or failure
  mode) and its knowledge columns — entry labels align one-for-one with the tornado's, so the
  two diagnostics cross-reference cleanly. Movement blocks are computed at the system scope,
  where the criteria are defined, one per configured `TolerableRiskCriterion` in declared
  order.
- `ValueOfInformationResults` is a plain query result, never serialized with the analysis:
  the columns re-derive bit-exactly from the content seeds, so the ranking is recomputable on
  demand. Nothing about the query is serialized, hashed, or seed-affecting.
- Like the sensitivity queries, the call re-runs the component sampler setup at the ensemble
  size as a documented side effect; a subsequent run re-seeds itself at start.

## 5. Verification

The family [`ValueOfInformationVerification`](../verification/value-of-information.md) anchors
the estimator to a linear-Gaussian knowledge map — where the binned main effect and the
probit confidence movement are exactly computable — and the full engine query to an
independent re-implementation of the documented convention over the publicly stored
per-realization measures, unweighted and weighted, with the movement baselines pinned
bit-exactly to the published tolerable-risk confidence entries.

## Related chapters

[sensitivity-analysis.md](sensitivity-analysis.md) — the shared knowledge columns and the
association-measure catalog · [uncertainty-analysis.md](uncertainty-analysis.md) — the
epistemic ensemble and realization weights · [results-catalog.md](results-catalog.md) — the
stored measure catalog and the tolerable-risk confidence surface.
