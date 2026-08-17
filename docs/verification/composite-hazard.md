# Composite Hazard Function Verification

**Test class:** `CompositeHazardVerification` · **Tests:** 11 · **Run of record:** 2026-07-25, isolated run, ✅ all passed

> Family: `CompositeHazard` (`RMC.TotalRisk.RiskFunctions.Hazards`)
> Anchor: *Verification of the RMC-TotalRisk Software* (2024), §Composite Hazard and Response Functions (Equation 49, Tables 44–46)
> All comparisons **very good** (≤ 1%; worst case 0.42% against a published value that is itself the less accurate of the two)

## Overview

A composite hazard function combines a weighted list of child hazard functions under one of two
rules:

| Combination | Rule | Reading |
|---|---|---|
| **Mixture** (default) | `F(x) = Σ ωᵢ·Fᵢ(x)`, ω ∈ [0,1], Σω = 1 | Alternative descriptions of the loading; exactly one applies to any event. Report Equation 49. |
| **CompetingRisks** | the maximum rule under the configured dependence | All loading mechanisms occur; the most severe controls. Weights are inert. |

The mixture is **aleatory**: the weights are the fraction of the event population each child
describes, so the combination is a single distribution carried through every realization rather than
a per-realization branch pick. Knowledge uncertainty enters through the children's own posteriors —
exactly how report Table 46 models it. See
[composite-functions](../technical-reference/composite-functions.md) for the doctrine.

## Verification scenario (report Table 44)

Three parametric children, each bootstrapped at effective record length 100 over 10,000
realizations at the legacy per-child seeds (12345 / 67891 / 45678):

| Distribution | Mean, μ | Std. deviation, σ | ERL | Weight |
|---|---|---|---|---|
| 1 | 10 | 2 | 100 | 0.3 |
| 2 | 20 | 1 | 100 | 0.2 |
| 3 | 30 | 5 | 100 | 0.5 |

The report's R reference code:

```r
library(mistr)
n1 = normdist(mean = 10, sd = 2); n2 = normdist(mean = 20, sd = 1); n3 = normdist(mean = 30, sd = 5)
mix = mixdist(n1, n2, n3, weights = c(0.3, 0.2, 0.5))
```

**Axis.** Tables 45 and 46 give hazard magnitudes at fixed annual exceedance probability, so a
tabulated AEP maps to the non-exceedance probability `1 − AEP` that
`UncertaintySummaryProbabilities()` reports and `ComputeUncertaintyResults()` inverts at. The 25
tabulated AEPs are exactly the parametric hazard's default probability ordinates, so the engine's
summary grid and the report's table rows correspond one to one.

## Results

### Mixture curve (report Table 45)

The combined CDF is checked as an **exact identity** against the closed-form weighted sum at 1e-12,
so the curve itself is not an approximation. Against the published R `mistr` quantiles the worst
disagreement is 0.42% (AEP 0.5) — "very good" by the report's own convention.

| AEP | R `mistr` | RMC-TotalRisk v1.1 | % difference |
|---|---|---|---|
| 1.0E-06 | 53.10 | 53.06 | 0.08% |
| 1.0E-04 | 47.70 | 47.70 | 0.00% |
| 1.0E-02 | 40.27 | 40.27 | 0.00% |
| 3.0E-01 | 28.72 | 28.73 | 0.05% |
| 5.0E-01 | 21.38 | 21.29 | 0.42% |
| 9.9E-01 | 6.34 | 6.34 | 0.00% |

### Finding: v1.1 is the more accurate of the two on the mid-distribution rows

`Test_MixtureQuantiles_InvertTheExactMixtureCdf` measures both sides against the analytic mixture
CDF. A correct quantile reproduces the requested non-exceedance probability:

| AEP | Published value | inverts to | v1.1 value | inverts to | Target |
|---|---|---|---|---|---|
| 5.0E-01 | 21.38 | 0.5044 | 21.290 | 0.5007 | 0.5000 |
| 3.0E-01 | 28.72 | 0.69949 | 28.733 | 0.70000 | 0.70000 |

v1.1 is about six times closer on the worst row. The 2024 report records 0.0% difference there
because v1.0 agreed with `mistr`, so this is a v1.1 accuracy improvement rather than a regression.
It is why the curve comparison uses the report's 1% "very good" band rather than a print-rounding
tolerance: a tighter absolute bound would encode the published error as the target.

Quantile inversion error is bounded separately at 5 × 10⁻³ of the exceedance probability. That is
the interpolation error of the roughly 200-bin empirical CDF the composite builds — the deliberate
v1.0-parity tradeoff that turns `InverseCDF` into an interpolation rather than a Brent solve at
every quadrature node.

### Bootstrap confidence bands (report Table 46)

