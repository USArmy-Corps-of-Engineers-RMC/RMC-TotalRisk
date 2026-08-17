# Composite Response Function Verification

**Test class:** `CompositeResponseVerification` · **Tests:** 8 · **Run of record:** 2026-07-25, isolated run, ✅ all passed

> Family: `CompositeResponse` (`RMC.TotalRisk.RiskFunctions.Responses`)
> Anchor: *Verification of the RMC-TotalRisk Software* (2024), §Composite Hazard and Response Functions (Tables 44–46) + exact probability identities for the weakest-link rule
> All comparisons **very good** (≤ 1%)

## Overview

A composite response function combines a weighted list of child fragilities under one of two rules:

| Combination | Rule | Reading |
|---|---|---|
| **Mixture** (default) | `p(h) = Σ ωᵢ·pᵢ(h)` | Alternative descriptions of the response; exactly one applies |
| **CompetingRisks** | the minimum rule — the weakest link | All mechanisms act; any one can fail the system. Weights are inert. |

The weakest-link rule is the one substantive divergence from `CompositeHazard`, which takes the
maximum because the most severe *loading* controls. For a response, any mechanism failing is enough,
so the combination is the **union** of the child failure events.

The mixture is **aleatory**, as for the hazard composite — see
[composite-functions](../technical-reference/composite-functions.md).

## Verification scenario

The report verifies both composites with one scenario, noting that *"the composite response function
produces the same results, but it is plotted as a CDF with the hazard levels versus the
non-exceedance probabilities, whereas the composite hazard function plots the exceedance
probabilities versus the hazard levels."* This family therefore reuses the Table 44 children —
N(10, 2), N(20, 1), N(30, 5) at ERL 100, weights 0.3/0.2/0.5, legacy seeds 12345/67891/45678 — and
checks the mixture on the probability axis a fragility is actually read on.

## Results

### Mixture rule

The combined conditional failure probability is checked as an **exact identity** against the
weighted sum of the child fragilities across the full stage range, at 1e-12.

### Cross-check against report Table 45

Read as a fragility, the combined conditional failure probability at each published Table 45
magnitude must equal the corresponding non-exceedance probability `1 − AEP`. Asserted at 1% of the
target — the report's own "very good" band — because the published magnitudes carry the small
mid-distribution error the [composite hazard page](composite-hazard.md) documents, which shows up
here as a probability offset rather than a magnitude one.

### Weakest link (no published table)

Anchored on exact probability identities at 1e-10:

| Dependence | Closed form |
|---|---|
| Independent | `p(h) = 1 − ∏(1 − pᵢ(h))` — the union of the child failure events |
| PerfectlyPositive | `p(h) = max pᵢ(h)` |

Bracketing is asserted structurally at every grid point: the weakest link never fails *less* readily
than its most fragile child, and the mixture always lies between the extreme child fragilities. The
two rules are additionally asserted to differ materially somewhere on the range — they are genuinely
different models, not two spellings of one.

Weight inertness is pinned directly: under CompetingRisks a weight edit leaves both the sampled
curve and the canonical hash bit-identical.

### Uncertainty bands

The response composite summarizes conditional failure probability across a **hazard grid**, whereas
the hazard composite inverts across a **probability grid**. This family pins that second axis
against an index-parity oracle built from Numerics `BootstrapAnalysis` and `Mixture`, mirroring the
engine's empirical-CDF construction so both sides evaluate through the same path. Assert delta 1e-9.

The parent fragility is separately asserted to lie inside the bands at every summary hazard.

## Reproducibility pins

A serialization round-trip plus metadata edits leave the canonical hash unmoved and every sampled
draw bit-identical; a weight edit moves the hash. The round-trip stands in for a stored-and-reloaded
project, and is used instead of a second independently estimated composite because of the upstream
bootstrap nondeterminism the [composite hazard page](composite-hazard.md) documents and pins.

## Test map

| Test method | Checks |
|---|---|
| `Test_Mixture_EqualsWeightedSumOfChildFragilities` | The mixture identity, exact at 1e-12 |
| `Test_MixtureFragility_AtReportMagnitudes_MatchesNonExceedance` | Report Table 45 on the probability axis |
| `Test_CompetingRisks_WeakestLink_ClosedForms` | Union and maximum identities across dependences |
| `Test_CombinationRules_Bracketing` | Weakest-link and mixture bracketing; the rules differ materially |
| `Test_CompetingRisks_WeightEdits_AreInert` | Weight inertness in curve and hash |
| `Test_UncertaintyBands_VsIndexParityOracle` | Hazard-grid bands against the in-test Numerics oracle, 1e-9 |
| `Test_ParentFragility_LiesInsideBands` | Structural band containment |
| `Test_Reproducibility_RoundTripAndMetadataPins` | Round-trip and metadata bit-identity; compute edit moves |

## Notes

`SampleResponseFunction()` and its two overloads throw, matching v1.0 and the two sibling response
types. The engine consumes the distribution form exclusively, and a union-knot re-tabulation would
agree with the true combined curve only *at* the knots under the weakest-link rule, where the
combination is nonlinear in the children — that would create a second, subtly wrong response
surface. The plotting need is served by `ComputeUncertaintyResults()`.

## Engine-level coverage

The engine-level oracle — legacy `Test_Composite_Response` (two `Normal` fragilities behind a
mixture, w = 0.45, `MersenneTwister(12345)`) — is converted in the
[composite-engine family](composite-engine.md).
