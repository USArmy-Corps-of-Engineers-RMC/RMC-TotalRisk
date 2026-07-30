# Parametric Consequence Function Verification

**Test class:** `ParametricConsequenceVerification` · **Tests:** 7 · **Run of record:** 2026-07-21, isolated run, ✅ all passed

> Family: `ParametricConsequence` (`RMC.TotalRisk.RiskFunctions.Consequences`)
> Anchor: closed-form algebra and exact lognormal theory + an independent Monte Carlo oracle —
> this family is **greenfield** (new in v1.1, no v1.0 ancestor, no legacy oracle)
> All comparisons **very good** (≤ 1%; worst case 0.078% between two Monte Carlo estimators)

## Overview

The parametric consequence function is the closed-form power model

> C(h) = clamp(α · max(h − h₀, 0)^β, 0, U)

per architecture doc §6.4 and USACE depth-damage conventions (ER 1110-2-1156 / HEC-FDA): zero
consequence at and below the damage-initiation threshold h₀, power-law growth above it (the
best-fit parametric form for observed depth-damage data in the literature), and saturation at
the cap U. Knowledge uncertainty realizes the coefficients per realization as
α_i = α·e^{σ_α·Z₁}, β_i = β·e^{σ_β·Z₂} with independent standard normal deviates from two
sampler dimensions (log-space scatter preserves coefficient positivity).

Two exact anchors make the family verifiable without a legacy oracle:

1. **σ_β = 0, no cap:** C(h) = α·s^β·e^{σ_α·Z} with s = h − h₀ is *exactly lognormal* at every
   hazard — LogN(ln(α·s^β), σ_α) — so means and percentiles have closed forms.
2. **The unit offset (h = h₀ + 1):** s = 1 makes the exponent drop out entirely,
   C = α·e^{σ_α·Z₁}·1^{β_i} = α·e^{σ_α·Z₁}, so the distribution is LogN(ln α, σ_α) *regardless
   of σ_β* — a sharp check that the two sampler dimensions do not leak into each other.

**Documented omission:** no mean asserts exist for σ_β > 0 with U = ∞ at hazards more than one
unit above the threshold. There the exact mean is E[s^{β_i}] — the lognormal moment-generating
function evaluated at ln(s) > 0 — which **diverges**: the sample mean does not converge and no
tolerance can make such an assert meaningful. The model validates this configuration with a
heavy-tail warning; verification checks means only under a finite cap (which bounds them) and
checks percentiles (always well-defined) in every configuration. A failing mean assert in that
regime is mathematics, not tolerance — never "fix" one by widening.

## Reference configuration

α = 10, β = 1.5, h₀ = 2, σ_α = 0.3, σ_β = 0.2. Engine ensembles: **N = 1,000,000**
realizations (`SetupSampler(N, 12345, LatinHypercube)`).

## Results

### Deterministic curve — exact closed form (tolerance 10⁻¹²)

With U = 500 (saturation crossing h₀ + (U/α)^{1/β} = 2 + 50^{2/3} = 15.5721):

| Hazard | Expected | Notes |
|---|---|---|
| h = 1, h = 2 | 0 | exactly zero at and below the threshold (not an epsilon offset) |
| h = 3 | 10 | the unit offset: C = α |
| h = 6 | 80 | α·4^{1.5} |
| h = 12 | 316.23 | α·10^{1.5} |
| h = crossing | 500 | the cap engages exactly at the crossing |
| h = crossing + 10 | 500 | flat at U beyond it |

All exact (pure double algebra); `MaxHazard()` returns the crossing.

### σ_α-only ensemble vs the exact lognormal (σ_β = 0, U = ∞)

| Hazard | Statistic | Exact (lognormal) | RMC.TotalRisk | % difference |
|---|---|---|---|---|
| h = 3 | Mean | 10.4603 | 10.4603 | 0.000% |
| h = 3 | 5th percentile | 6.1051 | 6.1051 | 0.000% |
| h = 3 | 95th percentile | 16.3797 | 16.3797 | 0.000% |
| h = 6 | Mean | 83.6822 | 83.6822 | 0.000% |
| h = 6 | 5th percentile | 48.8410 | 48.8411 | 0.000% |
| h = 6 | 95th percentile | 131.0374 | 131.0374 | 0.000% |

