# Verification Results

Living verification documentation for the v1.1 model library — the per-family Markdown
counterpart of the v1.0 Word report (*Verification of the RMC-TotalRisk Software*, 2024,
`docs/reports/`). Each page documents one verified family the way the report's sections do:
the math, the scenario inputs, the expected values and where they come from (exact solution,
analytic oracle, independent Monte Carlo oracle, or published report constants), the engine
results from an actual run of the family's verification tests, and the percent differences.

The **policy** (oracle-conversion strategy, fixed seeds, realization counts, and the k·SE
assert tolerances) lives in [docs/verification.md](../verification.md). These pages record the
**results**; the tests under `src/RMC.TotalRisk.Verification/` are the executable source of
truth, and every assert documents its own tolerance derivation in its XML docs.

## Performance metric

Every comparison is assessed with the report's percent-difference metric:

> % difference = |computed − expected| / |expected| × 100

with the report's performance ratings:

| Rating | Percent difference |
|---|---|
| Very good | ≤ 1% |
| Satisfactory | 1% – 5% |
| Unsatisfactory | > 5% |

Function-level and risk-analysis verification carry a Monte Carlo component, so the relaxed
(5%) bound is the formal acceptance line — but the **assert** mechanism in the tests is
stricter than the reporting metric: engine estimates must land within k·SE (k = 4) of the
expected value, where SE is the independent-sampling Monte Carlo standard error at the test's
realization count. At N = 1,000,000 that typically binds the estimates to well under 0.1%
relative. Latin hypercube stratification makes the engine estimators tighter still, so the
documented tolerances are conservative in the engine's favor.

## Running the verification suite

Verification runs **deliberately** — it is excluded from Release builds and from the fast PR
gate by construction:

```bash
dotnet test src/RMC.TotalRisk.Verification                     # the full suite (Debug)
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~CompositeConsequenceVerification"
```

> ⚠ .NET 10 `dotnet test` gotcha: `--filter` placed **before** `--` is silently ignored for
> MSTest.Sdk projects. Scope by project path, or place the filter after `--` (which fails the
> run if the filter matches zero tests in the assembly).

Per-test console output (the engine estimates recorded in these pages) is captured by running
the test executable with a TRX report:
`RMC.TotalRisk.Verification.exe --report-trx --results-directory <dir>`.

## Verified families

