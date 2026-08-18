# System Components and the Risk-Element Graph

> Technical reference for `RMC.TotalRisk.Systems.Components` (`SystemComponent`, `FailureMode`,
> `ResponseStage`, `EndStateGroupLayout`) and `RMC.TotalRisk.Systems.Components.Graph`
> (`ComponentGraph`, `RiskElementBase` with `HazardElement`/`TransformElement`/`ResponseElement`/
> `ConsequenceElement`, `RiskConnection`, `RiskElementFactory`, `RiskElementResolver`,
> `HazardSourceOption`). Normative spec:
> [../requirements/MODEL_LIBRARY_ARCHITECTURE.md](../requirements/MODEL_LIBRARY_ARCHITECTURE.md)
> §3 (layout), §5.5.3–§5.5.4 (component identity and occurrence indices), §7.9 (end states), §8
> (layer boundaries and the authoring surface). Executable evidence:
> [../verification/engine-reproducibility.md](../verification/engine-reproducibility.md) and
> [../verification/cascade-end-states.md](../verification/cascade-end-states.md).

A `SystemComponent` is one hazard driving potential failure modes and their consequences, plus
the options that govern how those modes combine — a component is *identified by its hazard
function* [25], so one dam with flood and seismic hazards is a two-component system. The graph is
the model: the component persists its `ComponentGraph` — a typed, validated DAG of risk elements —
and `FailureModes` is a fresh deterministic **projection** of that graph on every access, never
independent state. Each projected mode is a root-first chain of the grammar `T* (R T*)* C`: zero
or more transforms, response stages each optionally followed by transforms, and one terminal
consequence element; a non-failure mode connects the hazard directly to non-failure consequences
with no response.

At the system level components connect in **series** — any component's failure contributes to
system risk; parallel redundancy (both gates must fail) is modeled *inside* a component's response
through event trees [25]. Failure-mode capacities are statistically independent across components
(local materials, geometry, construction); the physical linkage between components is the shared
hazard environment, carried entirely by the joint hazard probability. Capacity dependence *within*
a component is the failure-mode dependency option — a different control from cross-component
hazard dependency ([risk-analysis-engine.md](risk-analysis-engine.md) §7).

The mathematics that flows through this surface lives in
[risk-analysis-engine.md](risk-analysis-engine.md), [risk-integration.md](risk-integration.md),
[loss-exceedance-curves.md](loss-exceedance-curves.md),
[failure-mode-combination.md](failure-mode-combination.md),
[risk-contribution.md](risk-contribution.md), and [cascading-end-states.md](cascading-end-states.md);
this page documents the structure, authoring, validation, identity, and serialization contracts.

## The element graph

`ComponentGraph` owns the ordered element list and nothing else — it carries no name, no hash,
and no compute. Connections are object references stored on the **consumer** (`RiskConnection`:
an immutable `Source` element, `SourcePort`, and optional stable `SourceBranchId`/
`SourceBranchName` for expanded response branches); fan-out is derived by scanning consumers,
and Kahn topological sorting doubles as cycle detection. Declared element order is **semantic**:
it fixes the order of the projected failure modes.

| Element | Inputs | Outputs | Wraps | Special slots |
|---|---|---|---|---|
| `HazardElement` | 0 | 1 | `IHazardFunction` | — |
| `TransformElement` | 1 (`Input`) | 1 | `ITransformFunction` | — |
| `ResponseElement` | 1 (`Input`) | port 0 = Fail, port 1 = Non-Fail; opt-in expanded branch ports | `IResponseFunction` | `ExpandBranchOutputs`, reserved `SecondaryInput` |
| `ConsequenceElement` | 1 (`Input`) | 0 | ordered `Functions` list (index 0 primary) | `HazardSource` binding |

Membership and naming run through the graph: `AddElement`/`RemoveElement` enforce Id and
non-empty-name uniqueness, `GetElement`/`GetElementById`/`GetElements<T>` look up,
`IsNameAvailable`/`GetUniqueName`/`TryRenameElement` form the name authority (`GetUniqueName`
numbers collisions "base (2)", "base (3)", …), and `SortedElements`/`TopologicalSort`/
`GetDownstreamElements`/`GetUpstreamPath` answer topology queries. The wrapped input-function
clusters are documented on their own pages ([hazard-functions.md](hazard-functions.md),
[transform-functions.md](transform-functions.md), [response-functions.md](response-functions.md),
[consequence-functions.md](consequence-functions.md)); expanded branch ports are documented with
the event trees ([event-trees.md](event-trees.md)).

