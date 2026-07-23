# System Risk Matrix

**Test class:** `SystemRiskMatrixVerification` · **Status:** ✅ Verified (2026-07-23, Phase 6)

The Phase 6 conversion of the legacy `Test_MC_SystemRisk` family — multi-component system risk
across the full correlation × aggregation matrix, verified against consolidated brute-force
Monte Carlo oracles at the legacy seeds and pinned to the 2024 verification report's published
constants (tables 77–103). Where the Phase 4b [system-risk family](system-risk.md) validated
the aggregation *machinery* (lattice convolution, VEGAS enumeration, γ audit) on its own
scenario, this family validates the *matrix*: every legacy dependency option crossed with
every joint-consequence rule, on the legacy fixtures.

## Scenario (the shared legacy Bucket-1 model, system form)

Every component draws its own hazard from LnNormal(85, 20) through a correlated latent
Gaussian; component *c* carries the legacy fragility Φ((h − μ_c)/σ_c) and failure-consequence
curve of PFM-(c+1); every component shares the legacy non-failure curve
{60→0, 100→1, 140→10, 200→100, 250→150}. Engine and oracle interpolate the same dense z-grid
tables (hazard step 0.1, fragilities step 0.05σ, ±8 range), so tabulation error cancels from
the engine-versus-oracle asserts. Per annual event, each failed component contributes its
failure consequence and each surviving component its non-failure consequence; the
across-component sets combine under the joint-consequence rule (failed → Fail/Excess,
surviving → NonFail, all → Background), and Total = Fail + NonFail.

## Legacy method mapping (36 methods → 12 consolidated group tests)

The four combination rules within a dependency group share identical sampling streams, so one
oracle pass accumulates all rules — bit-identical to the separate legacy passes at the same
seeds (the Phase 5 consolidation, applied at the system level). N = 10⁶ (legacy 10M ÷ 10 per
policy).

| Group test | Legacy methods (lines) | Seeds (hazard MVN / capacities) | Report tables |
|---|---|---|---|
| `Test_2Comp2Pfm_Independent_Additive_VsOracle` | `Test_2_Component_2_PFM_JointFailures_Independent_Additive` (14) | 78910 / 12345 + 45678 (per-component streams) | 89 |
| `Test_2Comp2Pfm_PerfectlyPositive_Additive_VsOracle` | `…_Positive_Additive` (178) | 78910 / 12345 + 45678 | 90 |
| `Test_2Comp2Pfm_PerfectlyNegative_Additive_VsOracle` | `…_Negative_Additive` (342) | 78910 / 12345 + 45678 | 91 |
| `Test_2Comp2Pfm_CorrelationMatrix_Additive_VsOracle` | `…_Correlation_Additive` (506) | 78910 / 12345 + 45678 | — |
| `Test_2Comp1Pfm_Independent_AllRules_VsOracle` | `Test_2_Component_1_PFM_…_Independent_{Additive,Average,Maximum,Minimum}` (670–1174) | 67891 / 12345 (shared columns) | 77–80 |
| `Test_2Comp1Pfm_PerfectlyPositive_AllRules_VsOracle` | `…_Positive_{4 rules}` (1240–1810) | 67891 / 12345 | 81–84 |
| `Test_2Comp1Pfm_PerfectlyNegative_AllRules_VsOracle` | `…_Negative_{4 rules}` (1814–2388) | 67891 / 12345 | 85–88 |
| `Test_2Comp1Pfm_CorrelationMatrix_AllRules_VsOracle` | `…_Correlation_{4 rules}` (2389–2963) | 67891 / 12345 | — |
| `Test_5Comp1Pfm_Independent_AllRules_VsOracle` | `Test_5_Component_1_PFM_…_Independent_{4 rules}` (2964–3567) | 67891 / 12345 | 92–95 |
| `Test_5Comp1Pfm_PerfectlyPositive_AllRules_VsOracle` | `…_Positive_{4 rules}` (3568–4171) | 67891 / 12345 | 96–99 |
| `Test_5Comp1Pfm_PerfectlyNegative_AllRules_VsOracle` | `…_Negative_{4 rules}` (4172–4775; the Minimum method is mislabeled `Test_5_Component_5_PFM_…` — ported from the body) | 67891 / 12345 | 100–103 |
| `Test_5Comp1Pfm_CorrelationMatrix_AllRules_VsOracle` | `…_Correlation_{4 rules}` (4776–5376) | 67891 / 12345 | — |

Dependency constants: Independent r = 0; PerfectlyPositive r = 1 − √ε; PerfectlyNegative
r = −1/(D−1) + √ε (−1 + √ε at D = 2, −0.25 + √ε at D = 5); CorrelationMatrix r = 0.5.

**Documented deviations from the legacy bodies:** the 2-component 2-PFM Positive/Negative
bodies used r = 1 − ε and −1 + ε; the port uses the engine constants 1 − √ε and −1 + √ε
(statistically indistinguishable, Cholesky-stable — the deviation class Phase 5 ratified for
the 5-PFM joint family). The 1-PFM families already used the engine constants verbatim.

## Engine paths asserted

