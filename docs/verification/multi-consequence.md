# Multi-Consequence Axis Verification

**Test class:** `MultiConsequenceVerification` · **Tests:** 6 · **Run of record:** 2026-07-23, isolated run, ✅ all passed

The declared consequence-type axis (analysis-level declaration, strict bubble-down validation)
computes **every** type through one engine pass: position k of each failure mode's ordered
consequence list samples at coupling column k (the per-type shared coupling draw), the probability
structure is computed once and shared by all types, adaptive refinement is driven by the
primary type, and type k's results record into the primary containers (k = 0) or
`AdditionalCurves[k − 1]` at every scope. This family verifies that closure end to end.

## Scenarios

**A — oracle-grade (deterministic):** the mean-parity dense tables — stage frequency from
Normal(100, 20) quantiles and fragility Φ((h − 140)/30) on a z-grid (step 0.25, range ±8) with
linear interpolation — carrying two consequence types on both the failure and non-failure
paths:

| Type | Failure curve | Non-failure curve |
|---|---|---|
| Life Loss (primary) | linear (60 → 0, 200 → 1,000) | linear (60 → 0, 200 → 100) |
| Damages (position 1) | linear (60 → 0, 200 → 5,000,000) | linear (60 → 0, 200 → 1,500,000) |

The damages failure/non-failure ratio (10:3) deliberately differs from the life-loss ratio
(10:1), so the per-type excess clamps behave differently — the axes are genuinely independent,
not scalings of one another.

**B — ensemble-grade (uncertain):** the trivial engine fixture (stage frequency 0.999 → 0 ft,
0.5 → 10 ft, 0.001 → 30 ft; triangular-ordinate uncertain fragility; linear consequences over
0–30 ft), run single-type and two-type at 400 LHS realizations.

## Oracle

One million hazard draws through `MersenneTwister(12345)` (the legacy seed), inverse-transform
sampling the shared hazard table with the oracle's own linear interpolator, accumulating the
five stream summands (fail, non-fail, total, excess, background) and the failure probability
for **both** types per draw. Assert tolerances are k·SE (k = 4) with SE = σ̂/√N computed in-run
per output — at N = 10⁶ roughly 0.1% of σ̂ ([docs/verification.md](../verification.md)).

## Checks

| # | Test | Verifies | Tolerance | Result |
|---|---|---|---|---|
| 1 | `Test_MeanPass_TwoTypes_VsOracle` | All five stream means + AFP on **both** types of one engine pass vs the independent two-type oracle | 4·SE per output | ✅ |
| 2 | `Test_MeanPass_PrimaryUnperturbed_BitIdentical` | Declaring a second type leaves the single-type primary results **bit-identical** (summaries and full LEC arrays) — the mean pass is seed-free and refinement is primary-driven | 0 (bit) | ✅ |
| 3 | `Test_SecondaryVsDedicatedPrimary_QuadratureParity` | The two-type run's secondary axis vs a dedicated damages-primary analysis (the secondary rides primary-driven refinement nodes) | 1e-4 relative (bounds the quadrature mass-accounting residual on both sides — measured ≈ 3e-6 on means under the earlier midpoint-trapezoid partition, since replaced by the recorded-mass ledger) | ✅ |
| 4 | `Test_Ensemble_CrossModelParity_And_WithinRunIdentity` | Cross-model ensemble parity on the primary axis (adding a consequence function legitimately re-rolls seeds → statistical) + within-run per-type failure-probability identity on every realization | 4·√(SE₁² + SE₂²); 1e-12 relative | ✅ |
| 5 | `Test_AdditiveSystem_SecondaryConvolutionIdentity` | Two-component additive: the secondary system Total mean equals the sum of the component secondary means (the convolution identity) and the failure union is shared verbatim across types | 1e-6 relative; 0 (bit) | ✅ |
| 6 | `Test_JointSystem_SecondaryMatchesAdditive` | Two-component joint vs additive secondary system means on strictly independent components + per-type failure-mass identity within the joint run | 1e-2 relative (VEGAS mean-only error at the default budget); 1e-12 relative | ✅ |

## Fixed-model invariance (the companion evidence)

The closure itself was proven engine-inert for existing models at the code level: for any fixed
model, enabling K > 1 evaluation consumes **no** new RNG draws (the coupling matrix has carried
K columns from its introduction, and consequence functions are outside the seeded sampler
walk), so the `EngineReproducibilityVerification`, `SingleComponentMeanParityVerification`, and
`ExactLecTailVerification` families — and the full pinned suite — passed **unchanged** when the
axis landed. That zero-pin-change run is the recorded invariance gate for the closure.

## Notes

- Secondary types report `ConsequenceThresholdProbability` (assurance) as NaN when only the
  analysis-level threshold is set — it is declared in the primary type's units and cannot be
  evaluated on another type's axis. A secondary type evaluates a threshold only when its own
  declared per-type `ConsequenceThreshold` supplies one.
- Per-type percentile curves assemble on per-type consequence grids — types live on different
  magnitude scales (lives versus dollars), so a shared grid would starve one axis.
- Runtime at these settings: ≈ 50 s for the family (dominated by the two N = 400 ensembles of
  Scenario B and the 10⁶-draw oracle).
