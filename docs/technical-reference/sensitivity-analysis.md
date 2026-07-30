# Sensitivity Analysis

> Technical reference for the unified sensitivity engine on `RMC.TotalRisk.Analyses.RiskAnalysis`:
> `MeasureSensitivity`, `MeasureSensitivityMatrix`, and `HazardLevelSensitivity`, the
> `SensitivityMeasure` association catalog, and the `SensitivityResults`/`SensitivityEntry`
> containers. v1.1 replaces the v1.0 tornado analysis with this engine by approved scope decision.
> Executable evidence: [../verification/sensitivity.md](../verification/sensitivity.md).
> Methodology grounding: the RMC-TotalRisk Technical Reference Manual, Appendix G.

## The design in one sentence

Sensitivity is a **post-processing diagnostic over stored results**: the per-realization outputs the
run already persisted are correlated against the per-function knowledge percentile draws, re-derived
bit-exactly from the content seeds — no re-simulation, no integration, and no serialized state of
its own (`SensitivityResults` is runtime-only, never persisted).

## The three entry points

| Method | Output being explained | Where the outputs come from |
|---|---|---|
| `MeasureSensitivity(outputMeasure, riskType, measure, componentIndex, failureModeIndex, consequenceType)` | One stored scalar risk measure (APF, mean, conditional mean, σ, skewness, kurtosis, threshold probabilities, VaR, CVaR) on one risk-type stream | The per-realization summaries persisted in `RiskResults` |
| `MeasureSensitivityMatrix(riskType, measure, …)` | The full scalar-measure catalog in one pass | Same — one input-matrix derivation is reused across all measures |
| `HazardLevelSensitivity(componentIndex, hazardLevel, measure, riskType, realizations, consequenceType)` | The risk at one hazard level (the tornado diagnostic) | A dedicated content-seeded design (default 100 realizations) that evaluates the failure-mode combination decomposition at the level — one evaluation per realization |

Scope arguments select the output: `componentIndex = −1` explains the overall system (inputs are
every component's knowledge columns); a component position narrows the inputs to that component's
columns (other components' draws are independent of its results by construction);
`failureModeIndex` narrows further to one failure mode's summaries (Excess and Fail streams only —
any other stream throws `ArgumentException`). `consequenceType` selects the declared
consequence-type position (0 is the primary). The measure methods return null when no
full-uncertainty results are stored, the scope's outputs are unavailable, fewer than three valid
realization pairs remain after NaN filtering, or the scope has no knowledge inputs;
`HazardLevelSensitivity` additionally returns null for an invalid analysis (the v1.0 contract) or a
deterministic component.

## `SensitivityMeasure` — the association catalog

Runtime-only (never serialized, no hash surface); the v1.0 member names are preserved:

| Member | Definition | Reading |
|---|---|---|
| `PearsonCorrelation` | Pearson's linear correlation between input draws and output | linear association |
| `SpearmanCorrelation` | Pearson over fractional ranks | exact under any monotone re-expression of the input; preferred for curvilinear monotone relations |
| `SensitivityIndex` | squared Pearson correlation | the input's fractional contribution to output variance — because the inputs are independent per-function knowledge draws (near-orthogonal under Latin hypercube stratification), r² estimates the same main-effect variance share as the regression form of the Technical Reference Manual Appendix G (Eq. 249–250), and the indices sum to at most one across inputs |

## The input-column contract

Input columns are collected by the **exact sampler walk** the engine samples with, so labels,
ordering, and dimensionality match the run:

- Every non-deterministic function with `SamplingDimensions > 0` contributes one column per
  sampling dimension, labeled `"{Component} - {Function}"` (multi-dimensional functions gain a
  `[d]` suffix; duplicate labels dedupe with an ordinal). Deterministic functions are skipped —
  their pre-allocated percentile rows are never read, and an inert column would only add tornado
  noise bars.
- A **shared function instance contributes one column** (reference-identity dedup): one live
  function wired into several places is one knowledge quantity.
- Each failure mode contributes its **consequence-coupling columns** — the shared
  failure/non-failure coupling draw, one per declared consequence type — but only where an
  uncertain consequence actually consumes them.
- The walk order is: the hazard function, then per mode the coupling columns, the stage
  transforms, the stage responses, and the trailing transforms.

## The seed-rederivation contract

The correlation pairs stored outputs with re-derived inputs **through the content seeds**: the
component sampler setup is re-run at the stored ensemble size (`SetupSamplers`), reproducing the
run's percentile matrices bit-exactly because seeds derive from (analysis seed, component canonical
hash, occurrence index) — never a wall clock. Two consequences:

1. **The model must be unchanged since the run** — the same guarantee every diagnostic over stored
   results carries. An edited function has a different content hash, therefore different seeds,
   therefore columns that no longer pair with the stored outputs.
2. **Sampler state is mutated as a documented side effect.** `HazardLevelSensitivity` re-runs
   `SetupSamplers` at its own design size, so a caller reading sampled functions directly after a
   sensitivity call sees the sensitivity design; `RunAsync` and the measure methods re-seed
   defensively at their own entry.

`HazardLevelSensitivity` preserves the legacy hazard-bin weighting (the sampled hazard's
probability mass over ±(range/200) around the level, tail masses at the domain ends) and is native
to the component's selected profile hazard axis: when a profile element is set, each realization
inverts its own sampled profile chain back to the driving hazard, which requires every transform on
the chain to declare an ordered output axis (an unordered declaration faults the query loudly
rather than inverting ambiguously).

## Results containers

`SensitivityResults` (runtime-only) carries `OutputLabel` (e.g. `"Mean — Total — System"`),
`RiskType`, `Measure`, `Realizations` (the valid pair count), and `Entries` — one
`SensitivityEntry { Label, Value }` per input column in the sampler walk order.
`RankedByMagnitude()` returns the tornado view (descending absolute association). Nothing here is
serialized; the diagnostic recomputes on demand from stored results.

## Verification

[../verification/sensitivity.md](../verification/sensitivity.md): the analytic
corr(U, Φ⁻¹(U)) = √(3/π) Pearson pin at the Fisher-z bound, Spearman rank exactness with the
inert-input 4/√N null band, an independent hazard-level response oracle with affine invariance,
content-seeded bit-reproducibility, and the profile-axis-native pin (the tornado at profile level
T(h) is bit-identical to the raw-axis tornado at h on the unprofiled clone).
