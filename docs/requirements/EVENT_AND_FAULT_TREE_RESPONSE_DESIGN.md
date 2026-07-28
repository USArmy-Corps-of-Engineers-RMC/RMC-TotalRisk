# Event-Tree and Fault-Tree Response Functions

> **Status:** Normative implementation design, approved for Phases 10A and 10B (2026-07-28).
> **Applies to:** `RMC.TotalRisk.dll`, its fast unit tests, and `RMC.TotalRisk.Verification`.
> **Authority:** This document specializes, but does not replace, [MODEL_LIBRARY_ARCHITECTURE.md](MODEL_LIBRARY_ARCHITECTURE.md). If an implementation discovery would change a probability rule, sampling rule, canonical-hash contract, or reference-result contract described here, stop and obtain Haden Smith's approval before changing the design.

## 1. Purpose and responsibility boundary

`EventTreeResponse` and `FaultTreeResponse` are response functions. Given a hazard intensity `h` and, when requested, one epistemic realization, each produces a fragility relationship:

`P(F | h) = probability of the modeled failure state conditional on hazard level h`.

They do **not** assign an annual probability to `h`, integrate a hazard curve, attach consequences, or compute risk. Those responsibilities remain separated:

1. an `IHazardFunction` supplies hazard probability or frequency;
2. zero or more `ITransformFunction`s map hazard quantities;
3. an event-tree or fault-tree `IResponseFunction` supplies conditional failure probability;
4. `RiskConnection`s in a `ComponentGraph` connect the response to consequences and other component stages; and
5. `RiskAnalysis` combines the connected hazard, response, and consequence functions to calculate risk.

Calling a tree a “probability model” in this document always means a conditional response model. A tree must not acquire an AEP, return period, hazard density, annualized failure rate, consequence, or risk API.

### 1.1 Goals

- Port the useful v1.0 event-tree response behavior into the headless v1.1 model library.
- Add a static fault-tree response function when the event-tree foundation is stable.
- Support conditional branch/end-state output as well as the ordinary aggregate failure/non-failure response contract.
- Make internal and external references explicit, deterministic, cycle-safe, serializable, and useful for authoring repeated branches.
- Provide copy, paste, add, insert, delete, move, replace, and link operations without exposing mutable collection invariants to callers.
- Add topological ordering, deterministic traversal, search, reachability, pruning, and structural validation.
- Use the engine's Latin hypercube sampling (LHS), deterministic content-derived seeds, and posterior-index sampling conventions.
- Improve performance and numerical robustness without reducing accuracy or silently changing the legacy probability rules that are retained.
- Supply full unit, verification, reproducibility, serialization, performance, and traceability coverage.

### 1.2 Non-goals

- UI controls, canvas coordinates, commands, clipboard formats, dialogs, project stores, and direct file I/O.
- Hazard-frequency or risk computation inside either tree.
- The unused legacy `SecondaryHazardNode` chain.
- `BivariateResponse` or `WeightedHazardLevel`; those remain separate Phase 11 work.
- Dynamic fault trees, time-to-failure simulation, repair/availability, standby/spare gates, sequence-dependent gates, Markov models, or common-cause failure models.
- Automatically treating repeated event-tree links as shared physical events. Event-tree reuse is an independent clone unless a future, separately approved dependency feature says otherwise.
- Approximate fault-tree cut-set truncation as a production probability algorithm.

## 2. Legacy audit and porting disposition

The implementation must use the legacy repositories as evidence, not copy them blindly. The primary sources are:

| Legacy source | What it establishes | Porting disposition |
|---|---|---|
| `RMC.TotalRisk.IO/Project/Elements/ResponseFunctions/EventTreeResponse.cs` | Partial C# class shape, sampling lifecycle, XML conventions | Port behavior after cross-checking VB; remove UI/store coupling and per-sample cloning |
| `RMC.TotalRisk/Project/Elements/ResponseFunctions/EventTreeResponse.vb` | Released v1.0 behavior | Reference-result authority unless this document records a deliberate improvement |
| `.../EventTrees/Nodes/InitiatingNode`, `ChanceNode`, `RemainderNode`, `EventNodeBase`, `IEventNode` | Recursive tree, branch probabilities, normalization, and leaf aggregation | Reshape behind controlled graph APIs; retain probability semantics |
| `.../EventTrees/ChanceSources/*` | scalar, uncertain table, response-function, and event-node probability sources | Replace with a discriminated source model and explicit structural links |
| `Test_TotalRisk/Test_EventTree.vb` | Round-trip smoke test and a method named `Test_Product` | Not an asserted compute oracle; use only as a fixture source |
| the 29 shipped `.tra` templates | Real authoring shapes and depth/size evidence | Convert representative fixtures; do not infer unused capabilities from absent instances |

The audited templates contain 466 event nodes across 29 trees, with a largest observed tree of 33 nodes and depth 10. Their chance values use direct single-value and multi-value sources; none exercises event-node or response-function references. This makes reference behavior a required new verification area rather than a legacy-oracle parity claim.

