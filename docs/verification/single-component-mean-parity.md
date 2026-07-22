# Single-Component Mean Parity — Verification Results

**Test class:** `SingleComponentMeanParityVerification` · **Status:** ✅ Verified (2026-07-22, Phase 4)

The free regression gate of the v0.13 means-versus-tails policy: the Phase 4 engine
corrections (exact LEC construction, stable moments, mixture exposure) deliberately move tail
measures but leave the **means** algebraically unchanged — so the five summary means and the
annualized failure probability verify against a legacy-style Monte Carlo oracle that never
touches the engine.

## Scenario

Deterministic tabular inputs, shared exactly between the engine and the oracle (both sides
interpolate the same piecewise-linear tables, so the model is identical by construction):

- Hazard: stage-frequency table from Normal(100, 20) quantiles on a z-grid (step 0.25, ±8),
  linear probability interpolation.
- Fragility: P[F|h] table from Φ((h − 140)/30) on the matching grid, linear interpolation.
- Failure consequence: linear (60 → 0) to (200 → 1000), clamped; non-failure consequence:
  linear (60 → 0) to (200 → 100), clamped.

## Oracle and tolerances

One million hazard draws through `MersenneTwister(12345)` (the legacy seed) inverse-transform
sampling with the oracle's own linear interpolator, accumulating per draw `pF·C_F`,
`(1 − pF)·C_NF`, their sum, `pF·max(0, C_F − C_NF)`, `C_NF`, and `pF`. Tolerances are
k·SE with k = 4 and SE = σ̂/√N computed in-run per output (≈ 0.1% of σ̂ at N = 10⁶); the
engine side is Gauss–Kronrod quadrature at relative tolerance 1e-8, so the oracle's Monte
Carlo error dominates every comparison.

## Pins

| Pin | Outputs | Result |
|---|---|---|
| Mean-only vs oracle | Fail, NonFail, Total, Excess, Background means + annualized failure probability, each within 4·SE | ✅ within 4·SE |
| Full-MC vs mean-only | On the deterministic scenario every realization equals the mean-only answer (1e-12 relative) — both paths share one compute kernel | ✅ degenerate ensemble |

The risk-type decomposition identities `E[C_T] = E[C_F] + E[C_NF] = E[C_Δ] + E[C_B]` are
additionally pinned exactly in the fast unit suite (`RiskAnalysisTests`).
