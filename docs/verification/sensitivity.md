# Sensitivity Verification

**Test class:** `SensitivityVerification` · **Tests:** 3 · **Run of record:** 2026-07-24, isolated run, ✅ all passed

v1.1 replaces the legacy tornado analysis with a **unified sensitivity engine**
(an approved scope decision): one typed API whose **outputs** are either any stored scalar
risk measure (`MeasureSensitivity`/`MeasureSensitivityMatrix` — APF, mean, conditional mean,
σ, skewness, kurtosis, threshold probabilities, VaR, CVaR, per risk type and per consequence
type at system/component/failure-mode scope) or the risk at a hazard level
(`HazardLevelSensitivity` — the tornado, native to the profile axis),
and whose **inputs** are the per-function knowledge draws plus the consequence-coupling
columns, labeled by input function and collected by the exact sampler walk with its
shared-instance dedup.

**Efficiency (the user's requirement):** measure-level sensitivity performs **no simulation at
all** — the outputs are already stored per realization in the ensemble, and the inputs
re-derive bit-exactly from the content seeds (the same `(analysis seed, component hash,
occurrence index)` walk a run uses), so a full input × measure × type × scope sweep costs
matrix regeneration plus correlations (milliseconds). Hazard-level sensitivity runs one
combination-kernel evaluation per realization — no integration — on a dedicated content-seeded
design (default 100 realizations, the v1.0 size; no wall-clock PRNG anywhere).
`SensitivityMeasure` keeps the legacy members: Pearson, Spearman, and the sensitivity index
= Pearson² — with independent (near-orthogonal LHS) inputs, r² estimates the TR Appendix G
regression main-effect share (Eq. 249–250), the documented equivalence, and the indices are
true variance fractions summing to ≤ 1 (the legacy intermediate-quantity bars were collinear;
the derived "Probability of Non-Failure" bar is deliberately not ported).

## Scenarios

**L — linear map (N = 5,000):** deterministic hazard and fragility; one uncertain consequence
top ordinate Normal(1,000, 100). Every realization's Total mean is an exact linear map of the
single coupling draw's normal quantile, so Pearson against the uniform draw has the closed
form ρ = corr(U, Φ⁻¹(U)) = √(3/π) ≈ 0.977205 and Spearman is exactly one.
**T — two inputs (N = 1,000):** an uncertain triangular-ordinate fragility (drives the failure
probability) plus the uncertain consequence (exactly inert on it — the probability structure
never reads consequence draws). **O — hazard-level oracle (N = 400):** the uncertain fragility
with a deterministic consequence at level 15 ft — midway between the fragility knots, so the
sampled response is the average of the two triangular quantiles at the (public) sampled
percentile, computable independently.

## Checks

| # | Test | Verifies | Tolerance | Result |
|---|---|---|---|---|
| 1 | `Test_MeasureSensitivity_LinearMap_AnalyticPearson` | Spearman exactly one on the monotone map; Pearson ≡ √(3/π) | 1e-12 (rank); Fisher-z bound at k = 4 — tanh(atanh ρ + 4/√(N−3)) − ρ (conservative under LHS stratification) | ✅ |
| 2 | `Test_MeasureSensitivity_InertInput_NullBand` | The fragility draw rank-perfect for the APF; the consequence draw inside the 4/√N null band; the sensitivity indices separating the two (> 0.9 vs < 0.02) | 1e-12; 4/√N | ✅ |
| 3 | `Test_HazardLevelSensitivity_AnalyticOracle_AndReproducibility` | Repeated content-seeded queries bit-identical; the engine association ≡ the independent triangular-quantile response oracle (affine invariance of Pearson — deterministic hazard weight and consequence scale cannot move it) | 0 (bit); 1e-9 | ✅ |

Unit-level companions (fast suite, 10 tests): enum member pins; the input-column walk (label
catalog, coupling columns consumed only where an uncertain consequence reads them,
deterministic functions contribute no column, one shared instance = one column, duplicate
labels suffixed); the scope model (system = union of every component's columns, component =
own columns only, failure-mode scope narrows the output and gates to Excess/Fail) with the
**component-scope invariance pin** — a component's sensitivity is bit-identical whether or not
other components exist in the analysis (content-seeded independence); rank-exactness and the
r² = Pearson² identity; matrix-vs-single-call bit equality; the null contracts (unrun,
mean-only, deterministic component, invalid analysis); the profile-axis-native pin — the
tornado at profile level T(h) is **bit-identical** to the raw-axis tornado at h on the
unprofiled clone (per-realization chain inversion; the seed-inert selector leaves draws
untouched); and the ranked tornado view.

Run of record 2026-07-24: `SensitivityVerification` 3/3 passed (~37 s wall); the fast suite at
that date passed in full (471/471, Debug and Release).