### 2.1 Legacy event-tree behavior to retain

- Each chance child contributes a conditional branch probability at the current hazard level.
- When the sum `S` of explicit sibling chance probabilities exceeds one, explicit probabilities are proportionally normalized by `S`.
- A remainder child receives the residual probability after the explicit chance children; when the explicit sum is at or above one, the remainder is zero.
- A leaf path probability is the product of its conditional branch probabilities.
- Failure probability is the sum of the failure-leaf path probabilities; remainder leaves are non-failure leaves by default.
- Scalar, uncertain tabular, referenced response-function, and repeated-node sources are recognized concepts.
- A mean curve, percentile curve, and indexed realization can be sampled through the `IResponseFunction` contract.

### 2.2 Deliberate improvements over v1.0

- Compile the authored graph once and evaluate it iteratively; do not deep-clone the tree for every realization or create local `Random` instances.
- Use the shared sampler lifecycle and LHS/posterior indices instead of ad hoc uniform random draws.
- Address nodes and links with stable identities rather than display names alone.
- Replace recursive public mutation with transactional authoring methods and deterministic diagnostics.
- Detect cycles before compute and report the complete link path.
- Evaluate aggregate probability in one traversal, while optionally recording leaf weights, rather than rewalking from every leaf.
- Use compensated summation and explicit finite/range checks.
- Serialize through `RiskSerializationMode` and preserve hash/seed invariance under renaming, reordering, and reference-mode round trips.
- Distinguish structural independent-clone links from shared logical fault events; the legacy `ChanceSource.EventNode` ambiguity must not survive in the new public model.

### 2.3 Secondary-hazard confirmation

`SecondaryHazardNode` is not active v1.0 event-tree behavior. The class and its related code paths are commented out in both the VB.NET implementation and the partial C# port, no live event-tree factory creates it, no shipped template contains it, and no active legacy test exercises it. It is therefore excluded.

`WeightedHazardLevel` must not be deleted or described as dead event-tree scaffolding. It is actively used by legacy `BivariateResponse`; it belongs to the separate bivariate-response port and is outside Phases 10A/10B.

## 3. Domain model and namespaces

The common implementation belongs under `RMC.TotalRisk.RiskFunctions.Responses.Trees`; event-tree types belong under `.Responses.EventTrees`, and fault-tree types under `.Responses.FaultTrees`. Folder layout mirrors those namespaces.

Every concrete response remains a `ResponseFunctionBase` and therefore an `IResponseFunction`. Every new concrete response must receive an append-only `ResponseFunctionType` discriminator member, both `RiskSerializationMode` paths, `RiskFunctionFactory` support, canonicalization classification, a ported-types matrix row, and its own fast test class.

### 3.1 Common types

The common tree foundation must provide these concepts. Exact member spelling may change only to avoid a demonstrated C# or existing-API collision; such changes must be reflected here before implementation proceeds.

```csharp
public enum TreeLinkMode
{
    IndependentClone = 0,
    SharedLogicalEvent = 1,
}

public sealed class TreeNodeReference
{
    public Guid? FunctionId { get; }
    public Guid NodeId { get; }
    public string? FunctionName { get; }
    public string? NodeName { get; }
}

public sealed class ResponseBranchDescriptor
{
    public Guid Id { get; }
    public string Name { get; }
    public bool IsFailure { get; }
    public int OutputPort { get; }
}

public interface IBranchingResponseFunction : IResponseFunction
{
    IReadOnlyList<ResponseBranchDescriptor> GetBranches();
    ResponseBranchSample SampleBranches();
    ResponseBranchSample SampleBranches(double percentile);
    ResponseBranchSample SampleBranches(int realizationIndex);
}
```

`ResponseBranchSample` carries a common ascending hazard axis and one conditional-probability ordinate array per branch. It validates that every value is finite and in `[0,1]` and that branch probabilities sum to one within the documented floating-point tolerance at each hazard. It is a typed result, not an `IUnivariateDistribution`, because an individual end-state probability need not be monotonic in hazard.

The common layer also supplies internal compilation and traversal records, immutable diagnostics, a function-reference enumerator for recursive sampler discovery, and canonical path/occurrence assignment. These implementation helpers are not public unless callers need them for authoring or inspection.

### 3.2 Event-tree model

- `EventTreeResponse`: the public response function; owns one `EventTree`, hazard levels, validation, sampling, serialization, and aggregate/branch outputs.
- `EventTree`: the controlled node collection and root identity; owns all authoring, search, traversal, link, pruning, and compilation operations.
- `EventNodeBase`: stable `Id`, display `Name`, metadata, parent/child inspection, and change notification. IDs persist but are stripped from identity hashing.
- `InitiatingNode`: the single root and a structural container, not a hazard-probability node.
- `ChanceNode`: an explicit conditional branch whose value comes from a `ProbabilitySource`.
- `RemainderNode`: the residual sibling branch; at most one per parent and ordered after explicit branches for presentation.
- `EventTreeLinkNode`: a structural reference to an internal or external event-tree subtree. It has only `IndependentClone` semantics in Phase 10A.
- `ProbabilitySource`: a discriminated value source: deterministic scalar, uncertain tabular values aligned to the tree hazard levels, or an `IResponseFunction` reference evaluated at the current hazard level.