Exact values: mean = α·s^β·e^{σ²/2}; q_p = α·s^β·e^{σ·z_p}. Tolerances (k = 4, N = 10⁶): mean
SE = mean·√(e^{σ²}−1)/√N → ±0.0129 (h=3) / ±0.1027 (h=6); percentile SE = √(p(1−p)/N)/f(q)
with the lognormal density f(q) = φ(z_p)/(q·σ) → ±0.0155/±0.0416 (h=3), ±0.1239/±0.3323 (h=6).
The 0.000% differences reflect Latin hypercube stratification: with one stratified dimension
driving an exactly-lognormal output, the empirical quantiles are near-exact by construction.

### Two-sigma ensemble at the unit offset (σ_α = 0.3, σ_β = 0.2, U = ∞, h = 3)

| Statistic | Exact (LogN(ln 10, 0.3)) | RMC.TotalRisk | % difference |
|---|---|---|---|
| Mean | 10.4603 | 10.4603 | 0.000% |
| Median | 10.0000 | 10.0000 | 0.000% |
| 5th percentile | 6.1051 | 6.1051 | 0.000% |
| 95th percentile | 16.3797 | 16.3797 | 0.000% |

Identical to the σ_α-only anchors because the exponent drops out at s = 1 — the direct proof
that dimension 2 (σ_β·Z₂) does not contaminate dimension 1.

### Two-sigma capped ensemble vs the independent MC oracle (U = 200, h = 6)

Oracle: `MersenneTwister(12345)`, 10⁶ draws, two uniforms per draw through the standard normal
inverse CDF → C = min(α_i·4^{β_i}, 200) — Numerics primitives only, never model-library code.
Both sides are Monte Carlo, so tolerances are k·(SE_oracle + SE_engine) = 8·SE computed in-run
(mean SE = σ̂/√N; percentile SE = √(p(1−p)/N)/f̂ with f̂ from a central difference on the
sorted oracle).

| Statistic | MC oracle | RMC.TotalRisk | % difference | Tolerance |
|---|---|---|---|---|
| Mean | 92.3632 | 92.3728 | 0.010% | ±0.3632 |
| 5th percentile | 37.1206 | 37.1495 | 0.078% | ±0.2818 |
| Median | 81.1561 | 81.0959 | 0.074% | ±0.4209 |
| 95th percentile | 200.0000 | 200.0000 | 0.000% | exact (cap atom) |

The 95th percentile sits on the cap atom — more than 5% of the mass clamps to U, the density
there is infinite and the percentile SE is exactly zero — so both estimators return the cap
itself and the comparison is exact by construction rather than statistical.

## Reproducibility pins

- **Same content + same seed → bit-identical** streams across independently built instances
  (`Test_Reproducibility_SameSeed_BitIdentical`).
- **Metadata edits are inert**: rename, `AssignNewId`, and relabeling followed by re-setup
  reproduce the stream bit-for-bit (`Test_Reproducibility_MetadataEdits_BitIdentical`).

## Test map

| Test method | Checks |
|---|---|
| `Test_DeterministicCurve_ClosedFormPoints_Exact` | Closed-form algebra at 10⁻¹² |
| `Test_SigmaAlphaOnly_LognormalMeanAndPercentiles_VsExact` | Exact lognormal at h = 3 and h = 6 |
| `Test_TwoSigma_UnitOffset_LognormalPercentiles_VsExact` | Dimension-independence anchor at s = 1 |
| `Test_TwoSigma_McOracle_PercentilesAndClampedMean_VsSampler` | Full configuration vs independent MC oracle |
| `Test_Reproducibility_SameSeed_BitIdentical` / `..._MetadataEdits_BitIdentical` | Seed-identity contract |