- **Additive method** (Independent groups, additive rule): the deterministic Gauss–Kronrod +
  exact-lattice-convolution path, asserted on the full Phase 5 catalog — five summary means,
  failure union + non-failure complement, σ(Fail)/σ(Total), assurance P(C > 100), two
  data-driven failure-curve probes plus a total-curve probe, value-at-risk in probability
  space (the 4b convention), and conditional value-at-risk. Probes run the convolution at
  65,536 lattice nodes: at the default 4,096 the ≈ 1-unit quantization of the five-component
  support shifted the conditional-median probe by ≈ 2× the binomial tolerance (a resolution
  property, not engine math — the discovery is recorded here and in the test docs).
- **Joint method** (every group × every rule, tail focus off): the VEGAS combination
  enumeration, asserted per the 4b convention — five means with the reported VEGAS standard
  error added, union and curve probes with binomial errors combined at the recorded
  evaluation count (5 × final evaluations), the exhaustive mass balance, and the
  additive-rule component-mean identity.

## Results

Every assert green across the 12 groups: 36 joint-path assert sets (every legacy
scenario·rule combination) plus 3 additive-method assert sets on the Independent groups, with
all 27 published report scenarios pinned.
Representative engine-versus-oracle comparisons from the verifying run (oracle/engine; the
full per-rule console records ship with the test output):

| Scenario (oracle / engine) | Fail mean | Total mean | Union | Notes |
|---|---|---|---|---|
| 2C1P Independent Additive (additive method) | 2.63155 / 2.60827 | 4.91737 / 4.88810 | 0.069266 / 0.069181 | σF 18.995/18.802, CVaR 160.5/158.8; report 77 pinned |
| 2C1P PerfectlyPositive Average (joint) | 2.16047 / 2.13414 | 3.47420 / 3.44555 | 0.067088 / 0.066983 | report 82 pinned |
| 2C1P PerfectlyNegative Minimum (joint) | 2.62304 / 2.60752 | 2.88649 / 2.87097 | 0.069391 / 0.069386 | report 88 pinned |
| 2C1P r = 0.5 Maximum (joint) | 2.56972 / 2.54239 | 4.34039 / 4.30760 | 0.068500 / 0.068489 | unpublished (legacy-only) |
| 2C2P Independent Additive (additive method) | 5.21749 / 5.21654 | 7.15672 / 7.15374 | 0.129355 / 0.129469 | σF 33.468/33.524, CVaR 273.8/274.5; report 89 pinned |
| 2C2P PerfectlyNegative Additive (joint) | 5.22886 / 5.21557 | 7.16931 / 7.15282 | 0.132883 / 0.133167 | report 91 pinned |
| 5C1P Independent Additive (additive method) | 7.79830 / 7.74241 | 13.3692 / 13.3063 | 0.209270 / 0.209045 | σF 44.244/43.899, CVaR 376.7/373.7; report 92 pinned |
| 5C1P PerfectlyPositive Average (joint) | 3.46223 / 3.41195 | 4.83222 / 4.77330 | 0.181672 / 0.181561 | report 97 pinned |
| 5C1P PerfectlyNegative Minimum (joint) | 7.17127 / 7.13281 | 7.27302 / 7.23515 | 0.213023 / 0.213306 | report 103 pinned (the mislabeled legacy method) |
| 5C1P r = 0.5 Additive (joint) | 7.84572 / 7.71148 | 13.4266 / 13.2543 | 0.197227 / 0.196347 | unpublished (legacy-only) |

Worst deviations sit inside their documented tolerances everywhere: additive-path (deterministic
engine) means within ≈ 0.9% of the 1M oracles (well inside 4·SE on these zero-inflated streams,
where SE_mean ≈ 0.7–1% relative), σ within 0.8%, CVaR within 1.1%; joint-path means within
≈ 2.2% at the widest (5C Positive — a heavy-σ shared-hazard stream where the 1M oracle itself
sits ~4 of its own SEs from the report's 10M value while the engine sits 0.1% from the report),
unions within 0.5%. All 27 published report scenarios pinned; the two 5C Positive/Negative
Additive report pins agree with the engine at 0.1–0.6%.

Physics visible in the numbers (the report's own tables confirm each): the ADDITIVE rule's
means are dependency-invariant (the Σ-of-expectations identity — tables 77/81/85 agree to
their sampling error); under perfectly negative 2-component hazards the four rules' failure
means coincide (simultaneous failures vanish, so the combination rule has nothing to
combine); under perfectly positive hazards the Minimum background equals the single-component
background (identical hazards make every component's non-failure consequence equal).

## Findings recorded

- **Engine fix (landed with this family):** the additive system's failure union folded the
  per-component probabilities in declaration order while every other additive aggregate uses
  the canonical-hash order — component reordering moved the union by 1–2 units in the last
  place, against the Phase 4b bit-inertness contract. The union now folds in canonical order;
  the 5-component shuffle/rename pin (`Test_5CompAdditive_ShuffleRename_BitIdentical`) holds
  bit-identically, extending the 4b 2-component pin.
- **Enumeration-truncation witnesses:** the joint mass balance and the additive-combine
  identity are exact (1e-9) at D = 2 and drift ~1e-6 relative at D = 5 — the documented
  Numerics `IndependentExclusive` convergence shortcut (its 1e-4 early-exit tolerance bounds
  the drift), surfaced honestly by the engine and asserted at that bound.
- **Report-pin allowance:** system pins carry a 2e-3 relative tabulation allowance (twice the
  single-component figure) — the z-grid bias adds coherently across summed components, and
  the report's own table 92 background sits ≈ 3.8 of its own standard errors from five times
  its single-component background constant (the widest internal spread in tables 77–103).
