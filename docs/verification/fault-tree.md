# Fault-tree response

**Test classes:** `FaultTreeVerification` (11) · `FaultTreeMonteCarloVerification` (7) · **Tests:** 18 · **Run of record:** 2026-08-28 (`FaultTreeVerification`, incl. the exact importance measures) and 2026-07-31 (`FaultTreeMonteCarloVerification`), isolated runs, ✅ all passed

## Scope and status

This is a **greenfield family — no legacy oracle exists**: v1.0 shipped no fault-tree capability,
so every expected value is derived independently in the tests. The family verifies the exact
decision-diagram evaluator against exhaustive Boolean enumeration and closed-form gate algebra;
shared-versus-independent transfer semantics against explicit expansions; minimal cut sets
against hand derivation with an explicit anti-approximation proof; referenced tabular, event-tree,
and fault-tree sources against manual inlining across both serialization modes; graph-connected
risk equivalence proving hazard/consequence integration stays outside the tree; the loud
decision-diagram resource diagnostics; an independent million-trial Boolean simulation;
shared-once/clone-separate sampling with complete Latin hypercube strata coverage; replicate
SRS-versus-LHS variance reduction with exact expectations; end-to-end scheduling bit identity;
canonical-identity realization invariance; and node-importance oracles for both tree kinds. At
the run of record the isolated family was **17/17**, the fast suite **868/868**, and unit-only
library line coverage **90.39%**.

The response computes conditional fragility `P(F|h)` only. Hazard probability, annualization,
consequences, and risk remain outside the fault tree.

## Enumeration oracle

The oracle walks the authored trees itself: it resolves transfers from their public targets,
unifies shared-logical occurrences onto one Boolean variable per independent context, forks a
fresh context at every independent-clone traversal, and sums probability-weighted top-gate truth
over all `2^V` assignments with compensated addition:

```text
P(F|h) = sum over assignments s of [ truth(top, s) * prod_i ( s_i ? p_i(h) : 1 - p_i(h) ) ]
```

It never touches the compiled decision diagram. Agreement at `1e-13` — roundoff scale for these
expression sizes — verifies the production kernel end to end. Indexed-realization parity uses two
constructions that stay independent of the production dimension layout: a one-realization
median-LHS sampler (a single median stratum pins every percentile at one half for any seed) and a
single-uncertain-dimension tree whose recovered `SampledPercentile(r, 0)` feeds the oracle
directly for all 64 realizations.

