# Engine Reproducibility — Verification Results

**Test class:** `EngineReproducibilityVerification` · **Status:** ✅ Verified (2026-07-22, Phase 4)

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

## Notes

Results JSON carries display labels (component and mode names), which legitimately change on
rename — the metadata pin therefore compares the label-free summary ensemble whole and the
curve arrays directly. The full shuffle/duplicate-component matrix extends in Phase 4b when
multi-component analyses exist.
