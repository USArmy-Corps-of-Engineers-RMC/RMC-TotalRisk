# Expected Annual Damage

**Test class:** `EadVerification` · **Tests:** 2 · **Run of record:** 2026-07-23, isolated run, ✅ all passed

The conversion of the legacy `Test_EAD` oracle (`Test_RiskAnalysis.vb:2375`) — Monte
Carlo integration of one eight-knot damage-frequency curve — verified **three ways**: an exact
closed form, the ported Monte Carlo oracle, and two equivalent engine mappings of the same
curve. This family also anchors the engine's damage-frequency (background-risk / EAD) use case
— the Dam Screening Tool's mean-only compute shape.

## Scenario (the exact legacy curve)

| Exceedance p | 0.5 | 0.2 | 0.1 | 0.04 | 0.02 | 0.01 | 0.005 | 0.002 |
|---|---|---|---|---|---|---|---|---|
| Damage ($) | 212 | 24,545 | 275,766 | 296,022 | 333,920 | 395,563 | 448,005 | 962,545 |

Linear between knots in probability, clamped flat outside (212 above exceedance 0.5; 962,545
below 0.002 — the legacy interpolator's end behavior). Oracle: uniform draws through
`MersenneTwister(12345)`, N = 10⁶ (legacy 10M ÷ 10 per policy).

## Three-way verification

| Quantity | Exact closed form | Oracle (±SE) | Engine |
|---|---|---|---|
| EAD (mean) | 52,085.41 | 52,214.76 (± 112.7) | 52,085.26 |
| Standard deviation | 112,502.46 | — (validated at 4·SE) | 112,501.17 |
| VaR at α = 0.01 | 395,563 (the knot damage) | — | 395,623 |
| CVaR at α = 0.01 | 614,983.50 | — | 615,180.94 |

- The closed form integrates the clamped piecewise-linear curve exactly (trapezoids + clamp
  rectangles; second moment by (Δp)(D²ᵢ + DᵢDᵢ₊₁ + D²ᵢ₊₁)/3; CVaR from the two deepest
  segments plus the clamp).
- The **oracle validates against the closed form** at 4·SE (mean and dispersion) — the ported
  legacy body reproduces the true integral.
- The **engine reproduces the closed form** at 1e-5 relative on the mean (measured 2.9e-6 at
  the run of record): the adaptive quadrature refines at 1e-8, and the measured residual was
  the then-current midpoint-trapezoid mass partition crossing the curve's probability kinks.
  The published means now read the quadrature's own recorded weights (the recorded-mass
  ledger), so the residual is the adaptive refinement's rather than the partition's and the
  bound holds a fortiori. The standard deviation carried the same residual doubled through
  the second moment (measured 1.1e-5; asserted at 5e-5). VaR/CVaR carry 0.1% relative
  output-resolution floors; off-knot exceedance probes carry 0.5% (the documented cost of
  reading a linear-in-probability segment through the output curve's log-log interpolation —
  measured ≈ 0.2%).

## Two engine mappings, equal by construction

The frequency curve becomes the component hazard (exceedance versus damage) with an identity
consequence (knots y = x — piecewise-linear identity is exact):

| Mapping | Shape | Pins |
|---|---|---|
| A — background | a single response-free non-failure mode | Background.Mean = Total.Mean = NonFail.Mean = EAD; APF = 0; Total stream exhaustive (probability 1) |
| B — always-fail | a single certain-failure mode (fragility ≡ 1) | Fail.Mean = EAD; APF = 1; ConditionalMean = EAD |

The two mappings agree with each other at 1e-6 relative (independent adaptive refinements of
the same integrand).

## Reproducibility pins

| Pin | Result |
|---|---|
| Function/component renames + `AssignNewId` | ✅ bit-identical |
| XML round-trip | ✅ bit-identical |

## Notes

The legacy oracle computed the mean and returned without printing (debugger-inspected); the
closed form now anchors the family, with the oracle serving as the ported-body cross-check.