Every terminal node has a stable branch descriptor. A chance or link terminal defaults to `IsFailure = true`; a remainder terminal defaults to `false`. Callers may explicitly classify a terminal as failure or non-failure. Classification affects aggregate `P(F|h)` and is compute-relevant. Display labels are metadata.

### 3.3 Static fault-tree model

- `FaultTreeResponse`: the public binary response function; owns one `FaultTree`, hazard levels, validation, sampling, serialization, and the top-event output.
- `FaultTree`: a controlled directed acyclic graph with a single top-event root.
- `FaultTreeGateNode`: `And`, `Or`, `Xor`, or `KOfN`; `K` is required only for `KOfN` and satisfies `1 <= K <= input count`.
- `FaultTreeBasicEventNode`: a Boolean basic event driven by a local probability source or a referenced `IResponseFunction`.
- `FaultTreeHouseEventNode`: a deterministic true/false event.
- `FaultTreeTransferNode`: an internal or external link to a fault-tree node.

Two link meanings are supported for fault trees:

- `SharedLogicalEvent`: all occurrences represent the same Boolean event and therefore the same sampled state in exact evaluation. This is the default for fault-tree transfers and repeated basic-event identities.
- `IndependentClone`: the target structure is cloned logically with a distinct canonical occurrence path and independent epistemic sampling stream. This is available for deliberate repeated-but-independent equipment.

Dynamic gates and common-cause models are outside scope. A model needing those behaviors must fail validation with a specific unsupported-feature diagnostic; it must never be silently approximated as an independent static tree.

## 4. Authoring and manipulation contract

Public collections are read-only views. All mutation goes through `EventTree` or `FaultTree` so invariants, cache invalidation, reference policy, property notification, and diagnostics cannot be bypassed.

| Operation | Required behavior |
|---|---|
| `Add` | Add a new child/input at the end of the requested position and return its stable ID. |
| `Insert` | Insert before a named/identified sibling; presentation order changes, compute identity does not. |
| `Copy` | Produce an in-memory `TreeFragment` snapshot; retain source IDs only for internal-link remapping and do not mutate the tree. |
| `PasteClone` | Deep-copy the fragment, allocate fresh persistent IDs, rewrite all references whose targets were inside the fragment, and preserve external references. |
| `LinkIndependent` | Insert a lightweight reference to the source subtree; compile it as an under-the-hood independent clone with its own occurrence path and sampling stream. This supports building a branch left-to-right once and reusing it vertically. |
| `LinkShared` | Fault trees only: insert a shared-logical reference whose repeated occurrences resolve to the same Boolean variable. Event trees reject it in Phase 10A. |
| `Move` | Reparent/reorder a node transactionally after cycle and type checks. |
| `Replace` | Replace a node while applying the selected child/reference policy explicitly. |
| `Delete` | Require a `RejectIfReferenced`, `CascadeLinks`, or `MaterializeLinks` policy. The default is `RejectIfReferenced`. No dangling link is allowed. |
| `PruneUnreachable` | Remove only nodes unreachable from the root after returning a preview/diagnostic set; link targets count as reachable. |
| `MaterializeLink` | Replace one link with a deep independent clone, preserving branch classification and external function references. |

`Copy`/`PasteClone` is duplication. `LinkIndependent` is reuse that continues to follow the source structure but behaves as an independent clone during sampling and evaluation. This distinction must remain visible in serialization and authoring APIs.

Transactions either complete fully or leave the tree and all caches unchanged. Failed operations return/throw one deterministic diagnostic that includes the operation, source node, target node, and reason. No operation may leave a temporarily invalid public state.

### 4.1 Traversal, search, and topology

Both tree types must expose deterministic, allocation-conscious operations:

- depth-first pre-order and post-order traversal;
- breadth-first traversal;
- topological sort of the expanded dependency graph;
- `FindById`, exact-name search, case-insensitive name search, and predicate search;
- ancestor, descendant, reachability, incoming-reference, and outgoing-reference queries;
- leaf/end-state enumeration;
- unreachable-node detection and optional pruning;
- structural equality and subtree canonical hash for copy/link tooling; and
- validation without evaluation.

When more than one ordering is valid, persistent child/input order is the first tie-breaker and stable canonical occurrence path is the second. Display name and `Guid` must never affect computation order, sampling order, or canonical identity.

Topological sorting expands links according to their semantic identity: independent links create distinct occurrence paths; shared-logical fault links point to the same logical node. Cycle detection operates on function-plus-node targets, so cross-function cycles are caught as well as local cycles.

## 5. Reference and link semantics

### 5.1 Addressing and resolution

An internal reference stores `NodeId` plus `NodeName` fallback. An external reference stores `FunctionId`, `FunctionName`, `NodeId`, and `NodeName`. IDs are the primary persistence keys; names are a lenient migration fallback only, consistent with other v1.1 function references.

