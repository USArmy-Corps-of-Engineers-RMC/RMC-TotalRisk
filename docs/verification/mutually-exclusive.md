# Mutually Exclusive Failure Modes

**Test class:** `MutuallyExclusiveVerification` · **Status:** ✅ Verified (2026-07-23, Phase 5)

The Phase 5 conversion of the legacy `Test_MC_MutuallyExclusive.vb` family — one system
component with 2 or 5 potential failure modes treated as exclusive events, their marginal
probabilities normalized whenever the sum exceeds one. The engine's
`Probability.MutuallyExclusiveAdjustment` path is verified against an independent Monte Carlo
oracle; no constants were published for this family, so the asserts are engine-versus-oracle
only.

## Scenario

The shared legacy Bucket-1 model — see [joint-failures.md](joint-failures.md) for the full
input table. Seeds: hazard `MersenneTwister(12345)`, one selection uniform per realization
from `MersenneTwister(45678)`; N = 10⁶. The method has no dependence model (the engine coerces
the dependency to Independent; the legacy bodies' multivariate objects were declared but never
used).

## Legacy method mapping

| Converted test | Legacy method (`Test_MC_MutuallyExclusive.vb`) |
|---|---|
| `Test_2PFM_VsOracle` | `Test_1_Component_2_PFM_ME` (line 11) |
| `Test_5PFM_VsOracle` | `Test_1_Component_5_PFM_ME` (line 150) |

## Results (oracle / engine, N = 10⁶)

| Scenario | Fail mean | Total mean | Excess mean | Background | Non-fail | APF | σ(Fail) | VaR₀.₀₁ | CVaR₀.₀₁ |
|---|---|---|---|---|---|---|---|---|---|
| 2-PFM MutuallyExclusive | 2.21777 / 2.23162 | 3.16281 / 3.17542 | 1.73545 / 1.7457 | 1.42736 / 1.42972 | 0.945036 / 0.943798 | 0.067513 / 0.0678455 | 15.6495 / 15.7339 | 47.6776 / 47.7071 | 128.229 / 128.54 |
| 5-PFM MutuallyExclusive | 3.90948 / 3.92643 | 4.45658 / 4.47355 | 3.02922 / 3.04383 | 1.42736 / 1.42972 | 0.5471 / 0.547121 | 0.20143 / 0.201615 | 23.5619 / 23.8137 | 77.7912 / 77.2652 | 180.042 / 181.442 |

Worst per-measure difference: **0.63%** on a mean and **1.07%** on a dispersion measure —
every deviation within its 4·SE assert (tolerance catalog identical to the joint family).

## Physics cross-checks visible in the numbers

- The mutually-exclusive failure probability is the **capped sum** — the upper unimodal
  bound: 5-PFM APF 0.2016 sits above every other combination method's union on the same
  marginals (joint/competing/common-cause run 0.132–0.190 across the dependency options).
- At D = 2 the capped sum coincides with the perfectly-negative union, and the oracle
  mechanics coincide with the common-cause negative case — the 2-PFM row reproduces the
  [common-cause family's](common-cause.md) 2-PFM PerfectlyNegative row exactly.
- **The normalization warning surfaces** ("Mutually exclusive failure mode probabilities
  summed above one and were normalized") — asserted in both tests; the fragilities saturate at
  high hazard, so Σp exceeds one by construction.

## Reproducibility pins

| Pin | Result |
|---|---|
| Function/component renames + `AssignNewId` | ✅ bit-identical |
| XML round-trip (coerced-dependency state included) | ✅ bit-identical |
