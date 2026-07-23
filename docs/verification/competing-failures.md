# Weak-Link Competing Failure Modes

**Test class:** `CompetingFailuresVerification` · **Status:** ✅ Verified (2026-07-23, Phase 5)

The Phase 5 conversion of the legacy `Test_MC_CompetingFailures.vb` family — one system
component with 2 or 5 potential failure modes **racing to first failure** (the weakest exceeded
mode wins and takes its own consequence; no joint-consequence rule applies), across the four
dependency options. The engine's 200-level cumulative-incidence-function pre-processing
(`Numerics.CompetingRisks`) is verified against an independent brute-force Monte Carlo oracle
and pinned to the 2024 report's published constants (tables 59–60).

## Scenario

The shared legacy Bucket-1 model — see [joint-failures.md](joint-failures.md) for the full
input table (identical hazard, fragilities, consequences, seeds, and N = 10⁶; engine and
oracle interpolate the same dense tables).

## Legacy method mapping (8 methods → 8 tests)

| Converted test | Legacy line (`Test_MC_CompetingFailures.vb`) |
|---|---|
| `Test_2PFM_Independent_VsOracle` | 12 |
| `Test_2PFM_PerfectlyPositive_VsOracle` | 146 |
| `Test_2PFM_PerfectlyNegative_VsOracle` | 280 |
| `Test_2PFM_CorrelationMatrix_VsOracle` | 414 |
| `Test_5PFM_Independent_VsOracle` | 548 |
| `Test_5PFM_PerfectlyPositive_VsOracle` | 692 — **corrected oracle** (see Notes) |
| `Test_5PFM_PerfectlyNegative_VsOracle` | 852 |
| `Test_5PFM_CorrelationMatrix_VsOracle` | 996 |

## Oracle mechanics and tolerances

Per realization: one hazard uniform (`MersenneTwister(12345)`), the realization's row of
correlated capacity draws (`MultivariateNormal`, seed 12345); each mode's **resistance** is the
tabulated fragility inverted at Φ(z); a mode causes the failure only when it is exceeded AND
holds the minimum resistance — the exact legacy selection logic. The incremental draw is
max(0, fC − nfC). The legacy bodies also declared an unused 45678 stream — vestigial, not
ported. The tolerance catalog is identical to the joint family (k = 4; mean σ̂/√N; binomial;
delta-method σ; ratio-estimator conditional mean; density-scaled VaR and tail-mean CVaR with
0.1% relative floors; report pins at 4·σ̂/√10⁷ + 0.1%·|pin|).

## Results (oracle / engine, N = 10⁶)

| Scenario | Fail mean | Total mean | Excess mean | Background | Non-fail | APF | σ(Fail) | VaR₀.₀₁ | CVaR₀.₀₁ |
|---|---|---|---|---|---|---|---|---|---|
| 2-PFM Competing Independent | 2.20457 / 2.21286 | 3.17266 / 3.18152 | 1.74529 / 1.7518 | 1.42736 / 1.42972 | 0.968081 / 0.968655 | 0.06685 / 0.0669759 | 16.3284 / 16.42 | 46.086 / 46.2404 | 130.195 / 130.627 |
| 2-PFM Competing PerfectlyPositive | 2.16506 / 2.17359 | 3.15659 / 3.16534 | 1.72923 / 1.73562 | 1.42736 / 1.42972 | 0.991533 / 0.991755 | 0.066115 / 0.0662519 | 16.523 / 16.6101 | 44.7833 / 44.9789 | 129.423 / 129.887 |
| 2-PFM Competing PerfectlyNegative | 2.27002 / 2.27694 | 3.21368 / 3.22209 | 1.78632 / 1.79237 | 1.42736 / 1.42972 | 0.943663 / 0.945145 | 0.067709 / 0.0678186 | 16.3689 / 16.4738 | 47.5546 / 47.6308 | 132.855 / 133.226 |
| 2-PFM Competing CorrelationMatrix | 2.175 / 2.18645 | 3.15588 / 3.1675 | 1.72852 / 1.73779 | 1.42736 / 1.42972 | 0.980885 / 0.981058 | 0.066401 / 0.0665361 | 16.3222 / 16.4561 | 45.3305 / 45.5332 | 129.158 / 129.876 |
| 5-PFM Competing Independent | 2.99623 / 2.98758 | 3.64355 / 3.63615 | 2.21619 / 2.20643 | 1.42736 / 1.42972 | 0.64732 / 0.648569 | 0.181897 / 0.181903 | 19.2204 / 19.2993 | 59.2117 / 58.9197 | 144.422 / 143.871 |
| 5-PFM Competing PerfectlyPositive | 1.15342 / 1.15881 | 2.00687 / 2.01171 | 0.579512 / 0.581997 | 1.42736 / 1.42972 | 0.853449 / 0.852901 | 0.13197 / 0.132124 | 7.11687 / 7.17672 | 18.9458 / 18.9513 | 54.8826 / 55.4793 |
| 5-PFM Competing PerfectlyNegative | 3.26603 / 3.25528 | 3.873 / 3.86367 | 2.44564 / 2.43395 | 1.42736 / 1.42972 | 0.606966 / 0.608394 | 0.190252 / 0.190176 | 19.9616 / 20.0249 | 66.5552 / 65.9016 | 150.756 / 150.233 |
| 5-PFM Competing CorrelationMatrix | 2.50109 / 2.5039 | 3.17652 / 3.17944 | 1.74915 / 1.74973 | 1.42736 / 1.42972 | 0.675426 / 0.675541 | 0.17811 / 0.1784 | 15.5309 / 15.5562 | 45.2475 / 45.3981 | 114.228 / 114.52 |

Worst per-measure difference: **0.54%** on a mean and **1.09%** on a tail measure — every
deviation within its 4·SE assert. The engine's 200-bin cumulative-incidence discretization
(the report observed ≤ 0.3% at 10M draws) stayed an order below the Monte Carlo tolerances; no
separate allowance was needed. Both report-pin scenarios (tables 59–60, Independent) passed.

## Physics cross-checks visible in the numbers

- The dominance structure under perfect positive dependence is stark at 5 PFM: the weakest
  mode (PFM-4, mean capacity 130) wins nearly always, and its cheap consequence curve drives
  the failure mean down to 1.16 versus 3.00 independent — the CIF concentration the technical
  note's §4.4 describes.
- The failure union matches the joint family's per dependency (same marginals, same copula):
  e.g. 0.181903 vs 0.181905 at 5-PFM Independent.

## Reproducibility pins

| Pin | Result |
|---|---|
| Function/component renames + `AssignNewId` | ✅ bit-identical |
| XML round-trip (G17 correlation matrix included) | ✅ bit-identical |

## Notes

- **Corrected oracle (ratified decision):** the legacy 5-PFM Positive body is a broken code
  path — its multivariate generation is commented out, it computes an unused Cholesky product,
  and it assigns one shared in-loop standard normal to every mode. The port replaces it with
  the correct r = 1 − √ε equicorrelated Gaussian copula matching every sibling method; the
  report published no constants for that case, so nothing is lost.
- The 5-PFM Positive oracle also uses r = 1 − √ε rather than the legacy 1 − ε (the joint
  family's documented deviation).
