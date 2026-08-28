# Value of Information Verification

**Test class:** `ValueOfInformationVerification` · **Tests:** 4 · **Run of record:** 2026-08-27, isolated run, ✅ all passed

The value-of-information surface ranks candidate studies by the epistemic variance of a stored
risk measure they could resolve — the between-bin variance of the weighted conditional measure
means over equal-weight bins of each knowledge column — and by the expected movement of every
configured tolerable-risk confidence statement
([value-of-information](../technical-reference/value-of-information.md)). This family anchors
the conditioning estimator to a **linear-Gaussian knowledge map**, where both quantities are
exactly computable, and the full engine query to an **independent re-implementation of the
documented estimator convention** over measures read from the public per-realization
summaries. The knowledge columns themselves are the engine's recorded percentile draws —
content-seeded stream reproduction is pinned by the seed-scribe and reproducibility families —
so this family's independence claim covers the conditioning mathematics and the measure
extraction, never the library's estimator, measure selectors, or weighted paths.

## Scenarios

**Linear-Gaussian designs.** y = Σ aᵢ·Φ⁻¹(uᵢ) with a = {3, 2, 1} over Latin-hypercube designs
(`LatinHypercube.Random`, seeds 20260827/+1) at n = 200,000 (main effect) and n = 100,000
(movement). The Latin-hypercube stratification aligns each column's equal-frequency bins
exactly with the equal-probability bins of the closed form.

**Engine scenario.** The trivial uncertain single-component fixture of the weighted-ensemble
family — stage frequency (0.999 → 0 ft, 0.5 → 10 ft, 0.001 → 30 ft), a triangular-ordinate
uncertain fragility (10 → 20 ft), a triangular-ordinate uncertain failure consequence ramping
to 300, a deterministic non-failure consequence to 60 — at N = 1,000 realizations (50 per
conditioning bin), with the criterion mean Excess > 1 configured so the movement blocks
publish; three knowledge columns (the fragility, the consequence function, and the mode's
consequence-coupling draw). The weighted check adds the house pattern
w(i) = 0.25 + ((37·i) mod 11) post-hoc.

## Oracle derivations

- **Exact binned main effect.** Over B equal-probability bins of a standard normal, the bin
  means are m_b = B·(φ(z_(b−1)) − φ(z_b)) (truncated-normal means), so the exact binned
  variance of the conditional mean of y given input i is aᵢ²·Σ (1/B)·m_b², computed by the
  oracle from its own density formula. The estimator's expectation at the aligned partition is
  exactly this value; the residual is the other columns' conditional noise.
- **Exact movement.** P(y > 0 | uᵢ) = Φ(aᵢ·Φ⁻¹(uᵢ)/σᵣ) with σᵣ² = Σⱼ≠ᵢ aⱼ², integrated over
  each probability bin by the oracle's own Simpson rule (2,000 nodes per bin); the exact
  movement is Σ (1/B)·|p_b − 1/2|.
- **The re-implementation.** Pairwise NaN filtering, the stable ascending input sort with the
  realization-index tie break, the equal-weight partition closed at each cumulative target
  k·W/B with the last bin absorbing the residual, weighted bin means, and weighted
  strict-exceedance fractions — coded locally from the documented convention over
  `results[i].{stream}.Mean` read from the public summaries.

## Checks

| # | Test | Verifies | Tolerance | Result |
|---|---|---|---|---|
| 1 | `Test_LinearGaussian_MainEffect_MatchesClosedBinnedForm` | Each input's estimated resolvable variance against aᵢ²·Σ (1/B)·m_b², and the total variance against Σ aᵢ² | 5e-3 relative on the main effects — the aligned bins leave only the other columns' conditional noise, whose bin-mean standard error √(Σⱼ≠ᵢ aⱼ²)/√(n/B) enters as a positive noise bias of order B/n (≈ 5e-4 absolute) plus root-n jitter; 2e-2 relative on the total (an unstratified fourth-moment estimate) | ✅ |
| 2 | `Test_LinearGaussian_Movement_MatchesProbitQuadrature` | Each input's estimated confidence movement against the probit quadrature, and the monotone ordering movement(a=3) > movement(a=2) > movement(a=1) | 8e-3 absolute — each of the 20 bin fractions carries a standard error ≤ √(0.25/(n/B)) ≈ 7.1e-3, and the weighted average of their absolute deviations concentrates by a further √B | ✅ |
| 3 | `Test_EngineQuery_MatchesIndependentReimplementation` | Behind the full engine: every entry's resolvable variance and the total against the re-implementation, walk-order label and group alignment, the rollups partitioning the entries bit-exactly, and the movement block whose baseline must equal the published tolerable-risk confidence entry | 1e-12 relative — the same doubles reduced through independently coded arrangements, bounded by rounding over ≤ 1,000 summands; the baseline and rollup-partition comparisons at 0 (bit) | ✅ |
| 4 | `Test_WeightedEngineQuery_MatchesIndependentReimplementation` | Post-hoc realization weights flowing through every conditional reduction against the weighted re-implementation, the weighted movement baseline bit-equal to `ComputeTolerableRiskConfidence()`, and the stored payload byte-identical after the queries once the weights are cleared | 1e-12 relative as in check 3; the baseline and payload comparisons at 0 (bit) | ✅ |

Unit-level companions (fast suite): the estimator's exact two-bin hand decompositions, the
exact between-plus-within identity, the integer-weight replication identity, pairwise NaN
filtering, the fewer-pairs-than-bins NaN guard, deterministic tie handling, the argument-guard
matrix, the movement hand cases (a separating input hits the 2·p·(1−p) ceiling; an
uninformative input moves nothing); the result containers' construction, snapshotting, derived
standard deviations, and NaN-last stable ranked views; and the engine surface's null
conditions, walk alignment with the sensitivity engine, exact share and rollup identities,
determinism, stored-ensemble inertness, published-confidence bit agreement across degenerate
and mid-scale thresholds, post-hoc weight honoring, and the component-scope movement rule.

Run of record 2026-08-27: `ValueOfInformationVerification` 4/4 passed (7.2 s wall — two
linear-Gaussian designs at n = 200,000/100,000 and two N = 1,000 engine runs), isolated
invocation `dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~ValueOfInformationVerification"`.