Resolution order is:

1. resolve the function by ID when an external function ID is present;
2. otherwise resolve a unique function-name fallback and emit the existing lenient-reference warning;
3. resolve the node by ID inside that function;
4. otherwise resolve a unique node-name fallback and emit a migration warning; and
5. reject missing or ambiguous targets.

The resolver must carry a recursion stack of `(canonical function identity, node identity)`. A repeated entry is a cycle and returns the full path in the validation message. A repeated target that is not on the active stack is legal reuse.

### 5.2 Independent-clone links

An independent link follows the target's current authored structure but compiles as a distinct occurrence. Compilation memoizes the target's immutable structural plan, then binds a distinct occurrence path; it does not allocate a full public object graph per link or per realization. Local mutations of a compiled sample are impossible.

The link's probability math is exactly the target subtree math at the caller's current hazard level. Referenced response functions are evaluated at that level, even if their native knot grid differs. Independent occurrences receive distinct sampler substreams derived from canonical content plus canonical occurrence index. Renames, canvas placement, insertion of unrelated siblings, and GUID regeneration cannot change those streams.

### 5.3 Shared-logical fault links

Shared fault links resolve to one Boolean variable in the compiled decision diagram. They do not multiply the same probability as if it were independent. For example, `AND(A, A)` and `OR(A, A)` both reduce to `A`; an independent clone must be requested to obtain two independent occurrences.

### 5.4 Serialization modes

- `ByReference` writes a function reference and node reference and requires `IRiskFunctionResolver` on read.
- `SelfContained` embeds a canonical snapshot of an external target subtree/function sufficient for headless validation, hashing, and compute. The embedded snapshot behaves as an independent local source after deserialization; it does not require a project store.
- Internal links always serialize as node references inside their owning tree.

Both modes must produce the same canonical identity and sampled values. Canonical hashing substitutes the referenced target's projected canonical subtree/function identity; it never hashes GUIDs, display names, persistence wrappers, or `FunctionReference` markers.

## 6. Event-tree probability model

At hazard level `h` and realization `r`, let a parent have explicit chance branches with raw conditional probabilities `q_i(h,r)`. Validation requires each source to be finite and bounded by `[0,1]` before sibling normalization. Let `S = sum(q_i)` using compensated summation.

- If `S <= 1`, the effective explicit probability is `p_i = q_i` and a remainder child, when present, receives `p_rem = 1 - S`.
- If `S > 1`, the effective explicit probability is `p_i = q_i / S`, the remainder receives zero, and validation emits the existing proportional-normalization warning with the affected node and maximum observed excess.
- Without a remainder child, unassigned probability mass is a valid non-failure/unmodeled outcome; it is included in the aggregate non-failure port so binary outputs still sum to one.

For a terminal leaf `l`, its conditional path weight is

`w_l(h,r) = product over edges e on path(root,l) of p_e(h,r)`.

The aggregate response is

`P(F | h,r) = sum over failure leaves l of w_l(h,r)`.

Branch sampling returns every modeled terminal weight plus one stable implicit-unmodeled branch when the tree can leave probability unassigned. The aggregate non-failure value is `1 - P(F|h,r)` after a tolerance-scale clamp; it is not reconstructed by dropping explicitly modeled non-failure leaves.

Evaluation uses a topological dynamic program: propagate a probability mass from the root through each compiled occurrence and accumulate leaf masses once. Link occurrences reuse compiled instructions but own distinct mass slots and, when applicable, sampling bindings. This is `O(V + E)` work per hazard/realization and avoids legacy leaf-by-leaf ancestor walks.

## 7. Fault-tree probability model

At hazard level `h` and realization `r`, each unique basic event `B_i` has conditional probability `p_i(h,r)`. Gate states follow exact Boolean truth semantics. The top-event response is the exact probability that the root expression is true under independent unique basic events, with shared-logical references mapped to the same variable.

The production algorithm is an ordered reduced binary decision diagram (ROBDD):

1. compile gates and transfers into a canonical Boolean expression DAG;
2. simplify constants and idempotent repeated shared events;
3. choose a deterministic variable order from canonical structural occurrence, never display name or GUID;
4. build/reduce the BDD with unique-table and computed-table memoization; and
5. evaluate bottom-up with `P(node) = (1 - p_i) * P(low) + p_i * P(high)`.

This produces exact floating-point probability for the specified independent/shared static model without enumerating `2^N` event combinations. Read-once trees may use direct gate formulas as a verified fast path. The BDD remains the reference implementation for repeated events, non-coherent XOR, and `KOfN` compositions.

Minimal cut sets are an inspection result for coherent `AND`/`OR`/`KOfN` trees only. They are not the probability engine. XOR models return a clear “cut sets not defined for non-coherent tree” diagnostic rather than a misleading approximation.

The response samples the exact top-event probability at the response's ordered hazard levels and exposes the resulting `OrderedPairedData`/`IUnivariateDistribution` through the normal response contract. `IsMonotonic()` reports the sampled curve just as other nonparametric responses do; non-monotonicity is warned, not silently repaired. No gate clips, renormalizes, or forces a fragility shape beyond tolerance-scale endpoint cleanup.

