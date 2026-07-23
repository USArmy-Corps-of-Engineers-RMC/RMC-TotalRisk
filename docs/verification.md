# Verification Strategy

> How the legacy `Test_TotalRisk` Monte Carlo suite becomes the formal `RMC.TotalRisk.Verification` suite, and the tolerance policy every verification test documents. Companion to [ROADMAP.md](ROADMAP.md) Phases 5–6 and 9–11 (numbering per the 2026-07-20 roadmap reorder). Results are documented per family in [docs/verification/](verification/README.md).

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
| 6 | `SystemRisk` (2-comp/2-PFM, 5-comp/1-PFM × correlation × aggregation), `RiskAnalysis` N-element combos, NFIP Assurance TOL 50/55/70, LHS variance-reduction |
| 9 | `Composite` family engine-level scenarios (mixture hazard/response/consequence behind the engine, bootstrap uncertainty), NFIP TOL 60/65 |
| 10 | `EventTree` (serialization round-trip + product oracle) |
| 11 | `BivariateRisk` (100M→1M), `DAMRAE`, BestFit import contract |
| Future | FDA integration (needs committed datasets via `verification-requests/`) |
