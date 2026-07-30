# Joint Failure Modes

**Test class:** `JointFailuresVerification` · **Tests:** 9 · **Run of record:** 2026-07-23, isolated run, ✅ all passed

The conversion of the legacy `Test_MC_JointFailures.vb` family — one system component
with 2 or 5 potential failure modes where **all exceeded modes fail together**, across the four
dependency options and all four joint-consequence rules. The engine's inclusion–exclusion
pathway enumeration (`Probability.IndependentExclusive` / `PositivelyDependentExclusive` /
`ExclusivePCM`) is verified against an independent brute-force Monte Carlo oracle and pinned to
the 2024 verification report's published constants (tables 61–76).

## Scenario (the shared legacy Bucket-1 model)

| Input | Definition |
|---|---|
| Hazard | LnNormal(85, 20) (real-space moments), tabulated on a ±8 z-grid at step 0.1 (161 knots) |
| Fragilities | Normal CDFs — PFM-1 (140, 30), PFM-2 (160, 10), PFM-3 (150, 20), PFM-4 (130, 35), PFM-5 (160, 15) — each tabulated on its own ±8σ z-grid at step 0.05σ (321 knots) |
| Failure consequences | Five-knot curves over stages {60, 100, 140, 200, 250}: PFM-1 {0, 5, 50, 500, 750}, PFM-2 {0, 3, 30, 300, 450}, PFM-3 {0, 10, 100, 1000, 1500}, PFM-4 {0, 2, 20, 200, 300}, PFM-5 {0, 8, 80, 800, 1200} |
| Non-failure consequence | {0, 1, 10, 100, 150} over the same stages |
| Dependencies | Independent (r = 0); PerfectlyPositive (r = 1 − √ε); PerfectlyNegative (r = −1/(D−1) + √ε: −1 at D = 2, −0.25 at D = 5); CorrelationMatrix (r = 0.5 at D = 2; the legacy full symmetric 5×5 matrix at D = 5) |
| Seeds / N | Hazard `MersenneTwister(12345)`; capacities `MultivariateNormal.GenerateRandomValues(N, 12345)`; N = 10⁶ (legacy 10M ÷ 10 per policy) |

Engine and oracle interpolate the **same tables**, so both sides integrate the identical
piecewise-linear model and tabulation error cancels from the engine-versus-oracle asserts. The
report pins additionally anchor the engine to the published exact-distribution constants at
4·σ̂/√10⁷ + 0.1%·|pin|.

## Legacy method mapping (32 methods → 8 consolidated tests)

The 32 legacy methods share identical sampling within each dependency group — the combination
rule only changes how the failing modes' consequences aggregate (Sum / Mean / Max / Min). Each
converted test runs ONE oracle pass accumulating all four rules from the same draws
(bit-identical to four separate legacy passes at the same seeds) and asserts each rule against
its own mean-only engine run:

| Converted test | Legacy methods (file lines in `Test_MC_JointFailures.vb`) |
|---|---|
| `Test_2PFM_Independent_AllRules_VsOracle` | 13, 145, 276, 407 |
| `Test_2PFM_PerfectlyPositive_AllRules_VsOracle` | 539, 672, 804, 936 |
| `Test_2PFM_PerfectlyNegative_AllRules_VsOracle` | 1069, 1201, 1334, 1467 |
| `Test_2PFM_CorrelationMatrix_AllRules_VsOracle` | 1600, 1733, 1866, 1999 |
| `Test_5PFM_Independent_AllRules_VsOracle` | 2131, 2271, 2411, 2551 |
| `Test_5PFM_PerfectlyPositive_AllRules_VsOracle` | 2691, 2831, 2971, 3111 |
| `Test_5PFM_PerfectlyNegative_AllRules_VsOracle` | 3251, 3391, 3531, 3671 |
| `Test_5PFM_CorrelationMatrix_AllRules_VsOracle` | 3811, 3950, 4089, 4228 |