If BDD node count exceeds a configurable resource budget, setup fails before analysis with the observed count, configured limit, and remediation advice. Phase 10B must not silently fall back to rare-event approximation or cut-set truncation. A future approximation mode requires separate technical approval and a distinct result-quality surface.

## 8. LHS, uncertainty, and deterministic seeding

Tree responses participate in the same engine-owned sampler lifecycle as every other risk function:

- `SetupSampler(sampleSize, scheme, seed)` discovers dimensions, allocates strata/posterior indices, compiles the tree, and freezes immutable evaluation state.
- `SampleFunction(int realizationIndex)` and `SampleBranches(int realizationIndex)` consume the prepared index; they do not draw random numbers.
- percentile overloads apply one explicit percentile consistently to local uncertain sources and call the referenced function's percentile overload.
- mean overloads use source means and referenced-function mean curves.

The sampling dimension graph is compiled across local sources, external referenced response functions, and independent link occurrences. A direct uncertain scalar/table source contributes one local dimension according to its documented co-monotonic sampling rule. A referenced function contributes its own dimensions; the tree must not flatten it to one guessed dimension. A shared-logical fault event is sampled once and reused. An independent link occurrence receives a separate occurrence binding even when its target content is identical.

For LHS, every continuous local dimension receives exactly one deterministic permutation of `N` strata and one within-stratum variate per realization, using the existing Numerics stratification facilities. Posterior-indexed children use the established `realizationIndex` contract and capacity validation. The tree adds no local `Random`, Mersenne Twister, wall-clock seed, or static mutable sampler.

Seed identity is derived from the tree response's canonical content hash plus canonical source/occurrence path. It is invariant to function/node rename, GUID, sibling presentation reorder, link storage mode, XML round trip, thread count, and copy/paste IDs. It changes when compute-relevant probability content, gate type, branch classification, link mode, target content, or topology changes. Identical independent occurrences receive deterministic occurrence indices after canonical structural ordering so they are independent but reproducible.

Required LHS verification includes:

- exact stratum occupancy for every direct and nested dimension;
- same seed and definition producing bit-identical branch and aggregate samples at every thread count;
- metadata, rename, reorder, GUID, serialization-mode, and round-trip invariance;
- different independent link occurrences receiving different streams;
- shared fault occurrences receiving one stream;
- indexed posterior parity with direct child sampling; and
- replicate evidence that LHS reduces estimator variance relative to simple random sampling on a nontrivial tree scenario, with health guards.

## 9. Compilation, caching, and performance

Authoring objects remain observable and convenient; compute uses an immutable compiled plan. Setup performs resolution, validation, canonical occurrence assignment, topological ordering, source binding, leaf/port mapping, and BDD construction. Evaluation uses arrays of primitive values and indices rather than recursive virtual calls or object allocation.

Caches are keyed by canonical compute identity plus sampling setup (`sampleSize`, scheme, seed where applicable). Any compute-relevant property change invalidates the affected plan and parent plans. Metadata changes do not invalidate compute caches. Because plans are immutable after publication, parallel evaluation performs no lazy writes and needs no lock in the hot path.

Performance improvements must be measured against a checked-in fixture before acceptance. Add one event-tree fixture with repeated independent links and one fault-tree fixture with repeated basic events. Record setup time, evaluation time, allocation, compiled instruction/BDD count, and result hash in `scripts/perf/RESULTS.md`. Acceptance targets are:

- no per-hazard or per-realization public-tree clone;
- no per-evaluation graph search or reference resolution;
- no hot-path LINQ allocation;
- event evaluation linear in expanded compiled edges;
- fault evaluation linear in compiled BDD nodes after setup;
- material performance improvement over a faithful legacy-style baseline on the event fixture; and
- no result movement beyond an explicitly documented floating-point ordering allowance approved before re-pinning.

An optimization is rejected if it changes probability semantics, replaces exact fault evaluation with approximation, weakens range checks, or reduces reproducibility/accuracy. Result hashes are gates, not permission to change algorithms.

## 10. Serialization and canonical identity

The canonical v1.1 XML shape is explicit and versioned. Node collections serialize once and structural relationships use IDs; recursive duplicate serialization is forbidden. Attribute order and existing attribute names become contract when the implementation lands.

```xml
<EventTreeResponse Name="..." SpecifiedHazard="..." HazardUnit="...">
  <HazardLevels><Level Value="..." /></HazardLevels>
  <EventTree RootNodeId="...">
    <Nodes><!-- InitiatingNode, ChanceNode, RemainderNode, EventTreeLinkNode --></Nodes>
    <Edges><!-- ordered parent/child relationships --></Edges>
  </EventTree>
</EventTreeResponse>
```

```xml
<FaultTreeResponse Name="..." SpecifiedHazard="..." HazardUnit="...">
  <HazardLevels><Level Value="..." /></HazardLevels>
  <FaultTree TopNodeId="...">
    <Nodes><!-- gates, basic events, house events, transfers --></Nodes>
    <Inputs><!-- ordered gate inputs --></Inputs>
  </FaultTree>
</FaultTreeResponse>
```

