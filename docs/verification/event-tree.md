# Event-tree response — Phase 10A foundation and independent-link slices

## Scope and status

`EventTreeVerification` verifies the numerically observable behavior from the first two coherent
Phase 10A slices: controlled scalar/tabular event trees, legacy conditional-probability algebra,
aggregate failure, exhaustive terminal outputs, indexed LHS table sampling, internal/external
`IndependentClone` links, both serialization modes, and linked-occurrence reproducibility. The
third slice adds authoring-only fragment, mutation, and topology inspection APIs. Fast tests cover
that structural surface and prove numerical parity and exact rollback, so no new verification
oracle was introduced. This remains a partial family, not the Phase 10A exit gate. Legacy recursive
XML/templates, graph-connected per-leaf consequences, independent branch-routing Monte Carlo, LHS
variance reduction, thread-count reproducibility, and performance gates are still open.

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

Observed 2026-07-28: **5/5 passed**.

| Fixture | Independent expectation | Result |
|---|---|---|
| Deep/wide tree | `0.1 + 0.6×0.25 + 0.6×0.15 = 0.34`; every hazard's branch mass = 1 | Exact within `1e-14` |
| Over-allocated siblings | failure = `0.8/(0.8+0.7)`; remainder = 0 | Exact within `1e-14` |
| Indexed uncertainty | 256 realization outputs equal the aligned `UncertainOrderedPairedData.CurveSample(p)` at the response's recorded LHS percentile | Bit-equal |

| Linked subtree parity | internal link and external link after self-contained/by-reference round trip equal an explicitly cloned tree | Bit-equal branch/aggregate curves and canonical hashes |
| Linked uncertainty reproducibility | two independent occurrences reproduce across XML modes and sibling reordering | Bit-equal for 256 realizations; occurrences remain distinct |
The unit suite additionally pins nested path products, implicit residual mass, percentile samples,
same-content LHS reproducibility, independent repeated-source sampler occurrences,
metadata/GUID/name/order/reference-wrapper hash invariance, compute sensitivity, linked external
hazard-axis interpolation, resolver-backed self-contained/by-reference round trips and ID/name
repair, useful full-path cross-function cycle diagnostics, validation, and factory registration.
The third slice's fast tests cover immutable fragment snapshots; fresh-ID paste with local-reference
remapping; replace, link materialization, delete-materialization, and unreachable pruning;
deterministic expanded topological/internal/external reference inspection; cross-tree links; and
failed-mutation rollback of XML topology, IDs, output ports, canonical hash, and sampler results.