The primary oracle is an **index-parity** construction rebuilt in-test straight from Numerics
`BootstrapAnalysis` and `Mixture`: bootstrap each child at its legacy seed, form the mixture per
realization with the fixed weights, invert at each tabulated probability, and take the 5th and 95th
percentiles across realizations. Because parametric children are posterior-indexed, the composite
consumes those same streams in the same order, so the comparison is exact rather than statistical
once the oracle mirrors the engine's empirical-CDF inversion path. Assert delta 1e-9.

The published constants corroborate at `max(0.05, 0.012·value)` — 1.2%, covering the report's own
largest recorded R-versus-TotalRisk disagreement of 0.7% plus two-decimal print rounding. **The
analytic and index-parity oracles are primary; the published constants are corroboration, and
neither is ever tightened toward the other.**

| AEP | 5% R | 5% v1.1 | 95% R | 95% v1.1 |
|---|---|---|---|---|
| 1.0E-06 | 50.27 | 50.46 | 55.80 | 55.87 |
| 1.0E-04 | 45.49 | 45.62 | 49.91 | 49.93 |
| 1.0E-02 | 38.81 | 38.89 | 41.71 | 41.68 |
| 5.0E-01 | 20.98 | 20.98 | 21.73 | 21.64 |
| 9.9E-01 | 5.80 | 5.77 | 6.87 | 6.87 |

The parent mixture curve is separately asserted to lie inside the bands at every tabulated
probability — a structural check independent of any published constant.

### Competing risks (no published table)

The report tabulates only the mixture, so the competing-risks mode is anchored on exact probability
identities at 1e-10:

| Dependence | Closed form |
|---|---|
| Independent | `F(x) = ∏ Fᵢ(x)` — every mechanism stayed below the level |
| PerfectlyPositive | `F(x) = min Fᵢ(x)` |

Bracketing is asserted structurally at every grid point: the maximum-rule curve never exceeds the
smallest child CDF, and the mixture always lies between the extreme child CDFs.

Weight inertness is pinned directly — under CompetingRisks a weight edit leaves both the sampled
CDF and the canonical hash bit-identical, so it cannot re-roll a Monte Carlo seed.

## Reproducibility pins

A serialization round-trip plus metadata edits (rename, new id, child rename) leave the canonical
hash unmoved and every sampled draw bit-identical; a weight edit moves the hash and therefore every
child's derived seed.

> **Upstream finding (recorded with this family, since resolved).** When the family landed, two
> separate `Estimate()` calls on a parametric child with identical inputs and an identical
> `PRNGSeed` did **not** produce a bit-identical posterior — the Numerics `BootstrapAnalysis`
> summary assembly used a parallel, order-nondeterministic reduction. The posterior is serialized
> content, so a composite over freshly estimated parametric children inherited that instability
> in its canonical hash. The upstream reduction now sums over a fixed chunk count, and
> `Test_UpstreamEstimation_IsBitReproducible` pins the repaired behavior. The reproducibility pin
> keeps its round-trip form deliberately: round-tripping carries the posterior verbatim, which is
> exactly what a stored project does, so it isolates the composite's own contract from the
> estimation path.

## Test map

| Test method | Checks |
|---|---|
| `Test_MixtureCurve_VsRMistrConstants` | Published Table 45 quantiles at the report's 1% band |
| `Test_MixtureCdf_EqualsWeightedSumOfChildCdfs` | The mixture identity, exact at 1e-12 |
| `Test_MixtureQuantiles_InvertTheExactMixtureCdf` | Inversion error bound + the accuracy finding above |
| `Test_BootstrapBands_VsIndexParityOracle` | Table 46 bands against the in-test Numerics oracle, 1e-9 |
| `Test_BootstrapBands_VsReportConstants_Corroboration` | Published band constants at 1.2% (corroboration only) |
| `Test_ParentCurve_LiesInsideBands` | Structural band containment |
| `Test_CompetingRisks_MaximumRule_ClosedForms` | Product and minimum identities across dependences |
| `Test_CombinationRules_Bracketing` | Maximum-rule and mixture bracketing |
| `Test_CompetingRisks_WeightEdits_AreInert` | Weight inertness in curve and hash |
| `Test_Reproducibility_RoundTripAndMetadataPins` | Round-trip and metadata bit-identity; compute edit moves |
| `Test_UpstreamEstimation_IsBitReproducible` | The repaired upstream estimation reproducibility, pinned |

## Engine-level coverage

The engine-level composite oracles — legacy `Test_Composite_Hazard` (two-branch `LnNormal` mixture
behind a fragility, w = 0.45, `MersenneTwister(12345)`) and the `Test_Composite`
mixture-consistency identity — are converted in the [composite-engine family](composite-engine.md).
This family verifies the function itself; that family verifies it behind the risk engine.