## Fixtures and results

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~FaultTree"
```

Observed 2026-07-31: **17/17 passed**.

| Fixture | Independent expectation | Result |
|---|---|---|
| Feature-rich tree (shared internal/external, independent clone, Xor, k-of-n, both house states, mixed sources) | Exhaustive enumeration at every hazard knot, mean and median realization | Exact within `1e-13` |
| Single-uncertain-dimension indexed realizations | Enumeration at the recovered LHS percentile, 64 realizations × 3 knots | Exact within `1e-13` |
| Gate algebra pins | `∏p`, `1−∏(1−p)`, shared `= p` vs independent `= p²` (+ OR duals), two-input XOR, binomial tails for k = 1, 2, 3 of 3 | Exact within `1e-15` |
| Repeated-event bridge | Hand inclusion–exclusion over four paths on five shared components (fifteen alternating union-product terms) | Exact within `1e-13` |
| Minimal cut sets | `OR(AND(A,B), C)` → `{C}`, `{A,B}`; exact union `0.71` vs rare-event sum `0.92` | Sets match; response equals enumeration, not the bound; non-coherent trees refuse |
| Referenced sources | Tabular/event/fault-backed events vs manual inlining; both XML modes resolver-backed | Exact within `1e-14`; round trips bit-equal |
| Referenced-source realizations | An isolated clone of the referenced table prepared with the production-derived content seed | Bit-equal for all 64 realizations |
| Transfer expansions | Shared pair `= p`; independent pair `= 1−(1−p)²` equal to a hand-built explicit twin; internal `AND(A, shared A)` collapse | Exact within `1e-15` |
| Graph-connected equivalence | A deterministic fault response vs a tabular response pinned to the same symmetric-Shannon ordinates and Normal-Z transform, each integrated by `RiskAnalysis` | Bit-equal mass balance, annualized failure probability, and mean consequence |
| Decision-diagram budget | `BddNodeLimit = 2` fails validation with observed count, limit, and remediation; restoring the default restores enumeration parity | Loud failure; exact recovery |
| Medium-tree Boolean simulation | Exhaustive expectation `0.97161166844151381` over 16 unique variables with three shared repetitions | Observed `0.971555`, error `5.67e-5`, Hoeffding bound `0.0026933861344527097` |
| Sampling dimensions and strata | Shared occurrence adds no dimension, the clone adds one; every LHS stratum covered once per dimension; per-realization closed form `1−(1−p(u₀))(1−p(u₁))` | Exact; distinct streams proven |
| Aggregate SRS versus LHS | Affine `OR(A, 0.5)`, exact expectation `0.65`, `N=256`, `R=12` paired seeds `10,203,731..742` | Both unbiased; variance ratio `32,365.366` |
| Scheduling bit identity | One uncertain graph-connected fault analysis, 100 LHS realizations, seed `10,203,791`, sequential/four-worker/two default runs | Bit-identical JSON, curves, hashes, and seed maps |
| Canonical invariance | Metadata rename, GUID regeneration, sibling reorder, and by-reference mode at one seed | Bit-identical hash and all 64 realization curves |
| Node importance, affine event tree | Exact `Pearson = a·σ/√(Σa²σ²) = √0.5` and `index = a²σ²/Σa²σ² = 0.5` for both uncertain entries | Within replicate standard error at 1,000 iterations |
| Node importance, shared fault tree | A from-scratch BCL-random two-pass sweep with hand-rolled mean/variance/quartile/Pearson statistics | Agreement within replicate error; determinism and live-state inertness exact |

## Independent Boolean simulation

The medium tree has sixteen unique events (`p_i = 0.15 + 0.03i`) across series, 2-of-3 voting,
exclusive, parallel, and tail gates, with `E1`, `E4`, and `E7` each repeated once through
shared-logical transfers. A BCL `Random` simulator (seed **10,203,671**, **N = 1,000,000**) draws
each unique variable once per trial — repeated occurrences reuse the draw — and evaluates a
hand-coded gate twin bottom-up, never the production evaluator. The exhaustive expectation over
all `2^16` assignments is `0.97161166844151381`, the production diagram reproduces it within
`1e-13`, and the two-sided finite-sample Hoeffding bound at familywise `alpha = 1e-6` for the one
simulated comparison is

```text
epsilon = sqrt(log(2/alpha) / (2N)) = 0.0026933861344527097
```

The observed frequency was `0.971555` (absolute error `5.67e-5`). The bound is finite-sample and
uses no normal approximation; the test verifies the tree's probability, not the BCL generator.

## Aggregate LHS variance reduction

The affine fixture `OR(A, 0.5) = 0.5 + 0.5·p_A` with `p_A ~ U(0.1, 0.5)` has exact expectation
`0.65` and per-draw variance `V = 0.25·(0.4²/12)`. Twelve paired-seed replicates (seeds
**10,203,731 through 10,203,742**, equal `N = 256`) give exact replicate-mean variances `V/N`
(SRS) and `V/N³` (one jittered draw per LHS stratum); the unbiasedness assertions use 25 pooled
standard errors and the reduction gate is a deliberately conservative ratio of 100. Measured
pooled means were **0.64956082662961312** (SRS) and **0.64999744086406031** (LHS); replicate
sample variances were **3.7179652794040251e-6** and **1.148748114315832e-10**, ratio
**32,365.36567999818**. All twelve LHS means were distinct by seed. The exact-variance argument
applies to this analytically affine fixture; nonlinear trees are covered by the enumeration and
simulation oracles instead.

## Node-importance oracles

The affine event tree `F = p_A + 0.5·p_F` with `p_A ~ U(0.1, 0.3)` and `p_F ~ U(0.2, 0.6)` makes
the two variance contributions equal by construction, so the exact statistics are
`Pearson = a_i σ_i / sqrt(Σ a_j² σ_j²) = sqrt(0.5)` and
`index = a_i² σ_i² / Σ a_j² σ_j² = 0.5` for both uncertain entries, with the residual branch
anti-correlated at the same magnitude and the deterministic gate reporting NaN correlation and a
zero index. Bounds are replicate standard errors at 1,000 iterations (about `(1−ρ²)/√N` for the
correlation).

The shared-event fault tree `OR(AND(A, shared A), B)` reduces to `1−(1−p_A)(1−p_B)`; a
from-scratch reimplementation draws each unified variable once per iteration with BCL
`Random` (seeds 97,531 and 86,420 for the two passes) and computes mean, unbiased variance,
order-statistic quartiles, and Pearson correlation by hand. Both implementations estimate the
same population statistics and agree within replicate sampling error; the production analysis is
additionally pinned bit-deterministic across repeated calls, and the configured live sampler and
canonical hash survive the analysis untouched.

## Exact importance measures

`Test_ExactImportance_MatchesExhaustiveEnumeration` verifies `FaultTreeImportance.Compute` — the
exact Birnbaum, criticality, Fussell-Vesely, risk achievement worth, and risk reduction worth
from two frozen-diagram conditional evaluations per unified variable — against the exhaustive
enumeration oracle extended with per-variable forced conditionals (`EnumerateWithOverride`
forces one unified variable's probability before the 2^V probability-weighted truth sum, exact
for forced values of zero and one). The fixture is a shared-transfer tree with a 2-of-3 vote —
Or(And(A, C), 2-of-3(B, D, shared C)) — mixing deterministic scalars with an uncertain tabular
source, evaluated at the source means and at a co-monotonic 0.75 percentile. Every measure of
every unified variable, the unified baseline probabilities, and the baseline top event agree at
1e-13 (1e-12 relative on the compounded ratios) — floating-point roundoff scale, since both
sides are exact algebra over the same double-precision source probabilities. Run of record
2026-08-28: `FaultTreeVerification` 11/11 passed (6.6 s wall), isolated invocation.

## Limitations

- The exhaustive oracle is capped near twenty unique variables by design; larger trees are
  covered by the closed-form, simulation, and equivalence fixtures rather than enumeration.
- The variance-reduction proof is exact only for the affine fixture; it makes no claim about
  nonlinear interaction structures.
- The Boolean simulation verifies routing probability at the authored knot, not generator quality
  or temporal dependence.
- Node-importance closed forms cover affine aggregates; nonlinear aggregates are checked by the
  independent reimplementation within replicate error, not exactly.