`ProbabilitySource` serializes a stable kind plus exactly one scalar, tabular uncertainty definition, or inline/reference response payload. Link mode, gate type, `K`, topology, terminal failure classification, hazard levels, probability transforms, and referenced canonical content are compute-relevant. Names, descriptions, UI order where mathematically commutative, GUIDs, and diagnostics are metadata/identity-inert. Event child order is presentation-only except where it establishes a stable branch output port; branch identity, not list position, must preserve graph connections across reorder.

Canonical identity is a projected, normalized form analogous to `SystemComponent` and `CompositeConsequence`:

- strip all persistent IDs and names;
- replace edges with canonical structural occurrence paths;
- sort mathematically commutative fault-gate inputs by child canonical hash;
- retain event-tree structural branch order only where the authored sequence changes branch identity/output mapping;
- include normalized referenced target identity and link mode;
- exclude `SelfContained` versus `ByReference` wrappers; and
- use invariant-culture numeric formatting and existing canonicalization helpers.

The implementation must include a legacy reader for the shipped event-tree XML shapes. It converts legacy chance sources to the new source/link types and emits only the new shape on write. Unknown, commented secondary-hazard elements are rejected with a migration message rather than partially loaded.

## 11. Risk-graph and branch-output integration

The ordinary response contract remains binary: response output port `0` is failure and port `1` is non-failure, preserving `BranchPolarity` and current `ResponseStage` behavior. `FaultTreeResponse` needs no additional ports because its top event is binary.

`EventTreeResponse` additionally implements `IBranchingResponseFunction`. A `ResponseElement` may opt into expanded end-state ports. Expansion is off by default for backward compatibility. In expanded mode:

- each terminal branch descriptor receives an append-only runtime output port and stable branch ID;
- one implicit-unmodeled non-failure branch is exposed when necessary;
- `RiskConnection` and response-stage persistence identify the selected output primarily by branch ID and secondarily by name for migration;
- reordering or renaming a branch does not retarget an existing connection;
- deleting a connected branch follows the same reject/cascade/materialize policy as other referenced deletion; and
- aggregate failure/non-failure ports remain available only through an explicit aggregate view, so callers cannot accidentally connect both aggregate and constituent ports and double count.

`RiskElementFactory`, `RiskElementResolver`, `ComponentGraph.Validate`, `SystemComponent` projection, end-state grouping, canonical hashing, and occurrence assignment must all understand dynamic branch descriptors through one shared authoring surface. The UI must not invent its own port mapping later.

Per-leaf connections mean that the risk graph, not the event tree, attaches consequences or additional response stages to an end state. A leaf may therefore connect to another event-tree response, a fault-tree response, an ordinary response, or a consequence through normal graph connections. The tree itself never owns those downstream risk links.

## 12. Validation and diagnostics

Validation returns stable codes plus actionable messages. At minimum it covers:

- null/empty tree, missing or multiple roots/top events, disconnected nodes, and illegal node/gate relationships;
- duplicate persistent IDs, ambiguous name fallbacks, unresolved function/node references, wrong referenced function type, and cross-function cycles;
- invalid hazard axes, non-finite values, probability sources outside `[0,1]`, posterior capacity mismatch, and unavailable sampler setup;
- multiple remainder siblings, remainder nodes in illegal positions, and empty structural link targets;
- illegal `SharedLogicalEvent` links in event trees;
- empty gates, invalid `K`, unsupported dynamic/common-cause gate requests, and BDD resource-limit exceedance;
- invalid terminal classifications or duplicate branch IDs/ports;
- stale risk-graph branch connections;
- self-contained/by-reference resolution failures; and
- non-monotonic aggregate fragility as a warning consistent with other response functions.

Warnings versus errors must follow existing response validation: correctable shape concerns such as non-monotonicity and sibling over-allocation are warnings when compute remains well-defined; missing targets, cycles, invalid ranges, and unsupported semantics are errors.

## 13. Implementation sequence and file plan

### Phase 10A — common foundation and event-tree response

