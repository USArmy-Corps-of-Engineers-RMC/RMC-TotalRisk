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
