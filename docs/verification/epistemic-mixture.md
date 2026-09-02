# Epistemic Mixture (Logic Tree) Verification

> **Family:** `EpistemicMixtureVerification` (5 tests, run isolated)
> **Subjects:** the `EpistemicMixture` mode on `CompositeHazard`/`CompositeResponse`/`CompositeTransform`/`CompositeConsequence`, shared epistemic variables, fractile-pin branch conditioning
> **Doctrine:** [composite-functions.md](../technical-reference/composite-functions.md) §2, §4

The epistemic mixture selects one child per realization by inverse-CDF of the cumulative weights —
the logic tree — so its verification targets are the selection algebra itself, the sharing
contract, and the two identities that make the mode auditable: the ensemble must partition exactly
into its branch-conditional sub-ensembles, and it must converge to exact branch-combination
enumeration.

## Oracles

| Test | Oracle | Tolerance |
|---|---|---|
| `Test_ConditionedInterleaving_ExactPartition` | A fractile pin overwrites only the selector column, so the unpinned epistemic ensemble (N = 1,000, three uncertain triangular fragilities at 0.3/0.4/0.3) is the exact per-index interleaving of the three branch-pinned ensembles. Every realization matches one pinned run's same-index value **bitwise**, and the branch counts are exactly N·ω (300/400/300 — the cumulative boundaries sit on Latin-hypercube strata edges). This is simultaneously the epistemic-conditioning composition proof: "risk given one model alternative" partitions the unconditional ensemble. | Bitwise per-index match; exact counts |
| `Test_IndependentBranchModels_StatisticalMixtureIdentity` | The epistemic ensemble mean failure probability (N = 4,096) against the credence-weighted means of three standalone single-branch models. The standalone models seed from their own content walks, so this is a genuine cross-model identity, not a seed replay. | 4·SE of the difference (both Monte Carlo means' variances combined) |
| `Test_JensenDoctrine_BlendedVersusEpistemic` | The doctrine's three-rating-curve example: stages 100/103/106 ft at the 1% flow through the Normal(105, 1) fragility. The `Average` composite answers Φ(−2); the epistemic ensemble mean answers 0.3·Φ(−5) + 0.4·Φ(−2) + 0.3·Φ(1) — exactly, at the boundary-aligned count, since every branch chain is deterministic. Pins the published 0.023 / 0.262 numbers and the elevenfold gap. | 1e-12 against the closed forms; 5e-4 against the published roundings |
| `Test_SharedVariable_MatchesIndependentDerivation` | Two components binding one variable (N = 512, deterministic branches): each realization's selected branch is recovered from the published component failure probabilities (deterministic branches rank descending), and both binders must match, at every realization, the selection of an **independently re-implemented** column — SHA-256 over the variable name, the little-endian seed fold, the positive-seed map, and the Latin-hypercube block, with no library seed-kernel call. Anti-oracle: the unbound variant's two selection sequences must disagree somewhere. | Exact selection match at every realization |
| `Test_ExactEnumeration_Convergence` | A two-variable tree (an epistemic hazard pair at 0.5/0.5 crossed with an epistemic fragility triple at 0.3/0.4/0.3) enumerated exactly as the weight-product sum of six deterministic single-combination mean-only runs. The sampled ensemble (N = 4,096) must reproduce one enumerated combination per realization exactly and converge in mean — the seed of the future exact logic-tree enumerator. The sampled run pins its ensemble integration discipline to the mean-only discipline (1e-8, minimum depth 2), because the default ensemble/mean discipline split (1e-4/0 for ensembles) would otherwise separate identical curves by integration tolerance. | 1e-12 per-realization combination match; 4·SE on the mean |

## Runs of record

- **2026-09-02**, commit `b068d15` — 5/5 passed, isolated invocation, 4 m 17 s.

## Notes

- The interleaving oracle is deliberately bitwise: fractile pins are runtime-only and overwrite
  only the target's percentile matrix, so realization *i* of the unpinned run and realization *i*
  of the branch-pinned run integrate identical sampled chains through identical adaptive meshes.
- The shared-variable oracle's re-implementation mirrors the declared derivation —
  `ToPositiveSeed(HashCombine(analysisSeed, SHA-256(UTF-8(name)), 0))` through the run's scheme —
  from `System.Security.Cryptography` and `Numerics.Sampling` primitives alone, so a regression in
  the library's seed kernel or the run-scope wiring cannot hide behind itself.
- Fast-suite companions (in `RMC.TotalRisk.Tests`): the selector-recipe bit-parity pins, the
  conditional-presence/hash-event pins for the mode and the variable, the walk-shape invariance of
  the mode switch (captured seed-map ordinal counts), the run-scope alignment and sensitivity
  dedup, and the deterministic-branch identity behind a full engine run.
- The enumeration convergence oracle's roles have since reversed: the exact logic-tree enumerator
  (`RiskAnalysis.LogicTreeEnumerationRealizations`) automates the weight-product construction
  `Test_ExactEnumeration_Convergence` performs by hand, and the
  [logic-tree-enumeration](logic-tree-enumeration.md) family pins the enumerator as the exact
  oracle the sampled mode converges to. The manual construction here stays verbatim — it is the
  independent anchor both sides answer to.