| Family | Test class | Anchor | Status |
|---|---|---|---|
| [Composite consequence](composite-consequence.md) | `CompositeConsequenceVerification` | 2024 report §Composite Consequence Function (exact Normal-theory solutions + Numerics `Mixture` inverse CDF + report constants) | ✅ Verified (2026-07-21) |
| [Parametric consequence](parametric-consequence.md) | `ParametricConsequenceVerification` | Closed-form algebra + exact lognormal theory + independent MC oracle (greenfield family — no legacy oracle) | ✅ Verified (2026-07-21) |
| [Engine reproducibility](engine-reproducibility.md) | `EngineReproducibilityVerification` | The v1 seed-dependency bug regression: repeated-run, metadata-edit, and round-trip bit-identity of full results JSON | ✅ Verified (2026-07-22) |
| [Single-component mean parity](single-component-mean-parity.md) | `SingleComponentMeanParityVerification` | Legacy-style MC oracle over shared tables at `MersenneTwister(12345)`, N = 10⁶ — the five summary means + AFP (the free regression gate) | ✅ Verified (2026-07-22) |
| [Exact LEC / mixture tail](exact-lec-tail.md) | `ExactLecTailVerification` | New brute-force MC oracle (seeds 12345/45678/78910, N = 10⁶) — σ, exceedance ordinates, VaR, CVaR for the day/night mixture (Monte-Carlo-parity per the v0.13 policy) | ✅ Verified (2026-07-22) |
| [Multi-component system risk](system-risk.md) | `SystemRiskVerification` | New brute-force event-level MC oracles (seeds 12345/45678, N = 10⁶) — the additive lattice convolution's full system LEC, the correlated joint VEGAS path, the γ tail-focus audit (N9 gate), and the system reproducibility pins | ✅ Verified (2026-07-23) |
| [Joint failure modes](joint-failures.md) | `JointFailuresVerification` | Legacy `Test_MC_JointFailures` ported (32 methods consolidated to 8 dependency groups, seeds 12345/12345, N = 10⁶) + 2024 report constants (tables 61–76) — means, unions, σ, LEC probes, VaR/CVaR across {2, 5}-PFM × 4 dependencies × 4 rules | ✅ Verified (2026-07-23) |
| [Competing failure modes](competing-failures.md) | `CompetingFailuresVerification` | Legacy `Test_MC_CompetingFailures` ported (8 methods incl. the corrected 5-PFM Positive body) + report constants (tables 59–60) — the weak-link CIF path across 4 dependencies | ✅ Verified (2026-07-23) |
| [Common cause adjustment](common-cause.md) | `CommonCauseVerification` | Legacy `Test_MC_CommonCause` ported (10 methods, the `_CCA` pair merged into Independent; seeds 12345/45678) + report constants (tables 55–58) — the CCA factor across 4 dependencies | ✅ Verified (2026-07-23) |
| [Mutually exclusive](mutually-exclusive.md) | `MutuallyExclusiveVerification` | Legacy `Test_MC_MutuallyExclusive` ported (seeds 12345/45678) — the capped-sum normalization with its warning surface pinned | ✅ Verified (2026-07-23) |
| [Expected annual damage](ead.md) | `EadVerification` | Legacy `Test_EAD` ported + exact closed form (mean, σ, VaR, CVaR of the clamped piecewise-linear curve) — two equivalent engine mappings (background and always-fail) | ✅ Verified (2026-07-23) |
| [Single-component uncertainty](single-component-uncertainty.md) | `SingleComponentUncertaintyVerification` | NEW two-loop oracle with an exact (per-interval Simpson) inner integral — ensemble grand means and percentiles, the 90% confidence LEC band, the Q-N coupling pin with a decoupled counter-pin, LHS/MC scheme agreement, and the realization-for-realization `ParametricResponse` posterior-injection anchor | ✅ Verified (2026-07-23) |
| [Combination-method consistency](combination-method-consistency.md) | `CombinationMethodConsistencyVerification` | NEW engine-only property pins from the failure-mode-combination technical note: the §6.1 union invariance across methods, the Fréchet bound ordering (with its D = 2 negative = exclusive degeneracy), background invariance, `RiskIntegrand` invariance, reliability parity | ✅ Verified (2026-07-23) |
| [System risk matrix](system-risk-matrix.md) | `SystemRiskMatrixVerification` | Legacy `Test_MC_SystemRisk` ported (36 methods consolidated to 12 dependency groups; seeds 67891/78910/12345/45678, N = 10⁶) + 2024 report constants (tables 77–103) — 2-comp/2-PFM, 2-comp/1-PFM, and 5-comp/1-PFM across 4 dependencies × 4 rules, additive and joint engine paths | ✅ Verified (2026-07-23) |
| [Risk-analysis combos](risk-analysis-combos.md) | `RiskAnalysisCombosVerification` | Legacy `Test_RiskAnalysis` combos ported (1-comp 3/4-PFM negative groups, 3/4-comp negative systems, the r = −0.25 average scenario; seeds 12345, native N = 10⁶) with every legacy method's disposition documented | ✅ Verified (2026-07-23) |
| [NFIP assurance](nfip-assurance.md) | `NfipAssuranceVerification` | Legacy `Test_NFIP_Assurance_TOL_50/55/70` ported (seed 45678, N = 10⁶) + exact quadrature + 2024 report Table 104 — the annual probability of inundation in reliability mode (both hazard types), and the TR-appendix full-uncertainty assurance ensemble over an injected LP3 posterior | ✅ Verified (2026-07-23) |
| [LHS variance reduction](lhs-variance-reduction.md) | `LhsVarianceReductionVerification` | NEW roadmap test: MC vs LHS at N = 1k over 5 replicate pairs — replicate variance ratio, unbiasedness, and the deterministic mean-only linearity anchor | ✅ Verified (2026-07-23) |
| [Multi-consequence axis](multi-consequence.md) | `MultiConsequenceVerification` | NEW Phase 6.5 family (Q-U closure): a two-type MC oracle at `MersenneTwister(12345)`, N = 10⁶ verifying both axes of one engine pass, the single-type bit-identity pin, dedicated-primary quadrature parity, cross-model ensemble parity, and the additive/joint per-type system identities | ✅ Verified (2026-07-23) |
| [Risk profiles](risk-profiles.md) | `RiskProfileVerification` | NEW Phase 6.6 family (Q-T closure + the profile catalog): the profile-axis pushforward exact at knots with bit-identical non-profile outputs, the hazard-threshold knot equivalence, the cumulative and response profiles vs an independent dense-quadrature oracle on a two-mode joint scenario, reliability-mode presence, and the five-stream band parity restoration | ✅ Verified (2026-07-24) |
| [% contribution](contribution.md) | `ContributionVerification` | NEW Phase 6.6 family (no v1.0 counterpart): the Shapley/consequence-proportional exclusive-event attribution vs adjusted-marginal quadrature oracles (ME, CCA), the Eq. 14 incidence oracle (competing), the Sum-rule marginal and Maximum-rule proportional-split oracles (joint), the additive system's brute-force Shapley enumeration with reorder bit-inertness, and the joint-system Σ identities with reproducibility pins | ✅ Verified (2026-07-24) |
| [Scalar-measure confidence intervals](scalar-uncertainty.md) | `ScalarUncertaintyVerification` | NEW Phase 6.6 family: the `EnsembleSummary` percentile reduction vs closed-form quantile targets on an exactly linear knowledge map (self-derived stratified-LHS bound), the sequential-mean and bit-identical round-trip/reproducibility pins, and the convergence-indicator evidence | ✅ Verified (2026-07-24) |
| [Sensitivity](sensitivity.md) | `SensitivityVerification` | NEW Phase 6.6 family (the unified engine replacing the legacy tornado port — ratified scope amendment): the analytic corr(U, Φ⁻¹(U)) = √(3/π) Pearson pin at the Fisher-z bound, rank exactness with the inert-input 4/√N null band, the independent hazard-level response oracle with affine invariance, and content-seeded bit-reproducibility | ✅ Verified (2026-07-24) |
| [Cascading end states](cascade-end-states.md) | `CascadeEndStateVerification` | NEW Phase 6.7 family (the Q-X closure, arch doc §7.9): the partial-damage cascade vs its natural MC oracle (final-polarity APF, conditional complement, sibling-paired excess), the across-unit joint/mutually-exclusive/competing matrix, the bit-exact saturated-stage single-stage equivalence, reliability-mode APF, system aggregation smokes, and the port/polarity reproducibility pins | ✅ Verified (2026-07-24) |
| [Closed-form functions](closed-form-functions.md) | `ClosedFormFunctionsVerification` | NEW Phase 7 family (no legacy oracles exist — Dev-repo sweep): the 2024 report's SF-8 vs HEC-FDA Table 38 pins (all 20 constants, log10 ±2SD quantiles) + an independent legacy-pipeline re-derivation (the Brent-vs-closed-form optimization-equivalence anchor), transform-chain ensembles vs flat MC oracles (`MersenneTwister(12345)`, N = 10⁶ — the first engine passage of the closed-form transforms), D = 0 dense-quadrature parity, and the nonparametric reliability AFP vs independent knot-semantics oracles with bit-identity pins | ✅ Verified (2026-07-25) |

