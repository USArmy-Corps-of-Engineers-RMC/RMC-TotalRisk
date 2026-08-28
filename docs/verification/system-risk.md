# Multi-Component System Risk — Verification Results

**Test class:** `SystemRiskVerification` · **Tests:** 6 · **Run of record:** 2026-08-28 (incl. the joint Sobol driver), isolated run, ✅ all passed

The system-aggregation family of the means-versus-tails policy: both multi-component methods are verified
against NEW brute-force **event-level** Monte Carlo oracles that draw annual outcomes — hazards
by inverse transform, component failures by Bernoulli draws against the fragilities — and
accumulate the realized system loss. That realized-loss distribution is exactly the zero-inflated
combination distribution the additive convolution enumerates, and exactly the correlated-hazard
combination distribution the joint VEGAS path integrates, so the oracles test the full curve, not
just the moments. v1.0 offers nothing to compare against here: its additive path combined two
moments and produced **no system LEC at all**, and its joint path collapsed each component to its
conditional mean before combining (`ComponentRiskOutput.vb:39`).

## Scenario

Two components on deterministic tabular inputs shared bit-for-bit with the oracles
(dense z-grid tables, step 0.25 over ±8):

| | Component A ("Dam") | Component B ("Levee") |
|---|---|---|
| Stage frequency | Normal(100, 20) quantiles | Normal(80, 15) quantiles |
| Fragility | Φ((h − 140)/30) | Φ((h − 115)/20) |
| Failure consequence | linear (60 → 0, 200 → 1000) | linear (50 → 0, 160 → 400) |
| Non-failure consequence | linear (60 → 0, 200 → 100) | linear (50 → 0, 160 → 40) |

Each component also carries its non-failure mode, so all five risk-type streams are live. The
additive scenario is independent; the joint scenario correlates the hazards at ρ = 0.6 through
the correlation-matrix dependency.

## Oracles and tolerances

One million annual events per oracle. Draw order per event: the two hazard uniforms, then the
two failure uniforms — `MersenneTwister(12345)` for the additive/independent oracle,
`MersenneTwister(45678)` for the correlated oracle (which maps its uniforms through the exact
2-D Cholesky, z_B = ρ·z_A + √(1 − ρ²)·z_B). Tolerances are k·SE with k = 4:

| Measure | SE derivation |
|---|---|
| Mean | σ̂/√N (plus the engine's reported VEGAS standard error in quadrature on the joint side) |
| Standard deviation | delta method: √(m̂₄ − σ̂⁴)/(2σ̂√N) |
| Failure union, exceedance at c | binomial √(p̂(1 − p̂)/N) (joint side adds a conservative binomial error at the 100,000 recorded VEGAS evaluations) |
| Value-at-risk (α = 0.01) | compared in probability space — the engine curve's exceedance at the oracle's empirical 1% quantile must be 0.01 within 4·√(0.01·0.99/N) plus a 1e-3 output-resolution slack |
| Conditional value-at-risk | tail mean σ̂_tail/√(0.01·N) plus a documented 5% envelope for the log-log quantile integration of the thinned output curve |

## Results — additive method (exact lattice convolution)

Engine values from the recorded run of `Test_AdditiveSystem_LecVsBruteForceOracle`
(mean-only, `SystemConvolutionPoints` = 4096):

| Measure | Oracle (±SE) | Engine | % difference | Rating |
|---|---|---|---|---|
| System mean | 102.273 ± 0.148 | 102.405 | 0.13% | Very good |
| System standard deviation | 147.609 ± 0.187 | 147.755 | 0.10% | Very good |
| Failure union | 0.20520 ± 0.00040 | 0.20568 | 0.23% | Very good |
| P(L > 150) | 0.19186 | 0.19222 | 0.19% | Very good |
| P(L > 400) | 0.08094 | 0.08138 | 0.55% | Very good |
| P(L > 700) | 0.003824 | 0.003841 | 0.44% | Very good |
| Exceedance at oracle VaR₀.₀₁ (631.24) | 0.01 | 0.01005 | 0.5% | Very good |
| CVaR₀.₀₁ | 699.12 ± 0.62 | 699.52 | 0.06% | Very good |

Every deviation is within ~1.2 oracle standard errors — the engine side is quadrature plus an
exact convolution, so the oracle's own sampling noise dominates every comparison.

## Results — joint method (correlated VEGAS with real combination enumeration)

Engine values from the recorded run of `Test_JointSystem_CorrelatedVsBruteForceOracle`
(mean-only, γ = 1 baseline, warm-up 2,000 × 5 cycles, recording 20,000 × 5 passes):

| Measure | Oracle (±SE) | Engine | % difference | Rating |
|---|---|---|---|---|
| System mean | 102.613 ± 0.156 | 102.400 (engine SE 0.0043) | 0.21% | Very good |
| Failure union | 0.19867 ± 0.00040 | 0.19810 | 0.29% | Very good |
| P(L > 150) | 0.18626 | 0.18542 | 0.45% | Very good |
| P(L > 400) | 0.08236 | 0.08214 | 0.26% | Very good |
| P(L > 700) | 0.008872 | 0.008828 | 0.50% | Very good |

Two exact pins ride along: the recorded system mean equals the sum of the recorded component
means to 1e-9 relative (both 102.399799… — the additive-combine identity survives the
combination enumeration algebraically), and the exhaustive Total budget self-normalizes to
1 + 4.5e-14 across the five recording passes.

The physics reads correctly off the two runs: positive hazard correlation *lowers* the failure
union (0.1987 vs 0.2052 — the failures bunch into the same years) while *fattening* the far
tail (P(L > 700): 0.00887 vs 0.00382 — both components fail together more often).

## Power-transform audit (the tail-focus empirical gate)

`Test_JointSystem_TailFocusAudit` runs the correlated scenario at γ = 1 (None), manual γ = 4,
and the automatic probe-driven focus: the system mean and failure union agree within the
combined reported errors, a deep-tail ordinate agrees between the samplings, and every recorded
budget self-normalizes to one — the power-transform Jacobian demonstrably reaches the recorded
weights (`Vegas.cs:466-472`), so γ > 1 is safe to enable. The default `Automatic` mode derives
its target from the deterministic per-component failure-probability quadrature probe
([technical-reference/risk-integration.md](../technical-reference/risk-integration.md)).

## Reproducibility pins

`Test_SystemReproducibility_ShuffleAndRename`: reordering **plus** renaming the components is
bit-inert on the additive path (content-based seeds; the convolution associates in
canonical-hash order), with component results following content identity through the shuffle;
renaming is bit-inert on the joint path. Joint component *reordering* is statistically
equivalent but not bit-identical by construction — the VEGAS variates couple the hypercube
dimensions, so reordering permutes which coordinate stream drives which component (documented
in the architecture doc §7.8).

## The joint Sobol driver

`Test_JointSystem_SobolDriver_OracleParityAndReproducibility` gates the opt-in
`RiskAnalysisOptions.UseSobolJointSampling`, which drives the joint VEGAS integration with
seeded Matousek-scrambled Sobol points at the same content-derived per-realization seed as the
default pseudo-random driver. Under the option, the correlated two-component system reproduces
the brute-force event oracle's mean and failure union within the same combined-error bounds as
the pseudo-random test, the recorded budget self-normalizes exactly with a manual γ = 4
tail focus (the power-transform Jacobian reaches the weights under the quasi-random driver
too), and two enabled runs publish byte-identical results — the seeded scrambling restoring the
content-seed reproducibility contract the v1.0 unrandomized sequence could not honor. Run of
record 2026-08-28: `SystemRiskVerification` 6/6 passed (20.1 s wall), isolated invocation.
