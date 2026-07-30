# Composite Consequence Function Verification

**Test class:** `CompositeConsequenceVerification` · **Tests:** 9 · **Run of record:** 2026-07-21, isolated run, ✅ all passed

> Family: `CompositeConsequence` (`RMC.TotalRisk.RiskFunctions.Consequences`)
> Anchor: *Verification of the RMC-TotalRisk Software* (2024), §Composite Consequence Function (Tables 47–51)
> All comparisons **very good** (≤ 1%; worst case 0.035% against exact values)

## Overview

A composite consequence function combines a weighted list of child consequence functions with
one of three methods:

| Method | Per-realization combination | Use case |
|---|---|---|
| **Additive** | C = Σ Cᵢ (weights ignored) | Damages estimated separately by sector (properties, industry, agriculture) aggregated to a total |
| **Average** | C = Σ wᵢ·Cᵢ, Σw = 1 | The historic day/night exposure practice (day ≈ 0.42 / night ≈ 0.58) |
| **Mixture** | One child sampled per realization with probability wᵢ | The preferred day/night treatment — fully captures scenario uncertainty |

Key theory (report Eq. 50–55): the mean of the Average equals the mean of the Mixture (both are
Σwμ), but the Mixture's variance Σw(σ² + μ²) − (Σwμ)² is always at least the Average's Σw²σ² —
treating day/night as a weighted average understates consequence uncertainty.

## Verification scenario (report Table 47)

Three child consequence functions, each zero at stage 0 and Normal at stage 10 ft:

| Function | Mean life loss, μ | Std. deviation, σ | Weight (Average/Mixture) |
|---|---|---|---|
| 1 | 10 | 2 | 0.3 |
| 2 | 20 | 1 | 0.2 |
| 3 | 100 | 5 | 0.5 |

