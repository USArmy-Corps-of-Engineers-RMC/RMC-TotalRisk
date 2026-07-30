# The Results Catalog

> Technical reference for `RMC.TotalRisk.Results`: the realization and summary trees, the risk
> profiles, ensemble summaries and convergence diagnostics, the multi-consequence axis,
> contribution, reliability-mode outputs, and the results-JSON conventions. Normative spec:
> [../requirements/MODEL_LIBRARY_ARCHITECTURE.md](../requirements/MODEL_LIBRARY_ARCHITECTURE.md)
> §7.5. Companions: [loss-exceedance-curves.md](loss-exceedance-curves.md) (how each curve is
> built) and [risk-contribution.md](risk-contribution.md) (the attribution mathematics).

## Two trees: full realizations and compact summaries

The engine publishes results on two parallel shapes:

- **The realization tree** — `SystemRealization` → `ComponentRealization` →
  `FailureModeRealization`. Each scope carries a `Curves` block (the five `Curve` streams:
  `Excess`, `Background`, `Total`, `Fail`, `NonFail`), `AdditionalCurves` (one `Curves` block per
  additional declared consequence type), and — at the failure-mode scope — the opt-in
  `AdjustedCurves`/`AdditionalAdjustedCurves` (the adjusted marginal streams, default off) and the
  per-type contributions. During a run the realizations live in the runtime-only `Ensemble`
  store, written strictly index-owned by the parallel loop (the discipline behind bit-identical
  results at any thread count), then post-processed and discarded.
- **The summary tree** — `SystemRiskResults` → `SummaryRiskResults` (the five stream summaries) +
  `ComponentResults` → `FailureModeResults`, plus `ConsequenceResults` under
  `AdditionalConsequences` for each additional type, echoing the declared `ConsequenceLabels` /
  `ConsequenceUnits`, and the integrator diagnostics (`FunctionEvaluations`, `StandardError`,
  `ChiSquared`). One `SystemRiskResults` summarizes one finished realization.

The persisted roots on `RiskAnalysis` after a run:

| Property | Content |
|---|---|
| `RiskResults` (`EnsembleResults`) | One `SystemRiskResults` per realization (a mean-only run publishes a single-entry ensemble), plus the run `Manifest` and the ensemble `Summary` |
| `MeanRiskResults` | The mean realization (mean-only run) or the ensemble mean curves (full run) — a `SystemRealization` curve tree |
| `MedianRiskResults`, `LowerRiskResults`, `UpperRiskResults` | The ensemble median and confidence-bound curve trees at the configured interval width (full runs only) |

Percentile band trees are assembled per scope, stream, and consequence type on per-type
consequence grids (types live on different magnitude scales); the Total percentile curve is read
from the Total LEC directly.

## The `Curve` measure and profile surface

Every `Curve` carries the risk-measure catalog of
[loss-exceedance-curves.md](loss-exceedance-curves.md) (`TotalProbability`, `Mean`,
`ConditionalMean`, `StandardDeviation`, `Skewness`, `Kurtosis`, `ConsequenceThresholdProbability`,
`HazardThresholdProbability`, `ValueAtRisk`, `ConditionalValueAtRisk`) plus the profile catalog:

| Profile | Content |
|---|---|
| `HazardFrequency` | hazard level vs cumulative exceedance probability |
| `CumulativeFailureProbabilities` | the ascending cumulate of recorded probability mass — the distribution of the failure-causing hazard; its terminal ordinate is the annualized failure probability |
| `CumulativeExpectedConsequences` | the ascending cumulate of expected consequence; terminal ≡ the stream mean |
| `SystemResponseProbabilities` | the response profile, plotted against annual exceedance probability (the normalized, transform-independent axis — a hazard-axis response profile is ill-posed when failure modes respond to different transformed signals) |

`[JsonIgnore]` normalized views expose the cumulative profiles as `OrderedPairedData`. When a
component selects a profile hazard element, the profile hazard coordinates are remapped through
the selected transform chain (a seed-inert selection; the response profile's exceedance axis is
deliberately never remapped). Optional measures are gated by the `[Flags]` `RiskMeasureOptions`
(`HigherMoments`, `ValueAtRisk`, `ThresholdProbabilities`, `RiskProfiles`, `Contributions`;
default `All`) — a disabled group is simply not computed, never partially populated.

