# Composite Transform Function Verification

**Test class:** `CompositeTransformVerification` · **Tests:** 6 · **Run of record:** 2026-07-25, isolated run, ✅ all passed

> Family: `CompositeTransform` (`RMC.TotalRisk.RiskFunctions.Transforms`)
> Anchor: Closed-form algebra + exact Normal theory + an independently coded child-seed oracle (greenfield family — no legacy implementation and no report table)
> Exact comparisons at 1e-12; sampled comparisons within 4 standard errors

## Overview

A composite transform blends candidate transforms — several rating curves with credibility weights —
into a single consensus curve, `Σ ωᵢfᵢ(x)`. It supports **`Average` only**; `Mixture` and `Additive`
are validation errors, for the reasons set out in
[composite-functions](../technical-reference/composite-functions.md) §4. The combine rides the
Numerics `CompositeFunction` in `WeightedAverage` mode.

This is a **greenfield** family: v1.0 had no composite transform, so there is no legacy behavior to
preserve and no oracle to port. Every expected value here is a closed form or an independently coded
construction, never model-library compute.

## Oracles

| Check | Oracle | Tolerance |
|---|---|---|
| Weighted average of linear children | `α = Σωᵢαᵢ`, `β = Σωᵢβᵢ` — exact | 1e-12 |
| Inverse of the combined curve | round-trip through the forward evaluation | 1e-6 (the Brent solve's own tolerance) |
| Transformed-hazard bounds | the combine evaluated at its own domain bounds | 1e-12 |
| Ensemble at fixed input, independent Gaussian residuals | `Normal(Σωᵢ(αᵢ + βᵢx), √(Σωᵢ²σᵢ²))` — exact | k·SE, k = 4 |
| Mixed linear + power children | weighted sum of the children's own realizations, seeded by the documented recipe reproduced independently | 1e-12 |
| Deterministic uncertainty summary | the closed-form combined curve | 1e-12 |
| Uncertain uncertainty summary | exact Normal theory on the internal 10,000-realization median-LHS design | k·SE, k = 4 at N = 10⁴ |

### Tolerance derivations

Per `docs/verification.md`, k·SE with k = 4 at N = 1,000,000 for the ensemble asserts:

- **Mean** — ±4σ/√N.
- **Standard deviation** — ±4σ/√(2N), from the Normal fourth moment μ₄ = 3σ⁴.
- **Percentiles** — ±4·√(p(1−p)/N)/f(x_p), with *f* the Normal density at the quantile.

The uncertainty-summary asserts use the same forms at N = 10,000, the internal summary design.
Latin hypercube stratification makes every estimator tighter than independent-sampling standard
error assumes, so all of these are conservative.

## The discriminating check

`Test_UncertainLinearChildren_VsExactNormalTheory` is the test that would catch a wrong seeding
recursion. Children are seeded independently by the composite's own content-derived recursion, so
the ensemble variance adds in **ω²**:

| Sampling | Ensemble standard deviation at ω = 0.4/0.6, σ = 1.5/2.5 |
|---|---|
| Independent children (correct) | `√(0.4²·1.5² + 0.6²·2.5²)` = **1.616** |
| Co-monotonic children (wrong) | `0.4·1.5 + 0.6·2.5` = **2.100** |

Those are 30% apart against a tolerance of 0.0046, so the test cannot pass with the wrong recursion.

## Reproducibility pins

Two independently built composites with identical compute content but different names and ids hash
identically and draw bit-identically; a weight edit moves the hash and therefore every child's
derived seed.

Unlike the hazard and response families, this one compares two independently built composites
**directly** rather than round-tripping: closed-form transform children carry no estimated
posterior, so they are free of the upstream bootstrap nondeterminism those families work around.

## Test map

| Test method | Checks |
|---|---|
| `Test_WeightedAverage_LinearChildren_ClosedForm` | Forward, inverse, and transformed-hazard bounds against exact linear algebra |
| `Test_UncertainLinearChildren_VsExactNormalTheory` | Ensemble mean, standard deviation, and 5th/95th percentiles at 4 SE |
| `Test_MixedChildFamilies_EqualWeightedSumOfChildRealizations` | Realization-for-realization identity against the reproduced child-seed recipe |
| `Test_UncertaintySummary_DeterministicChildren_IsExact` | Every band collapses onto the closed-form curve, no simulation |
| `Test_UncertaintySummary_UncertainChildren_VsExactNormalTheory` | Summary mean and bands against exact Normal theory |
| `Test_Reproducibility_SeedAndMetadataPins` | Two-instance hash and draw identity; compute edit moves |

## Notes

The composite's input domain is the **intersection** of its children's, not their union — the one
deliberate departure from the rule its sibling composites use. A weighted average needs every child
evaluable at every input, and averaging a rating curve extrapolated far outside its own table is a
modeling error rather than a bound. An empty intersection is a validation error; differing child
domains raise a warning.