## Authoring surface

Graph editors and agentic callers author through the model library's own surface, never a
caller-owned mapping (spec §8.5): `RiskElementFactory.CreateForFunction(function)` or
`Create(RiskElementType)` constructs the right element for a function or cluster;
`IRiskElement.TryAssignFunction(function, out error)` assigns with a reported (not thrown)
cluster mismatch, and a `ConsequenceElement` **appends** to its ordered consequence list;
`ComponentGraph.GetAvailableHazardSources(element)` enumerates the hazard signals reachable at an
element's position as `HazardSourceOption` records — the element reference and chain position are
the structural facts, the hazard/unit labels are advisory display metadata, and binding
validation reuses the same walk so the picker and the validator can never disagree; and
`SystemComponent.GetReferencedFunctions()` answers the delete-impact question for a consuming
store. `SystemComponent.AddFailureMode(failureMode)` expands a chain-style mode into wired
elements — lossless for failure chains, so re-projection reproduces the mode with the same
canonical hash; a non-default consequence hazard position becomes a `HazardSource` binding on the
terminal. Branch-carrying responses author through `ResponseElement.GetAvailableBranches()` /
`CreateBranchConnection(branchId)`, and `ComponentGraph.DeleteEventTreeNode(element, nodeId,
policy)` applies the tree delete policies transactionally (branch identity and the policies are
documented in [event-trees.md](event-trees.md)).

```csharp
var component = new SystemComponent(hazardFunction);
var response = (ResponseElement)RiskElementFactory.CreateForFunction(
    fragility, component.Graph.GetUniqueName("Levee Breach"));
var terminal = (ConsequenceElement)RiskElementFactory.CreateForFunction(damage);
component.Graph.AddElement(response);
component.Graph.AddElement(terminal);
response.Input = new RiskConnection(component.Graph.GetElements<HazardElement>().First());
terminal.Input = new RiskConnection(response);            // port 0 — the failure branch
var signal = component.Graph.GetAvailableHazardSources(terminal).First();
terminal.HazardSource = new RiskConnection(signal.Element, signal.OutputPort);
var (isValid, messages) = component.Validate();
```

Change notification propagates upward: elements re-raise wrapped-function changes, the graph
forwards element changes, and the component relays graph membership changes as a `FailureModes`
change, so consuming layers observe one surface (spec §8.4).

## Projection — modes are a view over the graph

`FailureModes` projects one mode per consequence element, in graph declared order. Each mode is
the terminal's root-first path with transforms classified into stages around responses; every
stage's `BranchPolarity` is read from the exit port the path uses — port 0 projects `Fail`
(the stage contributes `p(h)`), port 1 projects `NonFail` (the stage contributes `1 − p(h)`).
Transforms after the last response become the trailing chain, the terminal's ordered functions
become the mode's consequence list, and a response-free path projects the canonical non-failure
stage form (the `NonFailResponse` sentinel; at most one such path exists per component).
`MultipleConsequences` derives from same-port fan-out only — a Fail terminal and a Non-Fail
continuation are distinct end states, not multiple consequences. A structurally unsound graph
projects an empty list and validation reports why. The projection is a fresh snapshot per access:
callers capture it once per operation, and the engine freezes exactly one snapshot at
`SetupSamplers`.

`FailureMode` carries the projected chain (`ResponseStages`, `ResponseToConsequence`,
`ConsequenceFunctions`, `ConsequenceHazardPosition` — null meaning the last response's input,
always serialized resolved), the single-stage v1.0 views (`HazardToResponse`, `ResponseFunction`,
`ConsequenceFunction`), and runtime-only projection stamps (`ProjectedResponseOrdinals`,
`ProjectedTerminalName`). `ResponseStage` pairs `Transforms` with a never-null `Response` (a null
assignment coerces to a fresh `NonFailResponse`) and the polarity; `BranchPolarity` is written
resolved, loads forward as `Fail` when absent, and is part of the identity surface — flipping a
polarity is a deliberate hash event that moves seeds. `SelectedBranchId`/`SelectedBranchName`/
`GetSelectedBranch()` address an expanded event-tree branch; the polarity-product algebra those
stages feed is owned by [cascading-end-states.md](cascading-end-states.md).

## The end-state group layout

