# Life cycle

**Test class:** `LifeCycleVerification` · **Tests:** 5 · **Run of record:** 2026-09-06, isolated run, ✅ all passed

## Scope and status

The greenfield family for the life-cycle foundations. This slice verifies the age-indexed
deteriorating response: the transform-equivalence bit-oracle across ages, realization-for-
realization knowledge parity on one content-seeded stream, the age-zero base identity, an
engine-level mean-only twin against the re-authored base-plus-shift-transform model, and the
full-uncertainty reproducibility pin.

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~LifeCycleVerification"
```

Observed 2026-09-06: **5/5 passed** (≈ 2 s).

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
