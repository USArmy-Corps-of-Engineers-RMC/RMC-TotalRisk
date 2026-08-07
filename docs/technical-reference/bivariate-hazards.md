# Bivariate Hazard Functions

> Technical reference for `BivariateHazard` in `RMC.TotalRisk.RiskFunctions.Hazards` — copula-based compound hazards over two linked univariate marginals, the conditional-trapezoid discretization the risk engine integrates, and the practitioner guidance the verification measurements ground. Companion pages: [response-functions](response-functions.md) (the joint response surface and the preserved collapse mode), [transform-functions](transform-functions.md) and [consequence-functions](consequence-functions.md) (the deterministic two-way tables), and [risk-integration](risk-integration.md) (the outer quadrature the conditional bins ride).

A **bivariate hazard** models two jointly occurring hazard variables — e.g., seismic peak ground acceleration and coincident reservoir stage, or flood depth and duration — as a primary marginal X, a secondary marginal Y, and a **copula** carrying their dependence. Sklar's theorem factors any continuous joint distribution into exactly these parts: H(x, y) = C(F_X(x), F_Y(y)).

## Structure

`BivariateHazard` owns no marginal content. `MarginalX` and `MarginalY` **link** univariate hazard functions already in the analysis by `IRiskFunction.Id` (name fallback is lenient; a resolver re-attaches live instances in by-reference persistence), so the marginals keep their own uncertainty models, their own seeds, and their single stored identity — editing a marginal where it is stored is seen by every bivariate hazard that links it. The copula is a Numerics `BivariateCopula` with **fixed parameters**, defaulting to independence; `SecondaryIntegrationBins` (default 20, range [3, 1000]) sets the conditional discretization below.

## The (u, v) convention and tail orientation

Every copula argument in this library is a **non-exceedance probability**: u = F_X(x), v = F_Y(y). Hazard curves are typically authored as exceedance curves; the conversion is u = 1 − AEP_X(x). The convention decides which tail a copula's dependence loads: Gumbel-family **upper-tail** dependence (λ_U > 0) concentrates joint extremes at u, v → 1 — simultaneously large X and Y, the joint-failure-driving corner. Flipping the convention to exceedance probabilities would silently mirror the dependence into the wrong tail; the verification family pins the orientation by asserting that Gumbel dependence strictly **raises** a joint-extreme failure probability over independence.

## Conditional integration

The engine never builds the joint density. At a primary evaluation point x with u = F_X(x), the conditional distribution of V = F_Y(Y) given U = u is the copula's **h-function**:

- h(v | u) = ∂C(u, v)/∂u — the conditional CDF of v given u;
- v = h⁻¹(u, t) — its inverse, mapping a conditional probability level t back to v;
- y = F_Y⁻¹(h⁻¹(u, t)) — the conditional secondary quantile.

Any conditional expectation over the secondary axis becomes a one-dimensional integral over t ∈ [0, 1]:

E[g(x, Y) | X = x] = ∫₀¹ g(x, F_Y⁻¹(h⁻¹(u, t))) dt.

**The conditional-trapezoid rule.** With N = `SecondaryIntegrationBins`, the engine evaluates N + 1 nodes t_j = j/N with the composite trapezoid weights:

- nodes: t₀ = 0, t₁ = 1/N, …, t_N = 1, with the endpoint nodes clamped to [10⁻¹⁶, 1 − 10⁻¹⁶] (the same exhaustive-support clamp the primary quadrature uses);
- weights: w₀ = 1/(2N), w₁ = … = w_{N−1} = 1/N, and the LAST weight is the exact residual 1 − Σ w_{j<N}, so the ordered weight sum is exactly one in floating point;
- the discretized expectation is Σⱼ wⱼ · g(x, yⱼ) with yⱼ = F_Y⁻¹(h⁻¹(u, tⱼ)).

Under independence h⁻¹(u, t) = t, so the nodes are plain secondary quantiles and the rule is exact for any integrand linear in the secondary quantile function — the verification family's exactness identity.

**Per-realization state.** Sampling a bivariate hazard freezes a snapshot per realization: the realization's sampled secondary marginal, a **cloned** copula (copula caches are not share-safe across threads), and the precomputed node/weight vectors. Filling the bins allocates nothing and draws nothing — every random draw happened at sampling time, so repeated evaluations at different x are pure function evaluations.

## Engine placement

The conditional loop lives inside the sampled component's risk computation, behind a single dispatch branch — univariate components take the untouched original path. At each primary evaluation the engine fills the bins once, evaluates **every** failure mode at the same (x, yⱼ), runs the failure-mode combination rules per bin, and folds the bin results into one recorded risk point per stream: **combine, then marginalize**. Marginalizing each mode first and combining the marginals is wrong by exactly the conditional covariance the shared secondary induces, −Cov_j(P_A, P_B) — two modes that both worsen with Y are more likely to fail together than their marginal probabilities suggest.

The outer primary integration, the endpoint rectangles, the exhaustive mass accounting, and every downstream results container are unchanged; a bivariate component contributes one dimension to the multi-component joint integrand, with its secondary axis already marginalized inside the component evaluation.

## Seeding and identity

