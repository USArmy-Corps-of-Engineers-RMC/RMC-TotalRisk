# Copula Dependence Verification

**Test class:** `CopulaDependenceVerification` · **Tests:** 6 · **Run of record:** 2026-08-07, isolated run, ✅ all passed

> Family: the conditional-bin integration of `BivariateHazard` under dependence (greenfield — no legacy counterpart)
> Anchors: exactness identities, analytic copula closed forms independently transcribed, a dense analytic-CDF moment re-derivation, and the bin-count convergence study whose measured figures are the pinned tolerance source for [bivariate-risk](bivariate-risk.md)
> Exactness identities at 1e-10 relative; copula-conditioned engine comparisons at measured-and-documented allowances

## Fixture design

The marginals are two-knot tabular curves with probability knots at 1e-15 and 1 − 1e-15 — uniform distributions up to saturation atoms of mass 1e-15, far below every tolerance here. The response surface is a single bilinear cell over the marginal supports, so with corner probabilities z₀₀, z₀₁, z₁₀, z₁₁ the joint failure probability reduces to E[P] = m·(z₀₀ + z₁₁) + (1/2 − m)·(z₀₁ + z₁₀) with m = E[UV] under the copula — an exact target whenever m is known:

- independence: m = 1/4;
- Normal copula: m = 1/4 + arcsin(ρ/2)/(2π) — write U = Φ(s), V = Φ(t); E[Φ(s)Φ(t)] = P(A < s, B < t) with A, B independent standard normals, and (A − s, B − t) is bivariate normal with correlation ρ/2;
- Archimedean families: m re-derived densely from the analytic copula CDF alone through E[UV] = ∫∫ P(U > s, V > t) ds dt = ∫∫ C(s, t) ds dt (the linear terms integrate to zero) — smooth and bounded, unlike Simpson over the inverse conditional, whose endpoint cusps corrupt a uniform grid. The integrator self-checks against the independence identity (1/4 at 1e-10) and by refinement doubling (2048² vs 4096², < 1e-8).

## Results by test