> **Implementation status (2026-07-28, partial):** four landed vertical slices now include common
> branch/reference/fragment contracts; controlled transactional authoring with fresh-ID copy/paste,
> replace, materialize, delete-materialize, and prune; deterministic expanded topological,
> unreachable, and reference queries; scalar/tabular/ordinary-response sources; internal/external
> `IndependentClone` links and occurrence-aware sampling; resolver-backed two-mode XML; full-path
> cross-function cycle diagnostics; link-aware evaluation; recursive `EventTreeResponse`
> probability sources evaluated on the caller hazard axis; recursive discovery and independent
> occurrence sampling through nested responses, ordinary responses, tables, and links; mixed
> source/link cycle diagnostics; transactional sampler setup; and projected recursive hash/seed
> identity. Failed mutations and failed sampler setup restore topology, output ports, persistent
> IDs, hashes, and configured-sampler state exactly. The established raw/normalized sibling,
> residual, interpolation, seed, and output-port rules are unchanged. Phase 10A remains open for
> the unimplemented portions of steps 3 and 5–7, including legacy conversion, expanded graph ports,
> immutable cached-plan performance, and the remaining verification/performance gates.
1. Add append-only discriminators, common tree interfaces/results, source/link/reference types, and diagnostic codes.
2. Add the controlled `EventTree` authoring model and transactional manipulation/search/topology APIs.
3. Add legacy XML conversion plus canonical v1.1 self-contained/by-reference serialization and projected hashing.
4. Add link-aware compilation, probability propagation, branch output, and aggregate `IResponseFunction` sampling.
5. Integrate recursive sampler discovery, LHS, content seeds, and immutable compiled-plan caching.
6. Add `IBranchingResponseFunction` support to the risk-graph authoring/projection/serialization surface.
7. Land unit tests, verification family, representative converted templates, technical-reference page, traceability updates, performance fixture, and results documentation.

Expected source folders are `RiskFunctions/Responses/Trees`, `RiskFunctions/Responses/EventTrees`, and mirrored test folders. Existing graph types are edited narrowly; tree-specific logic must not be duplicated across `ResponseElement`, `ResponseStage`, and the UI-facing authoring surface.

### Phase 10B — static fault-tree response

1. Add fault node/gate/transfer types and controlled authoring APIs on the common foundation.
2. Add shared-logical versus independent-clone identity compilation and static-tree validation.
3. Implement the exact ROBDD compiler/evaluator, read-once verified fast path, and coherent-tree cut-set inspection.
4. Add response sampling, LHS/referenced-function integration, serialization, canonical hashing, and factory support.
5. Land unit tests, exhaustive small-tree and independent-Monte-Carlo verification, performance/resource fixtures, technical-reference/results pages, and traceability/matrix updates.

Phase 10B begins only after Phase 10A's API, serialization/hash recipe, LHS behavior, branch outputs, and performance gates are green. This is sequencing, not a request to redesign the common foundation twice.

### 13.1 No new runtime dependencies

Both phases must use only `RMC.Numerics` and the BCL already allowed by the model-library architecture. Do not introduce a graph, BDD, UI, serialization, or random-number package. A small internal ROBDD implementation is preferable because its ordering, determinism, resource limits, and exactness are part of the verification contract.

## 14. Unit-test plan

Every public class receives a corresponding test class in a mirrored folder. Fast tests must cover:

- every constructor/property default, validation matrix, and `INotifyPropertyChanged` contract;
- scalar, tabular, response-reference, internal-link, and external-link probability sources;
- sibling sums below/equal/above one, remainder behavior, unassigned mass, leaf products, and aggregate sums;
- AND/OR/XOR/K-of-N/house-event truth tables and exact probability identities;
- repeated shared events versus independent clones (`AND(A,A) = A` shared, `= p^2` independent);
- topological/traversal order, search, reachability, incoming references, and deterministic diagnostics;
- copy/paste remapping, internal/external link preservation, materialization, move/replace, all delete policies, failed-transaction rollback, and pruning;
- local and cross-function cycle rejection with full paths;
- branch descriptor/port stability under rename and reorder, plus stale-connection rejection;
- self-contained and by-reference XML round trips, legacy reads, resolver fallbacks, malformed XML, and unknown-node rejection;
- canonical-hash invariance under metadata/name/GUID/order/mode changes and sensitivity to every compute property;
- sampler dimension counts, posterior capacity, LHS strata, occurrence independence/sharing, and thread-count bit identity;
- compiled-cache invalidation for compute edits and retention for metadata edits;
- BDD reduction, deterministic variable order, resource-limit errors, and direct-formula/BDD parity; and
- factory/discriminator append-only pinning and non-serialization of runtime discriminators.

Property-based tests should generate small valid trees and compare traversal mass conservation, clone equivalence, serialization equivalence, and fault BDD outputs against exhaustive Boolean enumeration. Generation must use fixed seeds and print the minimized serialized counterexample on failure.

## 15. Verification plan

### 15.1 `EventTreeVerification`

- Convert the legacy `TestIO` shape into an asserted legacy-read/new-write round trip.
- Recreate representative small, deep, wide, over-allocated, remainder, referenced-response, internal-link, and external-link scenarios from the legacy templates.
- Compare aggregate and every terminal branch against an independent analytic path-product oracle at all hazard knots.
- Compare uncertain indexed realizations one-for-one against independently sampled child curves.
- Verify linked independent clones against an explicitly deep-cloned equivalent model.
- Run an independent fixed-seed Monte Carlo branch-routing oracle for nontrivial trees and assert within documented binomial error.
- Verify graph-connected per-leaf consequences against an equivalent explicitly expanded component graph, proving that annualization/risk occurs outside the tree.
- Pin same-seed/thread-count/round-trip/metadata bit identity and LHS variance reduction.

Legacy `Test_Product` contains no assertion and does not exercise `EventTreeResponse`; it must not be cited as a compute oracle. Its simple multiplication idea may seed an analytic test, but expected values are independently derived and documented.