The canonical hash is a projected identity: the marginals' **content** hashes (never their ids or names — link repair and renames are hash-inert), the copula type and parameters, and the bin count, with the X and Y roles deliberately asymmetric. Seeding follows the composite forward rule — each marginal's sampler seed is `HashCombine(seed, marginal.CanonicalHash(), ordinal)` with ordinals 0 and 1 — so equal-content marginals draw independently and a marginal's stream is stable against everything except its own content.

## Worked example: PGA × pool stage

A seismic scenario with an annual PGA frequency curve (log-interpolated exceedance), a pool-stage duration curve (normal-Z-interpolated), an independence copula, and a joint response surface P(F | pga, stage):

1. The quadrature visits pga = 0.8 g; the primary marginal gives u = F_X(0.8).
2. With independence, the 20-bin rule places y-nodes at the stage quantiles F_Y⁻¹(10⁻¹⁶), F_Y⁻¹(0.05), …, F_Y⁻¹(1 − 10⁻¹⁶). Duration-curve saturation clamps the extreme nodes to the tabulated end stages.
3. Each failure mode evaluates at (0.8, yⱼ): the joint response reads its surface, a secondary-bound consequence reads yⱼ, a primary-bound consequence repeats its value across bins.
4. The bin results combine per j, weight-sum over j, and record as one risk point at pga = 0.8.

Because this scenario's response probabilities are log-interpolated and the stage curve carries a normal-Z tail, the conditional integrand concentrates sharply in the top bins — the case study below.

## Accuracy and the cost model

Each primary evaluation costs ×(N + 1) function work relative to a univariate component, and recorded joint-entry widths scale with the (bin × pathway × branch) cross product. The default N = 20 is a cost/accuracy compromise whose adequacy is **fixture-dependent**, measured by the verification convergence study ([copula-dependence](../verification/copula-dependence.md)):

| Fixture regime | Measured relative error at N = 20 | Decay |
|---|---|---|
| Smooth surface, uniform-like marginal | ≈ 1.1e-3 | textbook O(N⁻²) (ratios 25.0 / 100.0 measured) |
| Dependent copula (Normal ρ = 0.7) | ≈ 6.9e-4 | reduced order ≈ O(N^{−(2−ρ²)}) — the conditional map v(t) has one-sided endpoint cusps |
| Tail-concentrated log-scale surface under a normal-Z-tailed marginal | ≈ 0.35 | rate-limited by the Φ⁻¹ endpoint blow-up before saturation; still ≈ 2e-3 at N = 1000 |

The third regime is the practitioner warning: when the response probability spans orders of magnitude across the secondary axis AND the secondary marginal carries a normal-Z (or similarly heavy) probability transform, the conditional integrand's mass hides in the last few bins of t, and the uniform-t trapezoid needs hundreds of bins for percent-level accuracy. Raise `SecondaryIntegrationBins` toward its 1000 cap for such fixtures, and confirm convergence by comparing two bin counts — the bin count is hashed content, so the comparison is two deliberate runs.

## Which variable belongs on the primary axis

The two axes are **not interchangeable**, and the choice matters more than the bin count. The primary axis is integrated by the adaptive quadrature to its own tolerance; the secondary axis is discretized — by conditional bins here, or by the collapse mode's weighted levels. So the rule is:

> Put the axis whose integrand is hardest — the one carrying the steep probability variation, the heavy-tailed probability transform, and usually the consequence dependence — on the **primary** axis. Collapse or discretize the axis whose mass is concentrated where the response barely varies.

The legacy seismic scenario measures this directly ([bivariate-risk](../verification/bivariate-risk.md)). Driving on PGA with stage as the conditional secondary puts the exact quadrature on the mild axis and the trapezoid on the normal-Z-tailed one: **35.4%** relative error in failure probability at the default 20 bins. Driving on **stage** with PGA collapsed out through the response's automatic Voronoi weights — the v1.0 arrangement — inverts that: **0.241%** on both the failure probability and the risk, better than the same scenario integrated at 1000 conditional bins and roughly fifty times cheaper. The PGA frequency curve puts 99.96% of its mass below the surface's first level (derived weights {0.999593, 2.90e-4, 7.49e-5, 4.21e-5}), inside the clamped region a single weighted point reproduces almost exactly, while the stage axis — where the surface spans four orders of magnitude and the life loss rises — receives the exact treatment.

This is why the collapse mode is preserved rather than deprecated: for a compound hazard whose secondary variable is mass-concentrated and mildly influential, collapsing it against its own frequency curve is not an approximation of the joint path — it is the better-conditioned arrangement of the same double integral. Reach for a bivariate hazard with conditional bins when the dependence is genuine (a non-independence copula), when both axes vary materially, or when the secondary axis feeds transforms or consequences that the collapse cannot express.

## Scope guards

Ratified deferrals, each enforced loudly rather than approximated: nested copulas (a bivariate marginal is a validation error — univariateness of marginals is the rule), copula-parameter uncertainty (parameters are fixed; the sampler reserves the ordinal a future θ posterior will claim), and 2-D surface uncertainty (every bivariate surface is deterministic). Composite functions reject bivariate children in both validation and sampling; tree probability sources reject bivariate referenced responses in validation.