`EndStateGroupLayout.Build(projectedModes)` derives pure structure — no sampling: leaf signatures
(ordered response occurrence-ordinal/polarity pairs) partition shared-first-response terminals
into exclusive state groups, duplicate and prefix-nested signatures eject to standalone units,
and classification is by final-stage polarity. Chain-authored modes carry no ordinals and behave
as standalone Fail-final units, so every non-cascading model produces the trivial layout
(`IsTrivial` true, kernels byte-identical to the plain per-mode forms).

| Member | Meaning |
|---|---|
| `StateCount` / `IsFailureState` | terminal end states and their final-polarity classification |
| `CombinationUnitCount` | **the dimension** of the combination caches, the multivariate normal, and the correlation matrix; equals the failure-path count under a trivial layout |
| `StateToCombinationUnit` / `CombinationUnitStates` | the state ↔ unit maps |
| `PairingPartnerState` | the flipped-final sibling used by excess pairing; −1 means the background fallback |
| `ClaimedStateUnit` / `ClaimedStateCount` / `ClaimingCascadeCount` | the claimed non-failure state bookkeeping |
| `HasNonFailBranchFailureState` | whether any failure state rides a Non-Fail branch (the competing-method gate) |

The layout is public so consuming layers can display and edit against the same structure the
engine combines with; the weight algebra, claimed-complement mixture, and across-unit combination
live in [cascading-end-states.md](cascading-end-states.md) (spec §7.9).

## Combination options and failure-mode dependence

`FailureModeMethod` selects the combination rule; choosing common-cause adjustment or mutually
exclusive coerces `FailureModeDependency` to `Independent` (the v1.0 behavior). Under the
automatic dependency modes the component materializes a Gaussian copula over the response
probabilities with the exact v1.0 off-diagonal constants `1 − √εmach` (perfectly positive) and
`−1/(D − 1) + √εmach` (perfectly negative); `CorrelationMatrix` is user content only under
`DependencyType.CorrelationMatrix` (positive definite via Cholesky, one row per **combination
unit**), is back-filled with the derived matrix in the automatic modes, and serializes and hashes
only in the matrix mode. `IsCorrelationMatrixValid()` and `FailureModeMultivariateNormal` expose
the checks and the materialized model. `FailureModeIndicators` and
`FailureModeBinomialCombinations` are dense inspection caches materialized only on request — the
engine enumerates exclusive combinations lazily and never reads them. `JointConsequences`,
`HazardThreshold`, and `IsDeterministic` complete the option surface;
[risk-contribution.md](risk-contribution.md) documents the exclusive-event mathematics behind the
four combination methods.

`ProfileHazardElementId` / `SetProfileHazardElement` bind the reporting axis for the risk-profile
catalog to a `TransformElement` in the component's own graph whose upstream path reaches the
hazard root. The selection is deliberately **seed-inert** — serialized as an append-only
attribute, excluded from the identity form — so flipping a reporting axis can never re-roll
seeds; with a profile bound, `HazardThreshold` is interpreted on the profile axis and validation
says so. The profile surfaces it re-expresses are documented in
[results-catalog.md](results-catalog.md).

## Validation

Validation runs at three scopes — element, graph, component — with the shared message contract
(`"Error: …"` invalidates, `"Warning: …"` advises), and `Validate(RiskAnalysisMode)` relaxes
exactly the consequence-content requirements in reliability mode (terminals need no functions,
positional alignment is not enforced). Graph structural checks run in order: exactly one hazard
element; duplicate name/Id backstop; per-element validation; dangling connections and port bounds
(structural inputs and hazard-source bindings, including stale branch references); cycle
detection; then, on a single-root acyclic graph — reachability from the root, leaves must be
consequence elements, at least one consequence element, at most one response-free path, binding
targets on the consumer's own path at or before the last response's input, consequence-count
alignment against the non-failure path (count mismatch errs; paired label/unit mismatches warn),
and shared function instances (warning: independent copies on reload). Polarity-aware advisory
checks activate when cascade structure is present: identical-signature terminals double-count a
branch, a prefix-signature terminal overlaps its continuation, and an unwired Fail port routes
failure mass to background — each warns. Component-level checks add the correlation-matrix
dimension/definiteness error, profile-element resolution errors with the threshold
reinterpretation warning, deduplicated projected-mode warnings, the two cascade gates (one
claiming state group per component; competing is undefined for else-chain failure states — both
errors), and the joint-method branch guardrail (within a unit exposure branches add, across units
they multiply; more than 64 warns, more than 1,024 errs).

## Identity, occurrence indices, and the sampler walk