### 15.2 `FaultTreeVerification`

- Exhaustively enumerate all Boolean states for small trees and compare exact top-event probabilities to the ROBDD at every hazard/realization.
- Pin closed forms for series, parallel, repeated shared events, independent clones, XOR, and K-of-N systems.
- Compare coherent-tree cut-set inspection to hand-derived sets while separately proving the BDD probability does not depend on cut-set approximation.
- Compare medium repeated-event models to an independent fixed-seed Boolean Monte Carlo oracle with binomial tolerances.
- Verify referenced ordinary/event/fault response functions and internal/external transfers.
- Verify graph-connected fault response risk against an equivalent ordinary response curve, proving the same hazard and consequence integration path.
- Pin LHS strata/variance reduction, sampler sharing/independence, bit reproducibility, canonical invariance, and BDD resource diagnostics.

Each family runs isolated. Each gets a page under `docs/verification/` containing formulas, fixtures, expected values, measured results, tolerance derivation, seeds, realization counts, and limitations. `legacy-traceability.csv` must classify the legacy methods honestly and the validator must remain green.

## 16. Level of effort and cost-benefit assessment

These are engineering estimates for one experienced contributor familiar with the model library, including implementation, review fixes, documentation, unit tests, verification, and performance measurement. They exclude UI work and external project migration tooling.

| Work package | Estimated effort | Risk |
|---|---:|---|
| Common graph/reference/manipulation/sampling foundation | 2–3 weeks | High: identity, cycles, mutation, and deterministic sampling touch core contracts |
| Event-tree response port and legacy conversion | 2–3 weeks | Medium-high: legacy semantics are understandable, but branch outputs and links expand the API |
| Event-tree unit/verification/performance closure | 1.5–2 weeks | Medium |
| Static fault-tree model and ROBDD engine | 2.5–4 weeks | High: repeated-event correctness and resource behavior require careful implementation |
| Fault-tree unit/verification/performance closure | 1.5–2.5 weeks | High |
| Total Phase 10A | approximately 5.5–8 weeks | Medium-high |
| Incremental Phase 10B after 10A | approximately 4–6.5 weeks | High |
| Combined | approximately 9.5–14.5 weeks | High |

The fault-tree addition has a favorable long-term cost-benefit **if** static Boolean reliability modeling is a real practitioner need. It reuses the most expensive non-domain-specific work from Phase 10A—references, authoring, traversal, serialization, hashing, LHS, diagnostics, and compiled plans—while adding a capability that event trees do not express well when basic events repeat or feed multiple gates. It also avoids forcing users to manually expand equivalent event trees, which is error-prone and can grow exponentially.

The benefit is not “cheap,” however. A numerically credible fault tree cannot be implemented as a few AND/OR formulas because repeated events invalidate naive independence multiplication. The exact ROBDD, shared-versus-independent identity rules, verification oracle, resource limit, and authoring semantics account for most of the incremental 4–6.5 weeks. Shipping a naive gate evaluator would have poor cost-benefit and unacceptable correctness risk.

Recommendation: approve Phase 10B now as a planned follow-on and deliberately design the Phase 10A common foundation for it, but keep separate exit gates. Begin fault implementation only after event-tree references, LHS, canonical identity, and mutation APIs are verified. If schedule pressure intervenes, ship Phase 10A without fault trees rather than weakening exactness. If static fault trees are used by even a modest number of risk models, the reduction in manual expansion, review burden, and modeling error is likely to repay the incremental effort; if no concrete models need repeated basic events, defer the 10B implementation while retaining the compatible foundation.

## 17. Definition of done

Phase 10A or 10B is complete only when all applicable items below are true:

- public APIs, XML, hash recipe, math, references, manipulation semantics, and diagnostics match this document;
- response outputs are conditional fragility only; no hazard probability or risk logic entered the tree;
- all new classes appear in the ported-types matrix with P/T/V status and have matching fast tests;
- factory/discriminator/two-mode serialization and canonical kitchen-sink registrations are complete;
- LHS, deterministic seed identity, independent/shared occurrence behavior, and thread-count reproducibility are verified;
- unit coverage for `RMC.TotalRisk.dll` remains above 90%; build and fast tests have zero warnings/failures;
- the relevant verification family passes in an isolated run and has a results page;
- representative legacy event templates convert and compute, with secondary-hazard code excluded and bivariate types untouched;
- performance fixtures and result hashes are recorded, resource limits are exercised, and no accuracy-reducing fallback exists;
- `validate-code-xml-docs.ps1` and `validate-verification-traceability.ps1` pass; and
- `ROADMAP.md`, `PROGRESS.md`, architecture status/history, technical-reference map, verification map, references, and this document reflect the landed behavior.

## References

The reliability standards and handbooks supporting the event/fault terminology, Boolean semantics, and verification approach are listed as [19]–[22] in [docs/references.md](../references.md). Repository-specific probability, sampling, identity, and risk-graph contracts remain governed by the architecture and verification documents cited above.
