# Event-tree response - Phase 10A foundation through graph integration

## Scope and status

`EventTreeVerification` now verifies the numerically observable behavior from the first six
Phase 10A slices: controlled scalar/tabular event trees, legacy conditional-probability algebra,
aggregate failure, exhaustive terminal outputs, indexed LHS table sampling, internal/external
`IndependentClone` links, direct and multi-level nested `EventTreeResponse` probability sources,
both serialization modes, occurrence reproducibility, recursive v1.0 XML conversion and shipped
templates, and graph-connected arbitrary n-way per-leaf consequences. The authoring-only fragment,
mutation, stable-port, stale-connection, and topology APIs are covered by fast structural parity
and rollback tests. This is a partial family, not the Phase 10A exit gate.

Still open are immutable compiled-plan caching/invalidation; large-tree performance;
property-based testing; independent branch-routing Monte Carlo; aggregate LHS variance reduction;
and the remaining thread-count, coverage, and performance exit gates.

The response computes conditional fragility `P(F|h)` only. Hazard probability, annualization,
consequences, and risk remain outside the event tree.

## Probability oracle

For explicit siblings with raw conditional probabilities `q_i`, let `S` be their compensated sum.
The implemented and verified legacy rule is

```text
p_i = q_i                  when S <= 1
p_i = q_i / S              when S > 1
p_remainder = 1 - S        when S <= 1
p_remainder = 0            when S > 1
```

The probability of terminal path `L` is the product of its conditional branch probabilities.
Aggregate failure is the compensated sum of the path probabilities whose terminal descriptors are
classified `IsFailure = true`. When no authored remainder exists, unassigned mass is emitted as a
stable implicit non-failure branch so terminal probabilities remain exhaustive.

## Legacy authority and conversion boundary

The conversion was audited against these Dev-repository authorities:

- partial C#: `RMC.TotalRisk.IO/Project/Elements/Response Function/EventTreeResponse.cs` and its
  `Event Nodes` types;
- released VB: `RMC.TotalRisk/Project/Elements/Response Function/EventTreeResponse.vb` and its
  `Event Nodes` types;
- shipped templates: `RMC-TotalRisk/Resources/TreeTemplates.xml`; and
- legacy smoke test: `Test_TotalRisk/Test_EventTree.vb::TestIO`.

The shipped template audit found 29 roots, 466 total nodes, and 219 chance nodes: 208 use
`MultiValue`, 11 use `SingleValue`, and none uses `ResponseFunction`, `EventNode`, or structural
links. The committed [fixture manifest](../../src/RMC.TotalRisk.Verification/Data/MANIFEST.md)
records the exact `Basic` and `Concrete Dam Gate Failure` roots copied for verification.

The import-only adapter accepts a direct recursive `Node`, an `EventTreeResponse/Node` envelope,
or an `EventTreeResponse/EventTree/Node` envelope. It accepts both `HazardLevels` and
`HazardIntervals`, both `NodeGuid` and `NodeGUID`, legacy `SingleValue` and `MultiValue` encodings,
and name-only `ResponseFunction` sources. Resolver-backed imports repair the response ID on the
next current-format write. It preserves valid IDs, derives deterministic path IDs when absent,
adds the legacy automatic remainder when omitted, and emits only the explicit-node/edge v1.1 form.

A legacy `EventNode` probability source means reuse of another node's probability source, not
reuse of a subtree. Mapping it to Phase 10A `IndependentClone` would change semantics, so the
converter rejects it with a deterministic path diagnostic. The commented `SecondaryHazardNode`
remains excluded, and `WeightedHazardLevel` remains Phase 11 bivariate-response work.

No value from legacy `Test_EventTree.Test_Product` is used as an oracle: that method has no
assertion and does not construct an event tree.

## Fixtures and results

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~EventTreeVerification"
```

Observed 2026-07-28: **11/11 passed**.

| Fixture | Independent expectation | Result |
|---|---|---|
| Deep/wide tree | `0.1 + 0.6 x 0.25 + 0.6 x 0.15 = 0.34`; every hazard's branch mass = 1 | Exact within `1e-14` |
| Graph-connected n-way leaves | Five terminal consequences independently expect `0.10`, `0.15`, `0.09`, `0.36`, and `0.30`; failure leaves sum to aggregate `0.34` | Exact within `1e-14` per leaf and aggregate |
| Over-allocated siblings | failure = `0.8/(0.8+0.7)`; remainder = 0 | Exact within `1e-14` |
| Indexed uncertainty | 256 realization outputs equal the aligned `UncertainOrderedPairedData.CurveSample(p)` at the recorded LHS percentile | Bit-equal |
| Linked subtree parity | Internal and external links after self-contained/by-reference round trip equal an explicitly cloned tree | Bit-equal branches, aggregate curves, and canonical hashes |
| Linked uncertainty reproducibility | Two independent occurrences reproduce across XML modes and sibling reordering | Bit-equal for 256 realizations; occurrences remain distinct |
| Multi-level nested analytic response | Deepest `0.2` to `0.6` response is Normal-Z interpolated at caller hazard `h=1` and multiplied by the explicit `0.6` path | Exact within `1e-14` at all three caller hazards |
| Multi-level nested LHS | 256 indexed results equal direct deepest-table samples followed by independent Normal-Z caller-hazard interpolation | Exact within `1e-14` realization-for-realization |
| Legacy `TestIO` Basic shape | Recursive direct-root XML converts to canonical v1.1 XML and matches an independent terminal path-product oracle | Exact terminal and aggregate parity |
| Shipped `Basic` and `Concrete Dam Gate Failure` | Every converted terminal and aggregate equals an independent recursive oracle with compensated sibling sums and residual/normalization rules | Exact within `1e-14` |
| Legacy nested response | Name-only response resolution, ID repair, caller-hazard interpolation, 256 LHS realizations, and both XML modes match an independent oracle | Exact within `1e-14` realization-for-realization |

## Fast-suite coverage

The fast suite additionally pins direct and multi-level analytic and percentile parity; exact
recursive `SamplingDimensions`; independent repeated occurrences; nested indexed/LHS
reproducibility; metadata/GUID/name/order/serialization/reference-wrapper hash and seed invariance;
both XML modes with resolver ID/name fallback and repair; complete structural/source/mixed cycle
paths; and exact sampler-state rollback. Authoring tests cover immutable fragments, fresh-ID paste
with local-reference remapping, replace/materialize/delete-materialize/prune operations,
deterministic expanded topology/reference inspection, and failed-mutation rollback of topology,
IDs, output ports, hashes, and configured samplers. Graph tests additionally cover opt-in output
discovery for arbitrary n-way splits/remainders, branch-ID/name/port migration, both graph XML
modes and live resolver reuse, rename/metadata/reorder/copy-paste/materialize/prune stability, every
connection slot, stale diagnostics, all three deletion policies, observer-failure rollback, exact
per-leaf mean/indexed projection and grouping, plus canonical hash/seed invariance and selected-path sensitivity.

`LegacyEventTreeConversionTests` adds the exact `TestIO` direct-factory import; both wrapper forms;
both hazard/GUID spellings; scalar, compact/table, ordinary-response, and nested-response sources;
current-only self-contained/by-reference writes with repaired IDs; metadata, ID, order, hash, and
seed invariance; deterministic malformed/missing/excluded/unsupported diagnostics; and proof that
failed conversion or converted-source cycle detection leaves an already configured live sampler
unchanged.
