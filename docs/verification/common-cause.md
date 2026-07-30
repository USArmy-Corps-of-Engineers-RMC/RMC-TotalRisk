# Common Cause Adjustment

**Test class:** `CommonCauseVerification` · **Tests:** 9 · **Run of record:** 2026-07-23, isolated run, ✅ all passed

The conversion of the legacy `Test_MC_CommonCause.vb` family — one system component
with 2 or 5 potential failure modes whose marginal probabilities are reapportioned by the
common-cause adjustment factor (c = P(∪F)/Σp), across the four dependency options. The
engine's `CommonCauseFactor` dispatch over `Probability.CommonCauseAdjustment` is verified
against an independent Monte Carlo oracle and pinned to the 2024 report's published constants
(tables 55–58).

## Scenario

The shared legacy Bucket-1 model — see [joint-failures.md](joint-failures.md) for the full
input table (identical hazard, fragilities, consequences, and N = 10⁶; engine and oracle
interpolate the same dense tables). Selection seeds: hazard `MersenneTwister(12345)`, one
selection uniform per realization from `MersenneTwister(45678)` — no multivariate sampling
(the correlation enters only through the adjustment factor, exactly as the legacy bodies,
whose multivariate objects were declared but never used).

## Legacy method mapping (10 methods → 8 tests)

The two `_CCA` bodies (one-argument adjustment) and the two `_CommonCause_Independent` bodies
(three-argument call with an identity matrix) produce **identical realization streams**, so
both convert into the Independent tests — four legacy methods, two converted tests, nothing
lost. The legacy Negative and Correlation bodies called the two-argument overload
(correlation-matrix dependency) — the same code path the engine's factor takes.

| Converted test | Legacy lines (`Test_MC_CommonCause.vb`) |
|---|---|
| `Test_2PFM_Independent_VsOracle` | 12 (`_CCA`) + 302 (`_Independent`) |
| `Test_2PFM_PerfectlyPositive_VsOracle` | 442 |
| `Test_2PFM_PerfectlyNegative_VsOracle` | 582 |
| `Test_2PFM_CorrelationMatrix_VsOracle` | 722 |
| `Test_5PFM_Independent_VsOracle` | 151 (`_CCA`) + 862 (`_Independent`) |
| `Test_5PFM_PerfectlyPositive_VsOracle` | 1011 |
| `Test_5PFM_PerfectlyNegative_VsOracle` | 1161 |
| `Test_5PFM_CorrelationMatrix_VsOracle` | 1311 |

## Oracle mechanics and tolerances

Per realization the marginals are scaled by `Probability.CommonCauseAdjustment` (the shared
Numerics kernel — the legacy oracles called the same function; the independent element of the
oracle is the Monte Carlo integration, not the adjustment algebra) and ONE mode is selected by
walking the cumulative adjusted probabilities against the selection uniform. The incremental
draw is max(0, fC − nfC). The tolerance catalog is identical to the joint family.

## Results (oracle / engine, N = 10⁶)

