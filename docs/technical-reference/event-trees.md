# Event-Tree Responses

> Technical reference for `RMC.TotalRisk.RiskFunctions.Responses.EventTrees` (`EventTreeResponse`,
> `EventTree`, the event-node types, `ProbabilitySource`, `LegacyEventTreeConverter`) and the shared
> tree contracts in `RMC.TotalRisk.RiskFunctions.Responses.Trees` (`TreeFragment`,
> `TreeNodeReference`, `TreeLinkMode`, `TreeDeletePolicy`, the response-branch contracts).
> Normative design:
> [../requirements/EVENT_AND_FAULT_TREE_RESPONSE_DESIGN.md](../requirements/EVENT_AND_FAULT_TREE_RESPONSE_DESIGN.md).
> Executable evidence: [../verification/event-tree.md](../verification/event-tree.md).

An `EventTreeResponse` is a **response function**: it produces conditional fragility `P(F|h)` under
the exact `IResponseFunction` contract, and nothing else — hazard probability, annualization,
consequences, and risk stay outside the tree, owned by the component graph and `RiskAnalysis`. The
response owns a finite, strictly ascending hazard axis and a controlled `EventTree` whose chance
nodes carry conditional branch probabilities; between axis knots the aggregate and per-branch
curves follow the established response-CDF interpolation/extrapolation policy (no alternate
interpolator exists).

## Node algebra

The tree is `InitiatingNode` → `ChanceNode` children (plus at most one `RemainderNode` per sibling
set and `EventTreeLinkNode` references). Explicit siblings keep their **legacy conditional
meaning**: with raw probabilities `q_i` and compensated sum `S`,

```
p_i = q_i        and p_remainder = 1 − S,   when S ≤ 1
p_i = q_i / S    and p_remainder = 0,       when S > 1
```

A terminal's probability is the product of its conditional path, so terminals are mutually
exclusive end states. Aggregate `P(F|h)` is the compensated sum of the terminals explicitly
classified as failure; remainder mass omitted from the authored tree is surfaced as a **stable
implicit non-failure branch** rather than silently dropped. Sibling compensated-sum order,
normalization only above one, and residual assignment are pinned contracts.

## Probability sources

Each chance node carries a `ProbabilitySource` of one of three kinds (`ProbabilitySourceKind`):

| Kind | Value | Evaluation |
|---|---|---|
| `DeterministicScalar` | a fixed conditional probability | constant across hazards |
| `UncertainTabular` | a co-monotonic uncertain probability table aligned to the tree hazards | one knowledge percentile drives every ordinate |
| `ResponseFunctionReference` | any `IResponseFunction` — including another `EventTreeResponse`, at arbitrary acyclic nesting depth | evaluated at the owning response's current hazard ordinate through `SampleFunction(...).CDF(h)` |

Referenced responses may be held inline (self-contained) or by reference through an
`IRiskFunctionResolver`. Structural, source, and mixed source/link cycles — same-tree and
cross-function — are rejected transactionally with the complete deterministic function/node path
in the diagnostic.

## Links and occurrences

`EventTreeLinkNode` references another node's subtree, internal or external. Event trees admit
only `TreeLinkMode.IndependentClone`: the referenced subtree is evaluated as a **distinct
occurrence** with its own epistemic sampler stream (`SharedLogicalEvent` — the same Boolean event —
is reserved for fault trees). Repeated references to one live source therefore draw independently,
each canonical nested occurrence seeded from the established source-identity/occurrence recipe.
Deterministic expanded queries (topological order, unreachable/internal/external references) run on
the link-expanded view.

## Sampling

Setup recursively discovers sampler dimensions through nested event trees, ordinary responses,
aligned uncertain tables, and independent-clone links, then copies each child occurrence's exact
percentile columns into the owner's flattened sampler. Mean, co-monotonic percentile, and indexed
LHS sampling are preserved through arbitrary nesting depth. A failed recursive compile, clone,
capacity check, or child setup restores the owner's prior sample size, percentile matrix, sampler
identity, and occurrence bindings exactly.

