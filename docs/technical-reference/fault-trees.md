# Fault-Tree Responses

> Technical reference for `RMC.TotalRisk.RiskFunctions.Responses.FaultTrees` (`FaultTreeResponse`,
> `FaultTree`, the gate/basic/house/transfer node types, `FaultTreeCutSet`,
> `FaultTreeCutSetEvent`) and the shared tree contracts in
> `RMC.TotalRisk.RiskFunctions.Responses.Trees` (`ProbabilitySource`, `TreeFragment`,
> `TreeNodeReference`, `TreeLinkMode`, `TreeDeletePolicy`, `TreeNodeImportance`).
> Normative design:
> [../requirements/EVENT_AND_FAULT_TREE_RESPONSE_DESIGN.md](../requirements/EVENT_AND_FAULT_TREE_RESPONSE_DESIGN.md).
> Executable evidence: [../verification/fault-tree.md](../verification/fault-tree.md).
> References: [19]–[21] in [../references.md](../references.md).

A `FaultTreeResponse` is a **binary response function**: it produces the exact top-event
conditional failure probability `P(F|h)` under the ordinary `IResponseFunction` contract — hazard
probability, annualization, consequences, and risk stay outside the tree, owned by the component
graph and `RiskAnalysis`. The response owns a finite, strictly ascending hazard axis and a
controlled `FaultTree`; between axis knots the top-event curve follows the established
response-CDF interpolation policy through the sampled function, exactly like every other
nonparametric response.

## Structural model

The authored `FaultTree` is a **single-parent tree** with one protected top-event gate root.
Gates (`FaultTreeGateNode`) are the only interior nodes; basic events
(`FaultTreeBasicEventNode`), house events (`FaultTreeHouseEventNode`), and transfers
(`FaultTreeTransferNode`) are leaves. All reuse and repetition flows through transfers, and the
expanded compiled plan — transfers resolved to their targets — is the directed acyclic logic
graph the probability model evaluates. Structural, source, and mixed cross-function cycles are
rejected transactionally with the complete deterministic function/node path in the diagnostic.

Two transfer meanings exist (`TreeLinkMode`):

- `SharedLogicalEvent` (the fault default): every occurrence is the **same Boolean event**, so
  repeated references resolve to one unified variable — `AND(A, A)` and `OR(A, A)` both reduce
  exactly to `A`.
- `IndependentClone`: the target structure is logically cloned with a fresh variable context and
  an independent epistemic sampling stream, for deliberate repeated-but-independent equipment.

## Gate algebra

Gate truth follows exact Boolean semantics over the expanded inputs:

| Gate | Truth | Identical-input probability form |
|---|---|---|
| `And` | all inputs true | `∏ p_i` (independent inputs) |
| `Or` | any input true | `1 − ∏ (1 − p_i)` |
| `Xor` | exactly one of exactly two inputs true | `p₁(1−p₂) + (1−p₁)p₂` |
| `KOfN` | at least `K` inputs true | the binomial upper tail at identical `p` |

`Xor` gates take exactly two inputs; wider parity compositions must be authored explicitly.
`KOfN` requires `1 ≤ K ≤ n`. House events are deterministic constants folded into the diagram
before expansion. Repeated **shared** events are never multiplied as if independent — that is
the entire point of the unified-variable model.

## The exact decision diagram

The production evaluator is an ordered reduced binary decision diagram (ROBDD) built over the
unified variables ([19]–[21] are the underlying fault-tree canon):

1. transfers and gates compile into the expanded occurrence graph;
2. constants and idempotent shared repetitions simplify structurally;
3. the deterministic variable order comes from canonical structural occurrence — never display
   name or GUID;
4. the diagram builds through memoized if-then-else composition with unique and computed tables
   and an exact `KOfN` threshold recurrence; and
5. evaluation is the Shannon recurrence `P(node) = (1 − p)·P(low) + p·P(high)` over a frozen
   children-before-parents array form that allocates nothing per read.

