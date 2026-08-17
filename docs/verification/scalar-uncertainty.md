# Scalar-Measure Confidence Interval Verification

**Test class:** `ScalarUncertaintyVerification` · **Tests:** 2 · **Run of record:** 2026-07-24, isolated run, ✅ all passed

The engine has always banded the percentile curves; the scalar risk-measure catalog
carries its intervals through `EnsembleSummary`: four `SystemRiskResults` trees —
Lower, Upper, Median, Mean — whose every scalar (the ten-measure catalog at system, component,
failure-mode, and consequence-type scope, the three contribution values, and the integrator
diagnostics) is the ensemble percentile or sequential mean of that measure, plus the
aggregated `ConvergenceDiagnostics` (integrator effort/error summaries and the
realization-adequacy indicators of the uncertainty technical note [26] §8.2: SD/√N and confidence
half-widths for the headline scalars). Each measure reduces independently —
percentile-consistent per measure, deliberately **not** one coherent realization (the 95th
percentile of the value-at-risk is not the value-at-risk of the 95th-percentile curve; that
distinction is the reason the scalars need their own reduction). Stored append-only as
`EnsembleResults.Summary`; recomputable from any loaded ensemble via `ComputeSummary`.

## Scenario

A deterministic stage-frequency curve (0.999 → 0 ft, 0.5 → 10 ft, 0.001 → 30 ft) and
deterministic fragility (10 → 0, 20 → 1) with a single background-free failure mode whose
consequence curve is linear from zero with one uncertain top ordinate, Normal(1,000, 100) at
stage 30, run at N = 2,000 LHS realizations. The sampled consequence curve scales linearly
with the single knowledge draw and the risk integral is linear in the consequence surface, so
**every realization's Total mean is exactly M₀ · s_i / 1000** with s_i = 1000 + 100·Φ⁻¹(p_i);
the deterministic companion run supplies M₀ exactly. The ensemble quantiles of the Total mean
therefore have closed-form targets: q ↦ M₀·(1000 + 100·Φ⁻¹(q))/1000.

## Checks

| # | Test | Verifies | Tolerance | Result |
|---|---|---|---|---|
| 1 | `Test_ScalarIntervals_VsAnalyticQuantiles` | Lower/Median/Upper Total-mean slots at the 90% width vs the closed-form quantile map; the mean slot vs M₀; a knowledge-free APF reduces to a (mass-noise) degenerate interval; the convergence indicator mirrors the interval evidence | self-derived stratified bound — the map's variation over ±2/N (LHS puts exactly one draw per 1/N bin, plus one bin for percentile interpolation); mean at 4·σ/√N with σ = 0.1·M₀ (conservative plain-MC bound; the LHS variance is far smaller); APF at 1e-9 relative (adaptive refinement follows the consequence draw, so the recorded mass differs at ~1e-12 relative across realizations) | ✅ |
| 2 | `Test_ScalarIntervals_RoundTrip_And_Reproducibility` | The stored summary reproduces **bit-for-bit** via `ComputeSummary` on a JSON round-trip, and repeated runs (fresh thread schedules) reproduce every slot bit-for-bit — the sequential-reduction determinism pin | 0 (bit) | ✅ |

Unit-level companions (fast suite): the reducer against direct `Statistics.Percentile` calls
on hand-built ensembles (exact), NaN filtering and the all-NaN → NaN rule, null-realization
exclusion, contribution-slot reduction, the convergence aggregates against hand sums, the
argument contracts, append-only serialization with forward-load, and the engine-level
full-run/mean-only presence semantics with per-scope interval ordering.

Run of record 2026-07-24: `ScalarUncertaintyVerification` 2/2 passed (~31 s wall — two
N = 2,000 ensembles plus the deterministic companion); the fast suite at that date passed in
full (462/462); gate `SingleComponentUncertaintyVerification` 4/4 passed unchanged (3 m 02 s).
