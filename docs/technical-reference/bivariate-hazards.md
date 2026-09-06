# Bivariate Hazard Functions

> Technical reference for `BivariateHazard` in `RMC.TotalRisk.RiskFunctions.Hazards` — copula-based compound hazards over two linked univariate marginals, the adaptive two-dimensional quadrature the risk engine integrates them with, and the practitioner guidance the verification measurements ground. Companion pages: [response-functions](response-functions.md) (the joint response surface and the preserved collapse mode), [transform-functions](transform-functions.md) and [consequence-functions](consequence-functions.md) (the deterministic two-way tables), and [risk-integration](risk-integration.md) (the primary-axis quadrature the conditional integration extends).

A **bivariate hazard** models two jointly occurring hazard variables — e.g., seismic peak ground acceleration and coincident reservoir stage, or flood depth and duration — as a primary marginal X, a secondary marginal Y, and a **copula** carrying their dependence. Sklar's theorem factors any continuous joint distribution into exactly these parts: H(x, y) = C(F_X(x), F_Y(y)).

## Structure

`BivariateHazard` owns no marginal content. `MarginalX` and `MarginalY` **link** univariate hazard functions already in the analysis by `IRiskFunction.Id` (name fallback is lenient; a resolver re-attaches live instances in by-reference persistence), so the marginals keep their own uncertainty models, their own seeds, and their single stored identity — editing a marginal where it is stored is seen by every bivariate hazard that links it. The copula is a Numerics `BivariateCopula` with **fixed parameters**, defaulting to independence; `SecondaryIntegrationBins` (default 20, range [3, 1000]) sets the fixed conditional grid the discretization diagnostic instruments and prices the per-slice refinement budget of the adaptive interior below (21 × bins evaluations per fixed-slice sweep).

## The (u, v) convention and tail orientation

Every copula argument in this library is a **non-exceedance probability**: u = F_X(x), v = F_Y(y). Hazard curves are typically authored as exceedance curves; the conversion is u = 1 − AEP_X(x). The convention decides which tail a copula's dependence loads: Gumbel-family **upper-tail** dependence (λ_U > 0) concentrates joint extremes at u, v → 1 — simultaneously large X and Y, the joint-failure-driving corner. Flipping the convention to exceedance probabilities would silently mirror the dependence into the wrong tail; the verification family pins the orientation by asserting that Gumbel dependence strictly **raises** a joint-extreme failure probability over independence.

## Conditional integration

The engine never builds the joint density. At a primary evaluation point x with u = F_X(x), the conditional distribution of V = F_Y(Y) given U = u is the copula's **h-function**:

- h(v | u) = ∂C(u, v)/∂u — the conditional CDF of v given u;
- v = h⁻¹(u, t) — its inverse, mapping a conditional probability level t back to v;
- y = F_Y⁻¹(h⁻¹(u, t)) — the conditional secondary quantile.

Any conditional expectation over the secondary axis becomes a one-dimensional integral over t ∈ [0, 1]:

E[g(x, Y) | X = x] = ∫₀¹ g(x, F_Y⁻¹(h⁻¹(u, t))) dt.

**The probit coordinate.** The engine integrates the conditional axis in z = Φ⁻¹(t) rather than in t itself, over [Φ⁻¹(10⁻¹⁶), Φ⁻¹(1 − 10⁻¹⁶)] with the Jacobian φ(z) folded into every recorded mass. The change of variable is what makes the hard class tractable: a secondary marginal carrying a normal-Z probability transform makes the integrand behave like e^{c·Φ⁻¹(t)} near t → 1 — invisible to any uniform-in-t rule and to error estimators sampling t-space — while in z the same integrand is a plain exponential the adaptive refinement resolves like any smooth function. Every adopted mass set renormalizes to a bit-exact unit sum (residual on the largest-mass node), so the φ-quadrature deficiency of an accepted mesh can bias no total.

