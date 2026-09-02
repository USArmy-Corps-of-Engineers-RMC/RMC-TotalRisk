# Composite Functions

> **Types:** `CompositeHazard`, `CompositeTransform`, `CompositeResponse`, `CompositeConsequence`
> **Namespaces:** `RMC.TotalRisk.RiskFunctions.{Hazards, Transforms, Responses, Consequences}`
> **Verification:** [composite-hazard](../verification/composite-hazard.md) · [composite-response](../verification/composite-response.md) · [composite-transform](../verification/composite-transform.md) · [composite-consequence](../verification/composite-consequence.md) · [epistemic-mixture](../verification/epistemic-mixture.md)

A composite function combines a weighted list of child functions of the same kind. It is the
standard way to express the dam-safety practice the 2024 verification report describes: *evaluate
various gate failure or debris blockage scenarios as separate analyses, then assign a likelihood to
each.*

This page is about **what the weights mean** and **which combination to choose**. The per-type API
surfaces are documented in the types' own XML remarks.

---

## 1. Three different questions

Practitioners reach for a composite to answer one of three questions, and they need different
combinations.

| Question | Combination | Reading |
|---|---|---|
| "Which of these describes the loading (or the response)? Each with this probability." | **Mixture** | Alternative *descriptions*; exactly one applies to any given event |
| "All of these mechanisms act at once — which one governs?" | **CompetingRisks** | Simultaneous *mechanisms*; the governing one controls |
| "Exactly one of these is the truth for the whole period of analysis — we do not know which." | **EpistemicMixture** | The logic tree; the weights are credences, one branch is selected per realization |

Mixture weights must lie in [0, 1] and sum to one. **Under CompetingRisks the weights are inert** —
the combination is governed by the rule and the configured dependence, not by weighting — and they
are coerced out of the canonical hash so editing them cannot re-roll a Monte Carlo seed.

The competing-risks rule differs by cluster, and the difference is physical — the same
competing-risks algebra run in its two directions [28]:

- **Hazards use the maximum rule.** All the loading mechanisms occur; the most severe controls. The
  combined non-exceedance probability is the joint probability that *every* mechanism stayed below
  the level — the product of the child CDFs under independence, `F_c(x) = ∏ Fᵢ(x)`, equivalently
  the USACE survival form `S_c(x) = 1 − ∏(1 − Sᵢ(x))` (the probability of union of exceedances).
- **Responses use the minimum rule** (the weakest link). Any mechanism can fail the system, so the
  combined conditional failure probability is the union of the child failure events,
  `1 − ∏(1 − pᵢ(h))` under independence — the min-of-random-variables direction, whose density
  decomposition (the sum of hazard rates times the joint survival, `f_c = [Σ fᵢ/Sᵢ]·∏Sᵢ`) is
  exactly the cumulative-incidence machinery of the competing failure-mode method
  ([failure-mode-combination.md](failure-mode-combination.md) §4.3).

`CompositeTransform` and `CompositeConsequence` have no competing-risks mode: they combine *curves*
pointwise rather than *distributions*, so there is no joint event to take.

The mixture alternative carries three well-known statistical challenges worth naming before
reaching for one [27]: **identifiability** (different weight/component configurations can produce
nearly identical mixtures — fitting one from data is ill-posed without constraints),
**label switching** (estimation methods can silently permute which component is "which"), and
**right-tail underestimation** — a mixture smooths its members' tails together, while the
competing-risks maximum envelops the most extreme member, so treating genuinely simultaneous
flood-generating mechanisms as a weighted mixture understates rare-event hazard. The
mixture-versus-union choice is a physical statement, not a stylistic one.

---

## 2. Aleatory and epistemic weights

This is the decision that most often gets made by accident. Ask:

> **Over the period of analysis, does the system experience all the sub-functions in proportion to
> their weights — or is exactly one of them the truth the whole time, and we just do not know
> which?**

**Aleatory** — the weights are *frequencies within the event population*:

- day versus night population at risk (the canonical consequence case);
- rain-driven versus snowmelt-driven flood populations;
- debris blockage that is re-rolled on each loading event.

Test: more data would *refine* the weights, not collapse them toward one branch.

**Epistemic** — the weights are *credibility that one fixed-but-unknown alternative is correct*:

- three expert-elicited rating curves;
- competing hydrologic models;
- an as-built versus as-designed drainage detail;
- a geologic interpretation that a site investigation could in principle resolve.

Test: a site investigation or more data could settle which branch is true.

### How each reading computes

**Both readings ship, and they compute differently by construction.** The aleatory reading (the
`Mixture` member) has no branch selection — the mixture is one object carried through every
realization — while the epistemic reading (`EpistemicMixture`) selects exactly one branch per
realization by inverse-CDF of the cumulative weights, so the ensemble carries branch-conditional
realizations and the epistemic percentiles straddle the alternatives instead of blending them:

| Cluster | Aleatory `Mixture` | `EpistemicMixture` |
|---|---|---|
| Hazard | a real `Mixture` distribution; the AEP integration integrates `Σ ωᵢFᵢ(x)` directly | one selector dimension; the realization integrates the selected child's own curve |
| Response | a real `Mixture` distribution; the conditional failure probability at *h* is `Σ ωᵢpᵢ(h)` | one selector dimension; the realization carries the selected fragility whole |
| Consequence | exposure branches — the engine enumerates weighted branches at every hazard point | the mode's coupling draw selects one branch; a nested aleatory mixture still enumerates inside it |
| Transform | **not representable** — transforms chain deterministically, with no within-realization branch surface | one selector dimension; the selected rating curve chains downstream whole |

For hazards and responses an aleatory realization *is already a distribution*, so the mixture folds
into it losslessly and the loss-exceedance tail stays exact — those composites declare zero sampling
dimensions in the aleatory modes. An epistemic composite on a walked cluster declares exactly one
sampling dimension of its own (the branch selector); its children keep their own streams, so the
selection is one more knowledge quantity, visible to the tornado, the given-data measures, and the
value-of-information rollups, and conditionable with a fractile pin ("risk given rating model B").

**Mean-only runs and the epistemic mode.** A mean pass has no realization to select a branch with,
so an epistemic hazard, transform, or response composite in a mean-only analysis would silently
answer with the analytic blend — the wrong side of §4's elevenfold example — and analysis
validation refuses the combination with an Error. An epistemic *consequence* composite is a Warning
instead: consequences enter mean risk linearly, so the blend keeps the exact mean and only the
epistemic spread is absent.

### Shared epistemic variables — state-of-knowledge correlation

A logic tree requires the same branch choice to apply everywhere it is relevant: three reaches
whose fragilities all depend on which geologic interpretation is true must move *together*, and
content-based seeding deliberately gives equal-content functions independent streams — the exact
opposite. Naming the same **`EpistemicVariable`** on several epistemic composites couples them: at
run seeding, one selector column per distinct variable is derived from the analysis seed and the
variable name alone and overwritten onto every binder's selector (the fractile-pin overwrite
pattern, so no walk ordinal moves and no other function's stream can shift). Every binder then
selects the same branch draw per realization — nuclear PRA's state-of-knowledge correlation
requirement, delivered without a first-class object.

Rules that follow from the mechanics:

- The variable's **name is its identity**: binding, unbinding, or renaming is compute-relevant
  hashed content (written and hashed only when non-empty, so unbound composites are byte-identical
  to their pre-epistemic forms). Renaming a variable re-rolls its shared draw.
- Binders may declare different weight vectors — the shared uniform still selects consistently by
  rank — but analysis validation warns, because branches then no longer correspond one-to-one.
- One shared variable is **one knowledge quantity**: the sensitivity and value-of-information
  walks collect a single column per variable, labeled `Epistemic Variable - <name>`.
- A fractile pin on a bound composite overrides the share for that function alone (validation
  warns); pinning the branch choice for the whole tree means pinning every binder.
- A consequence composite cannot bind a variable — its branch draw rides the failure mode's
  coupling matrix, which is per mode rather than per function (the same seat that keeps
  consequence fractile pins a no-effect warning; extending that seat is recorded future work).
- A standalone `SystemComponent.SetupSamplers` call shares within the component from the component
  seed; an analysis run shares across all components from the run seed.

**Branch attribution:** `SelectedBranchIndex(realizationIndex)` on the walked composites answers
"which model alternative did this realization live in" — runtime-only, re-derivable bit-exactly
from the content seeds. It composes with the A4 realization weights (weight the ensemble by branch
posteriors) and with retained realizations for per-branch disaggregation.

---

## 3. Rules of thumb