| Test | Anchor | Result |
|---|---|---|
| Independence exactness | surface linear in the secondary × uniform secondary ⇒ the conditional trapezoid is exact at ANY bin count | engine = 0.4125 exactly at 3 AND 20 bins (1e-10 relative) |
| Normal copula | h-function vs the analytic Φ((Φ⁻¹(v) − ρΦ⁻¹(u))/√(1−ρ²)) at 1e-12 (ρ = 0.7 and −0.4); engine vs the analytic mean | measured 3.8e-6 relative at 1000 bins; asserted at 1.5e-5 (≈ 4× head-room) |
| Convergence study | three fixtures at N ∈ {20, 100, 1000} — see below | pinned |
| Clayton | h-function u^(−θ−1)·(u^(−θ)+v^(−θ)−1)^(−(θ+1)/θ), the analytic inverse conditional, and the 1e-12 round trip; engine vs the dense ∫∫C re-derivation | asserted at 4e-5 relative (the θ = 2 lower-tail t^{1/(θ+1)} endpoint cusp bounds the 1000-bin trapezoid) |
| Gumbel orientation | h-function vs a central finite difference of the independently transcribed CDF (1e-8); engine: upper-tail dependence must RAISE joint-extreme failure probability over independence (analytic ratio ≈ 1.23 at θ = 2) | passes; the independence baseline is the exact corner mean 0.225 |
| Marginal-uncertainty propagation | a deterministically injected Normal posterior on the secondary marginal (a fixed formula, no randomness) ⇒ each of 120 ensemble realizations compares against a hand-rolled dense conditional integral of the same parameter set | every realization within 0.1% relative (the bound covers the ensemble-pass quadrature discipline at 1e-4, the 200-bin trapezoid residual, and the oracle's own density) |

## The bin-count convergence study

The derivation source for every trapezoid allowance in the bivariate families. Three fixtures at N ∈ {20, 100, 1000}, all measurements from the run of record:

| Fixture | Relative errors {20, 100, 1000} | Decay |
|---|---|---|
| (i) Smooth: independence with a log-interpolated surface whose corner exponents are additively separable (log₁₀ corners {−4, −3, −2, −1}), exact mean 10⁻⁴·(10² − 1)/(2·ln10)·(10 − 1)/ln10; primary quadrature pinned at 1e-10 so the measured error is the conditional discretization's alone | {1.104e-3, 4.418e-5, 4.418e-7} | ratios **24.995** and **99.999** — textbook O(N⁻²), matching the trapezoid prefactor k²/12 for ∫e^{kt}dt |
| (ii) Normal-copula cusp series (ρ = 0.7, the analytic-mean fixture) | {6.93e-4, 8.22e-5, 3.81e-6} | ratios 8.4 and 21.6 — the conditional map v(t) = Φ(√(1−ρ²)Φ⁻¹(t) + ρΦ⁻¹(u)) has one-sided endpoint cusps (dv/dt ~ t^(−ρ²) as t → 0), reducing the order to ≈ O(N^{−(2−ρ²)}) |
| (iii) The legacy seismic SRP probe (E_Y[P(0.8, Y)] against the exact union-grid closed form 0.029670393164335482) | {0.353, 0.0491, 1.98e-3} | rate-limited by the stage marginal's normal-Z tail: P ~ e^{c·Φ⁻¹(t)} concentrates the integrand in the top bins (the surface probability swings 0.35 → 0.81 inside t ∈ [0.999, 1]) before saturation flattens them |

**Pinned figures** (consumed by the legacy-oracle family's tolerances, with head-room over the measured values): the legacy fixture's bins = 20 relative error is pinned at 0.5 and its bins = 1000 error at 4e-3.

**Default-20 adequacy, stated honestly:** the default bin count is adequate for smooth, moderate-variation surfaces (≈ 0.1% relative) and INADEQUATE for tail-concentrated log-scale surfaces under normal-Z-tailed marginals (≈ 35% relative on the legacy seismic fixture, and still ≈ 0.2% at 1000 bins). Practitioner guidance is in [bivariate-hazards](../technical-reference/bivariate-hazards.md).

## Upstream defect found and fixed by this family

The Gumbel orientation pin exposed a genuine Numerics defect on its first run. `GumbelCopula.InverseConditionalCDF(u, t)` solves the h-function by Brent over the bracket [0, 1]. Algebraically h(1 | u) ≡ 1, but the objective computes it as exp(−pow(pow(−ln u, θ), 1/θ)), which rounds to 1 − |ln u|·ulp — BELOW the engine's top conditional node t = 1 − 1e-16 whenever |ln u| ≳ 1, so the bracket contained no sign change and Brent threw "root is not bracketed". A dense scan measured 121 of 1,999 uniform u values failing at θ = 2 (first at u = 0.006; the low clamp never fails because f(0) = −t < 0 always brackets), so with hundreds of primary quadrature nodes per run, any Gumbel-copula engine run was near-certain to crash. The AGK integrator absorbs integrand exceptions, so the failure surfaced only as "an integrand evaluation threw" — a first-chance hook was needed to see the cause.

Fixed upstream by evaluating the objective at the bracket end and saturating at the boundary when the requested level is unbracketed within rounding, with dense-sweep regression tests over a θ range. **`JoeCopula` carried the identical defect** — rare at moderate θ but affecting 1,648 of 1,999 u values at θ = 20 — and received the same guard. Those two are the only conditional inverses that solve numerically; the analytic-inverse families (Independence, Normal, Clayton, Frank, AMH, Student-t) were never affected.

A separate, pre-existing accuracy limitation was recorded while diagnosing and deliberately **not** changed: the Archimedean families inherit `ConditionalCDF` as the generator ratio φ′(u)/φ′(C(u,v)) rather than the closed form their inverse solves, and its precision degrades at v = 1 for large θ (Joe returns 0.5245 at θ = 20, u = 0.846 where the exact value is 1). The boundary regression tests therefore assert completion, range, and monotonicity in the conditional level rather than a round trip that would cross the two formulas at their weakest point; interior levels keep their reference-value round-trip pins.