Engine side: the composite's own sampler at **N = 1,000,000** realizations
(`SetupSampler(N, 12345, LatinHypercube)`), every realization curve evaluated at stage 10.
Expected values are exact closed forms (sums/averages of independent Normals are Normal; the
mixture's moments follow from E[X²] = Σw(σ² + μ²)); mixture percentiles have no closed form, so
the analytic oracle is the Numerics `Mixture` distribution's numerical inverse CDF, with an
independent `MersenneTwister(12345)` Monte Carlo oracle cross-checking it and the report's
published 10M-sample constants corroborating.

## Results

### Additive — exact solution N(130, √30) (report Table 48)

| Statistic | Exact | RMC.TotalRisk | % difference |
|---|---|---|---|
| Mean | 130.0000 | 130.0000 | 0.000% |
| Std. deviation | 5.4772 | 5.4761 | 0.020% |
| 5th percentile | 120.9908 | 121.0072 | 0.014% |
| 95th percentile | 139.0092 | 139.0178 | 0.006% |

Tolerances (k = 4, N = 10⁶): mean ±4σ/√N = ±0.0219; sd ±4σ/√(2N) = ±0.0155;
percentiles ±4·√(p(1−p)/N)/f(x_p) = ±0.0463 with f = φ(1.6449)/σ.

### Average, weights 0.3/0.2/0.5 — exact solution N(57, √6.65) (report Table 49)

| Statistic | Exact | RMC.TotalRisk | % difference |
|---|---|---|---|
| Mean | 57.0000 | 57.0000 | 0.000% |
| Std. deviation | 2.5788 | 2.5783 | 0.019% |
| 5th percentile | 52.7583 | 52.7633 | 0.009% |
| 95th percentile | 61.2417 | 61.2456 | 0.006% |

The Average engine path draws the children **independently** each realization (variance Σw²σ²) —
the match on σ = 2.58 is the direct check that pooling is independent, not co-monotonic.
Tolerances: mean ±0.0103; sd ±0.0073; percentiles ±0.0218.

### Mixture, weights 0.3/0.2/0.5 — exact moments + analytic percentiles (report Tables 50–51)

Exact moments: mean Σwμ = 57 (identical to Average); E[X²] = Σw(σ² + μ²) = 5123.9 →
σ = √1874.9 = 43.3001 — nearly 17× the Average's σ, the report's central point about day/night
exposure. Percentiles from the analytic oracle (`Numerics.Distributions.Mixture.InverseCDF`,
numerically exact via Brent): q05 = 8.0652, q95 = 106.4078.

| Statistic | Exact / analytic | RMC.TotalRisk | % difference |
|---|---|---|---|
| Mean | 57.0000 | 56.9963 | 0.006% |
| Std. deviation | 43.3001 | 43.2968 | 0.008% |
| 5th percentile | 8.0652 | 8.0680 | 0.035% |
| 95th percentile | 106.4078 | 106.3954 | 0.012% |

Tolerances: mean ±4σ/√N = ±0.1732; sd ±0.0204 via SE(s) = √((μ₄ − σ⁴)/(4σ²N)) with the exact
mixture fourth central moment μ₄ = Σw·((μᵢ−57)⁴ + 6(μᵢ−57)²σᵢ² + 3σᵢ⁴) = 3,705,312;
percentiles ±0.0233 (q05, mixture density f = 0.03745) and ±0.0497 (q95, f = 0.01755). The
independent MC oracle (`MersenneTwister(12345)`, component-then-inverse-CDF sampling, 10⁶
draws) reproduces the analytic percentiles within the same k·SE in the same test.

### Corroboration against the published report constants (report Table 51)

The report's mixture values are themselves 10,000,000-sample Monte Carlo estimates printed to
two decimals — its q05 of 8.04 sits ≈ 0.025 from the analytic 8.0652. The engine is therefore
pinned to the report constants at a widened tolerance covering both samplings plus rounding
(±0.06 for q05, ±0.11 for q95, ±0.18 mean, ±0.03 sd); the analytic oracle above is the primary
check and this pin is corroboration — neither is ever tightened toward the other.

| Statistic | Report (10M MC) | RMC.TotalRisk | % difference |
|---|---|---|---|
| Mean | 57.00 | 56.9963 | 0.006% |
| Std. deviation | 43.30 | 43.2968 | 0.007% |
| 5th percentile | 8.04 | 8.0680 | 0.348% |
| 95th percentile | 106.38 | 106.3954 | 0.014% |

## Reproducibility pins

Per the verification policy's step 4 (the v1.0 seed-dependency bug regression):

- **Same content + same seed → bit-identical** realization streams across independently built
  instances (`Test_Reproducibility_SameSeed_BitIdentical`).
- **Metadata edits are inert**: renaming, re-identifying (`AssignNewId`), and relabeling the
  composite and every child, then re-running setup, reproduces the stream bit-for-bit — child
  sampler seeds derive from `HashCombine(seed, child.CanonicalHash(), ordinal)` and canonical
  hashes are metadata-inert (`Test_Reproducibility_MetadataEdits_BitIdentical`).
- **Compute edits move results**: an Average-mode weight nudge changes the canonical hash and
  the realization stream (`Test_Reproducibility_ComputeEdit_MovesStream`).

## Test map

| Test method | Checks |
|---|---|
| `Test_Additive_ThreeNormalChildren_MeanSdPercentiles_VsExact` | Additive vs exact N(130, √30) |
| `Test_Average_WeightedChildren_MeanSdPercentiles_VsExact` | Average vs exact N(57, √6.65) |
| `Test_Mixture_WeightedChildren_MeanSd_VsExact` | Mixture moments vs exact Σw formulas |
| `Test_Mixture_Percentiles_VsMixtureInverseCdfOracle` | Mixture percentiles vs analytic oracle + independent MC cross-check |
| `Test_Mixture_ReportConstants_Pinned` | Corroboration vs report Tables 50–51 constants |
| `Test_Reproducibility_SameSeed_BitIdentical` / `..._MetadataEdits_BitIdentical` / `..._ComputeEdit_MovesStream` | Seed-identity contract |

Engine-level day/night scenarios (the legacy `Test_Composite.vb` oracles with a Log-Normal
hazard and Normal fragility in the loop, day weight 0.45) put a composite behind the full risk
engine rather than exercising the function alone; they are converted in the
[composite engine family](composite-engine.md) alongside `Test_Composite_Hazard` and
`Test_Composite_Response`. The sibling function-level families are
[composite-hazard](composite-hazard.md), [composite-response](composite-response.md), and
[composite-transform](composite-transform.md).