`ToXElement()` persists the element graph, whose link attributes carry Guids and names — that XML
can never be a seed surface. `SystemComponent.CanonicalHash()` therefore hashes a projected
**identity form** instead: the option attributes, the hazard content inline, and the projected
failure modes in path order, each annotated with its `ResponseNodes` occurrence-ordinal sequence
(identity form only, never persisted) so equal-content duplicate elements and one shared element
hash differently. Element names, ids, canvas metadata, link attributes, and the profile selection
never appear, and the serialization mode cannot move the hash — a component seeds identically
however it was persisted. `AssignOccurrenceIndices(components)` is the §5.5.4 reference
implementation: sort by (canonical hash, declared index), number each equal-hash bucket 0..n−1,
recompute before every run, persist nothing.

`SetupSamplers(sampleSize, componentSeed, scheme)` freezes one projection snapshot and one
`EndStateGroupLayout`, materializes the effective dependency model, then walks hazard → per mode
(the failure/non-failure coupling matrix claims the mode's first ordinal, then stage transforms,
stage response, trailing transforms) → profile transforms **last**, so the mode streams form a
stable prefix that a profile selection can never perturb. Every distinct function instance is
seeded once, at its first canonical position, with
`SeedHelpers.HashCombine(componentSeed, function.CanonicalHash(), ordinal)`, where the component
seed is `HashCombine(analysisSeed, CanonicalHash(), OccurrenceIndex)`; a shared live instance is
one knowledge quantity. Consequence functions are never walked — the N×K coupling matrix supplies
their shared percentile. `Sample(realizationIndex)` then produces the `SampledComponent` the
engine integrates ([results-catalog.md](results-catalog.md)); the full hashing and seeding
pipeline is documented in [hashing-and-seeding.md](hashing-and-seeding.md).

```csharp
SystemComponent.AssignOccurrenceIndices(components);
int componentSeed = SeedHelpers.HashCombine(analysisSeed, component.CanonicalHash(), component.OccurrenceIndex);
component.SetupSamplers(sampleSize, componentSeed, SamplingScheme.LatinHypercube);
var sampled = component.Sample(realizationIndex);
```

## Serialization, resolution, and cloning

Both `RiskSerializationMode`s persist the same graph shape; `SelfContained` writes function
content inline while `ByReference` writes `FunctionReference` markers that an
`IRiskFunctionResolver` re-attaches to the live stored instances (spec §8.3) — inline content
always wins on read, and unresolved references are recorded and reported by validation,
distinguished from "no function assigned". `RiskConnection` never self-serializes: the owning
element writes kind-prefixed dual-reference attributes — `{kind}ElementId` (Guid "D"),
`{kind}Element` (name fallback), `{kind}Port`, plus `{kind}BranchId`/`{kind}Branch` for expanded
branches — with kinds `Source`, `SecondarySource` (reserved), and `HazardSource`, an append-only
serialized contract. On load every element is constructed first and `RiskElementResolver` then
resolves the pending references: a serialized Id is authoritative and throws when stale, a
name-only reference is lenient (null flows to the dangling-connection validation), and an unknown
element type fails the load loudly rather than dropping part of a compute chain. The correlation
matrix serializes G17 row-major only under the matrix mode, and `ProfileHazardElementId` is an
append-only attribute whose absence loads forward as the primary-hazard default.

`SystemComponent.Clone()` deep-copies through the serialization round-trip (the occurrence index
is not carried); `ComponentGraph.Clone()` clones every element — clones **share element Ids**,
because a clone is the same logical element in an isolated graph copy — deep-copies wrapped
functions, and re-links every connection through the original→clone map.

## v1.1 changes versus v1.0

- The graph is the model: v1.0 stored failure modes as a collection the UI-side risk diagram
  projected from canvas topology; v1.1 persists the graph and projects the modes
  deterministically from structure.
- Response elements may chain (v1.0 forbade response→response), which is what multi-stage
  cascades are built from.
- `Clone` deep-copies via the round-trip; v1.0 clones shared live function references.
- The correlation matrix round-trips (the v1.0 read loop discarded every parsed value) and
  serializes only under the matrix mode (v1.0 lazily overwrote the user field with the derived
  matrix).
- The v1.0 response-function-uniqueness error is dropped — obsolete under inline ownership and
  occurrence indexing.
- The v1.0 name-matched `ProfileHazardFunction` reference is replaced by the structural,
  seed-inert `ProfileHazardElementId`.
- Hazard-type label mismatches are advisory warnings (v1.0 invalidated on them); labels are
  unhashed display metadata.
- Terminals carry multiple ordered consequences, and hazard bindings are structural element
  references, never labels.
