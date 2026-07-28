# Event-tree response — Phase 10A foundation, links, authoring, and recursion

## Scope and status

`EventTreeVerification` verifies the numerically observable behavior from the foundation,
independent-link, and recursive-source Phase 10A slices: controlled scalar/tabular event trees,
legacy conditional-probability algebra, aggregate failure, exhaustive terminal outputs, indexed
LHS table sampling, internal/external `IndependentClone` links, direct and multi-level nested
`EventTreeResponse` probability sources, both serialization modes, and occurrence reproducibility.
The third slice's authoring-only fragment, mutation, and topology APIs remain covered by fast
structural parity and rollback tests. This is a partial family, not the Phase 10A exit gate. Legacy
recursive XML/templates, graph-connected per-leaf consequences, independent branch-routing Monte
Carlo, aggregate LHS variance reduction, thread-count reproducibility, and performance gates are
still open.

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

Observed 2026-07-28: **7/7 passed**.

| Fixture | Independent expectation | Result |
|---|---|---|
| Deep/wide tree | `0.1 + 0.6×0.25 + 0.6×0.15 = 0.34`; every hazard's branch mass = 1 | Exact within `1e-14` |
| Over-allocated siblings | failure = `0.8/(0.8+0.7)`; remainder = 0 | Exact within `1e-14` |
| Indexed uncertainty | 256 realization outputs equal the aligned `UncertainOrderedPairedData.CurveSample(p)` at the response's recorded LHS percentile | Bit-equal |
| Linked subtree parity | internal link and external link after self-contained/by-reference round trip equal an explicitly cloned tree | Bit-equal branch/aggregate curves and canonical hashes |
| Linked uncertainty reproducibility | two independent occurrences reproduce across XML modes and sibling reordering | Bit-equal for 256 realizations; occurrences remain distinct |
| Multi-level nested analytic response | deepest `0.2→0.6` response is Normal-Z interpolated at caller hazard `h=1` and multiplied by the explicit `0.6` load path | Exact within `1e-14` at all three caller hazards |
| Multi-level nested LHS | 256 outer indexed results equal direct deepest-table `CurveSample(p)` draws followed by the independent Normal-Z caller-hazard interpolation | Exact within `1e-14` realization-for-realization |

The unit suite additionally pins direct and multi-level analytic and percentile parity; exact
recursive `SamplingDimensions` through event trees, ordinary responses, uncertain tables, and
internal/external links; independent repeated nested occurrences; nested indexed/LHS
reproducibility; metadata/GUID/name/order/serialization/reference-wrapper hash and seed invariance;
nested compute sensitivity; both XML modes with resolver ID/name fallback and reference repair;
unresolved nested-reference diagnostics; direct, indirect, mixed source/link, and cross-function
cycle paths; and exact sampler-state rollback after failed recursive compilation or capacity setup.
The authoring slice's fast tests cover immutable fragments, fresh-ID paste with local-reference
remapping, replace/materialize/delete-materialize/prune operations, deterministic expanded
topological/reference inspection, and failed-mutation rollback of XML topology, IDs, output ports,