## Ensemble summaries and convergence diagnostics

`EnsembleResults.Summary` (`EnsembleSummary`) reduces the per-realization scalars into four
`SystemRiskResults` trees — Lower, Upper, Median, Mean — whose every scalar (the measure catalog
at system, component, failure-mode, and consequence-type scope, the three contribution values,
and the integrator diagnostics) is the ensemble percentile or sequential mean of that measure.
`ComputeSummary(confidenceIntervalWidth)` recomputes at another width from the stored ensemble.
The aggregated `ConvergenceDiagnostics` carry the integrator effort/error summaries
(function-evaluation and standard-error statistics) and the realization-adequacy indicators
(SD/√N and confidence half-widths for the headline scalars). Reductions are sequential and
deterministic — repeated runs and JSON round-trips reproduce every slot bit-for-bit.

## The multi-consequence axis

An analysis declares its consequence types once (`ConsequenceTypeDescriptor` — label, unit, and
an optional per-type `ConsequenceThreshold`), and **every declared type is computed through the
one engine pass**: probability structure is shared, refinement is driven by the primary type, and
type k records into the primary containers (k = 0) or position k − 1 of the `Additional*`
collections at every scope. A secondary type evaluates assurance only when its own declared
per-type threshold supplies one (the analysis-level threshold is declared in the primary type's
units). All multi-type members are results-JSON append-only: an earlier payload loads with null
blocks meaning "not computed".

## Contribution

`RiskContribution` stores the three raw attributed values per (mode, type) —
`FailureProbability`, `FailureMean`, `ExcessMean` — with shares derived on read
(`ShareOf(total)`), summing exactly to the parent's recorded totals under every combination
method ([risk-contribution.md](risk-contribution.md)). Contributions ride
`FailureModeRealization.Contribution`/`AdditionalContributions` and
`ComponentRealization.SystemContribution`/`AdditionalSystemContributions`, with summary capture
in the summary tree — available in mean-only runs, per realization, and as ensemble confidence
intervals, all without re-simulation.

## Reliability mode

`RiskAnalysisMode.Reliability` is a mode of the same analysis, not a second type: the consequence
surface is degenerate at zero, the headline output is the annualized failure probability, the
cumulative failure profile is the headline profile, exhaustive Total streams still obey the
exact-mass rules, and contribution reports the consequence-free %-of-APF basis fully.

## Results-JSON conventions

Results serialize with System.Text.Json only — `ToJson()`/`FromJson()` plus GZip-compressed byte
overloads on the roots (`ResultsJson` holds the one shared configuration). The v1.0
BinaryFormatter BLOBs are deliberately not readable; v1.0 projects re-run their analyses.
Conventions:

- Doubles use the default shortest-round-trip formatting, which is bit-faithful;
  `JsonNumberHandling.AllowNamedFloatingPointLiterals` lets NaN and ±Infinity — legitimate values
  for unpopulated measures — round-trip as quoted literals.
- Serialized members are **append-only**: newer readers load older payloads with the missing
  blocks null ("not computed"), and nothing is ever renamed.
- Compressed payloads are compared on the **decompressed** bytes — the GZip header embeds
  non-content fields.
- `EnsembleResults.Manifest` (`AnalysisRunManifest`) records deterministic run provenance;
  `IsProvenanceVerified` is true only for a current-schema manifest. Out-of-range indexer reads
  on the ensemble preserve the v1.0 null-result convention, while writes fail closed.

## Verification

The results surface is pinned across the verification set: exact-construction and measure tests
in the fast suite; [engine-reproducibility](../verification/engine-reproducibility.md)
(bit-identity of full results JSON); [multi-consequence](../verification/multi-consequence.md)
(the declared-type axis); [risk-profiles](../verification/risk-profiles.md) (the profile
catalog); [scalar-uncertainty](../verification/scalar-uncertainty.md) (`EnsembleSummary` against
closed-form quantiles); [contribution](../verification/contribution.md) (the Σ identities).
