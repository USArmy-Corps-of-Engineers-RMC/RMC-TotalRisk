# Verification Strategy

> How the legacy `Test_TotalRisk` Monte Carlo suite becomes the formal `RMC.TotalRisk.Verification` suite, and the tolerance policy every verification test documents. Companion to [ROADMAP.md](ROADMAP.md) Phases 5–6 and 9–11 (numbering per the 2026-07-20 roadmap reorder).

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

## Suite mechanics

- `RMC.TotalRisk.Verification` is excluded from Release builds (no `.sln` Release `Build.0` line) and carries `[assembly: TestCategory("Verification")]`. The fast PR gate (`dotnet test -c Release`) never runs it.
- Run **deliberately**: the family you touched, after touching it (`dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~<Family>Verification"`); the full suite on demand.
- Verification classes are named `<Family>Verification` (e.g., `JointFailuresVerification`, `SystemRiskVerification`, `NfipAssuranceVerification`) and mirror the oracle family files of the legacy suite.
- Any committed benchmark data files (later phases) live under the Verification project, are copied to output, and are read from `AppContext.BaseDirectory` with `CultureInfo.InvariantCulture` parsing.

## Conversion order (mirrors the roadmap)

| Phase | Families |
|---|---|
| 4 | Engine reproducibility regressions (shuffle/rename/metadata → bit-identical; same seed → bit-identical at any thread count) |
| 5 | `JointFailures` (1-comp 2/5-PFM × correlation × aggregation), `CompetingFailures`, `CommonCause`, `MutuallyExclusive`, `EAD` |
| 6 | `SystemRisk` (2-comp/2-PFM, 5-comp/1-PFM × correlation × aggregation), `RiskAnalysis` N-element combos, NFIP Assurance TOL 50/55/70, LHS variance-reduction |
| 9 | `Composite` family (mixture hazard/response/consequence, bootstrap uncertainty), NFIP TOL 60/65 |
| 10 | `EventTree` (serialization round-trip + product oracle) |
| 11 | `BivariateRisk` (100M→1M), `DAMRAE`, BestFit import contract |
| Future | FDA integration (needs committed datasets via `verification-requests/`) |