**The additive interior (two-dimensional adaptive quadrature).** For a bivariate component in the additive/single-component path, each realization integrates over (u, z) with `AdaptiveGaussKronrod2D`, one pass per stratification strip of the primary support (the strip edges keep the v1.0 stratification and the threshold/α discontinuity injection):

- refinement is steered by a balanced scalar surrogate — the combined failure probability and the failure-consequence density at (x, y(u, t)), each normalized by a deterministic probe scale — so both probability structure and consequence tails attract nodes; the surrogate decides only *where* the mesh refines, never what is computed;
- each strip gets a fair share of the evaluation budget (`MaxEvaluations` split over the active strips, floor one 441-evaluation tensor region), and its recorded masses rescale to the strip's exact probability width;
- the flushed nodes of all strips are sorted and grouped by exact primary abscissa, and each distinct abscissa replays **one** staged bivariate evaluation over its merged conditional column — one recorded risk point per abscissa per stream, so the exhaustive mass ledger, the matched-count gates, and every downstream results consumer are structurally identical to the univariate path.

Where the refinement splits the primary axis at different conditional bands, neighboring columns cover complementary conditional slabs; their masses partition the probability axis exactly, and the merged points' entries are conditional on the covered slab — the documented output-granularity property of the two-dimensional rule. Curve and measure totals are unaffected.

**The fixed-slice conditional sweep.** Everywhere the engine needs the conditional expectation at one *fixed* u — the multi-component joint (VEGAS) evaluation, the annual-failure-probability probe, hazard-level sensitivity, and the support-edge endpoint columns of the additive interior — a one-dimensional adaptive Gauss–Kronrod pass over z refines the same surrogate at that slice, adopts its accepted composite rule as the conditional column (sorted, coalesced, Jacobian-folded, renormalized), and replays the same staged evaluation over it. The sweep budget is 21 × `SecondaryIntegrationBins` evaluations (231-node column bound at the default 20), raised to the strip fair share for the additive interior's endpoint columns.

**Per-realization state.** Sampling a bivariate hazard freezes a snapshot per realization: the realization's sampled secondary marginal, a **cloned** copula (copula caches are not share-safe across threads), and the shared conditional-node machinery. Conditional evaluation draws nothing — every random draw happened at sampling time, so repeated evaluations at different x are pure function evaluations; the adaptive passes evaluate already-sampled surfaces at deterministic points, and the mesh they adopt is fully determined by the sampled content.

## Engine placement

The conditional machinery lives inside the sampled component's risk computation, behind a single dispatch branch — univariate components take the untouched original path. At each committed abscissa the engine evaluates **every** failure mode at the same (x, yⱼ) column nodes, runs the failure-mode combination rules per node, and folds the results into one recorded risk point per stream: **combine, then marginalize**. Marginalizing each mode first and combining the marginals is wrong by exactly the conditional covariance the shared secondary induces, −Cov_j(P_A, P_B) — two modes that both worsen with Y are more likely to fail together than their marginal probabilities suggest.

The primary-axis endpoint rectangles, the exhaustive mass accounting, and every downstream results container are unchanged; a bivariate component contributes one dimension to the multi-component joint integrand, with its secondary axis adaptively marginalized inside each component evaluation.

## Seeding and identity

The canonical hash is a projected identity: the marginals' **content** hashes (never their ids or names — link repair and renames are hash-inert), the copula type and parameters, and the bin count, with the X and Y roles deliberately asymmetric. Seeding follows the composite forward rule — each marginal's sampler seed is `HashCombine(seed, marginal.CanonicalHash(), ordinal)` with ordinals 0 and 1 — so equal-content marginals draw independently and a marginal's stream is stable against everything except its own content.

## Worked example: PGA × pool stage

A seismic scenario with an annual PGA frequency curve (log-interpolated exceedance), a pool-stage duration curve (normal-Z-interpolated), an independence copula, and a joint response surface P(F | pga, stage):

