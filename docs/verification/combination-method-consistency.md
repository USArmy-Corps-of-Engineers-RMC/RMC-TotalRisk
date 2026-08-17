# Combination-Method Consistency

**Test class:** `CombinationMethodConsistencyVerification` · **Tests:** 6 · **Run of record:** 2026-07-23, isolated run, ✅ all passed

A NEW family of engine-only property tests grounded in the *Failure Mode Combination Methods*
technical note [24] — cross-method invariants the legacy suite never asserted. All runs are
mean-only on the shared Bucket-1 2-PFM scenario (see [joint-failures.md](joint-failures.md)),
so every comparison is quadrature against quadrature: the invariance tolerances are numerical
scales, not Monte Carlo statistics.

## The §6.1 union invariance

The system failure probability depends on the marginal response curves and the dependency
structure — **not** on the combination method:

| Dependency | Joint | Common cause | Competing |
|---|---|---|---|
| Independent | 0.066977264 | 0.066977264 | 0.066975904 |
| ρ = 0.5 | 0.066536298 | 0.066536298 | 0.066536087 |

Joint and common-cause agree to eight digits (they evaluate the same union — under
independence through exact kernels, under ρ = 0.5 through two different dependent kernels
that here coincide numerically); the competing union rides its 200-level
cumulative-incidence discretization (0.002% observed, asserted at the documented 1%).

## The unimodal (Fréchet) bound ordering

Realized by the joint method's dependency options and capped by the mutually-exclusive sum:

> positive 0.0662491 < ρ = 0.5 0.0665363 < independent 0.0669773 < negative 0.0678455 ≤ exclusive 0.0678455

Note the exact coincidence of the last two: at D = 2 the perfectly negative union IS the
capped sum (perfect negative dependence makes two failures effectively exclusive) — the
technical note's reversed-bounds observation reproduced by the engine.

## The other pins

| Pin | Result |
|---|---|
| Background invariance — `Background.Mean` identical across 9 method/dependency configurations | ✅ 1e-9 relative |
| `RiskIntegrand` invariance — all seven refinement objectives reproduce the default's total mean and failure union; all five risk streams always produced | ✅ 1e-4 relative (objectives move only the recorded-mass placement) |
| Reliability parity — reliability-mode AFP equals the risk-mode union for joint, common-cause, and competing | ✅ 1e-4 relative (the forced failure-probability objective shifts refinement; measured ≈ 2e-6) |

## Why this family exists

The conversion families verify each combination method against its own oracle; this family
verifies the methods against **each other**, pinning the structural relationships the
technical note derives (union invariance, bound ordering and its D = 2 degeneracies, the
allocation-versus-union separation). A regression that biased one method's union — or broke a
dependency option's wiring, as the perfectly-negative materialization defect did — trips these
property pins even where a single-family tolerance might absorb it.
