# Life cycle

**Test class:** `LifeCycleVerification` · **Tests:** 10 · **Run of record:** 2026-09-06, isolated run, ✅ all passed

## Scope and status

The greenfield family for the life-cycle foundations, in two halves. The deteriorating
response: the transform-equivalence bit-oracle across ages, realization-for-realization
knowledge parity on one content-seeded stream, the age-zero base identity, an engine-level
mean-only twin against the re-authored base-plus-shift-transform model, and the
full-uncertainty reproducibility pin. The trajectory query: the stationary bridge onto the
exposure-period conversions, the two-epoch closed form with independent annuity and survival
arithmetic, per-epoch re-authored configuration twins, the deterioration-monotone/
intervention-drop trajectory shape, and the author byte pin.

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~LifeCycleVerification"
```

Observed 2026-09-06: **10/10 passed** (≈ 12 s).

## The equivalence oracle

A deteriorating response at age t is exactly its base behind a deterministic unit-slope
`LinearTransform` with intercept Δ(t) and wide bounds: with slope one the transform computes
Δ + h and the wrapper computes h + Δ, bit-identical in IEEE arithmetic, and both paths
evaluate the same sampled base product. Every assert in this family is therefore **bit-exact
(no-delta equality)** — the oracles are algebraic identities of the same floating-point
operations, so any deviation is a defect, never statistical noise.

Fixtures: a tabulated Normal(150, 20) capacity fragility over stages −10 to 310 at unit steps
(deterministic, and an uncertain variant with per-ordinate Uniform(p ± 0.05) clamped to
[0, 1]); the deterioration law ages {0, 25, 50, 100} → shifts {0, 5, 12, 30} (deterministic,
and an uncertain variant pinning age zero at an exact zero via the degenerate uniform with
later shifts Uniform(0.8Δ, 1.2Δ)); probe ages {0, 10, 25, 60, 150} (the age-zero identity,
interpolated ages, a knot, and the boundary hold); probe hazards spanning 60–260. The engine
fixture drives a five-ordinate deterministic stage-frequency hazard (exceedance 0.999–0.001
over stages 60–260) and a five-knot deterministic stage-damage consequence through one
failure mode plus the non-failure complement.

| Test | Independent expectation | Result |
|---|---|---|
| `Test_FunctionLevel_TransformEquivalence_BitExact_AcrossAges` | Wrapper CDF probes equal base ∘ authored unit-slope shift transform at every probe age and hazard, deterministic and uncertain-at-mean, no delta | ✅ bit-exact |
| `Test_FunctionLevel_UncertaintyParity_RealizationForRealization_OneStream` | After one sampler setup (64 realizations, Latin hypercube, one seed), every realization at every age equals the independent reconstruction base_i.CDF(h + Δ_i(age)) from the wrapper's own sampled law percentile | ✅ bit-exact |
| `Test_FunctionLevel_AgeZero_EquivalentToBase` | With a zero shift at age zero, the wrapper reproduces its base realization for realization | ✅ bit-exact |
| `Test_Engine_MeanOnlyTwin_BitExact` | The wrapper model at age 50 reproduces the re-authored base-plus-stage-transform twin — annual failure probability, expected annual consequence, and the loss-exceedance ordinate arrays — with no delta; the aged model strictly exceeds the undegraded baseline | ✅ bit-exact |
| `Test_Engine_Reproducibility_SameSeedBitIdentical` | The full-uncertainty wrapper scenario run twice from independently built models publishes byte-identical ensemble and mean JSON | ✅ byte-identical |
| `Test_LifeCycle_StationaryMatchesExposurePeriod_BitExact` | A stationary trajectory on an all-deterministic model (discipline pinned, the smallest legal ensemble) collapses every `MeasureExposurePeriodRisk` percentile slot onto the life-cycle aggregates with no delta, the mean slot within its documented summation-rounding bound, at 3.5% and 0% discounting | ✅ bit-exact (percentiles) / 1e-13 rel (means) |
| `Test_LifeCycle_TwoEpochClosedForm_Exact` | The flat OR(AND(house, 0.375), 0.2) tree configured at year 10 of 20: probabilities 0.2 and exactly 0.5 (1e-10), the factored consequence ratio 2.5 (1e-10), the horizon aggregates against independent power-form annuities and a per-year survival loop (1e-12 rel), and the undiscounted PV ≡ cumulative identity with no delta | ✅ within documented tolerances |
| `Test_LifeCycle_EpochsMatchReauthoredTwins_BitExact` | Baseline, configured-house, and configured-house-plus-replacement epochs each equal a directly re-authored mean-only model of that cumulative state — system and component scope, no delta | ✅ bit-exact |
| `Test_LifeCycle_DeteriorationMonotone_InterventionDrops` | Under the monotone weakening law the epoch failure probabilities never decrease (strictly rising into the first aged epoch); a milder replacement hazard at year 20 leaves earlier epochs bit-untouched and strictly drops every later one | ✅ shape verified |
| `Test_LifeCycle_AuthorFullRun_ByteUntouched` | A 200-realization published run's ensemble JSON, component hashes, authored references, ages, and house states are byte-identical after a trajectory query exercising a house event, a hazard replacement, and per-epoch ages | ✅ byte-identical |

## Conventions and limitations

- The engine twin pins its consequence input to the raw hazard (`ConsequenceHazardPosition = 0`):
  the capacity shift is internal to the response path, while the last-response-input default
  would bind the twin's consequences to the transformed signal — a genuinely different model.
- The twins' differing canonical hashes and seeds are provably inert because every engine
  fixture function is deterministic and the twin runs are mean-only (the configuration-risk
  family's twin discipline).
- A run's isolated component snapshot carries the authored `EvaluationAge` onto its cloned
  instances by function id — the age is runtime-only state a serialization clone alone would
  reset; the engine twin exercises exactly this carry.
- The uncertain law pins age zero with the degenerate `Uniform(0, 0)` ordinate so the age-zero
  identity holds realization for realization while the table stays homogeneous in its declared
  distribution type.
- The stationary bridge cannot use fewer than one hundred realizations (the engine's floor),
  so its mean slots reduce one hundred bit-identical values: the percentile slots interpolate
  identical order statistics exactly, while the mean's sequential summation admits rounding of
  order the count times machine epsilon — asserted at 1e-13 relative and documented in the
  family remarks.
- The trajectory query is deliberately mean-only (the configuration-risk discipline): a
  configured state is compute content, so per-epoch full-uncertainty ensembles would re-roll
  every configured function's stream; the closed forms and twins here are exact because every
  engine fixture function is deterministic.
