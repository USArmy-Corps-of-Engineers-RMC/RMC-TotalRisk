# LHS Variance Reduction

**Test class:** `LhsVarianceReductionVerification` · **Tests:** 2 · **Run of record:** 2026-08-28 (incl. the scrambled-Sobol scheme), isolated run, ✅ all passed

The dedicated evidence for the v1.1 sampling upgrade: at N = 1,000 knowledge
realizations over repeated runs, the Latin hypercube scheme must estimate the same ensemble
grand mean as plain Monte Carlo sampling (both unbiased) with a substantially smaller
replicate-to-replicate variance. The [single-component uncertainty family](single-component-uncertainty.md) pinned LHS/MC *agreement*; this
family pins the *variance reduction* that justifies Latin hypercube as the default
`SamplingScheme`.

## Design

Scenario: a deterministic z-grid stage-frequency hazard from Normal(100, 20); a
**deterministic** two-knot fragility; uncertain two-knot failure/non-failure consequence
curves (symmetric Triangular ordinates, the failure mode's failure/non-failure coupling pairing them into one
stratified knowledge dimension). Each knowledge realization integrates deterministically
(adaptive Gauss–Kronrod), so the only stochastic input is the coupling percentile the scheme
controls — and the total-risk statistic is linear in that percentile's quantile functions.

The fragility is deterministic by design. The first cut of this test sampled the fragility
too, and its measured ratio was ≈ 9.7: the total-risk statistic then carries a multiplicative
P_F·C interaction, per-dimension Latin hypercube removes only main-effect variance, and the
ratio converges to the reciprocal of the interaction share — a property of the statistic, not
of the sampler. The linear fixture isolates the property under test (the stratification of
the engine's knowledge-percentile streams, including the coupling matrix), for which the
true ratio is orders of magnitude; a ratio near one would conversely be the loud failure
signature if the scheme option ever stopped reaching the samplers.

Per scheme, **5 replicate full-uncertainty runs at 1,000 realizations** differing only in
`PRNGSeed` (the content-based seed derivation folds the seed, so each replicate draws an
independent stream). The per-run statistic is the ensemble grand mean of the total risk mean.

## Asserts

| Assert | Criterion |
|---|---|
| Variance reduction | var(MC) / var(LHS) ≥ 10 across the 5 replicates |
| Unbiasedness | pooled LHS and MC grand means agree within 4·√(var(MC)/R + var(LHS)/R), R = 5 |
| Linearity anchor | both pooled means reproduce the deterministic mean-only run (the expectation is linear in each independently sampled knowledge dimension; the symmetric Triangulars make the mean curve the expected curve under either reading) |
| Seed sensitivity | every LHS replicate differs from every other (the seed genuinely re-randomizes the stratification) |

## Results

Measured (the verifying run): var(MC) = 0.3971, var(LHS) = 9.306e-6 — a variance ratio of
**4.27 × 10⁴** against the ≥ 10 assert. Pooled grand means: Monte Carlo 168.63729, Latin
hypercube 168.57136, deterministic mean-only anchor 168.57226 — the Latin hypercube pooled
mean sits 5.4e-4 relative from the anchor (inside its own replicate error), and the Monte
Carlo mean agrees within its 4·√(var/R) band. Every Latin hypercube replicate is distinct.

## Notes

Latin hypercube stratifies each function's percentile stream into N equal-probability bins,
so for these smooth monotone quantile functions the grand mean's sampling variance collapses
by orders of magnitude relative to independent uniform sampling — the assert threshold of 10×
is deliberately conservative so the pin stays robust across environments while still failing
loudly if the scheme option ever stopped reaching the samplers.

## The scrambled-Sobol scheme

`Test_ScrambledSobolVsMonteCarlo_GrandMeanVarianceReduction` gates
`SamplingScheme.ScrambledSobol` — Matousek-scrambled Sobol knowledge sampling seeded from each
function's content-derived stream seed — on the same fixture and replicate framework: five
replicate 1,000-realization runs must cut the grand-mean replicate variance against plain Monte
Carlo by at least the family's 10× floor (conservative for a low-discrepancy scheme on this
near-linear statistic, even at the fixture's non-power-of-two count, which validation advises
on), the pooled scheme means must agree within their combined 4·SE replicate errors, the
scrambled pooled mean must reproduce the deterministic mean-only answer, every replicate must
differ (the seed moves the scramble), an identical seed must reproduce the grand mean
bit-for-bit (the content-seed contract the unrandomized sequence cannot honor), and renaming
the model must be bit-inert (the scramble seed derives from content, never names). Run of
record 2026-08-28: `LhsVarianceReductionVerification` 2/2 passed (13 m 02 s wall), isolated
invocation.
