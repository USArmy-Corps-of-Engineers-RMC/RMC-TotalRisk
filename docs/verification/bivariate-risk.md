# Bivariate Risk Verification

**Test class:** `BivariateRiskVerification` · **Tests:** 5 · **Run of record:** 2026-09-05, isolated run, ✅ all passed (re-anchored for the two-dimensional adaptive conditional interior; prior run of record 2026-08-07 on the fixed conditional grid — its measured figures are retained below as the historical record)

> Family: the bivariate engine end to end (`BivariateHazard` + `BivariateTransform` + `BivariateResponse` + `BivariateConsequence` through `RiskAnalysis`) under the adaptive probit conditional interior
> Anchors: the ported legacy `Test_Bivariate_Risk` Monte Carlo oracle, an exact union-grid closed form with the re-captured legacy 100M cross-check, a re-anchored chained-bilinear oracle, and the collapse-versus-joint consistency cross-anchor
> Engine-versus-oracle comparisons are statistical (k·SE, k = 4) plus the adaptive conditional figure (1e-4 relative, measured ≈ 3.7e-6 on the seismic fixture class); every re-anchor cites the 2026-09-05 ruling in its test docs

## Fixture provenance

The seismic fixtures are transcribed **verbatim** from the legacy `Test_TotalRisk\Test_BivariateRisk.vb` and shared by both bivariate families through one internal fixture class: the 15-knot PGA frequency curve (log-interpolated exceedance), the 29-knot stage-duration curve (normal-Z-interpolated), the 4×6 system-response surface (log-interpolated probabilities), and the stage-driven life-loss curve. The engine scenario wires them through the graph: an independence-copula `BivariateHazard` over the two curves as deterministic tabular marginals, the joint 4×6 `BivariateResponse`, and the univariate life loss riding the failure path bound to the raw secondary (stage) signal.

The ported oracles preserve `MersenneTwister(45678)` and the exact per-realization draw order at 1,000,000 realizations (the legacy 100,000,000 dropped 100× per the conversion policy; tolerances derive from the count actually run). The oracle's surface lookups ride the same Numerics `BivariateEmpirical`/`Bilinear` primitives the legacy test used — out-of-range clamping is therefore identical on both sides by construction — while the risk mathematics (Monte Carlo simulation versus conditional-bin quadrature) is fully independent.

## Test_BivariateRisk_EngineVsLegacyOracle

The legacy oracle draws (pga, stage, rnd) each realization — rnd unconditionally, matching the legacy loop — and records the stage-interpolated life loss on failure. Oracle run of record: EAD 0.035252 (SE 2.24e-3), failure probability 2.98e-4 (298 failures).

