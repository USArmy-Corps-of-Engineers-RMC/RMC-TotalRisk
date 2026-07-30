# % Contribution Verification

**Test class:** `ContributionVerification` · **Tests:** 5 · **Run of record:** 2026-07-24, isolated run, ✅ all passed

The % contribution diagnostic (new in v1.1 — no v1.0 counterpart exists) attributes each
failure mode's share of its component's risk and each component's share of the system's risk,
for **all four** combination methods and **both** system methods, on three bases per
consequence type: attributed annualized failure probability, attributed failure mean, and
attributed excess (incremental) mean. Percentages derive on read (`RiskContribution.ShareOf`).

**The attribution scheme:** every combination method already
produces an exclusive failure-event decomposition — normalized marginals (ME), common-cause
adjusted marginals, competing cumulative incidence functions, and the joint method's
inclusion–exclusion pathways. Within each exclusive event the probability splits **equally**
among participants — exactly the Shapley value of the union game v(S) = P(∪ F_j), since each
exclusive event is a scaled dual-unanimity game — and the event's combined consequence splits
**proportionally to the participants' marginal consequences** (equal split at zero, which also
serves reliability mode: the probability base is consequence-free). Shares always sum to the
event values, so **Σ contributions ≡ the parent's raw recorded totals under every method,
rule, and dependency**; the per-mode methods are the |T| = 1 degenerate case, reducing exactly
to "adjusted marginal × marginal consequence" — the decomposition other tools report. Under
the Sum rule the split credits each mode exactly its own consequence, so joint-Additive
attribution ≡ the marginal integrals ∫ p_j·c_j dF. At the additive system level the Shapley
split of the independent failure union has the closed form φ_i = p_i · E[1/(1 + K_i)]
(K_i Poisson–binomial over the other components), computed by an O(D²) recursion — no 2^D
enumeration — folded in the canonical-hash component order per the system reorder contract.

## Scenario

The two-mode deterministic component over a 65-knot normal-quantile stage-frequency table
(Normal(100, 20), z ± 8 by 0.25): fragility A (100 → 0, 180 → 1), fragility B
(120 → 0, 200 → 1), consequences c_A (60 → 0, 200 → 1,000), c_B (60 → 0, 200 → 2,000),
non-failure (60 → 0, 200 → 100). System tests scale the consequences ×1/×2/×3 across
components. Deterministic inputs make the mean pass exactly the quadrature of the oracle's
own interpolation chains.

## Oracles

Independent dense-trapezoid quadrature (200,001 non-exceedance ordinates) over the oracle's
own interpolators — no engine code: the method-specific adjusted marginals (ME normalization,
CCA factor = union/Σ via De Morgan), the competing cumulative incidence functions by the tech
note's Eq. 14 rectangle rule at 20,001 bins, the joint Shapley integral
∫ [p_j(1 − p_other) + p_A·p_B/2] dF, the Sum-rule marginal integrals, and the Maximum-rule
proportional split in closed integrand form. The additive system oracle is the brute-force 2³
exclusive-combination enumeration with equal splits.

## Checks

| # | Test | Verifies | Tolerance | Result |
|---|---|---|---|---|
| 1 | `Test_Contribution_MEAndCCA_VsQuadratureOracle` | ME and CCA per-mode contributions (all three bases, both modes) ≡ the adjusted-marginal quadrature; Σ identities vs the recorded Fail mass balance / Fail mean / Excess mean | 1e-4 rel (the quadrature mass-accounting residual envelope); 1e-12 rel (identities) | ✅ |
| 2 | `Test_Contribution_Competing_VsIncidenceOracle` | Competing contributions vs the Eq. 14 incidence oracle at 20,001 bins; the engine's 200-bin CIF pre-processing (v1.0 constant) carries the documented discretization allowance; Σ identities exact | 1e-2 rel (CIF discretization; the competing family measured ≈ 0.3% at five modes); 1e-12 rel (identities) | ✅ |
| 3 | `Test_Contribution_Joint_SumRuleOracle_AndMaximumOrdering` | Joint Sum-rule attribution ≡ ∫ p_j·c_j dF (the own-consequence credit); the Shapley probability attribution ≡ its integral; Maximum-rule attribution ≡ the proportional-split oracle; Σ identities | 1e-4 rel; 1e-12 rel (identities) | ✅ |
| 4 | `Test_Contribution_AdditiveSystem_BruteForce_AndReorderInvariance` | The Poisson–binomial Shapley shares ≡ brute-force 2³ enumeration; Σ shares ≡ the folded union; Σ mean contributions ≡ the convolved system means; declaration-order bit-inertness | 1e-12 abs (shares); 1e-9 rel (convolved means); 0 (bit, reorder) | ✅ |
| 5 | `Test_Contribution_JointSystem_Identities_AndReproducibility` | Joint-system Σ contributions ≡ the recorded system Fail mass balance / Fail mean / Excess mean within the run (default VEGAS budget); mode-level contributions finalize under the weight regime; repeated runs bit-identical | 1e-12 rel; 0 (bit) | ✅ |

Unit-level companions (fast suite): hand-computed two-evaluation splits for the joint-Maximum
and common-cause kernels (exact literals), the zero-consequence equal-split fallback (the
reliability-mode guarantee), Σ identities across all four methods at engine level, the
joint-Additive ≡ marginal-Fail-mean identity, the D = 3 brute-force Shapley match, ensemble
summaries carrying per-realization contributions with band trees carrying none, and the
pre-6.6 JSON forward-load (missing members → null → "not computed").

## Tolerance derivations

- **Σ identities (1e-12 relative):** the attribution accumulates the same recorded products
  the curves integrate, in separate chains — the comparison is floating-point association
  only. Pinned against the raw `MassBalance`/unclamped means; the Min(·, 1) clamp and the
  Numerics `IndependentExclusive` convergence-shortcut truncation are documented exclusions.
- **Quadrature oracles (1e-4 relative):** the engine's recorded mass carries the quadrature
  mass-accounting residual (measured ≈ 3e-6 on means in the EAD family under the earlier
  midpoint-trapezoid partition, since replaced by the recorded-mass ledger — the bound holds
  a fortiori); the dense-trapezoid oracle contributes ≈ 1e-9.
- **Competing (1e-2 relative):** the engine interpolates CIFs pre-processed over 200
  stratified bins (the preserved v1.0 constant); the oracle's 20,001-bin Eq. 14 reference
  isolates that discretization. The [competing family](competing-failures.md) measured ≈ 0.3%
  at five modes; the two-mode deviations here sit well inside the allowance.

Run of record 2026-07-24: `ContributionVerification` 5/5 passed (~16 s wall); the fast suite
at that date passed in full (457/457, Debug and Release) including the eight new contribution
unit tests; gates `EngineReproducibilityVerification` 3/3 and `JointFailuresVerification` 9/9
passed unchanged. The results-JSON byte gate re-pinned for the new serialized members
(`scripts/perf/RESULTS.md`).
