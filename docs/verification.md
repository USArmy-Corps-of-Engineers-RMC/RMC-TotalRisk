# Verification Strategy

> How the legacy `Test_TotalRisk` Monte Carlo suite becomes the formal `RMC.TotalRisk.Verification` suite, and the tolerance policy every verification test documents. Companion to [ROADMAP.md](ROADMAP.md) Phases 5–6.7 and 9–11 (numbering per the 2026-07-20 roadmap reorder; the 6.5–6.7 gap-closure phases added their own families). Results are documented per family in [docs/verification/](verification/README.md).

## What the legacy suite is

`C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\Test_TotalRisk\` contains ~128 substantive Monte Carlo methods that are **oracles without asserts**: each hand-computes a risk quantity from Numerics primitives (`LnNormal`, `Normal`, `Linear`, `MultivariateNormal`, `EmpiricalDistribution`, `Mixture`, …) with **fixed seeds** and prints the result to the debugger. They re-implement the engine's math independently — they do not call the engine (except the FDA integration tests). That independence is exactly what makes them verification oracles for the new engine.

Legacy configuration facts that carry over:

- Seeds are always fixed: `MersenneTwister(12345)` (dominant), `MersenneTwister(45678)`; correlated draws via `MultivariateNormal.GenerateRandomValues(M, seed)` with seeds 12345 / 67891 / 78910.
- Realizations: 10,000,000 standard; 1,000,000 in some NFIP variants; 100,000,000 in `Test_BivariateRisk`.
- All input datasets are inlined arrays (no external files), except the FDA tests (external `C:\Projects\…` data — deferred until committed via a verification request).
- Several method names are mislabeled relative to their bodies — **always port from the body, never trust the name.**

## Conversion pattern (per scenario)

1. **Port the oracle** to C# in `RMC.TotalRisk.Verification`, preserving the legacy fixed seeds, at **N = 1,000,000 realizations** (the 10M default drops 10×; tolerance scales accordingly; the 100M bivariate cases also drop to 1M with widened tolerance).
2. **Run the new engine** on the same scenario expressed as typed model objects (hazard/transform/response/consequence + `SystemComponent`/`FailureMode` + `RiskAnalysisOptions`).
3. **Assert engine vs oracle** within a statistical tolerance derived from the Monte Carlo standard error (below). Engine seeds are content-based by design, so engine-vs-oracle agreement is **statistical, never bit-exact**.
4. **Pin reproducibility separately**: same seed → bit-identical engine results at any thread count; shuffle components / rename / edit metadata → bit-identical (the v1 seed-dependency bug regression).
5. Optionally **pin oracle constants**: once captured at fixed seed + N, the oracle's own outputs are exact and serve as cheap regression anchors.

Hand-rolled math is **allowed inside verification oracles only** — everywhere else, the "never hand-roll numerics" rule applies. Oracles must stay independent: do not refactor them to call model-library code.

## Tolerance policy

For a Monte Carlo mean estimate over N realizations, the standard error is SE ≈ σ/√N, where σ is the realization-level standard deviation of the summed quantity. At N = 1,000,000 this is ≈ 0.1% of σ.

- **Engine-vs-oracle asserts use k·SE with k = 4 by default** (both estimates carry MC error; 4·SE on their difference keeps false failures ≪ 1e-4 per assert while still catching real math errors an order of magnitude smaller than any engineering significance).
- Each test documents its tolerance **and its derivation** in the test's XML docs: the output asserted, σ estimate source (computed in-run or captured), N, and k. Never widen a tolerance to make a test pass without recording why.
- Probability-of-failure asserts use the binomial SE ≈ √(p(1−p)/N).
- Curve (FN / loss-exceedance) checks assert at a small set of exceedance levels, not all 200 stratification bins; tail bins with < ~100 expected exceedances get proportionally wider tolerances or are excluded (documented per test).
- Deep re-runs: any converted test can be re-run at the legacy N = 10M by a user-set realization constant; this is deliberate and user-triggered, never part of the standard suite.

### Finalized policy (Phase 6)

The policy above is **final** as of Phase 6, with the following amendments calibrated by the
system-risk, combos, NFIP, and variance-reduction conversions (each figure is measured and
documented in the owning test's XML docs):

- **Deterministic engine paths** (adaptive Gauss–Kronrod, the additive lattice convolution,
  reliability integrals): asserts carry the oracle's k·SE only, plus documented deterministic
  allowances where an interim applies — the N7 recorded-mass interim (≤ ~1e-5 relative on
  means), loss-exceedance output thinning (probes read resolution 1000 — the Phase 5 finding),
  convolution-lattice quantization (system probes run 65,536 nodes to keep it an order below
  the binomial tolerance), and log-log interpolation of frequency profiles at a threshold
  (≤ 1e-4 relative, the NFIP engine-versus-exact allowance).
- **Monte Carlo engine paths** (the joint VEGAS method): stream-mean asserts combine the
  oracle SE with the run's reported VEGAS standard error additively (the total-mean SE is the
  conservative proxy for every stream); probability and curve-ordinate asserts combine
  binomial errors in quadrature at the recorded evaluation count (five recording passes × the
  final evaluations). Tail focus is off (γ = 1) in oracle-parity runs; γ > 1 is audited by the
  dedicated N9 gate.
- **Report constant pins**: engine versus the 2024 report's published 10M Monte Carlo values
  at k·σ̂/√10⁷ (σ̂ from the converted oracle's matching stream) plus a relative
  tabulation allowance — 1e-3 for single-component scenarios, 2e-3 for multi-component
  scenarios (the z-grid bias adds coherently across summed components) — plus the reported
  VEGAS error on joint-method pins. The NFIP Table 104 pins bind the oracle at the combined
  1M/10M binomial error and the engine at a 0.5% v1.0-parity band.
- **Enumeration-truncation witnesses**: the joint method's exhaustive mass balance and its
  additive-rule component-mean identity are exact (1e-9) at D = 2 and bounded by the
  documented Numerics `IndependentExclusive` convergence tolerance (1e-4; observed ~1e-6)
  above — the engine's honest surface of the upstream shortcut, re-verified when the Numerics
  follow-up lands.
- **Ensemble (knowledge-uncertainty) asserts**: realization-for-realization parity against
  exact per-realization oracles where the sampling walk is deterministic (D = 0 posterior
  injection); ensemble summary statistics at the deterministic allowance; scheme-level
  variance-reduction asserts on replicate variances with health guards proving the scenario
  discriminates.

## v0.13 policy: means vs tails after the Phase 4 engine corrections

The Phase 4 / 4b / 4c engine corrections (arch doc v0.13: exact LEC construction, weighted-Welford
moments, mixture-branch exposure enumeration, FFT system convolution, real joint-combination
enumeration) **deliberately change results the legacy engine computes wrongly** — specifically the LEC
tail and everything derived from it. The verification split is therefore:

- **Means are v1.0-parity.** Mean total / incremental / irreducible / failure / non-failure risk and
  the failure probabilities are algebraically unchanged by these fixes (e.g. the mixture identity
  `Σ wᵢ P_F fᵢ = P_F Σ wᵢ fᵢ`, and convolved-mean = Σ component means). Verify them **closely** against
  the legacy oracles — they are the free regression gate that the fixes did not disturb the correct
  quantity. Tolerance is the usual k·SE.
- **Tails are Monte-Carlo-parity, not v1.0-parity.** Standard deviation, VaR, CVaR, threshold
  assurance, and the F-N curve tail are validated against **new, independently written brute-force
  Monte Carlo oracles** (draw the full model per realization — mixture branch, fragility, consequence
  — and form the empirical LEC), never against the legacy engine's tail, which is wrong there. Where a
  legacy oracle body itself encodes the conditional-mean collapse (multi-D joint) or the mixture-mean
  flattening (composite consequence under mean-only), port it **for the mean only** and pair it with an
  MC tail oracle.

This is a ratified deliberate departure in the spirit of content-based seeding and JSON results — the
spec wins over legacy where legacy is demonstrably flawed (porting rule, CLAUDE.md). Each affected test
documents which outputs are v1.0-parity and which are MC-parity, with the MC oracle's own SE in the
tolerance derivation.

## Suite mechanics

- `RMC.TotalRisk.Verification` is excluded from Release builds (no `.sln` Release `Build.0` line) and carries `[assembly: TestCategory("Verification")]`. The fast PR gate (`dotnet test -c Release`) never runs it.
- Run **deliberately**: the family you touched, after touching it (`dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~<Family>Verification"`); the full suite on demand.
- Verification classes are named `<Family>Verification` (e.g., `JointFailuresVerification`, `SystemRiskVerification`, `NfipAssuranceVerification`) and mirror the oracle family files of the legacy suite.
- Any committed benchmark data files (later phases) live under the Verification project, are copied to output, and are read from `AppContext.BaseDirectory` with `CultureInfo.InvariantCulture` parsing.

