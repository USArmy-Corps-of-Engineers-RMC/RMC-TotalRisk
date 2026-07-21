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

Engine-level composite scenarios (the legacy `Test_Composite.vb` day/night oracles with a
hazard curve and fragility in the loop) convert when the risk engine lands — see the
conversion order in [docs/verification.md](../verification.md); this folder gains their pages
then. Families verified in Phases 5–6 (joint/competing/common-cause failures, system risk,
NFIP assurance) add their pages the same way.
