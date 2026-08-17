# Verification Strategy

> How the legacy `Test_TotalRisk` Monte Carlo suite becomes the formal `RMC.TotalRisk.Verification` suite, and the tolerance policy every verification test documents. Results are documented per family in [docs/verification/](verification/README.md).

## What the legacy suite is

`C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\Test_TotalRisk\` contains 142 inventoried test methods, including the event-tree `TestIO` method outside the usual `Test_*` naming convention. The audited disposition is 116 covered, consolidated, or stream-identical methods; 16 empty placeholders; 3 debugger-only/non-oracle workbenches; 6 standalone FDA importer workflows blocked on external data; and 1 obsolete FDA/NFIP variant. The substantive oracles hand-compute risk quantities from Numerics primitives with fixed seeds and do not call the engine, which makes them independent references.

Legacy configuration facts that carry over:

- Seeds are always fixed: `MersenneTwister(12345)` (dominant), `MersenneTwister(45678)`; correlated draws via `MultivariateNormal.GenerateRandomValues(M, seed)` with seeds 12345 / 67891 / 78910.
- Realizations: 10,000,000 standard; 1,000,000 in some NFIP variants; 100,000,000 in `Test_BivariateRisk`.
- All applicable engine-oracle datasets are inlined. Six standalone FDA importer workflows retain external `C:\Projects\…` dependencies and are classified `BlockedExternalData`; the FDA/NFIP variant is explicitly obsolete.
- Several method names are mislabeled relative to their bodies — **always port from the body, never trust the name.**

The committed [legacy traceability matrix](verification/legacy-traceability.csv) accounts for
every source method and every one of the verification report's 49 system/joint-failure
configurations. `scripts/validate-verification-traceability.ps1` reconciles the matrix against
both repositories when the legacy checkout is present and validates every current test target.
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

### Finalized policy

The policy above is **final**, with the following amendments calibrated by the
system-risk, combos, NFIP, and variance-reduction conversions (each figure is measured and
documented in the owning test's XML docs):

- **Deterministic engine paths** (adaptive Gauss–Kronrod, the additive lattice convolution,
  reliability integrals): asserts carry the oracle's k·SE only, plus documented deterministic
  allowances — the quadrature mass-accounting residual (≤ ~1e-5 relative on means; measured
  ≈ 3e-6 under the earlier midpoint-trapezoid mass partition, since replaced by the
  recorded-mass ledger, whose A/B measurement against a 4,000,000-point dense reference moved
  the relative error 5.664e-7 → 3.996e-11 — the recorded bounds are unchanged and hold
  a fortiori), loss-exceedance output thinning (probes read resolution 1000),
  convolution-lattice quantization (system probes run 65,536 nodes to keep it an order below
  the binomial tolerance), and log-log interpolation of frequency profiles at a threshold
  (≤ 1e-4 relative, the NFIP engine-versus-exact allowance).
- **Monte Carlo engine paths** (the joint VEGAS method): stream-mean asserts combine the
  oracle SE with the run's reported VEGAS standard error additively (the total-mean SE is the
  conservative proxy for every stream); probability and curve-ordinate asserts combine
  binomial errors in quadrature at the recorded evaluation count (five recording passes × the
  final evaluations). Tail focus is off (γ = 1) in oracle-parity runs; γ > 1 is audited by the
  dedicated tail-focus audit in the system-risk family.
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

## The means-versus-tails policy

The v1.1 engine corrections (exact LEC construction, stable weighted central
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

This is a deliberate, approved departure in the spirit of content-based seeding and JSON results — the
porting rule: the spec wins over legacy where legacy is demonstrably flawed. Each affected test
documents which outputs are v1.0-parity and which are MC-parity, with the MC oracle's own SE in the
tolerance derivation.

## Suite mechanics

- `RMC.TotalRisk.Verification` is excluded from Release builds (no `.sln` Release `Build.0` line) and carries `[assembly: TestCategory("Verification")]`. The fast PR gate (`dotnet test -c Release`) never runs it.
- Run **deliberately**: the family you touched, after touching it (`dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~<Family>Verification"`); the full suite on demand.
- Verification classes are named `<Family>Verification` (e.g., `JointFailuresVerification`, `SystemRiskVerification`, `NfipAssuranceVerification`) and mirror the oracle family files of the legacy suite.
- Any committed benchmark data files (later phases) live under the Verification project, are copied to output, and are read from `AppContext.BaseDirectory` with `CultureInfo.InvariantCulture` parsing.

## Function-level verification

The first two families verify **input functions in isolation** — the way the 2024 report's
§Composite Consequence Function does — establishing the suite mechanics (fixed seeds, N = 10⁶,
k·SE documentation, reproducibility pins, results pages) that the engine-level families inherit:

- `CompositeConsequenceVerification` — the report's Additive/Average/Mixture scenarios against
  exact Normal-theory solutions, the Numerics `Mixture` inverse-CDF analytic oracle, an
  independent MC oracle, and the report's published constants
  ([results](verification/composite-consequence.md)).
- `ParametricConsequenceVerification` — the greenfield closed-form family against exact
  lognormal theory and an independent MC oracle
  ([results](verification/parametric-consequence.md)).

The engine-level composite scenarios (legacy `Test_Composite.vb`: day/night mixture behind a
hazard curve and fragility, weight 0.45, `MersenneTwister(12345)` at 10M) are converted
separately as the `CompositeEngine` family — the function-level families do not replace them.

## Conversion map (family × oracle source)

Run-of-record dates, test counts, tolerances, and pinned values live on each family's results
page, indexed in [docs/verification/](verification/README.md).

