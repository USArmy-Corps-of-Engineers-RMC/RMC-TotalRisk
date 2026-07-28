# Event-tree response — Phase 10A first vertical slice

## Scope and status

`EventTreeVerification` currently verifies the first coherent Phase 10A implementation slice:
controlled scalar/tabular event trees, legacy conditional-probability algebra, aggregate failure,
exhaustive terminal outputs, and indexed LHS table sampling. This is a partial family, not the
Phase 10A exit gate. Internal/external links, legacy recursive XML, graph-connected per-leaf
consequences, independent branch-routing Monte Carlo, LHS variance reduction, compiled-plan
performance, and the remaining manipulation surface are still open.

The response computes conditional fragility `P(F|h)` only. Hazard probability, annualization,
consequences, and risk remain outside the event tree.

## Probability oracle

For explicit siblings with raw conditional probabilities `q_i`, let `S = Σq_i`, computed with
compensated summation. The implemented and verified legacy rule is

```
p_i = q_i                  when S <= 1
p_i = q_i / S              when S > 1
p_remainder = 1 - S        when S <= 1
p_remainder = 0            when S > 1
```

The probability of terminal path `L` is the product of its conditional branch probabilities.
Aggregate failure is the compensated sum of the path probabilities whose terminal descriptors are
classified `IsFailure = true`. When no authored remainder exists, unassigned mass is emitted as a
stable implicit non-failure branch so terminal probabilities remain exhaustive.

No value from legacy `Test_EventTree.Test_Product` is used as an oracle: that method has no
assertion and does not construct an event tree.

## Fixtures and results

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~EventTreeVerification"
```

Observed 2026-07-28: **3/3 passed**.

| Fixture | Independent expectation | Result |
|---|---|---|
| Deep/wide tree | `0.1 + 0.6×0.25 + 0.6×0.15 = 0.34`; every hazard's branch mass = 1 | Exact within `1e-14` |
| Over-allocated siblings | failure = `0.8/(0.8+0.7)`; remainder = 0 | Exact within `1e-14` |
| Indexed uncertainty | 256 realization outputs equal the aligned `UncertainOrderedPairedData.CurveSample(p)` at the response's recorded LHS percentile | Bit-equal |

The unit suite additionally pins nested path products, implicit residual mass, percentile samples,
same-content LHS reproducibility, metadata/GUID/order hash invariance, compute sensitivity,
self-contained/by-reference response-source round trips, validation, factory registration, and the
controlled authoring/traversal surface.
