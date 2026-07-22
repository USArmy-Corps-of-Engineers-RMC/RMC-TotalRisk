# Exact LEC / Mixture-Exposure Tail — Verification Results

**Test class:** `ExactLecTailVerification` · **Status:** ✅ Verified (2026-07-22, Phase 4)

The first Monte-Carlo-parity family of the v0.13 policy: standard deviation, exceedance
ordinates, value-at-risk, and conditional value-at-risk are verified against a NEW brute-force
Monte Carlo oracle that draws the full model per realization — never against the v1.0 engine,
whose 200-bin midpoint histogram, raw-power-sum moments, and mixture-mean flattening are the
defects the v1.1 exact construction and ratified Q-V branch enumeration correct.

## Scenario

Deterministic tabular hazard and fragility on the shared z-grids (as in the mean-parity
family), with a day/night mixture consequence: day weight 0.55 at C_day(h) = 0.5·h and night
weight 0.45 at C_night(h) = 2·h (linear tables to stage 300); no non-failure mode. The
unconditional annual loss is `L = 1{failed}·C_branch(H)` — exactly the engine's defective
Fail curve with its implicit zero atom.

## Oracle and tolerances

One million realizations over three independent MersenneTwister streams — hazard 12345,
failure Bernoulli 45678, exposure branch 78910 (the legacy seed family). Tolerances (k = 4):

| Measure | SE derivation |
|---|---|
| Mean | σ̂/√N |
| Standard deviation | delta method: √(m̂₄ − σ̂⁴)/(2σ̂√N) |
| Exceedance at c | binomial: √(p̂(1 − p̂)/N) |
| Value-at-risk (α = 0.01) | quantile: √(α(1 − α)/N)/f̂(q̂), density from the empirical quantile slope over ±0.1% exceedance, plus a 0.1% relative floor absorbing the engine curve's output resolution |
| Conditional value-at-risk | tail mean: σ̂_tail/√(αN) |

## Pins

| Pin | Result |
|---|---|
| Mean (the v1.0-parity gate) | ✅ within 4·SE |
| Standard deviation (zero atom + branch spread) | ✅ within 4·SE |
| Exceedance at consequence 100 and 300 (300 is beyond the day branch's reach — only branch enumeration populates it) | ✅ within 4·SE |
| Value-at-risk and conditional value-at-risk at α = 0.01 | ✅ within tolerance |
| Counter-pin: the flattened (Average) composite reproduces the mean (1e-6 relative) but starves the deep tail — its exceedance at consequence 300 is an order of magnitude below the mixture's | ✅ defect made visible |

## Notes

This family also hardened the engine's output thinning: a purely log-exceedance target ladder
left the flat bulk of the curve sparse enough for log-log interpolation to overshoot (~4% at
consequence 100), so the stored-curve thinning is a **hybrid ladder** — half log-spaced in
exceedance for the tail, half linear in consequence for the bulk (arch doc v0.14, item 8).