1. The quadrature visits pga = 0.8 g; the primary marginal gives u = F_X(0.8).
2. With independence, the conditional column's y-nodes are stage quantiles F_Y⁻¹(Φ(zⱼ)) at the adaptively placed probit nodes — dense where the refinement surrogate says the failure structure lives, sparse where it is flat. Duration-curve saturation clamps the extreme nodes to the tabulated end stages.
3. Each failure mode evaluates at (0.8, yⱼ): the joint response reads its surface, a secondary-bound consequence reads yⱼ, a primary-bound consequence repeats its value across the column.
4. The node results combine per j, mass-sum over j, and record as one risk point at pga = 0.8.

Because this scenario's response probabilities are log-interpolated and the stage curve carries a normal-Z tail, the conditional integrand concentrates sharply near t → 1 — exactly the class the probit coordinate linearizes (the case study below).

## Accuracy and the cost model

The conditional axis is integrated adaptively, so accuracy follows the quadrature discipline rather than a fixed node count: the mean pass and the deterministic probes refine to the analysis `Tolerance` (default 1e-8 relative) and ensemble realizations to the `EnsembleTolerance` (default 1e-4), each with an absolute floor of 1e-15. The verification convergence study ([copula-dependence](../verification/copula-dependence.md)) measures three regimes against closed forms — a smooth separable surface, a dependent Normal-copula fixture whose conditional map carries endpoint cusps, and the tail-concentrated legacy seismic class whose integrand hides its mass within 10⁻³ of t = 1 under a normal-Z-tailed marginal:

| Fixture regime | Fixed 20-bin grid (historical default) | Adaptive interior (measured) |
|---|---|---|
| Smooth surface, uniform-like marginal | ≈ 1.1e-3 | < 1e-5 at every configured budget |
| Dependent copula (Normal ρ = 0.7) | ≈ 6.9e-4 | < 1e-4 at every configured budget |
| Tail-concentrated log-scale surface, normal-Z-tailed marginal | ≈ 0.35 (still ≈ 2e-3 at 1000 bins) | ≈ 4e-6 at the default budget |

The third row is the reason the interior is adaptive: the class that rate-limited the uniform-t grid — and that no practical bin count fully resolved — sits at quadrature scale under the probit coordinate, because the Φ⁻¹ blow-up that concentrated the mass is exactly what z = Φ⁻¹(t) linearizes. The extreme-dependence robustness probe extends the same conclusion through the numerically inverted copula families (Gumbel and Joe at θ = 8 land within ≈ 5e-12 of dense closed forms). Costs: each committed abscissa evaluates its adopted conditional column (bounded by the refinement budget rather than a fixed 21 nodes), recorded joint-entry widths scale with the (column × pathway × branch) cross product priced through `SecondaryIntegrationBins`, and the additive interior spends up to `MaxEvaluations` surrogate evaluations per realization steering the mesh.

### The discretization diagnostic (the fixed-grid instrument)

`RiskAnalysis.EstimateSecondaryDiscretizationError(componentIndex)` is the quarantined fixed-conditional-grid instrument: it integrates the mean pass on the **uniform conditional trapezoid** at the configured, halved, and quartered bin counts (on diagnostic snapshots — the stored count, the canonical hash, and every seed are untouched, and nothing is serialized) and Richardson-extrapolates at the trapezoid's second-order rate. The production interior never runs that grid; the instrument's extrapolated limit is the standing *independent cross-check* of the adaptive answer — a structurally different discretization converging on the same integral — and its **observed convergence ratio** remains the regime check (near four: in-regime, the estimate is trustworthy; well below four: the tail-concentrated class, where the fixed grid was inadequate and the extrapolation is indicative only). The verification family pins the instrument against the convergence study's measured true errors and pins the adaptive mean answer against the instrument's ladder. The configured count needs at least twelve bins for the three levels; the diagnostic re-runs the component's sampler setup like every stored-results diagnostic.

## Which variable belongs on the primary axis