| Scenario | Fail mean | Total mean | Excess mean | Background | Non-fail | APF | σ(Fail) | VaR₀.₀₁ | CVaR₀.₀₁ |
|---|---|---|---|---|---|---|---|---|---|
| 2-PFM CommonCause Independent | 2.11333 / 2.12222 | 3.08211 / 3.09082 | 1.65474 / 1.66111 | 1.42736 / 1.42972 | 0.968774 / 0.968601 | 0.066671 / 0.0669773 | 15.1844 / 15.2402 | 46.155 / 46.09 | 121.73 / 121.665 |
| 2-PFM CommonCause PerfectlyPositive | 2.00948 / 2.02195 | 3.00214 / 3.0139 | 1.57478 / 1.58418 | 1.42736 / 1.42972 | 0.992652 / 0.991941 | 0.065932 / 0.0662491 | 14.612 / 14.6957 | 44.6944 / 44.6551 | 114.688 / 114.916 |
| 2-PFM CommonCause PerfectlyNegative | 2.21777 / 2.23162 | 3.16281 / 3.17542 | 1.73545 / 1.7457 | 1.42736 / 1.42972 | 0.945036 / 0.943798 | 0.067513 / 0.0678455 | 15.6495 / 15.7339 | 47.6776 / 47.7071 | 128.229 / 128.54 |
| 2-PFM CommonCause CorrelationMatrix | 2.05578 / 2.06782 | 3.03742 / 3.04886 | 1.61006 / 1.61914 | 1.42736 / 1.42972 | 0.981643 / 0.981037 | 0.066223 / 0.0665363 | 14.8977 / 14.9751 | 45.2841 / 45.2465 | 118.019 / 118.217 |
| 5-PFM CommonCause Independent | 3.48142 / 3.50457 | 4.13004 / 4.15315 | 2.70268 / 2.72343 | 1.42736 / 1.42972 | 0.648622 / 0.64858 | 0.181748 / 0.181904 | 23.1868 / 23.4423 | 73.0439 / 73.1709 | 178.078 / 179.289 |
| 5-PFM CommonCause PerfectlyPositive | 2.61036 / 2.62158 | 3.46319 / 3.47459 | 2.03583 / 2.04487 | 1.42736 / 1.42972 | 0.852826 / 0.853005 | 0.131843 / 0.132134 | 21.4186 / 21.6166 | 54.2621 / 54.1143 | 158.683 / 159.208 |
| 5-PFM CommonCause PerfectlyNegative | 3.65058 / 3.67561 | 4.25893 / 4.2838 | 2.83157 / 2.85408 | 1.42736 / 1.42972 | 0.608346 / 0.60819 | 0.190008 / 0.190222 | 23.3689 / 23.6371 | 75.6048 / 75.4326 | 179.531 / 180.976 |
| 5-PFM CommonCause CorrelationMatrix | 3.35126 / 3.38101 | 4.02745 / 4.0566 | 2.60009 / 2.62689 | 1.42736 / 1.42972 | 0.676191 / 0.675595 | 0.178169 / 0.178411 | 22.9448 / 23.2056 | 70.3209 / 70.7262 | 175.461 / 176.721 |

Worst per-measure difference: **1.03%** on a mean and **1.15%** on a dispersion measure —
every deviation within its 4·SE assert. All four report-pin scenarios (tables 55–58,
Independent and PerfectlyNegative) passed.

## Physics cross-checks visible in the numbers

- At D = 2 the perfectly negative failure events are effectively exclusive, so the CCA union
  equals the mutually-exclusive capped sum — the 2-PFM PerfectlyNegative row reproduces the
  [mutually-exclusive family's](mutually-exclusive.md) 2-PFM row exactly (same mechanics, same
  seeds, APF 0.0678455 in both).
- The failure unions match the joint family per dependency (same marginals, same copula), while
  the **allocation** differs — the CCA's implicit-ordering redistribution versus the joint
  model's pathway enumeration (the technical note's §3.4/§6.1 observation).

## Reproducibility pins

| Pin | Result |
|---|---|
| Function/component renames + `AssignNewId` | ✅ bit-identical |
| XML round-trip (G17 correlation matrix + coerced-dependency state) | ✅ bit-identical |

## Notes

- **Build-order contract exercised:** selecting the common-cause method coerces the dependency
  to Independent (v1.0 behavior), so the scenario dependency is applied after the method — the
  order every consuming layer must follow.
- **Two engine corrections landed with this family** (recorded in
  [docs/PROGRESS.md](../PROGRESS.md)): the perfectly-negative dependency-matrix
  materialization, and the perfectly-positive common-cause factor call, which faulted against
  the current Numerics overload's unconditional null-matrix check (the positive kernel never
  reads the matrix; the engine now passes the materialized matrix, mirroring the legacy
  oracle's own three-argument call).