This yields the exact probability of the specified independent/shared static model without
enumerating `2^V` assignments. There is **one** evaluation path: the optional read-once
gate-formula fast path was deliberately not implemented, and the closed-form gate identities in
the unit and verification suites serve as its read-once verification.

`BddNodeLimit` (default 1,000,000; runtime-only — never serialized, never hashed) bounds diagram
construction. Exceeding it fails validation and setup loudly with the observed count, the
configured limit, and remediation guidance; exact evaluation is never silently approximated.

## Minimal cut sets

`GetMinimalCutSets(maxCutSets = 10000)` extracts minimal cut sets from the frozen diagram by
Rauzy's bottom-up minimization, ordered by cardinality and then unified-variable sequence, with a
loud failure when the bound is exceeded. Cut sets are **inspection output only** — the response's
probability never depends on them, and the verification family includes an explicit
anti-approximation proof that the rare-event cut-set sum is not the evaluator. Non-coherent trees
(any `Xor`) refuse extraction with a specific diagnostic rather than returning a misleading set.

## Probability sources

Each basic event carries a `ProbabilitySource` of one of three kinds: a deterministic scalar, an
uncertain probability table aligned to the owning tree hazards (one co-monotonic knowledge
percentile drives every ordinate), or a referenced `IResponseFunction` — an ordinary tabular
fragility, an `EventTreeResponse`, or another `FaultTreeResponse` at arbitrary acyclic nesting
depth — evaluated at the owning response's current hazard ordinate. Referenced functions may be
held inline (self-contained) or re-attached by an `IRiskFunctionResolver`.

## Sampling

Setup discovers sampler dimensions per **unified variable**: a shared-logical event contributes
its dimensions once no matter how many occurrences reference it, while every independent clone
contributes its own. Referenced response variables are prepared on isolated self-contained setup
clones seeded from the variable's content identity and canonical ordinal, so identical
independent clones receive distinct reproducible streams. Mean, co-monotonic percentile, and
indexed LHS sampling are preserved through arbitrary nesting depth, and a failed setup restores
the exact prior sampler state.

## Authoring and rollback

`FaultTree` owns the same controlled surface as `EventTree`: add, insert, move, replace, delete
(`RejectIfReferenced`/`CascadeLinks`/`MaterializeLinks`), copy/paste through immutable
`TreeFragment` snapshots with fresh IDs and fragment-local reference remapping, shared and
independent link creation (internal and external), materialization, unreachable-node pruning,
search/ancestry/reachability/traversal queries, subtree canonical hashing, and structural
equality. Materializing a **shared** transfer converts that occurrence to an independent clone,
so a repeated event stops unifying and the computed probability may deliberately change; the
diagnostic surface and both directions are tested. Every operation is transactional — a failed
edit restores topology, IDs, child order, the compiled-plan cache, and configured samplers
exactly.

## Serialization and identity

Both `RiskSerializationMode`s write the explicit node/input form:
`<FaultTree TopNodeId>` with a `<Nodes>` collection and single-parent
`<Input ParentNodeId ChildNodeId Order/>` edges. Self-contained external targets embed function
snapshots — repeated embeds of one function materialize as one live instance on read, so shared
unification survives round trips — while by-reference targets write `FunctionReference` markers a
resolver re-attaches, with lenient name fallback and ID repair on the next write.

Canonical identity is **projected**: persistent IDs, display names, commutative input
presentation order, serialization mode, and reference wrappers are projected away; gate types,
`K` thresholds, house states, source content, link modes, and topology participate. Shared
unification is encoded through `SharedVariable` first-occurrence ordinals, which is what
distinguishes `AND(A, A)`-shared from two content-identical independent events. Seeds derive from
this projected identity, so renames, reorders, GUID regeneration, and mode changes are seed-inert.

## The compiled plan

