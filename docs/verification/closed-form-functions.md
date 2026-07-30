# Closed-Form Functions

**Test class:** `ClosedFormFunctionsVerification` · **Tests:** 4 · **Run of record:** 2026-07-25, isolated run, ✅ all passed

The family for `LinearTransform`, `PowerTransform`, and `NonparametricHazard`. **No
legacy oracles exist for these types** — the Dev-repo sweep found zero `Test_TotalRisk` usages
of the two transforms and only external-dataset FDA-importer usages of the nonparametric hazard
(the legacy `Test_EAD` and NFIP TOL tests build their curves from raw Numerics primitives and
were converted with their own families). The documented anchor is therefore the 2024 verification
report's *Nonparametric Hazard Function* section — the Beargrass Creek **SF-8** reach vs
HEC-FDA 1.4.3, **Table 38** — plus fresh Monte Carlo and quadrature oracles for the first
engine passage of the closed-form transforms.

## Scenarios and results (run 2026-07-25, isolated)

### V1 — Linear-transform chain (`Test_LinearTransformChain_EngineVsOracle`)

Five-knot deterministic flow-frequency hazard → `LinearTransform` (α = 2, β = 0.005,
σ = 0.75 uncertain — one co-monotonic knowledge percentile) → stage-fragility ramp →
stage-consequence ramp. The ensemble grand means (1,000 LHS realizations) against the flat
two-uniform MC oracle (`MersenneTwister(12345)`, N = 10⁶; by linearity of the expectation the
knowledge grand mean equals the flat double integral); tolerance k = 4 on the combined
oracle + outer-sampling error, engine charged at the MC rate.

| Output | Oracle | Engine | Assessment |
|---|---|---|---|
| Failure risk grand mean | 16.413447 (SE 0.0828) | 16.40104 | ≈ 0.15·SE — very good |
| AFP grand mean | 0.049907231 | 0.049890469 | ≈ 0.19·SE — very good |
| D = 0 dense quadrature (mean-only) | 16.172306 | 16.17356 | 7.8e-5 relative |

The deterministic-transform (D = 0) variant also runs the ensemble path with **bit-identical
realizations** (the sampling-dimension-zero walk proof: a deterministic transform consumes no
percentile matrix and the by-index sampling path returns the mean function).

### V2 — Power-transform chain (`Test_PowerTransformChain_EngineVsOracle`)

Same chain shape with `PowerTransform` forward (α = 0.05, β = 0.8, σ = 0.15 log-space):

| Output | Oracle | Engine | Assessment |
|---|---|---|---|
| Failure risk grand mean | 30.221204 (SE 0.0852) | 30.252739 | ≈ 0.37·SE — very good |
| AFP grand mean | 0.11429551 | 0.11436669 | ≈ 0.5·SE — very good |
| Inverse-form D = 0 quadrature (mean-only) | 20.985318 | 20.985842 | 2.5e-5 relative |

The `IsInverse` variant (stage = √(flow/5), deterministic) proves the inverse branch through
the engine against dense quadrature.

### V3 — SF-8 / Table 38 (`Test_NonparametricHazard_Sf8_Table38`)

The report's SF-8 inputs (transcribed from Figure 16): nine AEP→flow ordinates
(0.999 → 900 … 0.002 → 9,610), logarithmic hazard + Normal-Z probability transforms,
**effective record length 48**, extrapolation AEP 1e-4. Two anchors:

1. **The independent re-derivation oracle** — the exact legacy object pipeline (snapshot-array
   extrapolation, order-statistic SEs with the 0.99/0.01 pins and both monotone-smoothing
   passes, the base-e `LogNormal` moment mapping, and the per-ordinate **Brent** 1%-bound
   repair). Every derived ordinate agrees: means at 1e-9 relative (inlined vs object moment
   mapping), spreads at 1e-6 relative (**the closed-form σ repair against the legacy Brent
   fixed point — the optimization-equivalence anchor** for the v1.1 implementation
   improvements documented in the class remarks).
2. **Table 38** — all twenty published RMC-TotalRisk ±2-log-SD quantiles pinned in log10 space
   at 5.5e-4 (the table's rounding half-width plus solver headroom). Representative: at
   AEP 1e-4 the computed quantiles are **4.2482 / 4.7404** against the published
   **4.248 / 4.740** (≈ 2e-4 agreement). The report's HEC-FDA columns are context — its
   ≤ 0.9% differences are interpolation-design gaps between the two programs, not targets.

The uncertain mean curve is additionally checked for internal consistency (strictly ordered;
grid spanning the full-uncertainty envelope); its 10,000-curve assembly is the landed
`TabularHazard` Hazard-mode pattern verified against exact quadrature by the NFIP
dense-tabular family.

### V4 — Reliability engine (`Test_NonparametricHazard_ReliabilityEngine`)

The SF-8 curve behind a deterministic flow-fragility ramp in reliability mode:

| Output | Oracle | Engine | Assessment |
|---|---|---|---|
| Deterministic-mode AFP | 0.0016519777 (SE 2.23e-5) | 0.001637202 | ≈ 0.66·SE — very good |
| Uncertain ensemble grand AFP | 0.0020874808 (two-loop) | 0.002318069 | ≈ 1.8·combined SE — good |

The deterministic-mode oracle reconstructs the derived knot ladder's declared interpolation
semantics independently (ln-flow linear in z(AEP); `MersenneTwister(45678)`, N = 10⁶). The
uncertain oracle is two-loop: 10,000 outer knowledge percentiles over the independently
verified derived table, each inner AFP a 2,000-step dense trapezoid; the engine runs 300 LHS
realizations (σ_k = 0.0021 — knowledge uncertainty dominates, hence the wider combined SE).
Reproducibility pins ride the deterministic-mode scenario: repeated-run, rename + new-id, and
XML round-trip AFPs all **bit-identical** (the round-trip pin also proves the inputs-only
serialization recomputes the derived table exactly on load). The uncertain mean-curve assembly
is deliberately excluded from bit pins — the documented `ExpectedProbabilities` parallel-sum
ulp nondeterminism.

## Tolerance derivations

Documented per assert in the test XML docs: k = 4 on Monte Carlo standard errors; grand-mean
comparisons combine the oracle SE with the engine's outer-sampling error charged at the MC
rate (LHS is tighter — conservative); dense-quadrature comparisons at 1e-4 relative (trapezoid
kink error); Table 38 at 5.5e-4 absolute in log10 space (rounding half-width + solver
headroom); the derivation-equivalence pins at 1e-9/1e-6 relative (object-vs-inline moment
algebra; closed-form vs Brent-tolerance fixed point).