| [Composite hazard](composite-hazard.md) | `CompositeHazardVerification` | NEW Phase 9 family: 2024 report Tables 44–46 (R `mistr` mixture curve and bootstrap bands) + an exact index-parity oracle rebuilt from Numerics `BootstrapAnalysis`/`Mixture` at the legacy seeds + competing-risks maximum-rule closed forms. Records two findings: v1.1 is ~6× closer to the analytic mixture than two published mid-distribution rows, and the upstream bootstrap summary reduction is order-nondeterministic | ✅ Verified (2026-07-25) |
| [Composite response](composite-response.md) | `CompositeResponseVerification` | NEW Phase 9 family: the shared Table 44 scenario read on the fragility (probability) axis + exact weakest-link probability identities (union under independence, maximum under comonotonic) + a hazard-grid index-parity band oracle | ✅ Verified (2026-07-25) |
| [Composite transform](composite-transform.md) | `CompositeTransformVerification` | NEW Phase 9 greenfield family (no legacy implementation, no report table): exact linear-combination algebra forward and inverse, exact Normal theory for the ω²-additive ensemble variance (the check that discriminates independent from co-monotonic child seeding), and a realization-for-realization identity against the independently reproduced child-seed recipe | ✅ Verified (2026-07-25) |
| [Composite engine scenarios](composite-engine.md) | `CompositeEngineVerification` | All four executable `Test_Composite.vb` configurations and the risk-analysis mixture identity behind the full engine, checked against independent fixed quadrature and child-engine identities | ✅ Verified (2026-07-27) |
| [Event-tree response — first slice](event-tree.md) | `EventTreeVerification` | Independent analytic path products and over-allocation normalization + realization-for-realization aligned-table LHS parity | 🟡 First verified slice (3/3, 2026-07-28); Phase 10A verification remains open |

## Remaining tree-response verification

The first `EventTreeVerification` slice is executable and documented above. Phase 10A still owes
legacy conversion, link/clone, branch-routing Monte Carlo, graph-integration, LHS variance,
reproducibility, and performance anchors. Phase 10B will add `FaultTreeVerification`. The complete gates are specified in the normative [event-tree and fault-tree response design](../requirements/EVENT_AND_FAULT_TREE_RESPONSE_DESIGN.md) §15.

**Forensic traceability closure.** The
[legacy traceability matrix](legacy-traceability.csv) accounts for all 141 legacy `Test_*`
methods and maps every applicable method to an existing current test. It also maps all 49
system and joint-failure configurations in the 2024 verification report. The repository
validator reconciles both source trees and fails on a missing method, stale target, unsupported
disposition, or missing report scenario. The active TOL 60/65 bootstrap-hazard bodies and
engine-level composite bodies are now covered. The FDA/NFIP variant is explicitly obsolete;
future-feature and external-data blockers remain visible in the matrix rather than implied covered.