1. **The mean is invariant to the aleatory/epistemic reading.** Both give the same expected
   annualized risk; they differ only in the spread. If you chose one hoping for a higher mean, the
   weights or the children are wrong, not the combination.
2. **Do not double-count.** If a child already carries the uncertainty as its own bootstrap
   posterior, do not also model it as a separate weighted branch.
3. **Resolution costs realizations.** A branch of weight ω is resolved by roughly `N·ω`
   realizations; aim for `N·ω_min ≥ 1000`. Prefer `LatinHypercube` over `MonteCarlo`, whose
   allocation is binomial rather than stratified.
4. **Nesting is the right way to layer readings.** A composite over composite children expresses
   "which model is right" above "what varies naturally." Just do not put the same source at both
   levels.
5. **Declared entry order is compute-relevant.** It drives the child sampler ordinals and the hashed
   entry order, so reordering entries changes the Monte Carlo stream (though not the expected
   result). Renaming a child never does.

---

## 4. `CompositeTransform`: blend or select

A composite transform combines candidate transforms — several rating curves with credibility
weights. It supports **`Average`** (the blended consensus curve, `Σ ωᵢfᵢ(x)`) and
**`EpistemicMixture`** (one candidate curve selected per realization); the aleatory `Mixture` and
`Additive` stay validation errors.

**Why the aleatory Mixture is still rejected.** It would require *within-realization* branch
enumeration, and there is no transform analog of the consequence exposure-branch surface —
`SampledFailureMode` chains transforms deterministically — so a mean-only run would collapse the
branch and its loss-exceedance tail would diverge from the mean of the full-uncertainty ensemble.
The epistemic reading needs no such surface: one branch per realization chains through the engine
like any other sampled transform, which is why it ships while the aleatory mode stays deferred.

**What the blend costs, stated plainly.** A weighted average is the aleatory-*mean* reading of a
set of candidate transforms: exact when everything downstream is linear, approximate otherwise.
Because a fragility is steeply nonlinear, blending rating curves *before* evaluating it is not the
same as weighting the risks each curve produces — `Risk(Σωᵢfᵢ) ≠ Σωᵢ·Risk(fᵢ)` by Jensen's
inequality — and the blended curve contributes no spread of its own to the uncertainty bands.

A worked example. Three candidate rating curves give stage at the 1% discharge, weights 0.3/0.4/0.3,
feeding a fragility of `Normal(µ = 105 ft, σ = 1 ft)`:

| Reading | Computation | P(failure \| 1% event) |
|---|---|---|
| `Average` (weighted average) | blended stage `0.3·100 + 0.4·103 + 0.3·106 = 103.0` → `Φ(−2)` | 0.023 |
| `EpistemicMixture` (weight the risks) | `0.3·Φ(−5) + 0.4·Φ(−2) + 0.3·Φ(+1)` | 0.262 |

An eleven-fold difference, driven entirely by the curvature of the fragility — and both rows are
now runnable model choices, pinned at engine grade by the
[epistemic-mixture verification family](../verification/epistemic-mixture.md). **Where the weights
genuinely mean "one of these curves is the truth", use `EpistemicMixture`.** Where they mean "give
me one consensus best-estimate curve" — model averaging — the weighted average is the correct and
intended tool.

---

## 5. Deferred

| Item | Blocked on |
|---|---|
| `CompositeTransform` aleatory `Mixture` | Engine support for enumerating transform branches within a realization, the transform analog of the consequence exposure-branch contract (the epistemic reading needs none and ships) |
| Shared epistemic variables on `CompositeConsequence` | A per-function selector seat — the consequence branch draw rides each mode's coupling matrix, the same seat that keeps consequence fractile pins a no-effect warning |

---

## References

- [8] 2024 verification report, *Verification of Input Functions* → *Composite Hazard and Response
  Functions* (Equation 49, Tables 44–46) and *Composite Consequence Function* (Tables 47–51).
- [`MODEL_LIBRARY_ARCHITECTURE.md`](../requirements/MODEL_LIBRARY_ARCHITECTURE.md) §5.5.3 (canonical
  content), §5.8.5 (composite recursion), §6.4.1 (exposure branches).
- [27] *Mixture Distribution Overview* and [28] *Competing Risks Overview* (RMC technical
  overviews); numbering per [../references.md](../references.md).
