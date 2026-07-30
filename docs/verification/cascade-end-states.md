# Cascading Response End States

**Test class:** `CascadeEndStateVerification` · **Tests:** 8 · **Run of record:** 2026-07-24, isolated run, ✅ all passed

The verification family for the cascade end-state design
([`MODEL_LIBRARY_ARCHITECTURE.md`](../requirements/MODEL_LIBRARY_ARCHITECTURE.md) §7.9): multi-stage
response chains as chance-node paths, end states as projected failure modes, final-polarity
classification, the claimed non-failure state's conditional complement, the flipped-final-sibling
excess pairing, and the across-unit combination semantics under every applicable
`FailureModeMethod`.

## Scenario

The Bucket-1 style model, wired through the **graph port surface** (the point of the family — the
`AddFailureMode` chain path cannot express Non-Fail ports):

| Piece | Definition |
|---|---|
| Hazard | LnNormal(85, 20), tabulated on a ±8 z-grid at step 0.1 (161 knots) |
| Initiation fragility | Φ((h − 140)/30), tabulated on a ±8σ z-grid at step 0.05σ (321 knots) |
| Progression fragility | Φ((h − 150)/20), same grid — fed by the initiation response's **Fail port** |
| Standalone fragility | Φ((h − 160)/10), same grid (the second combination unit) |
| Full breach (Fail port of progression) | five-knot consequence 0/10/100/1000/1500 |
| Partial damage (**Non-Fail port** of progression) | 0/2/20/200/300 — a claimed non-failure state |
| Standalone loss | 0/3/30/300/450 |
| Background (response-free path) | 0/1/10/100/150 |

Engine and oracle interpolate the SAME tables. N = 1,000,000; seeds `MersenneTwister(12345)`
(hazard) and `(45678)` (branch/selection/counterfactual uniforms, a FIXED draw count per
realization so the streams never depend on outcomes).

## Oracle mechanics

- **Natural oracles** (single cascade; joint independent): simulate the branch outcomes directly
  — initiation and progression uniforms give full-breach / partial / hold outcomes; the failure
  union is the final-polarity failure states only. The engine's claimed-state conditional
  `C·w/(1 − P_g)` is **exact under independence**, so natural simulation is the reference, not a
  convention mirror. The Background stream draws the complement-conditional mixture
  (q·partial + (1 − q)·background) — the engine's §7.9.5 no-failure world.
- **Convention oracles** (mutually exclusive; competing): implement the engine's documented
  across-unit conventions — unit-mass selection under the mutually-exclusive normalization, and
  capacity weak-link draws (the cascade unit's capacity inverts its monotone mass curve
  p₁(h)·p₂(h); competing admits only all-Fail signatures, §7.9.6) — with the complement split by
  the conditional share q. They verify the convention is implemented faithfully, which is all a
  convention admits.
- **Excess pairing:** full-breach excess pairs the partial sibling (§7.9.4, the per-mode methods'
  component excess); the joint scenario's excess pairs the complement mixture through an
  independent counterfactual uniform (the engine's pair baseline).

## Tolerances

k·SE with k = 4 (means σ̂/√N; σ by the delta method; probabilities binomial; VaR density-scaled
with the 0.1% relative floor; conditional mean by the first-order ratio-estimator SE; LEC probes
at data-driven levels carrying ≥ 100 exceedances). The competing scenario's total mean and σ
additionally carry a **1% relative floor** for the engine's 200-bin cumulative-incidence
discretization (the documented combination-consistency allowance).

## Results (oracle/engine, 2026-07-24)

| Scenario | Fail mean | Total mean | Excess mean | APF | σ_F | CVaR₀.₀₁ |
|---|---|---|---|---|---|---|
| Partial-damage cascade (ME, single unit) | 1.77903 / 1.79973 | 3.28622 / 3.30423 | 1.42322 / 1.43978 | 0.007136 / 0.0071313 | 28.157 / 28.561 | 177.90 / 180.04 |
| Joint across units (Independent, Maximum) | 1.93193 / 1.94492 | 3.36330 / 3.37901 | 1.60761 / 1.62082 | 0.008649 / 0.0085399 | 28.473 / 28.870 | 193.19 / 194.53 |
| Mutually exclusive across units | 1.76808 / 1.77272 | 3.16545 / 3.17270 | 1.37216 / 1.37572 | 0.009171 / 0.0092154 | 24.623 / 24.817 | 176.81 / 177.31 |
| Competing across units | 1.66734 / 1.69181 | 3.10389 / 3.12602 | 1.30030 / 1.31967 | 0.008476 / 0.0085378 | 24.444 / 24.774 | 166.73 / 169.22 |

Background and non-failure means agree at the same k·SE in every scenario (the claimed state's
conditional complement is the largest structural novelty — e.g. partial cascade background
1.75722 / 1.75820, versus 1.42736 for the raw background curve alone: the mixture is visibly
different and the engine matches it). VaR₀.₀₁ is zero on both sides (APF < α), so the tail
evidence is the CVaR and the two data-driven exceedance probes.

## Physics cross-checks

- **APF is the breach product only** (final polarity, §7.9.2): the partial-damage cascade's union
  ≈ E[p₁p₂] ≈ 0.00714, NOT the initiation probability (≈ 0.0308) — wiring the partial terminal
  moved complement mass, not failure mass.
- **Saturated-stage equivalence is bit-exact:** a two-stage cascade whose progression fragility
  is identically one reproduces the single-stage model to the bit (deterministic model, mean-only
  run — the polarity product's second factor is exactly 1.0 and the quadrature sees a
  bit-identical integrand).
- **Reliability mode:** the consequence-free cascade's APF matches the Rao-Blackwellized
  E[p₁(h)·p₂(h)] oracle at k·SE.
- **System aggregation:** the additive system's total mean equals the component-mean sum (1e-6,
  the convolution's mean-preservation gate) with a cascading component; the joint system runs
  with an intact mass balance.

## Reproducibility pins

| Pin | Result |
|---|---|
| Same seed, repeated run | bit-identical (LEC arrays + headline scalars via `DoubleToInt64Bits`) |
| Element renames + `AssignNewId` + XML round-trip | bit-identical — ports and polarities survive persistence |
| Partial terminal rewired onto the Fail port (duplicate-leaf claim, legal per Q2) | results move (counter-pin: polarity IS compute content) |

## Notes

- The knowledge-sampling contract is pinned at unit level
  (`Test_MultiStage_ResponseKnowledge_IndependentPerStage`, user directive 2026-07-24): every
  stage's response uncertainty samples independently like any other function — equal-content
  stages draw different curves — while one shared response instance wired into sibling end states
  is one knowledge quantity (its Fail and Non-Fail branches partition exactly on one sampled
  curve). The only cross-function coupling remains the failure/non-failure consequence
  pairing, which the §7.9.4 sibling resolution rides unchanged.
- No published constants exist for cascades; every assert is engine-versus-oracle. Oracle values
  may be pinned as captured constants in a later pass if regression pinning is wanted.