## Function-level verification (landed pre-Phase-4)

The first two families verify **input functions in isolation** — the way the 2024 report's
§Composite Consequence Function does — establishing the suite mechanics (fixed seeds, N = 10⁶,
k·SE documentation, reproducibility pins, results pages) before the engine phases inherit them:

- `CompositeConsequenceVerification` — the report's Additive/Average/Mixture scenarios against
  exact Normal-theory solutions, the Numerics `Mixture` inverse-CDF analytic oracle, an
  independent MC oracle, and the report's published constants
  ([results](verification/composite-consequence.md)).
- `ParametricConsequenceVerification` — the greenfield closed-form family against exact
  lognormal theory and an independent MC oracle
  ([results](verification/parametric-consequence.md)).

The engine-level composite scenarios (legacy `Test_Composite.vb`: day/night mixture behind a
hazard curve and fragility, weight 0.45, `MersenneTwister(12345)` at 10M) still convert in
their scheduled phase below — the function-level families do not replace them.

## Conversion order (mirrors the roadmap)

| Phase | Families |
|---|---|
| Pre-4 (landed 2026-07-21) | `CompositeConsequence`, `ParametricConsequence` function-level families (above) |
| 4 | Engine reproducibility regressions (shuffle/rename/metadata → bit-identical; same seed → bit-identical at any thread count); single-component mean parity + a new MC tail oracle for the exact-LEC and mixture-exposure fixes |
| 4b | Additive FFT convolution (convolved-mean = Σ means; MC tail cross-check) and joint combination enumeration (mean parity + MC tail) — both means-vs-tails per the v0.13 policy above |
| 5 (landed 2026-07-23) | `JointFailures` (1-comp 2/5-PFM × dependency × aggregation; 32 legacy methods consolidated to 8 shared-sampling groups), `CompetingFailures` (incl. the corrected 5-PFM Positive body — the legacy code path was broken), `CommonCause` (the `_CCA` pair merged into Independent — identical streams), `MutuallyExclusive`, `EAD` (+ exact closed form); **plus two new families beyond the legacy suite**: `SingleComponentUncertainty` (two-loop knowledge-uncertainty oracle with an exact inner integral, Q-N coupling counter-pin, `ParametricResponse` posterior-injection anchor) and `CombinationMethodConsistency` (engine-only property pins: union invariance across methods, Fréchet bound ordering, background/`RiskIntegrand` invariance, reliability parity). Results: [verification/](verification/README.md) |
| 6 (landed 2026-07-23) | `SystemRiskMatrix` (the legacy `Test_MC_SystemRisk` 36 methods consolidated to 12 dependency groups: 2-comp/2-PFM, 2-comp/1-PFM, 5-comp/1-PFM × {Independent, Positive, Negative, Correlation} × rules, + report tables 77–103 pins), `RiskAnalysisCombos` (the `Test_RiskAnalysis` combos: 1-comp 3/4-PFM, 3/4-comp systems, the r = −0.25 average scenario; every legacy method's disposition documented), `NfipAssurance` (TOL 50/55/70 API oracles + exact quadrature + Table 104 pins + the full-uncertainty assurance ensemble per the TR NFIP appendix), `LhsVarianceReduction` (MC vs LHS at N = 1k over replicate runs). Results: [verification/](verification/README.md) |
| 6.5 (landed 2026-07-23) | `MultiConsequence` (the declared consequence-type axis: two-type MC oracle, single-type bit-identity pin, dedicated-primary quadrature parity, additive/joint system identities). Results: [verification/](verification/README.md) |
| 6.6 (landed 2026-07-24) | Four NEW families beyond the legacy suite (the diagnostics carry no legacy oracles): `RiskProfile` (Q-T pushforward exact at knots + bit-identical non-profile outputs, threshold knot equivalence, cumulative/SRP profiles vs an independent dense-quadrature oracle, five-stream band restoration), `Contribution` (Σ-identities vs `MassBalance`/`Mean` at 1e-12, joint-Additive ≡ marginal means, quadrature oracles for the adjusted-marginal methods, additive Poisson-binomial φ vs brute enumeration, joint-system sums vs recorded mass), `ScalarUncertainty` (closed-form ensemble-quantile targets on a linear knowledge map, posterior APF CI), `Sensitivity` (analytic corr(U, Φ⁻¹(U)) = √(3/π) Pearson pin at the Fisher-z bound, rank exactness + inert-input null band, independent hazard-level response oracle, content-seeded reproducibility). Results: [verification/](verification/README.md) |
| 6.7 (landed 2026-07-24) | `CascadeEndStates` (8 tests, NEW family — the cascade design carries no legacy oracles): the partial-damage two-stage cascade vs its natural MC oracle (MT 12345/45678, N = 10⁶), the joint/ME/competing across-unit matrix, the bit-exact saturated-stage single-stage equivalence, reliability-mode APF vs the Rao-Blackwellized oracle, additive/joint system smokes, port/polarity reproducibility pins. Results: [verification/cascade-end-states.md](verification/cascade-end-states.md) |
| 7 (landed 2026-07-25) | `ClosedFormFunctions` (4 tests, NEW family — no legacy oracles exist for `LinearTransform`/`PowerTransform`/`NonparametricHazard`): the 2024 report's SF-8 vs HEC-FDA Table 38 pins + the independent legacy-pipeline re-derivation oracle, transform-chain ensembles vs flat MC oracles at MT(12345) N = 10⁶, D = 0 dense-quadrature parity, and the nonparametric reliability AFP with bit-identity pins. Results: [verification/closed-form-functions.md](verification/closed-form-functions.md) |
| 9 | `Composite` family engine-level scenarios (mixture hazard/response/consequence behind the engine, bootstrap uncertainty), NFIP TOL 60/65 |
| 10 | `EventTree` (serialization round-trip + product oracle) |
| 11 | `BivariateRisk` (100M→1M), `DAMRAE`, BestFit import contract |
| Future | FDA integration (needs committed datasets via `verification-requests/`) |