| Families (landed) | Oracle source |
|---|---|
| `CompositeConsequence`, `ParametricConsequence` (2026-07-21) | The function-level families above |
| `EngineReproducibility`, `SingleComponentMeanParity`, `ExactLecTail` (2026-07-22) | Engine seed regressions (shuffle/rename/metadata → bit-identical; same seed → bit-identical at any thread count); single-component mean parity vs a legacy-style MC oracle; a new brute-force MC tail oracle for the exact-LEC and mixture-exposure corrections |
| `SystemRisk` (2026-07-23) | Additive FFT convolution (convolved-mean = Σ means; MC tail cross-check) and joint combination enumeration (mean parity + MC tail; the dedicated γ tail-focus audit) — means-versus-tails per the policy above |
| `JointFailures`, `CompetingFailures`, `CommonCause`, `MutuallyExclusive`, `Ead` (2026-07-23) | The legacy single-component families: `Test_MC_JointFailures` (1-comp 2/5-PFM × dependency × aggregation; 32 legacy methods consolidated to 8 shared-sampling groups), `Test_MC_CompetingFailures` (incl. the corrected 5-PFM Positive body — the legacy code path was broken), `Test_MC_CommonCause` (the `_CCA` pair merged into Independent — identical streams), `Test_MC_MutuallyExclusive`, and `Test_EAD` (+ exact closed form) |
| `SingleComponentUncertainty`, `CombinationMethodConsistency` (2026-07-23) | New families beyond the legacy suite: a two-loop knowledge-uncertainty oracle with an exact inner integral, coupling counter-pin, and `ParametricResponse` posterior-injection anchor; engine-only property pins (union invariance across methods, Fréchet bound ordering, background/`RiskIntegrand` invariance, reliability parity) |
| `SystemRiskMatrix`, `RiskAnalysisCombos`, `NfipAssurance`, `LhsVarianceReduction` (2026-07-23) | The legacy `Test_MC_SystemRisk` 36 methods consolidated to 12 dependency groups (2-comp/2-PFM, 2-comp/1-PFM, 5-comp/1-PFM × {Independent, Positive, Negative, Correlation} × rules) + report tables 77–103 pins; the `Test_RiskAnalysis` combos (1-comp 3/4-PFM, 3/4-comp systems, the r = −0.25 average scenario; every legacy method's disposition documented); the TOL 50/55/70 API oracles + exact quadrature + Table 104 pins + the full-uncertainty assurance ensemble per the TR NFIP appendix; MC vs LHS replicate variances at N = 1k |
| `MultiConsequence` (2026-07-23) | The declared consequence-type axis: two-type MC oracle, single-type bit-identity pin, dedicated-primary quadrature parity, additive/joint system identities |
| `RiskProfile`, `Contribution`, `ScalarUncertainty`, `Sensitivity` (2026-07-24) | New families (the diagnostics carry no legacy oracles): pushforward exact at knots + cumulative/SRP profiles vs an independent dense-quadrature oracle; Σ-identities at 1e-12, quadrature oracles for the adjusted-marginal methods, and additive Poisson-binomial φ vs brute enumeration; closed-form ensemble-quantile targets on a linear knowledge map; the analytic corr(U, Φ⁻¹(U)) = √(3/π) Pearson pin, rank exactness + inert-input null band, and an independent hazard-level response oracle |
| `CascadeEndStates` (2026-07-24) | New family (cascades carry no legacy oracles): the partial-damage two-stage cascade vs its natural MC oracle (MT 12345/45678, N = 10⁶), the joint/ME/competing across-unit matrix, the bit-exact saturated-stage single-stage equivalence, reliability-mode APF vs the Rao-Blackwellized oracle, additive/joint system smokes, port/polarity reproducibility pins |
| `ClosedFormFunctions` (2026-07-25) | New family (no legacy oracles exist for `LinearTransform`/`PowerTransform`/`NonparametricHazard`): the 2024 report's SF-8 vs HEC-FDA Table 38 pins + the independent legacy-pipeline re-derivation oracle, transform-chain ensembles vs flat MC oracles at MT(12345) N = 10⁶, D = 0 dense-quadrature parity, and the nonparametric reliability AFP with bit-identity pins |
| `CompositeHazard`, `CompositeResponse`, `CompositeTransform` (2026-07-25) | Report Tables 44–46 + exact mixture/competing-risks identities and index-parity uncertainty; the report fragility-axis mixture + exact weakest-link identities; exact linear algebra + ω²-additive Normal theory (greenfield) |
| `CompositeEngine` + the NFIP TOL 60/65 closure (2026-07-27) | Closes all four `Test_Composite.vb` configurations plus the risk-analysis mixture identity against independent quadrature; the active TOL 60/65 bootstrap-hazard bodies against conditional quadrature |
| `EventTree` (2026-07-28) | Asserted legacy XML conversion plus analytic path-product/mass-conservation, explicit-clone/link equivalence, branch-routing MC, graph-connected equivalence, LHS, and reproducibility. Legacy `Test_Product` is not an oracle: it has no assertion and does not construct an event tree |
| `FaultTree`, `FaultTreeMonteCarlo` (2026-07-31) | Greenfield: exhaustive Boolean enumeration, closed-form gates/repeated-event identities, coherent cut-set inspection, independent Boolean MC, graph-connected equivalence, LHS, reproducibility, ROBDD resource/performance gates, and node-importance oracles |
| `BivariateRisk`, `CopulaDependence` (2026-08-07) | The legacy `Test_Bivariate_Risk` oracle (100M → 1M, seed 45678) + the exact union-grid SRP closed form and the re-anchored DAMRAE chained-bilinear scenario; independence exactness, Normal/Clayton analytic anchors, the bin-convergence study, and posterior-injected marginal realization parity |
| Not applicable | The FDA/NFIP variant is obsolete by technical-authority decision. Six standalone FDA importer workflows remain separately classified as external-data blockers. |