## Authoring and rollback

`EventTree` owns controlled add, insert, move, replace, delete, search, ancestry, reachability,
traversal, and structural-equality operations. `TreeFragment` is an immutable subtree snapshot for
copy/paste — **authoring-only state**, neither persisted nor hashed; pasting assigns fresh
persistent IDs and remaps fragment-local links while cross-tree references retain live external
targets. Deletion takes a `TreeDeletePolicy`: `RejectIfReferenced` (default), `CascadeLinks`, or
`MaterializeLinks`; unreachable-node pruning is explicit. Every controlled operation is
transactional — a failed edit restores topology, IDs, child order, output-port allocation,
connections, canonical hash, configured samplers, and the compiled caches exactly.

## Serialization — both modes, plus legacy import

v1.1 writes only the explicit node/edge graph form, in both `RiskSerializationMode`s:
self-contained sources serialize inline; by-reference sources write `FunctionReference` markers a
resolver re-attaches to live stored instances, with lenient function/node name fallback and stable
IDs repaired on the next write. The constructor additionally **imports** the v1.0 recursive `Node`
XML (direct, or inside the released `EventTreeResponse`/`EventTree` envelopes;
`HazardLevels`/`HazardIntervals` and both GUID spellings; scalar, compact/table, and name-only
response sources; the legacy automatic remainder), converting through
`LegacyEventTreeConverter` with deterministic path diagnostics for malformed, excluded, duplicate,
or semantically unmappable input. A legacy `EventNode` probability reference means reuse of a
probability source, not a subtree — mapping it to `IndependentClone` would change semantics, so
the converter rejects it explicitly.

## Identity and seeding

Canonical identity is **projected**: nested response content and a selected expanded branch's
metadata-free compute-occurrence path participate in the hash; persistent IDs, display names,
sibling presentation order, output ports, serialization mode, and reference wrappers are projected
away. Renamed, reordered, or materialized connections are hash- and seed-inert; a changed selected
path probability moves identity.

## The compiled plan

Each instance publishes one immutable occurrence/evaluation plan (canonical parent-before-child
primitive-index instructions, leaves, projected identity, sampler dimensions, recursive dependency
snapshots) behind a per-instance lock and volatile reference; concurrent read-only callers share
the published objects, and the hot path is a single linear pass over compiled edges with reusable
primitive work arrays. Controlled edits invalidate only after tentative expanded-cycle validation
succeeds; compute revisions propagate immediately, and canonical fingerprints defensively detect
silent in-place edits to mutable Numerics tables or live resolver-backed responses. Names,
descriptions, IDs, and hazard labels/units are cache-inert. Branch-address preparation is
separately checkpointed and lazy, so identity/hash-only reads allocate no ports.

## Expanded graph outputs

The default graph view is exactly port `0 = Fail`, `1 = Non-Fail`. Opt-in expanded output exposes
every arbitrary n-way terminal: port `2` is the stable implicit-unmodeled non-failure branch, and
direct/linked terminal ports allocate **append-only from 3** — never renumbered by rename,
metadata edits, sibling reorder, copy/paste, materialization, pruning, or XML round trip.
`RiskConnection` and `ResponseStage` persist branch ID as authority with the exact current name as
a migration fallback. `ComponentGraph.DeleteEventTreeNode` defaults to rejecting a delete that
would remove a connected terminal; cascade clears only slots selecting that branch; materialize
succeeds only when exact link materialization retains the same branch ID and port — the graph
never fabricates an equivalent-looking replacement. The expanded and aggregate views are mutually
exclusive, and per-leaf compute rides projected per-leaf hash/seed identity.

## Future work

The exact static fault-tree capability (`FaultTreeResponse`, `SharedLogicalEvent` references,
Boolean gate evaluation) is specified in the normative design and not yet implemented.