## Oracle and tolerances

| Measure | SE derivation (k = 4 throughout) |
|---|---|
| Five summary means | σ̂/√N per stream (Welford/Pébay in-run) |
| Annualized failure probability + complement identity | binomial √(p(1−p)/N) |
| σ (Fail, Total) | delta method √(m₄ − σ⁴)/(2σ√N) |
| Conditional mean | first-order ratio-estimator SE √(Σy² − R̂²·N_F)/N_F |
| Assurance P(C > 100) + two data-driven LEC probes (conditional median and 95th-percentile losses; ≥ 100 exceedances enforced) | binomial |
| VaR at α = 0.01 | density-scaled quantile SE + 0.1% relative output-resolution floor |
| CVaR at α = 0.01 | tail-mean SE + 0.1% relative floor |
| Report pins (means, tables 61–76) | 4·σ̂/√10⁷ + 0.1%·\|pin\| (the report's own 10M sampling error plus a tabulation/integration allowance) |

## Results (oracle / engine, N = 10⁶, mean-only engine runs at LEC output resolution 1000)

| Scenario | Fail mean | Total mean | Excess mean | Background | Non-fail | APF | σ(Fail) | VaR₀.₀₁ | CVaR₀.₀₁ |
|---|---|---|---|---|---|---|---|---|---|
| 2-PFM Independent Additive | 2.60117 / 2.60827 | 3.56925 / 3.57687 | 2.14189 / 2.14715 | 1.42736 / 1.42972 | 0.968081 / 0.968601 | 0.06685 / 0.0669773 | 23.55 / 23.705 | 46.1661 / 46.2991 | 169.831 / 170.144 |
| 2-PFM Independent Average | 2.12498 / 2.13432 | 3.09306 / 3.10292 | 1.6657 / 1.67321 | 1.42736 / 1.42972 | 0.968081 / 0.968601 | 0.06685 / 0.0669773 | 14.9947 / 15.0893 | 46.0201 / 46.1124 | 122.271 / 122.783 |
| 2-PFM Independent Maximum | 2.24403 / 2.25281 | 3.21211 / 3.22141 | 1.78475 / 1.79169 | 1.42736 / 1.42972 | 0.968081 / 0.968601 | 0.06685 / 0.0669773 | 16.9285 / 17.037 | 46.0998 / 46.2469 | 134.129 / 134.608 |
| 2-PFM Independent Minimum | 2.00594 / 2.01584 | 2.97402 / 2.98444 | 1.54666 / 1.55472 | 1.42736 / 1.42972 | 0.968081 / 0.968601 | 0.06685 / 0.0669773 | 13.3009 / 13.3831 | 45.9522 / 46.0303 | 110.448 / 111.016 |
| 2-PFM PerfectlyPositive Additive | 2.59696 / 2.60827 | 3.58849 / 3.60021 | 2.16113 / 2.17049 | 1.42736 / 1.42972 | 0.991533 / 0.991941 | 0.066115 / 0.0662491 | 24.0814 / 24.2661 | 44.8733 / 45.0656 | 172.595 / 173.322 |
| 2-PFM PerfectlyPositive Average | 2.03258 / 2.04096 | 3.02411 / 3.0329 | 1.59675 / 1.60319 | 1.42736 / 1.42972 | 0.991533 / 0.991941 | 0.066115 / 0.0662491 | 14.472 / 14.5577 | 44.6321 / 44.7938 | 116.257 / 116.668 |
| 2-PFM PerfectlyPositive Maximum | 2.17368 / 2.18279 | 3.16521 / 3.17473 | 1.73785 / 1.74501 | 1.42736 / 1.42972 | 0.991533 / 0.991941 | 0.066115 / 0.0662491 | 16.6973 / 16.8076 | 44.7833 / 44.9747 | 130.285 / 130.807 |
| 2-PFM PerfectlyPositive Minimum | 1.89149 / 1.89914 | 2.88302 / 2.89108 | 1.45566 / 1.46136 | 1.42736 / 1.42972 | 0.991533 / 0.991941 | 0.066115 / 0.0662491 | 12.4732 / 12.5353 | 44.5072 / 44.6572 | 102.307 / 102.633 |
| 2-PFM PerfectlyNegative Additive | 2.60043 / 2.60827 | 3.54409 / 3.55207 | 2.11673 / 2.12235 | 1.42736 / 1.42972 | 0.943663 / 0.943798 | 0.067709 / 0.0678455 | 23.0517 / 23.1942 | 47.5546 / 47.7051 | 165.896 / 166.214 |
| 2-PFM PerfectlyNegative Average | 2.2229 / 2.23353 | 3.16657 / 3.17733 | 1.7392 / 1.74761 | 1.42736 / 1.42972 | 0.943663 / 0.943798 | 0.067709 / 0.0678455 | 15.4432 / 15.5443 | 47.5546 / 47.7376 | 128.143 / 128.718 |
| 2-PFM PerfectlyNegative Maximum | 2.31728 / 2.32722 | 3.26095 / 3.27102 | 1.83359 / 1.8413 | 1.42736 / 1.42972 | 0.943663 / 0.943798 | 0.067709 / 0.0678455 | 17.1266 / 17.2367 | 47.5546 / 47.705 | 137.582 / 138.104 |
| 2-PFM PerfectlyNegative Minimum | 2.12852 / 2.13985 | 3.07218 / 3.08365 | 1.64482 / 1.65393 | 1.42736 / 1.42972 | 0.943663 / 0.943798 | 0.067709 / 0.0678455 | 13.9973 / 14.091 | 47.5546 / 47.7367 | 118.705 / 119.354 |
| 2-PFM CorrelationMatrix Additive | 2.60107 / 2.60827 | 3.58195 / 3.58931 | 2.15459 / 2.15959 | 1.42736 / 1.42972 | 0.980885 / 0.981037 | 0.066401 / 0.0665363 | 23.8299 / 23.9755 | 45.4351 / 45.6178 | 171.741 / 172.026 |
| 2-PFM CorrelationMatrix Average | 2.0738 / 2.08458 | 3.05469 / 3.06562 | 1.62733 / 1.6359 | 1.42736 / 1.42972 | 0.980885 / 0.981037 | 0.066401 / 0.0665363 | 14.7325 / 14.8377 | 45.1927 / 45.3532 | 119.112 / 119.737 |
| 2-PFM CorrelationMatrix Maximum | 2.20562 / 2.2155 | 3.1865 / 3.19654 | 1.75914 / 1.76682 | 1.42736 / 1.42972 | 0.980885 / 0.981037 | 0.066401 / 0.0665363 | 16.8144 / 16.928 | 45.3397 / 45.5289 | 132.216 / 132.774 |
| 2-PFM CorrelationMatrix Minimum | 1.94199 / 1.95365 | 2.92287 / 2.93469 | 1.49551 / 1.50497 | 1.42736 / 1.42972 | 0.980885 / 0.981037 | 0.066401 / 0.0665363 | 12.8863 / 12.9848 | 45.0466 / 45.2226 | 106.076 / 106.791 |
| 5-PFM Independent Additive | 7.69312 / 7.74247 | 8.34044 / 8.39105 | 6.91308 / 6.96133 | 1.42736 / 1.42972 | 0.64732 / 0.648577 | 0.181897 / 0.181905 | 77.068 / 78.1653 | 125.189 / 125.61 | 544.11 / 548.896 |
| 5-PFM Independent Average | 3.41648 / 3.41907 | 4.0638 / 4.06765 | 2.63644 / 2.63793 | 1.42736 / 1.42972 | 0.64732 / 0.648577 | 0.181897 / 0.181905 | 20.7332 / 20.8353 | 69.3564 / 69.2317 | 170.313 / 170.538 |
| 5-PFM Independent Maximum | 4.66011 / 4.66852 | 5.30743 / 5.3171 | 3.88007 / 3.88738 | 1.42736 / 1.42972 | 0.64732 / 0.648577 | 0.181897 / 0.181905 | 33.5608 / 33.7868 | 90.619 / 90.5695 | 268.364 / 269.205 |
| 5-PFM Independent Minimum | 2.30492 / 2.30157 | 2.95224 / 2.95015 | 1.52488 / 1.52043 | 1.42736 / 1.42972 | 0.64732 / 0.648577 | 0.181897 / 0.181905 | 11.1887 / 11.1842 | 47.7588 / 47.8104 | 90.8445 / 90.5584 |
| 5-PFM PerfectlyPositive Additive | 7.65607 / 7.74242 | 8.50952 / 8.59542 | 7.08216 / 7.1657 | 1.42736 / 1.42972 | 0.853449 / 0.853005 | 0.13197 / 0.132134 | 81.8165 / 83.086 | 133.385 / 134.8 | 578.79 / 586.985 |
| 5-PFM PerfectlyPositive Average | 2.37651 / 2.39615 | 3.22996 / 3.24915 | 1.8026 / 1.81944 | 1.42736 / 1.42972 | 0.853449 / 0.853005 | 0.13197 / 0.132134 | 18.1434 / 18.3806 | 46.6642 / 47.2505 | 140.663 / 142.6 |
| 5-PFM PerfectlyPositive Maximum | 3.77651 / 3.81506 | 4.62996 / 4.66806 | 3.2026 / 3.23834 | 1.42736 / 1.42972 | 0.853449 / 0.853005 | 0.13197 / 0.132134 | 31.7836 / 32.2417 | 78.2356 / 79.1403 | 243.883 / 247.583 |
| 5-PFM PerfectlyPositive Minimum | 1.15079 / 1.15561 | 2.00424 / 2.00862 | 0.576875 / 0.578897 | 1.42736 / 1.42972 | 0.853449 / 0.853005 | 0.13197 / 0.132134 | 7.03033 / 7.0814 | 18.9458 / 18.9545 | 54.6189 / 55.1568 |
| 5-PFM PerfectlyNegative Additive | 7.70793 / 7.74271 | 8.3149 / 8.35102 | 6.88754 / 6.92128 | 1.42736 / 1.42972 | 0.606966 / 0.608306 | 0.190252 / 0.190197 | 76.2783 / 77.1962 | 121.173 / 121.675 | 537.305 / 541.179 |
| 5-PFM PerfectlyNegative Average | 3.63131 / 3.62808 | 4.23828 / 4.23639 | 2.81091 / 2.80665 | 1.42736 / 1.42972 | 0.606966 / 0.608306 | 0.190252 / 0.190197 | 21.0418 / 21.1158 | 74.6477 / 74.4504 | 173.161 / 173.154 |
| 5-PFM PerfectlyNegative Maximum | 4.83534 / 4.84075 | 5.44231 / 5.44906 | 4.01495 / 4.01932 | 1.42736 / 1.42972 | 0.606966 / 0.608306 | 0.190252 / 0.190197 | 33.8178 / 34.0481 | 92.9555 / 92.5995 | 271.749 / 272.737 |
| 5-PFM PerfectlyNegative Minimum | 2.54827 / 2.53882 | 3.15524 / 3.14712 | 1.72788 / 1.71738 | 1.42736 / 1.42972 | 0.606966 / 0.608306 | 0.190252 / 0.190197 | 11.4764 / 11.4173 | 54.1939 / 53.8515 | 93.5024 / 92.8656 |
| 5-PFM CorrelationMatrix Additive | 7.70041 / 7.76427 | 8.37583 / 8.43862 | 6.94847 / 7.0089 | 1.42736 / 1.42972 | 0.675426 / 0.674351 | 0.17811 / 0.178461 | 77.8164 / 78.9788 | 132.344 / 132.929 | 551.979 / 558.165 |
| 5-PFM CorrelationMatrix Average | 3.20203 / 3.21688 | 3.87746 / 3.89123 | 2.4501 / 2.46151 | 1.42736 / 1.42972 | 0.675426 / 0.674351 | 0.17811 / 0.178461 | 20.1098 / 20.2893 | 57.8232 / 57.5414 | 162.904 / 164.319 |
| 5-PFM CorrelationMatrix Maximum | 4.52501 / 4.54892 | 5.20043 / 5.22327 | 3.77307 / 3.79355 | 1.42736 / 1.42972 | 0.675426 / 0.674351 | 0.17811 / 0.178461 | 33.4323 / 33.7646 | 88.6062 / 88.4316 | 266.644 / 269.088 |
| 5-PFM CorrelationMatrix Minimum | 2.04428 / 2.0503 | 2.71971 / 2.72465 | 1.29235 / 1.29493 | 1.42736 / 1.42972 | 0.675426 / 0.674351 | 0.17811 / 0.178461 | 10.0529 / 10.0985 | 40.6033 / 40.8119 | 79.7138 / 80.0977 |

Worst per-measure difference across all 32 scenarios: **1.18%** on a mean (5-PFM
PerfectlyPositive Additive excess) and **1.55%** on a dispersion measure — every deviation
within its 4·SE assert and dominated by the oracle's own Monte Carlo error on the zero-inflated
failure streams (the oracle SEs at N = 10⁶ run 0.3–1% relative there). All 80 report-pin
asserts (16 published joint scenarios × 5 means, tables 61–76) passed.

## Physics cross-checks visible in the numbers

- **The additive failure mean is dependency-invariant** — E[Σ fcⱼ·1{Fⱼ}] = Σ fcⱼ·P(Fⱼ)
  regardless of dependence: the engine reproduces 2.60827 across all four 2-PFM groups and
  ≈ 7.742 across the 5-PFM automatic groups.
- **Negative dependence raises the failure union; positive lowers it** (the reversed unimodal
  bounds): 5-PFM APF runs 0.1321 (positive) < 0.1819 (independent) < 0.1902 (negative).
- **The 5-PFM mixed-sign correlation matrix exposes the PCM approximation** at ≈ 0.3% (engine
  additive fail mean 7.76427 vs 7.7425 in the automatic modes, which are exactly
  marginal-preserving) — the documented v1.0-preserved product-of-conditional-marginals
  algorithm, well inside the Monte Carlo tolerance.

## Reproducibility pins

| Pin | Result |
|---|---|
| Function/component renames + `AssignNewId` → total/fail scalars and LEC arrays | ✅ bit-identical |
| XML round-trip (G17 correlation matrix included) | ✅ bit-identical |
| Failure-mode declaration reorder (equicorrelated matrix — model unchanged) | ✅ equal at 1e-9 relative (pathway summation reassociation only) |

## Notes

- **Documented deviation:** the legacy 5-PFM Positive bodies used r = 1 − ε_mach (the 2-PFM
  bodies used 1 − √ε_mach); this port uses r = 1 − √ε_mach for both — statistically
  indistinguishable, matches the engine's PerfectlyPositive constant, and Cholesky-stable.
- The legacy joint bodies computed the incremental draw as fC − nfC with **no clamp**; v1.1
  clamps at zero. Inert here — every failure curve pointwise dominates the non-failure curve.
- The exceedance probes read the output LEC at its maximum resolution (1000) so the output
  thinning stays an order below the binomial 4·SE; the default-200 presentation fidelity is a
  separate documented engine property.
- This family's Negative scenarios execute through the corrected perfectly-negative dependency
  wiring (the correction is recorded in [docs/PROGRESS.md](../PROGRESS.md)) — before that
  correction the run path silently produced zero risk under PerfectlyNegative.
