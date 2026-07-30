# NFIP Assurance

**Test class:** `NfipAssuranceVerification` · **Tests:** 7 · **Run of record:** 2026-07-27, isolated run, ✅ all passed

The conversion of the active legacy `Test_NFIP_Assurance_TOL_50/55/60/65/70` oracles — the annual
probability of inundation (API) for the hypothetical NFIP levee (LP3 flow frequency →
log-interpolated rating transform → prior-to-overtopping fragility) — plus the
full-uncertainty **assurance** computation the technical report's NFIP appendix requires.
This family is the verification anchor for `ParametricUnivariateHazard`, `TabularHazard`
(dense-tabulation variant), `TabularTransform` (the rating chain), and the reliability mode's
`HazardThreshold` surface.

## The API model (TR Appendix I, equations 257–260)

The leveed area floods three ways: failure prior to overtopping, overtopping with failure,
and overtopping without failure. The API is the union

> API = ∫ f(h)·P_F(h) dh + ∫_{h > h_T} f(h)·(1 − P_F(h)) dh

which in the engine is the component's **Fail stream total probability** plus the **NonFail
stream's hazard-threshold exceedance** — the exact surface the v1.0 assurance diagnostic
(`AssuranceControl`) reads per realization. The engine runs in **reliability mode** (no
consequence functions): the component carries the LP3 flow-frequency hazard, one failure mode
chains the rating transform (logarithmic flow interpolation) into the fragility response, a
response-free non-failure mode supplies the NonFail stream, and
`SystemComponent.HazardThreshold` carries the top-of-levee threshold **in flow units** — the
rating's stage ordinates are the integers 0–100, so each TOL stage maps to an exact rating
knot (stage 50 ↔ 37,719.40 cfs; 55 ↔ 45,744.67; 60 ↔ 53,769.94; 65 ↔ 61,795.21;
70 ↔ 69,820.48) and the flow-space threshold is algebraically identical to the stage-space
one for the strictly increasing rating.

## Scenarios and oracle

Five active levee heights are covered. TOL 50 / 55 / 70 use deterministic LP3 hazards,
legacy 101-point ratings, and 21-knot fragilities. Their oracle runs at
`MersenneTwister(45678)`, N = 10⁶: q = LP3⁻¹(u); h = rating(q); flood when h > TOL **or** a
second uniform falls below the fragility, preserving the legacy short-circuit draw order. Exact
composite-Simpson quadrature anchors oracle and engine.

TOL 60 preserves the active two-knot workbench ramp in the source body; TOL 65 preserves its
active 21-knot fragility. Both generate an LP3 method-of-moments bootstrap with sample size 114
for each retained realization. The verification preserves the source seed/event-draw order,
injects the resulting parameter sets into the engine, and compares the aggregate engine API to
the mean of independent per-distribution conditional quadratures. The source-style one-event
Monte Carlo estimate is retained as a corroborating 4·SE check.

## Results

| TOL | Oracle (±SE) | Exact quadrature | Parametric engine (Δ exact) | Tabular engine (Δ exact) | Report MC / engine |
|---|---|---|---|---|---|
| 50 | 0.038060 (±1.91e-4) | 0.03824695 | 0.03824896 (+2.0e-6) | 0.03825985 (+1.3e-5) | 0.038197 / 0.038212 |
| 55 | 0.018911 (±1.36e-4) | 0.01882232 | 0.01882356 (+1.2e-6) | 0.01882363 (+1.3e-6) | 0.018829 / 0.018807 |
| 70 | 0.003850 (±6.19e-5) | 0.00381196 | 0.00381217 (+2.2e-7) | 0.00381225 (+2.9e-7) | 0.003820 / 0.003809 |

The parametric engine reproduces the exact integral to 5e-5 relative or better at every height
(an order under the documented 1e-4 allowance), and the tabular variant to 3.4e-4 relative at
the widest (TOL 50, inside its tabulation allowance). Report Table 104 pins hold both ways:
the oracle against the 10M Monte Carlo column at the combined 1M/10M binomial error, and the
engine against the v1.0 RMC-TotalRisk column within 0.1% at every height (the 0.5% parity band
documented; the report's own engine-versus-MC differences reach 0.3%).

## Bootstrap-hazard results

The isolated family passes 7/7, including both active bootstrap cases. For TOL 60 and TOL 65,
the engine ensemble mean matches the independent conditional-quadrature mean at the existing
ensemble allowance; the source-ordered single-event estimate also lies within its binomial
4·SE interval. Individual threshold-profile deltas are reported diagnostically, while the
asserted quantity remains the aggregate API computed by the legacy bodies.

## Assurance under knowledge uncertainty

Assurance at the 0.01 AEP accreditation target is **P(API ≤ 0.01) over the
knowledge-uncertainty ensemble** — the TR appendix requires the risk analysis to run with
full uncertainty (the ECB 2019-11 bands: accredit above 85%, do not accredit below 65%). The
test injects a deterministic 300-set LP3 parameter posterior into the parametric hazard
through `Estimate(IList<ParameterSet>)` (the BestFit import surface; mean-of-log ±0.0153
normal, sd-of-log ×exp(0.10·normal), skew ±0.15 normal at fixed seed 45678 — representative
of a ~100-year record), runs the TOL-70 reliability ensemble at 300 realizations, and
verifies:

- **Realization-for-realization API parity** against an exact per-parameter-set quadrature
  oracle — the parametric hazard's D = 0 sampling walks the injected posterior by index, so
  the two ensembles pair exactly;
- the **ensemble mean API**;
- the **assurance fraction** P(API ≤ 0.01) — equal exactly, with a borderline guard proving
  no realization sits within twice the comparison tolerance of the target (so the counts
  cannot legitimately differ);
- scenario health: the posterior spread makes assurance a discriminating measure (strictly
  inside (0.05, 0.995)).

Measured (the verifying run): ensemble mean API 0.0044102 (engine) versus 0.0044100 (exact
ensemble); assurance **95.3%** at the 0.01 target (an accredit-band outcome for this posterior,
strictly inside the health bounds); maximum per-realization deviation 2.46e-6 against the 8e-6
borderline margin — the engine and exact assurance counts agree exactly.

## Reproducibility pins

| Pin | Result |
|---|---|
| Component + function renames with fresh ids (parametric TOL 55) | ✅ bit-identical API |
| Component XML round-trip (the parametric hazard's estimate travels in its serialized results) | ✅ bit-identical API |

## Notes

- The FDA/NFIP variant is obsolete by technical-authority decision and is intentionally not
  ported. The active non-FDA TOL 60/65 bodies are covered above.
- The tabular-hazard variant (641-knot ±8 z-grid at step 0.025, normal-z probability
  interpolation, logarithmic flow interpolation) verifies `TabularHazard` on the same oracle
  family with a documented 5e-4 relative tabulation allowance.
