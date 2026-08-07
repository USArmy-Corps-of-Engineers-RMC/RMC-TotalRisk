# Engine Reproducibility — Verification Results

**Test class:** `EngineReproducibilityVerification` · **Tests:** 3 · **Run of record:** 2026-07-22, isolated run, ✅ all passed

The analysis-level regression for the v1 seed-dependency bug: v1.0 handed each component a seed
from a master PRNG iterated in canvas order, so dragging a node changed Monte Carlo results.
v1.1 derives every seed from content — `(analysis PRNG seed, component canonical hash,
occurrence index)` per component and `(component seed, function canonical hash, structural
ordinal)` per function — so presentation can never be identity.

## Scenario

A single uncertain component at 200 realizations, full-uncertainty mode: a deterministic
tabulated stage-frequency hazard (Normal(100, 20) quantiles on a dense z-grid, linear
probability interpolation), a triangular-uncertain tabular breach fragility, and a
normal-uncertain tabular life-loss consequence.

## Pins

| Pin | Comparison | Result |
|---|---|---|
| Repeated runs | Three runs of equal-content analyses → full `EnsembleResults` and mean-realization JSON byte-identical. Each run's `Parallel.For` partitions work differently across threads, so repeated equality exercises the any-thread-count claim (writes are index-owned; reductions sequential). | ✅ byte-identical |
| Metadata edits | Rename the analysis, component, and every function; edit descriptions; assign fresh ids; round-trip the component through serialization → the summary-ensemble JSON (a pure numeric surface) is byte-identical and the mean/upper percentile curve arrays are element-identical. | ✅ byte-identical |
| Compute edit | Nudge one fragility ordinate (140 → 141) → the ensemble JSON differs. The counter-pin proving the equality asserts are not vacuous. | ✅ results move |

## Bivariate pins (run of record 2026-08-07, isolated, 7/7)

The bivariate fixture is an independence-copula hazard over uncertain Surge and Pool marginals
(8 conditional bins) with the failure path bound either to the raw secondary (pool fragility and
pool damages off hazard port 1) or entirely to the primary axis, at 100 ensemble realizations.

| Pin | Comparison | Result |
|---|---|---|
| Thread counts | The same content at `MaximumDegreeOfParallelismOverride` ∈ {1, 4, unbounded} → ensemble and mean JSON byte-identical (the conditional-bin fold has index-owned writes, sequential reductions, and no draws in the bin loop). | ✅ byte-identical |
| Metadata, canvas, and modes | Rename every function INCLUDING both marginal links with fresh ids, move every graph element on the canvas, round-trip through `SelfContained`, and round-trip through `ByReference` with a live resolver → the numeric surface is byte-identical (display labels stripped exactly as in the univariate pin). | ✅ byte-identical |
| Unrelated growth | Append an unrelated univariate component to the analysis → the bivariate component's per-realization summary numbers (total mean, failure mean, failure probability at every realization) and mean-pass summaries are bit-identical. Component-scope output CURVES resample onto analysis-level consequence grids whose extents legitimately widen with a second component — curve ordinates are presentation, not the seed-stability claim. | ✅ bit-identical |
| Pinned-seed secondary perturbation, both ways | On an all-primary-bound component, perturbing the pool marginal's content under `PinnedSamplerSeeds` is byte-identical (the integrand never reads the secondary axis; the conditional weights depend only on the bin count; the results manifest — which embeds the component content hashes and legitimately moves — is stripped before comparison). On the secondary-bound component the same perturbation is a pure parameter effect: the pinned results move from the baseline and the pinned perturbed run reproduces itself byte-identically. | ✅ both directions |

## Notes

Results JSON carries display labels (component and mode names), which legitimately change on
rename — the metadata pin therefore compares the label-free summary ensemble whole and the
curve arrays directly. The full shuffle/duplicate-component matrix is carried by the
multi-component families ([system-risk](system-risk.md),
[system-risk-matrix](system-risk-matrix.md), including the identical-content occurrence-index
scenarios).