The two axes remain structurally different — the primary axis drives the stratified outer quadrature and the loss-exceedance recording; the secondary is conditionally integrated per slice — but with both axes now refined adaptively to tolerance, **the arrangement no longer dominates accuracy**. The legacy seismic scenario measures this directly ([bivariate-risk](../verification/bivariate-risk.md)): under the historical fixed grid, driving on PGA with stage conditional read **35.4%** relative error at 20 bins while the v1.0 arrangement (stage primary, PGA collapsed through automatic Voronoi weights) read 0.241%; under the adaptive interior **both arrangements land within 0.5%** of each other and of the exact reference, and the bivariate arrangement's own error against the dense closed form sits near 4e-6. Choose the primary axis for modelling reasons — which variable the loss-exceedance curve should be expressed against, which axis the transforms and consequences key on — rather than for conditioning.

The collapse mode is preserved rather than deprecated: for a compound hazard whose secondary variable is mass-concentrated and mildly influential, collapsing it against its own frequency curve through the response's weighted levels remains a legitimate, cheaper arrangement of the same double integral (the consistency cross-anchor pins that both routes converge to the same answer under refinement). Reach for the bivariate joint path when the dependence is genuine (a non-independence copula), when both axes vary materially, or when the secondary axis feeds transforms or consequences the collapse cannot express.

## Copula families

The dependence structure is any Numerics bivariate copula (`CopulaType`, serialized by enum name —
an append-only contract). Orientation is fixed by the (u, v) **non-exceedance** convention:
upper-tail dependence in (u, v) is joint extreme-hazard dependence.

| Family | Tail behavior | Conditional machinery |
|---|---|---|
| `Normal` (Gaussian) | symmetric, no tail dependence | analytic h-function `h(v|u) = Φ((Φ⁻¹(v) − ρΦ⁻¹(u))/√(1−ρ²))` and inverse |
| `StudentT` | symmetric, dependence in **both** tails | analytic (t-based) h-function and inverse |
| `Clayton` | **lower**-tail dependence | analytic h-function `u^(−θ−1)·(u^(−θ) + v^(−θ) − 1)^(−(θ+1)/θ)` and inverse |
| `Gumbel` | **upper**-tail dependence | conditional inverse solved numerically (Brent), saturating at the boundary |
| `Joe` | **upper**-tail dependence | numeric inverse with the same boundary saturation |
| `Frank` | symmetric, no tail dependence | analytic inverse over the full θ range |
| `AliMikhailHaq` | weak-dependence range only | analytic |
| `Independence` | product copula C(u,v) = u·v | h(v|u) = v; zero parameters (the default when no copula is stored) |

Selection guidance: joint extreme-hazard scenarios (the flood-plus-surge class) want an
upper-tail-dependent family (Gumbel, Joe, or Student-t) — a Gaussian copula with matched rank
correlation understates joint extremes because its tail dependence is zero. Two implementation
facts matter to consumers: only Gumbel and Joe invert their conditional numerically, and both
saturate at conditional levels within rounding of one rather than failing the bracket; and the
Archimedean families' forward `ConditionalCDF` is the generator-ratio form, which degrades at
v → 1 for large θ — never round-trip the inverse against it at the exact boundary. Family-by-family
anchors (analytic h-functions, exact E[UV] targets, the orientation pin) are in
[../verification/copula-dependence.md](../verification/copula-dependence.md).

## Scope guards

Deliberate scope limits, each enforced loudly rather than approximated: nested copulas (a bivariate marginal is a validation error — univariateness of marginals is the rule), copula-parameter uncertainty (parameters are fixed; the sampler reserves ordinal 2 for a θ posterior), and 2-D surface uncertainty (every bivariate surface is deterministic). Composite functions reject bivariate children in both validation and sampling; a tree probability source accepts a bivariate referenced response only under a declared source axis with a hazard-transform chain supplying the other surface coordinate (an undeclared bivariate reference remains a validation error).
