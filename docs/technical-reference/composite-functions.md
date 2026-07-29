# Composite Functions

> **Types:** `CompositeHazard`, `CompositeTransform`, `CompositeResponse`, `CompositeConsequence`
> **Namespaces:** `RMC.TotalRisk.RiskFunctions.{Hazards, Transforms, Responses, Consequences}`
> **Verification:** [composite-hazard](../verification/composite-hazard.md) · [composite-response](../verification/composite-response.md) · [composite-transform](../verification/composite-transform.md) · [composite-consequence](../verification/composite-consequence.md)

A composite function combines a weighted list of child functions of the same kind. It is the
standard way to express the dam-safety practice the 2024 verification report describes: *evaluate
various gate failure or debris blockage scenarios as separate analyses, then assign a likelihood to
each.*

This page is about **what the weights mean** and **which combination to choose**. The per-type API
surfaces are documented in the types' own XML remarks.

---

## 1. Two different questions

Practitioners reach for a composite to answer one of two questions, and they need different
combinations.

| Question | Combination | Reading |
|---|---|---|
| "Which of these describes the loading (or the response)? Each with this probability." | **Mixture** | Alternative *descriptions*; exactly one applies to any given event |
| "All of these mechanisms act at once — which one governs?" | **CompetingRisks** | Simultaneous *mechanisms*; the governing one controls |

Mixture weights must lie in [0, 1] and sum to one. **Under CompetingRisks the weights are inert** —
the combination is governed by the rule and the configured dependence, not by weighting — and they
are coerced out of the canonical hash so editing them cannot re-roll a Monte Carlo seed.

The competing-risks rule differs by cluster, and the difference is physical:

- **Hazards use the maximum rule.** All the loading mechanisms occur; the most severe controls. The
  combined non-exceedance probability is the joint probability that *every* mechanism stayed below
  the level — the product of the child CDFs under independence.
- **Responses use the minimum rule** (the weakest link). Any mechanism can fail the system, so the
  combined conditional failure probability is the union of the child failure events,
  `1 − ∏(1 − pᵢ(h))` under independence.

`CompositeTransform` and `CompositeConsequence` have no competing-risks mode: they combine *curves*
pointwise rather than *distributions*, so there is no joint event to take.

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

### What v1.1 supports

**v1.1 implements the aleatory reading only.** Every composite mixture in the library is aleatory,
and that is deliberate rather than an oversight — aleatory representability differs by cluster, and
only three of the four clusters can represent it at all:

| Cluster | Aleatory mixture representable? | Mechanism |
|---|---|---|
| Hazard | Yes | a real `Mixture` distribution; the AEP integration integrates `Σ ωᵢFᵢ(x)` directly |
| Response | Yes | a real `Mixture` distribution; the conditional failure probability at *h* is `Σ ωᵢpᵢ(h)` |
| Consequence | Yes | exposure branches — the engine enumerates weighted branches at every hazard point |
| Transform | **No** | transforms chain deterministically; there is no branch surface in the engine |

For hazards and responses a realization *is already a distribution*, so the mixture folds into it
losslessly and the loss-exceedance tail stays exact. That is why those composites declare zero
sampling dimensions and carry no branch selector.

**Modeling an epistemic alternative today:** run the alternatives as separate analyses and combine
the results outside the model, rather than weighting them inside one composite. Adding a genuine
epistemic mode is deferred until the engine can enumerate branches for the cluster in question —
see §5.

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

## 4. `CompositeTransform` is weighted-average only

A composite transform blends candidate transforms — several rating curves with credibility weights —
into a single consensus curve, `Σ ωᵢfᵢ(x)`. It supports **`Average` only**;
`Mixture` and `Additive` are validation errors.

**Why Mixture is rejected.** Mixture would require per-realization branch selection. There is no
transform analog of the consequence exposure-branch surface — `SampledFailureMode` chains transforms
deterministically — so a mean-only run would collapse the branch and its loss-exceedance tail would
diverge from the mean of the full-uncertainty ensemble. That is exactly the defect ratified Q-V
solved for consequences. Enabling it requires the engine change, not just the function.

**What this costs, stated plainly.** A weighted average is the aleatory-*mean* reading of a set of
candidate transforms: exact when everything downstream is linear, approximate otherwise. Because a
fragility is steeply nonlinear, blending rating curves *before* evaluating it is not the same as
weighting the risks each curve produces — `Risk(Σωᵢfᵢ) ≠ Σωᵢ·Risk(fᵢ)` by Jensen's inequality — and
the blended curve contributes no spread of its own to the uncertainty bands.

A worked example. Three candidate rating curves give stage at the 1% discharge, weights 0.3/0.4/0.3,
feeding a fragility of `Normal(µ = 105 ft, σ = 1 ft)`:

| Reading | Computation | P(failure \| 1% event) |
|---|---|---|
| Weighted average | blended stage `0.3·100 + 0.4·103 + 0.3·106 = 103.0` → `Φ(−2)` | 0.023 |
| Weight the risks | `0.3·Φ(−5) + 0.4·Φ(−2) + 0.3·Φ(+1)` | 0.262 |

An eleven-fold difference, driven entirely by the curvature of the fragility. **Where the weights
genuinely mean "one of these curves is the truth", model the alternatives as separate analyses**
rather than blending them. Where they mean "give me one consensus best-estimate curve" — model
averaging — the weighted average is the correct and intended tool.

---

## 5. Deferred

| Item | Blocked on |
|---|---|
| An explicit epistemic mixture mode for hazards and responses | A ratified design; the code is inexpensive (a per-realization selector), the doctrine and verification are the work |
| `CompositeTransform` Mixture | Engine support for enumerating transform branches, the transform analog of ratified Q-V |
| An epistemic mode for `CompositeConsequence` | Same ratification as the hazard/response one; today its mixture is aleatory by Q-V |

---

## References

- 2024 verification report, *Verification of Input Functions* → *Composite Hazard and Response
  Functions* (Equation 49, Tables 44–46) and *Composite Consequence Function* (Tables 47–51).
- [`MODEL_LIBRARY_ARCHITECTURE.md`](../requirements/MODEL_LIBRARY_ARCHITECTURE.md) §5.5.3 (canonical
  content), §5.8.5 (composite recursion), §6.4.1 (exposure branches, ratified Q-V).
- *Mixture Distribution Overview* and *Competing Risks Overview* (RMC technical notes, 2026).
