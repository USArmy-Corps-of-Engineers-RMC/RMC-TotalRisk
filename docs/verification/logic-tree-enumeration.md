# Exact Logic-Tree Enumeration Verification

> **Family:** `LogicTreeEnumerationVerification` (5 tests, run isolated)
> **Subjects:** `RiskAnalysis.LogicTreeEnumerationRealizations`, the enumeration forcing scope,
> `LogicTreeEnumerationMap` attribution, the exact realization-weight products, and the
> enumeration boundary gates
> **Doctrine:** [composite-functions.md](../technical-reference/composite-functions.md) §2,
> [uncertainty-analysis.md](../technical-reference/uncertainty-analysis.md) §6.6

The enumerator runs one full-uncertainty ensemble of K·M realizations — every branch combination
of the model's epistemic axes held on one block of M continuous-knowledge realizations by
selector-column forcing, with the exact branch-weight products published as realization weights —
so its verification targets are exactness itself: the enumerated ensemble against manual
single-combination runs, the weighted reductions against hand-summed weight products, the sampled
epistemic mode against the enumerator (the roles-reversed convergence), and the composition with
the retained-ensemble re-banding.

## Oracles

| Test | Oracle | Tolerance |
|---|---|---|
| `Test_AnalyticTree_ExactWeightedFractiles` | The closed-form analytic tree: a two-axis deterministic logic tree (an unbound epistemic hazard pair at 0.5/0.5 crossed with an unbound epistemic fragility triple at 0.3/0.4/0.3) enumerated at M = 1 (N = 6, ensemble discipline pinned to 1e-8 / minimum depth 2). Every realization must equal its decoded combination's manual mean-only run; the published weight vector must be the exact branch-weight products (and bitwise the map's `RealizationWeight`); the weighted mean must equal the hand-blended enumeration; and weighted tolerable-risk exceedance fractions at thresholds placed between every pair of adjacent atoms must equal the exact weight mass above each threshold — the exact-weighted-fractile statement in convention-free form (the weighted CDF is exact at every probed level). | 1e-12 per-realization and per-reduction; weights 1e-15 |
| `Test_SampledMode_ConvergesToEnumerator` | Roles reversed: the enumerator's weighted mean must equal the manual weight-product blend of six mean-only runs at 1e-12 (the construction the epistemic-mixture family's enumeration oracle performs by hand), and an independently constructed sampled logic tree at N = 4,096 (its own content walks, discipline pinned) must land every realization on an enumerated atom and converge in mean to the enumerator. | 1e-12 exact side; 4·SE of the sampled mean |
| `Test_SharedVariableAxis_ForcesAllBindersIncludingNested` | One shared variable bound by a root composite on one component and by a composite nested inside an aleatory mixture on another: enumeration builds a single three-branch axis (K = 3, M = 4), and within every block both components must equal their manual single-branch references — the root binder's branch runs and the outer mixture with its nested binder replaced by each branch — with each realization weight exactly ω_b/M. | 1e-12 values; 1e-15 weights |
| `Test_PostHocReband_BitEqualToPublished` | The retention composition: a retained enumerated run (K = 6, M = 4) re-banded post hoc under its own published weights must reproduce the published lower/upper/median/mean band realizations byte-for-byte (serialized JSON, manifests stamped) — the weighted assembly is a pure reduction over retained state, so the enumeration's exact weights compose with re-banding with no re-simulation and no drift. | Byte-equal JSON |
| `Test_SmallWeightBranch_ExactResolutionAtLowCost` | The cost demonstration: a 0.02/0.98 epistemic pair with uncertain (triangular) branch chains. The enumerator at M = 64 (N = 128) must give the rare branch exactly one 64-realization block carrying exactly 0.02 of the weight mass; its branch-conditional mean must match an independent single-branch model at 128 realizations; and the sampled mode at N = 1,000 must allocate the rare branch exactly N·ω = 20 realizations (the stratified selector's boundary-aligned allocation, recovered from the author component's bit-exact public re-derivation) — three times the conditional sample at an eighth of the budget. | Exact counts and weight mass (1e-12); 4·combined SE on the conditional mean |

## Runs of record

- **2026-09-02**, commit `66c125b` (implementation) — 5/5 passed, isolated invocation, 2 m 14 s.

## Notes

- The forcing percentile of branch b is the midpoint of its cumulative-weight interval over the
  composite's positively weighted entries — the same inclusive-upper inverse-CDF algebra the
  sampled mode selects with — so an enumerated run's realizations are the same conditional
  objects the sampled mode draws, allocated deterministically and weighted exactly (the
  conditioned-interleaving identity of the epistemic-mixture family is the underlying mechanism).
- The analytic-tree tests pin the ensemble integration discipline to the mean-only discipline
  (1e-8, minimum depth 2) because per-realization values are compared to mean-only reference
  runs at the exact-match tolerance; the documented default split (1e-4/0 for ensembles) would
  otherwise separate identical curves by integration tolerance.
- Exactness is claimed for the branch axis: the weighted CDF's branch mixture is exact, while
  the continuous knowledge inside each combination carries its M-sample noise (absent entirely
  for deterministic branch chains). The exceedance-fraction probes state this without depending
  on any percentile-interpolation convention.
- Fast-suite companions (in `RMC.TotalRisk.Tests`): the enumeration gate matrix (mean-only,
  user weights, no axes, epistemic consequence composites, pins on enumerated composites,
  differing binder weights, tree-carried composites, size guardrails), the map decode and
  weight algebra, the id-keyed unbound and nested forcing, the published-map lifecycle, the
  sensitivity re-derivation reproducing the forcing columns bitwise, and the applied-key
  reconciliation surfaces of the sharing scope.