Each instance publishes one immutable occurrence/evaluation plan (expanded occurrences, unified
variable table, projected identity, the frozen diagram, recursive dependency snapshots) behind a
per-instance lock; concurrent read-only callers share the published objects. Controlled edits,
nested tree responses, mutable tables, and ordinary referenced functions invalidate through
revisions plus defensive live-content fingerprints, and authoring rollback restores the exact
cache checkpoint. The F7 performance fixture records the reference costs for a 23,344-node frozen
diagram over 60 unified variables in
[../../scripts/perf/RESULTS.md](../../scripts/perf/RESULTS.md).

## Node importance

`TreeNodeImportance.Compute` serves both tree kinds; the two-pass Monte Carlo sweep, its
statistics, and its seeding are documented with the other sensitivity tooling in
[sensitivity-analysis.md](sensitivity-analysis.md#tree-node-importance).

## Exact importance measures

`FaultTreeImportance.Compute(response, options)` produces the standard probabilistic-risk-
assessment structural set — Birnbaum, criticality, Fussell-Vesely, risk achievement worth, and
risk reduction worth — by exact algebra on the frozen decision diagram: one baseline evaluation
at the analyzed hazard level (every source at its mean, or at a co-monotonic percentile via
`FaultTreeImportanceOptions.Percentile`), then two allocation-free conditional evaluations per
unified variable with its probability forced to one and zero. With P the top event and
P(1)/P(0) the conditionals: B = P(1) − P(0) (the exact ∂P/∂q), criticality B·q/P,
Fussell-Vesely 1 − P(0)/P, RAW P(1)/P, RRW P/P(0). No simulation and no seed — the measures are
deterministic, and cost is two linear diagram passes per variable (negligible even at the F7
fixture's 23,344-node diagram over 60 variables).

Conventions: a variable sharing logic through transfers is one unified variable with one entry;
a variable reduced out of the frozen diagram (a constant-collapsed branch) has B exactly zero;
when P = 0 the ratio measures are NaN; when P(0) = 0 with P &gt; 0 the risk reduction worth is
positive infinity. The measures are defined for coherent trees only — a non-coherent tree (an
Xor gate) is refused loudly with the Monte Carlo redirect, exactly like the minimal cut-set
surface. Verified against the exhaustive Boolean-enumeration oracle with per-variable forced
conditionals ([../verification/fault-tree.md](../verification/fault-tree.md)).

## Configuration risk

`RiskAnalysis.MeasureConfigurationRisk(configuration)` answers the operational question the
importance measures rank in the abstract: the risk of the system **with named house events held
at specified states** — a spillway gate out of service, a bulkhead installed, a pump
unavailable. Each `HouseEventState(functionId, nodeId, state)` addresses one house event by the
fault-tree response's function id and the node's persistent id. The query clones the components
self-contained (nested and external functions come along embedded), applies the overrides
through a containment walk — element-assigned functions, external transfer targets,
tree-referenced responses, structural links, and composite-response children — and refuses any
override that matches nothing rather than silently ignoring it. Every live instance of one
function id is reconfigured, the physical reading of one piece of equipment reused across
components. Two mean-only quantifications, the unmodified baseline and the configured system,
publish an unpersisted `ConfigurationRiskResults`: system and per-component rows of annual
failure probability and per-consequence-type expected annual consequences with changes and
ratios (NaN at a zero baseline, the importance-measure convention), plus the applied-override
display labels.

The query is runtime-only — the authored model, its published results, and its estimated state
are byte-untouched, and nothing is serialized, hashed, or seed-affecting. It is deliberately
mean-only: a house state is compute content, so a configured realization ensemble would re-roll
every content-derived seed and mix stream noise into the difference; the deterministic
re-quantification is the "risk right now" answer. Because a configured clone is
content-identical to a re-authored model, the query is verified **bit-exactly** against
independently re-authored baseline and configured twins
([../verification/configuration-risk.md](../verification/configuration-risk.md)).