| Assert | Tolerance derivation |
|---|---|
| EAD vs the oracle (1000-bin configuration) | k·σ̂/√N (k = 4, σ̂ in-run Welford) + the adaptive conditional figure (1e-4 relative, measured ≈ 3.7e-6 on this fixture class — the study's adaptive layer) |
| Failure probability vs the oracle | binomial k·√(p(1−p)/N) + the adaptive figure |
| FN ordinates at {1, 25, 50, 100, 150} lives | log-log LEC reads; binomial k·SE per probe + the adaptive figure; expected exceedance counts {298, 298, 271, 179, 102}; the 200-lives probe is excluded at 12 expected (< ~100 per the family policy) |
| Default-vs-1000-bin consistency | both configurations integrate adaptively, so their answers must agree: \|ratio − 1\| < 5e-3 on both the probability and EAD axes |

The historical fixed-grid overshoot regime — EAD ratio 2.418 and probability ratio 1.351 between the 20- and 1000-bin runs, the consequence weighting compounding the tail concentration — is retired by the re-anchor: the bin count no longer selects the production grid, and the consistency pin above is its replacement. The fixed grid's convergence series remains measured by the discretization instrument ([copula-dependence](copula-dependence.md)).

## Test_BivariateSRP_ClosedForm

The legacy `Test_Bivariate_SRP` target E_Y[CDF(0.8, Y)] is computed **exactly**: at the primary knot 0.8 the surface row is piecewise log₁₀-linear in stage and the marginal interpolates stages linearly in Φ⁻¹(non-exceedance), so on the union grid each segment integrates in closed form as a lognormal partial expectation, e^{α+β²/2}·[Φ(z₂−β) − Φ(z₁−β)], plus the marginal's two saturation atoms (mass 0.001 at stage 2529.8 and 2.14e-6 at 2633.5). The closed form is cross-checked against a dense Simpson reference over the engine's own empirical-distribution semantics (2²¹ intervals; agreement 5.3e-9 relative, limited by the uniform grid's slope-discontinuity residuals at the union knots).

| Quantity | Value |
|---|---|
| Exact closed form | 0.029670393164335482 |
| Legacy 100M re-capture (`MersenneTwister(45678)`, both legacy draws per realization) | 0.029662826999636242 (σ̂ = 0.0794859, SE = 7.95e-6) — 0.95·SE from the closed form; asserted at k·SE |
| Engine probe, default (20-bin) configuration | measured ≈ 3.7e-6 relative from exact; pinned at 1e-4 |
| Engine probe, 1000-bin configuration | measured ≈ 3.7e-6 relative; pinned at 1e-4 — the bin knob bounds the fixed-slice sweep budget, it no longer sets a grid |
| Per-slice adaptive conditional sweep at the exact slice | measured ≈ 5.3e-12 relative; pinned at 1e-9 |
| The fixed-grid instrument at 1000 bins | inside the pinned 4e-3 figure — the instrument reproduces the historical fixed-grid series (0.353 at 20 bins, 1.98e-3 at 1000 on the 2026-08-07 run of record) |

The legacy test printed to the debugger and recorded nothing, so the 100M constant pinned in the test IS the run of record, captured once from the ported oracle at the legacy seed and count. The engine probe evaluates through a degenerate primary marginal bracketing PGA 0.8 at ±1e-6 (the symmetric bracket cancels the linear error terms, leaving an O(1e-12) residual).

## Test_Damrae_ChainedBilinear

The PFM-08 scenario **re-anchored as a new pinned oracle** (recorded in the traceability notes): the legacy method printed only, used BCL `Random` (no contractual stream, no recorded constants), and built its SRP from a per-realization triangular distribution that no deterministic engine chain can represent. The re-anchored scenario chains four bilinear surfaces, shared bit-for-bit between oracle and engine:

- the deformation surface (pga, pool) — **verbatim**;
- the warning-time surface — the **verbatim** grid with its probability axis affinely remapped onto deformation (def = (u − 0.11)·66 + 0.2; exact, and the map spans the deformation surface's output range so no clamping occurs at the seam);
- a re-anchored SRP surface over (time, pool) — the one genuinely new fixture;
- the life-loss surface (time, pool) — **verbatim**, as a `BivariateConsequence`.

The pool curve's endpoint exceedances trim from {1, 0} to {0.9999, 0.00001} (the normal-Z transform maps 1 and 0 to ±∞). The engine wires hazard → two chained bivariate transforms (secondary passthrough) → joint response → bivariate consequence; the oracle draws (pga, pool, rnd) with `MersenneTwister(45678)` at 1M and evaluates the same chain.

| Assert | Values | Tolerance derivation |
|---|---|---|
| EAD vs the oracle | oracle 5.8385 (SE 0.0471) | k·SE + the retained 2.5% allowance as a deliberate upper bound (under the fixed grid a deterministic +1.60% overshoot remained at 1000 bins — the pool marginal's normal-Z tail; the adaptive interior sits far inside it) |
| Failure probability vs the oracle | oracle 0.018498 (18,498 failures) | binomial k·SE + 2.5% (the same retained bound; fixed-grid measurement was +1.22%) |
| Default-vs-1000-bin consistency | — | \|ratio − 1\| < 0.01 on both axes (replacing the historical fixed-grid overshoot regime, EAD ratio 1.643 / probability ratio 1.460 on the 2026-08-07 run of record) |

## Test_CollapseVsJoint_Consistency

The deliberate cross-anchor between the preserved v1.0 collapse method and the new bivariate-hazard path: the SAME log-bilinear surface run (a) collapse-mode under the univariate PGA hazard with Voronoi weights derived from the stage hazard by the explicit estimator, and (b) joint-mode under the independence bivariate hazard — both with the same primary-bound damage, so both estimate ∫ E_Y[P(x, Y)]·C(x) dF_X and its probability analogue against the exact dense reference (the union-grid conditional expectation integrated densely over the PGA curve).

| Path | Distance to the exact reference (probability axis) |
|---|---|
| Collapse, verbatim surface (6 Voronoi cells) | 27.3% (2026-08-07 measurement; pinned regime < 35%) |
| Collapse, twice-refined interpolant-preserving surface (13 × 21, re-derived Voronoi weights) | 1.91% (pinned regime < 3%) |
| Joint, default configuration | at the study's near-exact figure (< 4e-3; the fixed grid read 35.4% here on the 2026-08-07 run of record) |
| Joint, 1000-bin configuration | at the same near-exact figure (the fixed grid read 0.199%) |

Re-anchored under the 2026-09-05 ruling: the joint side integrates adaptively at both bin configurations, so both joint runs pin inside the near-exact figure and the historical 20-versus-1000 joint refinement narrative is retired; the collapse side keeps its strict shrink-under-refinement asserts (that mechanism — surface refinement — is real and unchanged). The tightened paths agree within their combined pinned distances, and the engine's collapse semantics are pinned against an independent re-implementation — log-interpolated collapse knots with first/last-ordinate clamps — at 2e-5 relative (measured agreement ≈ 5e-6), so the comparison rests on verified semantics. The collapse refinement inserts axis midpoints with log-interpolated rows and columns, sampling the SAME surface interpolant at twice the density on both axes; refining only the secondary axis would leave the collapse curve's primary-axis log-interpolation gap in place, which is why both axes refine together.

## Test_V1Arrangement_StagePrimaryPgaCollapsed

The axis-choice question, measured on the legacy scenario: the STAGE curve drives the component as the univariate hazard and PGA is marginalized out through the transposed 6×4 surface's automatically derived Voronoi weights — the v1.0 arrangement — with the life loss reading stage, now the driving signal. The exact reference is the same double integral the other tests use, evaluated by Fubini in the opposite order (the closed-form stage marginalization at each PGA level, life-loss-weighted for the risk axis, integrated densely over the PGA curve); the weighted closed form is asserted against the unweighted one at a unit consequence before use.

The pins, re-anchored under the 2026-09-05 ruling: the v1.0 arrangement must sit within 0.5% of the exact reference on both axes (its 2026-08-07 measurement: 0.241% on both), **and so must the default-configuration bivariate arrangement of the identical scenario** — the pin that states the ruling's outcome. Under the fixed grid the default-bin bivariate arrangement read 35.4% / 145% against the same references (the historical > 50× superiority assert this replaces); under the adaptive interior both arrangements land at fractions of a percent, so the axis choice is a modelling decision rather than a conditioning one. The derived PGA weights {0.999593, 2.90e-4, 7.49e-5, 4.21e-5} still put 99.96% of the mass on the surface's first level — the collapse remains the cheaper arrangement of this scenario — and the engine values are additionally asserted against the 1M oracle at k·SE plus the 0.5% distance. The relaxed practitioner guidance is in [bivariate-hazards](../technical-reference/bivariate-hazards.md). Note the EAD comparison against the oracle is **oracle-limited, not engine-limited**: only 298 of the 1,000,000 realizations fail, so the oracle's own EAD standard error is ≈ 6.4% relative and the oracle itself sits ≈ 1.2σ from the exact reference both arrangements reproduce to fractions of a percent.
