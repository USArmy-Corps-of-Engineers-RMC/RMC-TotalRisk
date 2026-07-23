# RMC.TotalRisk Model Library Architecture

> Living architectural specification for `RMC.TotalRisk.dll` — the headless .NET 10 compute library at the heart of the v1.1.0 modernization. **Authoritative home (since 2026-07-20): `docs/requirements/` in the RMC-TotalRisk repo**; the phased plan implementing this spec is [../ROADMAP.md](../ROADMAP.md). The copy at the `C:\GIT\RMC-TotalRisk-Dev` root is frozen with a pointer here, and legacy porting-source paths referenced below (e.g., `RMC-TotalRisk/RMC.TotalRisk.IO/...`) live in that Dev repo. The locked sections are the contract every cluster-port PR references.

**Status**: 2026-07-23 — **v0.15** (Phase 4b/4c landing; supersedes conflicting text below wherever it appears). The implementation ratifications and errata from landing multi-component system risk and reliability mode:

1. **§7.8 additive convolution is an exact lattice, not `EmpiricalDistribution.Convolve`.** At implementation, `Convolve` proved unusable for the zero-inflated construction: it samples continuous `PDF`s on a uniform grid, and a distribution-function jump (the "component did not fail" atom) has no finite density — any ramp-width approximation either loses the atom or corrupts the sampled density and its renormalization, and the per-stage PDF-normalize/regrid chain cannot hold the 1e-6 mean-parity gate. The engine instead bins each component's exact recorded `(mass, consequence)` pairs onto a shared consequence lattice with a **moment-preserving two-node split** plus the zero atom, and convolves the lattice mass vectors by `Fourier.FFT` (the same Numerics primitive `Convolve` uses internally) — the public kernel is **`Analyses/SystemConvolution`**. Mass and first moments are preserved exactly, so the convolved mean equals Σ component means to roundoff *by construction*; higher moments carry an O(step²) quantization bounded by `SystemConvolutionPoints ≥ 4096`. **Numerics item N8 is extended:** the follow-up is an atom-aware (discrete/mixed) convolution alongside the log-spaced grid. The lattice zero node is kept only on exhaustive streams (on defective streams it is the no-event atom, and zero-valued events quantize into it); after convolution the defective system stream probabilities are restored to the **v1.0 system-state semantics** — Fail and Excess carry the independent failure union, NonFail its complement.
2. **§7.8 the automatic γ heuristic is a deterministic quadrature probe, not a warm-up harvest.** Two implementation facts broke the ratified v0.13 mechanism: `Vegas.ConfigureForRareEvents` raises `NumberOfBins`, whose setter reallocates the importance grid — so γ must be configured **before** the warm-up, not after it — and a γ = 1 Monte Carlo warm-up cannot observe the rare failure probabilities `pTarget` needs (that blindness is the very problem the transform solves). The engine instead probes each component's annualized failure probability on the mean sample with adaptive Gauss–Kronrod (`AFP_i = ∫ P_F,i(p) dp` — the one call site where the integral's returned value is the product), once per run, ~10³ evaluations per component, deterministic; `pTarget = clamp(min_i AFP_i · Alpha, 1e-12, 1e-2)` unchanged. The warm-up itself then adapts under the active γ — strictly better than the post-warm-up ordering the v0.13 text assumed.
3. **§7.8 recording passes and mass self-normalization.** The joint path records across **five** VEGAS recording passes (`Initialize = 1`, `IndependentEvaluations = 5`; an engine constant, not an option) with `FinalEvaluations` already D-scaled, and scales every recorded mass by the reciprocal of the realized weight sum — per-pass `Σ wgt` equals the domain volume only in expectation, so self-normalization makes the exhaustive Total budget exactly one (and stays consistent if the evaluation cap truncates a pass). VEGAS is driven with `UseSobolSequence = false` and `MersenneTwister(vegasSeed)`: the current Numerics default is a Sobol sequence, which would make the driving stream seed-independent and void the §5.5 content-seed contract. The §7.3 erratum's seed formula is realized as an iterative fold **in canonical-hash order**: `systemSeed = fold(PRNGSeed; (hash_i, occurrence_i))` over the sorted components, `vegasSeed = ToPositiveSeed(HashCombine(systemSeed, "VEGAS", realizationIndex))`.
4. **§7.8 reproducibility scope of the joint method.** Component **renaming** is bit-inert (nothing keys on names); component **reordering** is statistically equivalent but *not* bit-identical — the VEGAS variates couple the hypercube dimensions, so reordering permutes which coordinate stream drives which component. This is inherent to any joint driving stream (v1.0 had the same property) and is pinned as documented behavior. The additive path is fully bit-inert under reorder + rename: content-based seeds plus the canonical-hash convolution order (item 1) remove the association-rounding sensitivity.
5. **§7.8 combination-width guardrail.** The joint integrand crosses the components' recorded pathway/branch entry lists, so `RiskAnalysis.Validate()` bounds the product of per-component worst-case widths (`SystemComponent.EstimateRecordedFailureEntries`, internal): warning above 4,096 entries per evaluation, error above 65,536 — the system-level analog of the Q-W guardrails. The legacy `tPF` double-increment is not carried forward (the failure union is the Fail stream's recorded mass; no scalar duplicates it), and the `Probability.IndependentExclusive` convergence shortcut is inherited knowingly — the deepest combinations of a high-D system can be truncated within the enumeration tolerance, and the dropped mass surfaces through the mass-balance witness rather than a silent clamp.
6. **§7.7/§4 reliability mode lands (Phase 4c) with a mode-aware validation chain.** `Validate(RiskAnalysisMode)` overloads on `FailureMode`, `ConsequenceElement`, `ComponentGraph`, and `SystemComponent` (the parameterless `Validate()` remains the risk-mode contract): reliability relaxes exactly the consequence-content requirements — consequence elements stay the structural path terminals but need no functions, the positional excess-pairing alignment is skipped, and everything present is still validated. The engine forces the effective refinement objective to `RiskIntegrand.TotalProbabilityOfFailure` in reliability mode (a consequence-free model's consequence objectives are identically zero, which would defeat the adaptivity; the configured option applies to risk mode). **The reliability results shape is the existing containers**, not a parallel tree: the annualized failure probability is the Fail stream's `TotalProbability` at every level — failure mode, component, and system (union under the additive method; the recorded union under the joint method) — with the consequence surface degenerate at zero by construction, the ensemble carrying the AFP distribution for uncertainty, and the exhaustive mass-balance warning suppressed (a consequence-free total stream's recorded mass *is* the failure probability, not a leak).

Prior status — 2026-07-22 — **v0.14** (Phase 4 engine-core landing; supersedes conflicting text below wherever it appears). The session ratifications and implementation errata from landing the 1D engine core:

1. **Q-V ratified YES** (§11 → Resolved): mixture-branch exposure enumeration applies in the mean-only AND full-MC paths. The consequence contract carries `SampleExposureBranches()` and `SampleExposureBranches(double percentile)` (the percentile overload samples every branch co-monotonically at one shared knowledge percentile — the same draw Q-N pairs) plus structural `CountExposureBranches()` for the Q-W guardrails. `CompositeConsequence.SamplingDimensions` is 0 in every mode; the standalone per-realization mixture surface (`SampleFunction(int)`, the uncertainty summary) rides an internal selector matrix generated with the identical seed fold and scheme the pre-Q-V dimension used, so pre-existing standalone and verification streams are bit-identical.
2. **Q-N resolved** (§11): the technical reference grounds the v1.0 behavior — the failure/non-failure consequence pair is drawn perfectly correlated within a mode. v1.1 realizes it as an N×max(K,1) coupling matrix on the `FailureMode` (one column per consequence position), seeded content-first: the mode claims its walk's first ordinal for the matrix (`ToPositiveSeed(HashCombine(componentSeed, fm.CanonicalHash(), ordinal))`). **§5.8.7 erratum:** consequence functions are excluded from the `SetupSamplers` walk — they need no percentile matrices; the coupling matrix supplies their shared knowledge percentile.
3. **Multi-stage response composition deferred** (new **Q-X**): end users are expected to want event-tree branch semantics, not a stage-probability product, so the engine computes single-response-stage modes only — `RiskAnalysis.Validate()` errors on a multi-stage mode (message names the event-tree phase) while authoring/serialization of multi-stage modes stays untouched.
4. **Q-U interim ratified**: risk math and LECs consume the primary consequence (`ConsequenceFunctions[0]`) only; positions ≥ 1 stay coupled through the coupling-matrix shape but are not evaluated until the multi-axis results design lands. Q-W guardrails still bound the structural cross product.
5. **Q-T deferred**: `ProfileHazardFunction` (profile-axis remap) and the legacy `Sensitivity()` surface move to the risk-diagnostics/sensitivity sessions; profiles (`HazardFrequency`/`HazardvsCEN`) land on the driving hazard axis.
6. **§5.5.3 options recipe extended (append-only)**: `RiskAnalysisOptions` hashes every compute-relevant field — the v1.0 set plus `SamplingScheme`, `Mode`, `RiskIntegrand`, `VegasTailFocusMode`, `VegasTailFocusParameter`, `SystemConvolutionPoints`, and the correlation matrix only under the correlation-matrix dependency (the Q-G precedent). `UseDefaults` is convenience metadata — `"UseDefaults"` joined the `CanonicalizationRules.ModelRules` strip list. The full option surface (including the 4b/4c fields) serializes from Phase 4 so the append-only attribute surface is written once; the options hash never feeds Monte Carlo seeds (only `PRNGSeed` does). **Naming settled:** the enum is `SystemRiskType { AdditiveRiskMethod, JointRiskMethod }` (verbatim v1.0) carried by the options property `SystemRiskMethod`; §4/§7.8's "Additive"/"Joint" prose is shorthand.
7. **§7.3 errata**: there is no per-realization seed array — knowledge sampling is fully covered by the per-function percentile matrices, and only the joint path's VEGAS needs per-realization randomness (Phase 4b: `vegasSeed(idx) = ToPositiveSeed(HashCombine(PRNGSeed, systemContentHash, idx))` over the canonical-sorted component hashes). The sampler walk seeds each **distinct function instance once** (reference identity; first canonical owner wins): a shared instance is one knowledge quantity drawing identical realizations everywhere it appears, while equal-content distinct instances get different ordinals and draw independently (Q-J preserved). The parallel realization loop uses index-owned writes with sequential post-pass reductions (flags, extents, percentile means) — bit-identical at any thread count by construction.
8. **§3/§7.7 additions**: `Results/` gains `RiskComputeFlags` (the per-realization warning flags replacing the legacy `ref bool` quartet) and internal `ResultsJson`; `Analyses/SystemConvolution` arrives with 4b. LEC output thinning is a **hybrid ladder** — half the targets log-spaced in exceedance (tail density), half linear in consequence (bounding log-log interpolation gaps across the flat bulk), first/last always retained. Interim alongside N7–N9: the competing-risks inputs and cumulative-incidence outputs are rebuilt as non-strict ascending curves (fragility plateaus at 0/1 are legitimate; the Numerics CIF factory's strict two-list construction is a follow-up item).

Prior status — 2026-07-21 — **v0.13** (risk-engine port corrections ahead of Phase 4; supersedes conflicting text below wherever it appears). These items retarget the Phase 4 engine port away from a verbatim v1.0 reproduction — the legacy compute is demonstrably wrong on LEC tails and system aggregation, and Numerics has since gained the tools to fix it. Full math in [../technical-reference/risk-integration.md](../technical-reference/risk-integration.md) and [../technical-reference/loss-exceedance-curves.md](../technical-reference/loss-exceedance-curves.md); phasing in [../ROADMAP.md](../ROADMAP.md) Phases 4 / 4b / 4c.

1. **1D quadrature switches from `AdaptiveSimpsonsRule` to `AdaptiveGaussKronrod`** (G10K21). Drop-in surface (`Integrate()`/`Integrate(List<StratificationBin>)`/`MaxDepth`/`MaxFunctionEvaluations`/`RelativeTolerance`/`StandardError`). Both 1D call sites port: the per-component risk integral and the CVaR integral. §7.3 amended; §7.7 new.
2. **LEC construction is rebuilt exactly** (new §7.7). The v1.0 200-bin log10 histogram plotted at bin midpoints, the raw-power-sum moments (catastrophic cancellation on σ/skew/kurtosis), and the midpoint-trapezoid probability-mass re-derivation are all replaced: probability mass comes from the quadrature weight, the exceedance curve is built exactly from sorted `(mass, consequence)` pairs and thinned to `LECOutputLength` only for output, and moments use a weighted streaming (Welford) accumulation. `LECOutputLength` becomes an output-resolution knob, not a compute-resolution knob.
3. **Additive system risk is redefined to assume strictly independent components** and now produces a true system LEC via FFT convolution (new §7.8). The v1.0 additive path combined only the first two moments and emitted no system LEC. Validation errors if a correlation is supplied under the additive method; the hazard correlation matrix applies to the joint method only. Zero-inflating each defective component curve makes the D-fold convolution exactly equal to enumerating all 2^D component failure/non-failure combinations, via Numerics `EmpiricalDistribution.Convolve`.
4. **Joint system risk exposes the Vegas power transform** for rare-tail capture (§7.8) and **enumerates real component failure/non-failure combinations** instead of convolving per-component conditional means (the defect the legacy `ComponentRiskOutput.vb:39` TODO named). `RiskAnalysisOptions` gains `VegasTailFocusMode`/`VegasTailFocusParameter` with a warm-up-derived γ heuristic.
5. **The adaptive integrator's objective is selectable** via a new `RiskIntegrand` enum on `RiskAnalysisOptions` (default `MeanTotalRisk`). It steers *where the adaptive refinement spends evaluations* — the v1.0 integrator's returned value is discarded; it is used as an adaptive sampler whose side effect populates the risk points. All five risk-type LECs and every risk measure are produced regardless. `TotalProbabilityOfFailure` pairs with `RiskAnalysisMode.Reliability`. §4/§7.7.
6. **Mean-only compute treats composite-mixture weights as exposure probabilities** (§6.4 amended). The v1.0 mean-only path flattens a Mixture consequence into its weighted-mean curve — mean risk correct, variance/VaR/CVaR/tail wrong. v1.1 enumerates the mixture branches as weighted exposure states so the LEC carries the true spread. New `SampleExposureBranches()` on the consequence contract; every non-composite type returns a single unit-weight branch, so nothing else in the engine changes shape.
7. **Verification policy (ratified):** because items 2/4/6 correct real v1.0 tail errors, the **means** are verified against the v1.0 oracles (unchanged, and the free regression gate), while **σ / VaR / CVaR / F-N tails** are verified against new brute-force Monte Carlo oracles, not v1.0 parity. Recorded in [../verification.md](../verification.md). New open questions Q-V/Q-W in §11.

Prior status — **v0.12** (consequence-cluster completion; supersedes conflicting text below wherever it appears):

1. **`ParametricConsequenceFunction` is named `ParametricConsequence`.** The `Function` suffix was inconsistent with every sibling concrete type (`TabularConsequence`, `ParametricResponse`, `ParametricUnivariateHazard`); the class name is the XML element name and hash typeTag, so the choice was made before first landing and is now permanent. §3, §5.5.3, §6.4, and §9 are updated in place.
2. **`CompositeConsequence` hash recipe amended and implemented as a projected identity form** (the second instance of the v0.9 `SystemComponent` exception): typeTag + `CompositeFunctionType` + entry count + per entry (effective weight, child content hash). The original recipe omitted the combine mode — Additive vs Average vs Mixture changes results and must hash; Additive projects weights as 1 (computationally inert there); the persisted form (SelfContained inline vs ByReference `FunctionReference` markers) is never the hash surface, so the serialization mode and child metadata cannot move seeds. Structural wiring follows BestFit `CompositeAnalysis` with its warts fixed (complete self-written markers, id-authoritative resolution, entry-preserving unresolved references); nesting is allowed with a circular-reference validation error (deliberate divergence from BestFit).
3. **Q-I's composite half and Q-J are resolved for the consequence composite** (see §10): declared entry order is semantic (drives sampler ordinals and the hashed order), and identical-content siblings draw independently via `HashCombine(seed, child.CanonicalHash(), ordinal)` — no occurrence-index machinery inside composites. `CompositeHazard`/`CompositeResponse` adopt both rules at Phase 9.
4. **Two v1.0-behavior ratifications**: composite child label-mismatch checks are Warnings (the Phase 3 `FailureMode` downgrade — labels are unhashed metadata and never gate compute), and the composite's legacy child-level `HazardTransform`/`ConsequenceTransform` overrides are dropped (children are live functions that own their interpolation transforms; the hash recipe lists no composite transforms).

v0.11 (2026-07-20, layer boundaries; supersedes conflicting text below wherever it appears):

1. **New normative [§8 Layer boundaries & consumer contract](#8-layer-boundaries--consumer-contract).** What the UI/App/API layers own, how they reference model objects, and what the model library still refuses. Read it before starting the UI phase; it exists so that phase does not invent its own conventions.
2. **Input functions are referenced by `Guid`, not by name.** `IRiskFunction` gains `Id` + `AssignNewId()`, serialized and stripped by `CanonicalizationRules.ModelRules` (identity, never content). BestFit's name-based references are its own documented regret — a rename or collision can silently re-resolve to a different type — and the shared framework's `NodeBase.NodeGuid` is the counter-example.
3. **Two serialization modes.** `RiskSerializationMode { SelfContained, ByReference }` with additive `ToXElement(mode)` overloads; the no-arg overload stays `SelfContained`, so headless callers, verification oracles, and the API are untouched. `ByReference` writes `<FunctionReference Id Name/>` markers resolved through the new `IRiskFunctionResolver`, which must return **live** instances — that is what stops an embedded copy from shadowing an edit made where the function is stored. Policy mirrors `RiskElementResolver`: ids authoritative and loud, names lenient and reported.
4. **The mode cannot move a seed.** `SystemComponent.CanonicalHash()` hashes projected failure modes, which always serialize inline; `FailureMode`/`ResponseStage` take no mode. Directly tested — a project's results must not depend on how the project was saved.
5. **Components are analysis-owned.** They are not independently creatable in the UI/App; `RiskAnalysis` will own them and serialize **options only**, receiving components and results through its constructor (the BestFit `new UnivariateAnalysis(dist, xElement, results)` shape). This supersedes any reading of §3 that implies a component is a standalone stored item.
6. **Change propagation.** Elements re-raise their wrapped functions' notifications and the graph forwards element changes, so a consuming layer can invalidate stale results when a shared input is edited.

v0.10 (2026-07-20, namespace reorganization; supersedes conflicting text below wherever it appears):

1. **The `Models` namespace segment is retired.** The library's public shape is now `Core` / `Core.Enums` / `Core.Interfaces` / `RiskFunctions.*` / `Systems.*` / `Analyses` / `Results`, mirroring the sibling Hydrologics library. Folders mirror namespaces exactly; the per-cluster `Support` folders (which declared their parent's namespace and therefore violated that rule) are gone. **Every enum lives in `Core.Enums`, every interface in `Core.Interfaces`** — one type per file. §3 below is rewritten to this layout and is normative.
2. **Plural namespace segments.** `Systems`, `Hazards`, `Transforms`, `Responses`, `Consequences`. A singular `System` segment would shadow the BCL `System` namespace from inside every `RMC.TotalRisk.*` namespace (CS0234) and break any consumer writing `using RMC.TotalRisk;` (CS0104); a singular `Transform` segment shadows `Numerics.Data.Transform` (this one was caught by the compiler mid-refactor, not in theory). Type names remain singular — `SystemComponent`, `TabularTransform`.
3. **Runtime type discriminators.** `HazardFunctionType`, `TransformFunctionType`, `ResponseFunctionType`, `ConsequenceFunctionType`, and `RiskElementType` are exposed as `FunctionType`/`ElementType` properties on the cluster contracts (the Hydrologics `LossMethodType MethodType` pattern), giving results labeling, the future UI, and the REST/MCP layer something to branch on besides type tests. They are **never serialized**: the `ToXElement()` element name remains the serialization and canonical-hash discriminator, so the discriminators add no hash surface. Serializing one would be a hash break — the landing checklist says so explicitly.
4. **Reliability is a mode, not an analysis type.** `ReliabilityAnalysis` is withdrawn in favor of `RiskAnalysisMode { Risk, Reliability }` on `RiskAnalysisOptions`, superseding v0.8 item 7. Both modes walk the same `ComponentGraph` over the same hazard, response, and dependency machinery; a second class would be two implementations of one traversal to keep in agreement. Reliability simply stops before the consequence stage.
5. **Results get their own namespace.** `RMC.TotalRisk.Results` (the `Hydrologics.Output` analog) replaces `Models/RiskAnalysis/Results`, keeping "the model you build" separate from "what you get back" for the Phase 14 API layer.
6. **Hash inertness of the reorganization.** No type was renamed and no serialized attribute changed, so `ToXElement()` output, canonical content hashes, and Monte Carlo seed streams are bit-identical across the move. The hash-invariance and XML round-trip suites passed unchanged and are the evidence.

v0.9 (2026-07-20, Phase 3 session amendment; supersedes conflicting text below wherever it appears):

1. **Formal risk topology inside the model library.** Each `SystemComponent` owns a **`ComponentGraph`** (`Models/RiskAnalysis/Graph/`): a typed, validated, acyclic graph of `IRiskElement` nodes — `HazardElement` (the single root, output-only), `TransformElement`, `ResponseElement`, `ConsequenceElement` (terminal, input-only) — mirroring Hydrologics `IBasinElement`/`BasinModel` (object-reference links stored on the consumer, dual Id+Name serialization through `RiskElementResolver`, Kahn topological sort, clone-map re-linking, name authority). This supersedes §2's "DAG lives in the UI layer only" row and §7.2's UI-conversion story **as follows**: the model-library graph is dependency-free (no DAG.dll reference — that red line stands); DAG.dll/DAGControls remain the UI/App *visualization* layer, and the UI's `RiskDiagram` controls bind to these model types (the `RiskDiagram` name is reserved for the UI). The analysis boundary is unchanged: `RiskAnalysis` consumes a flat `SystemComponent[]` (≤ ~20), components never interconnect, and cross-component hazard correlation stays an analysis-level matrix (engine phase).
2. **Vocabulary refinement.** v0.8's "element purge" narrows: what stays banned is the wpf-framework `ProjectInterfaces.IElement` *wrapper* lingo; the headless DAG node contract is **`IRiskElement`**/`RiskElementBase` per the Hydrologics precedent. `SystemComponent`/`FailureMode` stay concrete (no interfaces), per v0.8.
3. **`FailureModes` are projected.** `SystemComponent.FailureModes` is a fresh, deterministic projection snapshot from graph topology on every access: one `FailureMode` per `ConsequenceElement` in graph **declared order**; transforms classify into stages around responses; a response-free path is the (≤ 1) non-failure mode in canonical stage form. `AddFailureMode(FailureMode)` expands a chain-style mode into wired elements (lossless — bit-identical hash — for failure chains). `FailureMode` remains independently constructible; `Parent` wiring feeds the component hazard's labels into continuity validation.
4. **Identity vs persistence split on `SystemComponent`.** `ToXElement()` persists the element graph, whose link attributes carry Guids and names and therefore CANNOT be a hash surface; `CanonicalHash()` hashes an internal **identity form** — options + hazard content + projected failure-mode XML in path order — realizing §5.5.3 exactly. Elements and `ComponentGraph` expose no `CanonicalHash`. Pinned guarantees: element renames, `AssignNewId`, canvas moves, and description edits are hash-inert; independently built equal-content components hash identically (occurrence indices per §5.5.4 disambiguate).
5. **Response chains via `ResponseStage`.** `FailureMode` generalizes to ordered stages (path grammar `T* (R T*)* C`) plus trailing `ResponseToConsequence` transforms and an ordered `ConsequenceFunctions` list (index 0 = the primary type used for integration; all types — e.g., economic and life loss — are computed and tracked). The v1.0 members (`HazardToResponse`, `ResponseFunction`, `ConsequenceFunction`) survive as views over stage 0. A downstream response applies to both branches of the upstream response — engine semantics land with the compute phase; the structure records response order now.
6. **Structural, label-free hazard binding.** `ConsequenceElement.HazardSource` optionally references any upstream hazard/transform output on the terminal's own path (e.g., consequences given peak flow while the chain transformed flow→stage); the projection converts the reference to `(ConsequenceHazardDimension, ConsequenceHazardPosition)` — position 0 = the raw hazard, k = after the k-th stage transform; the null default resolves-on-write to the last response's input (exact v1.0 behavior); trailing transforms always fold from the bound position. This generalizes §6.5's `ConsequenceHazardBinding` enum. `SpecifiedHazard`-family labels stay unhashed display metadata — a rename can never rewire compute — and chain label mismatches are downgraded from v1.0 errors to advisory warnings. `ComponentGraph.GetAvailableHazardSources` is the discoverability API (UI/agent binding pickers), reused by validation so picker and validator agree by construction.
7. **Bivariate ports now, functions Phase 11.** Elements expose `InputCount`/`OutputCount`; every connection serializes a `SourcePort` (port 1 ↔ `HazardDimension.Secondary`); `ResponseElement.SecondaryInput` and the `SecondarySource*` attributes are reserved (error while the wrapped response is univariate); `FailureMode.HazardBinding`/`ConsequenceHazardDimension` land now (Secondary errors until bivariate hazards exist). Phase 11 changes output-arity getters and validation only — zero serialized-shape breaks.
8. **Consequence pairing and combination bookkeeping.** Fail/non-fail excess pairing is **positional** (index k ↔ k; count mismatch vs the non-failure path = error; paired label/unit mismatch = warning). `MultipleConsequences` re-derives from the last response's fan-out at projection. v1.0's response-function-uniqueness error is dropped (obsolete under inline ownership + occurrence indexing). `CorrelationMatrix` serializes G17 row-major (rows `;`-separated, values `,`-separated) **only** under `DependencyType.CorrelationMatrix` — resolves Q-G; v1.0's braced format never round-tripped (its read loop discarded every parsed value), and auto-mode derived matrices are unserialized state so lazy MVN access cannot perturb hashes. The MVN off-diagonal constants (`1 − √εmach`, `−1/(D − 1) + √εmach`) and the CommonCause/MutuallyExclusive dependency coercion port exactly.
9. **Scope moves.** Results containers (`Models/RiskAnalysis/Results`), `SampledComponent`/`SampledFailureMode`, `ComponentRiskOutput`, and `SetupSamplers`/`Sample` land with the risk-engine phase (Q-N's shared-draw coupling is designed with the compute loop). `SystemRiskType` stays Analyses-level. `ProfileHazardFunction` is deferred to the results design (new Q-T). `CreatePRNGs`/`ClearPRNGs` are not ported (content-based seeding). New Q-U tracks the engine semantics of `MultipleConsequences` under response fan-out.

v0.8 (2026-07-20, same-day amendment ratified during the Phase 1–2 planning session — its items remain ratified except where v0.9 supersedes):

1. **v1.0 API preservation.** The input-function domain surface is preserved verbatim from the v1.0 implementation (`RMC.TotalRisk.IO` C# port, cross-checked against the VB engine): property names/types/defaults, `SampleFunction` overload shapes/returns, `Min/Max*` shapes, the `Estimate()` lifecycle, and legacy enum names. The analysis layer instead adopts a growth foundation (see #7). The future UI layer maps v1.0 projects onto the v1.1 analysis API on import.
2. **Model/UI vocabulary separation — element purge, and no root abstraction.** "Element" is wpf-framework UI lingo (`ProjectInterfaces.IElement`); it is reserved for the future UI layer exactly as `RMC.BestFit.UI\Elements\` does. The kernel contract is **`IRiskFunction`** with implementation base **`RiskFunctionBase`** — replacing this doc's `IModelElement`/`ModelElementBase`/`SampledModelElement`; `CanonicalizationRules.ModelElementRules` → **`ModelRules`**. **No `IModel`/`ModelBase` root is introduced**: BestFit's `IModel` exists because its estimation engines calibrate *any model* polymorphically; TotalRisk's engine consumes functions **by role** (hazard → transform → response → consequence) and has no "any model" consumer. The genuine polymorphic abstractions are exactly: `IRiskFunction` (sampler/seed orchestration walks heterogeneous function chains), the four cluster interfaces (multiple concretes, consumed by role), and `IAnalysis` (multiple analysis types, uniform lifecycle). `SystemComponent`/`FailureMode` stay concrete classes implementing `Validate`/`ToXElement`/`CanonicalHash` directly (v1.0 parity: `SystemComponent` has `Name`+`Clone()`; `FailureMode` has `Clone()` but no `Name`).
3. **Legacy enum names, standalone files**: `DependencyType`, `FailureModeMethod`, `JointConsequenceType`, `SystemRiskType`, `RiskType`, `FunctionUncertainty` (one per file, in cluster Support folders) — replacing this doc's `FailureModeDependency`/`JointConsequencesType`/`SystemRiskMethod` names. `NonFailResponse` becomes a plain instantiable type (no singleton); non-fail identification is a type test. `TabularHazard.NoUncertainyFunction` typo is fixed to `NoUncertaintyFunction` before entering the permanent XML/hash contract. `TabularResponse.SampleResponseFunction(int)` semantic changes from seed to realization index per §5.8.
4. **Risk results go System.Text.Json** (supersedes §7.5's XElement bullet): v1.0 persisted results as compressed BinaryFormatter BLOBs (removed from .NET 9+). v1.1 results containers are redesigned JSON-first — explicit public serializable state (v1.0's `Curve` hid moments/bin parameters in private fields), `ToJson()`/`FromJson()` (+ compressed-bytes overloads), in-memory only. Model *definition* types keep `ToXElement()` as the canonical-hash identity surface. v1.0 result BLOBs are not readable; old projects re-run their analyses.
5. **Posterior injection on parametric types**: `Estimate()` bootstrap is the default; `Estimate(IList<ParameterSet>)` accepts externally fitted posteriors (BestFit UnivariateAnalysis/Bulletin17C/PointProcess reduce to Numerics `ParameterSet` lists, passed by the UI importer). `CompositeHazard` gains the same option at its phase for BestFit competing-risks/mixture/composite imports. Open question: this injection path likely supersedes the planned `BestFitUnivariateHazard` type — resolve at the composites phase (bivariate/coincident import types unaffected).
6. **Uncertainty-results contract**: every function implements `UncertaintyAnalysisResults ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9)` on `IRiskFunction`. The v1.0 app-layer code-behind visualization math (per-ordinate percentile plotting) moves into the model library. Tabular types evaluate **exact co-monotonic percentile curves** (deterministic — CI bounds `CurveSample((1∓w)/2)`, median `CurveSample(0.5)`, mean via the type's mean assembly; no simulation); parametric types surface their stored bootstrap/imported `Results` and re-slice CIs from `ParameterSets` for a different width. One machinery, three consumers: UI plots, `RiskAnalysisOptions.ConfidenceIntervalWidth`, and the REST API.
7. **Analysis-layer growth foundation**: `RiskAnalysisOptions` extraction is ratified (v1.0 option property names/defaults preserved on the options class; `EstimateMeanRiskOnly` defaults `true` as in v1.0); a **`ReliabilityAnalysis`** sibling (failure probability / AFP without consequences) joins `Analyses`; a future **`CostBenefitAnalysis`** owning a `List<RiskAnalysis>` of alternatives is the design driver for keeping every analysis fully self-contained (components + options + results in one serializable object).
8. **Placement fixes**: `SamplingScheme` lives in `Models/Support` (needed by `RiskFunctionBase`); `FunctionHelpers` lives in `Models/Support` (no types in the bare root namespace). Phase numbering references below (§9) are superseded by [../ROADMAP.md](../ROADMAP.md).

v0.7 (2026-07-20): Moved to its authoritative home in the RMC-TotalRisk repo (v1.1 development), and **§5.6 reversed**: no per-file license headers — the USACE notice lives in the repo `LICENSE` only, with Authors in each class's XML `<remarks>` (repo-bootstrap decision). v0.6 (2026-07-19): **Shared-functions decision ratified** — see [SHARED_FUNCTIONS_STRATEGY.md](SHARED_FUNCTIONS_STRATEGY.md). The input-function *math* layer becomes an expansion of `Numerics.Functions` (new migration Phase 2.0); `RMC.BestFit.dll` is **removed** from the model lib's planned dependencies (the `BestFit*` types become posterior-import types holding Numerics artifacts); and §5.5's canonical hashing switches from per-class `WriteCanonical` binary writers to **XML canonicalization** over `ToXElement()` (the mechanism Hydrologics built from this doc's v0.5 spec, adopted back). v0.5 (2026-04-30) added bivariate hazards (`ParametricBivariateHazard`, `BestFitBivariateHazard`, `BestFitTabularHazard`), bivariate-aware `SystemComponent` / `FailureMode` dimensional binding, and new types per cluster: `CompositeTransform`, `BestFitTransform`, `ParametricConsequenceFunction`, `FaultTreeResponse` (v2 placeholder). Renames `ParametricHazard` → `ParametricUnivariateHazard` and `BestFitHazard` → `BestFitUnivariateHazard`. v0.4 introduced per-function `SetupSampler`; v0.3 introduced LHS; v0.2 introduced occurrence-index seeding. §5.8, §6.5, and §7.4 remain deliberate departures from v1 needing careful review before porting begins; §5.5's review completed 2026-07-19 (v0.6 — XML canonicalization adopted).

**Audience**: engineers porting code from `RMC.TotalRisk.IO` to `RMC.TotalRisk`; future Claude sessions resuming Phase 2; the future Phase 5 REST API author.

---

## Table of Contents

1. [Purpose & non-goals](#1-purpose--non-goals)
2. [Headless constraints](#2-headless-constraints)
3. [Solution & folder layout](#3-solution--folder-layout)
4. [Public API surface](#4-public-api-surface)
5. [Cross-cutting patterns](#5-cross-cutting-patterns)
6. [Cluster architecture](#6-cluster-architecture)
7. [Risk Analysis engine](#7-risk-analysis-engine)
8. [Layer boundaries & consumer contract](#8-layer-boundaries--consumer-contract)
9. [Dependency graph](#9-dependency-graph)
10. [Migration plan](#10-migration-plan)
11. [Open decisions / tracked questions](#11-open-decisions--tracked-questions)
- [Appendix A — Canonicalization rules (v0.6)](#appendix-a--canonicalization-rules-v06)
- [Appendix B — Seed helper types](#appendix-b--seed-helper-types-semantics-unchanged-from-v05)

---

## 1. Purpose & non-goals

`RMC.TotalRisk.dll` is the headless Monte Carlo compute engine for life-safety dam and levee risk analyses. Its only job: take a typed system definition (system components, failure modes, hazard/transform/response/consequence functions, run options) plus a seed → produce typed, **reproducible** results.

**In scope**:
- All pure-compute types currently entangled in `RMC.TotalRisk.IO`.
- Monte Carlo engine with `Parallel.For` over realizations.
- `XElement` round-trip serialization (in-memory; persistence is a caller concern).
- Validation via `(bool IsValid, List<string> ValidationMessages) Validate()`.
- **Stable, content-based seeding** — resolves the v1 reproducibility bug where canvas position changes drift MC results.

**Out of scope** (call-site responsibility):
- Persistence (SQLite, file paths, encryption) — caller passes `XElement` in/out.
- UI (WPF, XAML, PropertyGrid binding, `Dispatcher`, `Bitmap` icons).
- Project model (singleton `Project.Current`, element collections, name-based function lookup).
- Diagram visualization (canvas, drag-drop, Bezier connectors) — UI concern, see DAG/DAGControls.
- Dialogs, `MessageBox`, file/save prompts.
- Logging infrastructure — the lib produces validation messages and exceptions; the caller wires whatever logger.

## 2. Headless constraints

Recap from [CLAUDE.md](CLAUDE.md), with the discipline calls each implies:

| Constraint | Implication for design |
|---|---|
| No WPF / `System.Windows.*` | TFM `net10.0`, not `net10.0-windows`. No `Dispatcher`, `DependencyProperty`, `IValueConverter`. |
| No singletons | `Project.GetInstance()` removed. References to other model objects pass via constructor or method args. |
| No direct file I/O | No `File.ReadAllText`, no `SQLiteManager`. `XElement` round-trip is the only serialization contract. |
| No SQLite / DatabaseManager / FlowGraph / ProjectInterfaces / RMC-framework UI DLLs | All stripped at port time. |
| Deterministic entry points | Reproducible runs with explicit seeds. No dialog prompts. |
| `INotifyPropertyChanged` is allowed | Passive contract; headless callers don't subscribe. Provides clean WPF data-binding for the future UI layer. |
| Allowed sibling deps | **Numerics only** (v0.6 — same red line as Hydrologics). BestFit fitted results are imported as Numerics artifacts, not via `RMC.BestFit.dll`. DAG.dll stays in the UI layer only (v0.9: the model library owns its own dependency-free risk topology — `ComponentGraph` inside `SystemComponent`; DAG.dll/DAGControls remain the UI *visualization* binding to it). |
| Bit-stable float encoding | Doubles cross machine/runtime via `BitConverter.DoubleToInt64Bits`. Format with `"G17"` + `CultureInfo.InvariantCulture`. |

## 3. Solution & folder layout

Folders mirror namespaces exactly; there are no types in the bare `RMC.TotalRisk` root namespace and no `Support` folders. Final namespace map under root namespace `RMC.TotalRisk` (v0.10):

```
src/RMC.TotalRisk/
├── Core/                                   (v0.10 — the kernel; replaces Models/Support/)
│   ├── RiskFunctionBase.cs                 (INPC scaffolding + label backing + CanonicalHash() pipeline +
│   │                                        SetupSampler/_percentiles sampler machinery)
│   ├── CanonicalContentHasher.cs           (SHA-256 over canonicalized XML; adapted from Hydrologics)
│   ├── CanonicalizationRules.cs            (audited strip rules — static ModelRules: Name/Description/Guid/positions/units)
│   ├── ByteArrayComparer.cs                (lexicographic comparer for canonical-hash sorting)
│   ├── SeedHelpers.cs                      (HashCombine: PRNGSeed × component hash × occurrence)
│   ├── SerializationUtilities.cs           (G17/InvariantCulture format + null-safe parse helpers)
│   ├── FunctionHelpers.cs                  (ForceMonotonic; GenerateSeedFromObject dropped)
│   ├── TabularUncertainty.cs               (shared co-monotonic tabular percentile machinery)
│   ├── ParametricPosterior.cs              (shared bootstrap / imported-posterior machinery)
│   ├── Enums/                              (v0.10 — EVERY enum, one per file)
│   │   ├── SamplingScheme.cs               (MonteCarlo, LatinHypercube, LatinHypercubeMedian)
│   │   ├── FunctionUncertainty.cs          (tabular hazard uncertainty axis)
│   │   ├── FailureModeMethod.cs            (JointFailures, CompetingFailures, CommonCauseFailures, MutuallyExclusive)
│   │   ├── JointConsequenceType.cs         (Additive, Average, Maximum, Minimum — legacy v1.0 name)
│   │   ├── DependencyType.cs               (Independent, PerfectlyPositive, PerfectlyNegative, CorrelationMatrix)
│   │   ├── RiskType.cs                     (Excess, Background, Total, Fail, NonFail — legacy v1.0 name)
│   │   ├── HazardDimension.cs              (Primary, Secondary — bivariate port reservation)
│   │   ├── HazardFunctionType.cs           (v0.10 runtime discriminators — NEVER serialized)
│   │   ├── TransformFunctionType.cs
│   │   ├── ResponseFunctionType.cs
│   │   ├── ConsequenceFunctionType.cs
│   │   ├── RiskElementType.cs              (Hazard, Transform, Response, Consequence)
│   │   ├── RiskAnalysisMode.cs             (Risk, Reliability — reliability is a mode, not a type)
│   │   └── SystemRiskType.cs               (AdditiveRiskMethod, JointRiskMethod — Phase 4)
│   └── Interfaces/                         (v0.10 — EVERY interface)
│       ├── IRiskFunction.cs                (THE kernel contract: INPC + Name/Description + axis labels +
│       │                                    IsDeterministic + SamplingDimensions + SetupSampler +
│       │                                    ComputeUncertaintyResults + Validate + ToXElement + CanonicalHash)
│       ├── IHazardFunction.cs
│       ├── IUnivariateHazardFunction.cs    (marker; sampling returns IUnivariateDistribution)
│       ├── IBivariateHazardFunction.cs     (Phase 11: MarginalX, MarginalY, SampleConditionalYGivenX)
│       ├── ITransformFunction.cs
│       ├── IResponseFunction.cs
│       ├── IConsequenceFunction.cs
│       ├── IRiskElement.cs                 (the risk-graph node contract; Hydrologics IBasinElement mirror)
│       ├── IRiskElementNameAuthority.cs
│       └── IAnalysis.cs                    (Phase 4; mirrors BestFit IAnalysis)
├── RiskFunctions/
│   ├── RiskFunctionFactory.cs              (closed switch on the serialized element name)
│   ├── Hazards/
│   │   ├── HazardFunctionBase.cs
│   │   ├── UnivariateHazardBase.cs
│   │   ├── BivariateHazardBase.cs          (Phase 11)
│   │   ├── WeightedHazardFunction.cs
│   │   ├── TabularHazard.cs
│   │   ├── ParametricUnivariateHazard.cs   (renamed from ParametricHazard)
│   │   ├── NonparametricHazard.cs          (Phase 7)
│   │   ├── RFAHazard.cs                    (Phase 9+)
│   │   ├── CompositeHazard.cs              (Phase 9+)
│   │   ├── ParametricBivariateHazard.cs    (Phase 11: marginal X + marginal Y + copula)
│   │   ├── BestFitBivariateHazard.cs       (Phase 11: marginals + copula + ParameterSet[] posterior)
│   │   └── BestFitTabularHazard.cs         (Phase 11: coincident-frequency X/Y/Z arrays + posterior bounds)
│   ├── Transforms/
│   │   ├── TransformFunctionBase.cs
│   │   ├── WeightedTransformFunction.cs
│   │   ├── TabularTransform.cs
│   │   ├── LinearTransform.cs              (Phase 7)
│   │   ├── PowerTransform.cs               (Phase 7)
│   │   ├── CompositeTransform.cs           (weighted average / mixture of transforms)
│   │   └── BestFitTransform.cs             (SegmentedPowerFunction + ParameterSet[] posterior import)
│   ├── Responses/
│   │   ├── ResponseFunctionBase.cs
│   │   ├── WeightedResponseFunction.cs
│   │   ├── TabularResponse.cs
│   │   ├── ParametricResponse.cs
│   │   ├── NonFailResponse.cs
│   │   ├── BivariateResponse.cs            (Phase 11; see section 6.3)
│   │   ├── CompositeResponse.cs
│   │   ├── EventTreeResponse.cs
│   │   ├── FaultTreeResponse.cs            (v2 placeholder; see section 6.3)
│   │   └── EventNodes/                     (cluster-local node contract, not a kernel contract)
│   │       ├── IEventNode.cs
│   │       ├── EventNodeBase.cs
│   │       ├── EventNodeExtensions.cs
│   │       ├── ChanceNode.cs
│   │       ├── InitiatingNode.cs
│   │       ├── RemainderNode.cs
│   │       ├── SecondaryHazardNode.cs
│   │       └── WeightedHazardLevel.cs
│   └── Consequences/
│       ├── ConsequenceFunctionBase.cs
│       ├── WeightedConsequenceFunction.cs
│       ├── TabularConsequence.cs
│       ├── ParametricConsequence.cs        (power form per USACE ER 1110-2-1156)
│       ├── CompositeConsequence.cs
│       ├── LifeSimConsequence.cs
│       └── LifeSimResult.cs
├── Systems/                                (v0.10 — the system being analyzed; the namespace root is
│   └── Components/                          reserved for a future multi-component SystemModel)
│       ├── SystemComponent.cs
│       ├── FailureMode.cs
│       ├── ResponseStage.cs
│       └── Graph/
│           ├── RiskElementBase.cs
│           ├── HazardElement.cs
│           ├── TransformElement.cs
│           ├── ResponseElement.cs
│           ├── ConsequenceElement.cs
│           ├── RiskConnection.cs
│           ├── ComponentGraph.cs
│           ├── RiskElementFactory.cs
│           ├── RiskElementResolver.cs
│           └── HazardSourceOption.cs
├── Analyses/                               (Phase 4+)
│   ├── AnalysisBase.cs                     (mirrors BestFit AnalysisBase)
│   ├── AnalysisRunCompletedEventArgs.cs
│   ├── RiskAnalysis.cs                     (RiskAnalysis : AnalysisBase, IAnalysis)
│   ├── RiskAnalysisOptions.cs              (v1.0 option names/defaults preserved: EstimateMeanRiskOnly=true,
│   │                                        Realizations=1000, PRNGSeed=12345, LECOutputLength=200,
│   │                                        ConfidenceIntervalWidth=0.9, Alpha=0.01, ConsequenceThreshold=0,
│   │                                        SystemRiskMethod/JointConsequences/ComponentHazardDependency/
│   │                                        HazardCorrelationMatrix, integration options + UseDefaults,
│   │                                        SamplingScheme, Mode)
│   └── CostBenefitAnalysis.cs              (owns a List<RiskAnalysis> of alternatives)
└── Results/                                (Phase 4)
    ├── SampledComponent.cs
    ├── SampledFailureMode.cs
    ├── ComponentRiskOutput.cs
    ├── Curve.cs
    ├── Curves.cs
    ├── RiskPoint.cs
    ├── Ensemble.cs
    ├── ComponentRealization.cs
    ├── FailureModeRealization.cs
    ├── SystemRealization.cs
    ├── ComponentResults.cs
    ├── EnsembleResults.cs
    ├── FailureModeResults.cs
    ├── SummaryRiskResults.cs
    └── SystemRiskResults.cs
```

*(v0.10: the `Models` segment and the per-cluster `Support` folders are retired; every enum lives in `Core/Enums/` and every interface in `Core/Interfaces/`. Namespace segments that could shadow a BCL or Numerics identifier are plural — `Systems`, `Hazards`, `Transforms`, `Responses`, `Consequences` — because `RMC.TotalRisk.System` would hide the BCL `System` namespace from inside every `RMC.TotalRisk.*` namespace, and a singular `Transform` segment hides `Numerics.Data.Transform`.)*

Note divergence from legacy flat `TotalRisk` namespace — every ported type's namespace changes during the port.

## 4. Public API surface

What a headless caller imports:

```csharp
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.Systems.Components;

// Build a system definition (parameters come from JSON, agentic input, etc.)
var hazard = new ParametricHazard(parentDistribution: lp3Fitted, /* ... */);
var transform = new LinearTransform(alpha: 1.0, beta: 0.5, /* ... */);
var response = new TabularResponse(orderedPairedData);
var consequence = new TabularConsequence(orderedPairedData);

var fm = new FailureMode
{
    HazardToResponse = { transform },
    ResponseFunction = response,
    ConsequenceFunction = consequence,
};
var component = new SystemComponent { HazardFunction = hazard, FailureModes = { fm } };

// Configure and run
var ra = new RiskAnalysis(new[] { component })
{
    Options = new RiskAnalysisOptions
    {
        Realizations = 10_000,
        PRNGSeed = 12345,
        SamplingScheme = SamplingScheme.LatinHypercube,   // default — see §5.8
        EstimateMeanRiskOnly = false,
        SystemRiskMethod = SystemRiskMethod.AdditiveRisk, // strictly-independent components (v0.13)
        RiskIntegrand = RiskIntegrand.MeanTotalRisk,      // adaptive-refinement objective (v0.13, §7.7)
        // Joint-method tail focus (v0.13, §7.8); ignored under the additive method:
        VegasTailFocusMode = VegasTailFocusMode.Automatic,
    }
};

await ra.RunAsync(progressReporter: null, ct: cancellationToken);

// Read results (typed, deterministic)
EnsembleResults results = ra.RiskResults!;
double meanLEC = results.SystemRiskResults[0].Excess.Mean;
```

The same `RiskAnalysis` instance is what the future WPF UI binds to: it's `INotifyPropertyChanged`, exposes `AnalysisStarting` / `AnalysisCompleted` events, and supports `CancelAnalysis()`.

## 5. Cross-cutting patterns

### 5.1 INotifyPropertyChanged

Direct implementation; no UI dependency. `ModelElementBase` provides:

```csharp
public event PropertyChangedEventHandler? PropertyChanged;
protected virtual void RaisePropertyChange(string? propertyName)
    => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
```

Setters mutate, then call `RaisePropertyChange(nameof(...))`. Headless callers don't subscribe; the event stays dormant. Future WPF UI binds normally.

### 5.2 Validation

Every model type implements:

```csharp
(bool IsValid, List<string> ValidationMessages) Validate();
```

Mirrors BestFit. Replaces the legacy `BasicMessageItem` + `Messenger` global. Severity, code, source, property name metadata is dropped. Rationale: REST/agentic callers either succeed or get a list of human-readable messages; structured codes can be added in a future v1.x if a real consumer needs them.

### 5.3 XElement serialization

Every concrete model type provides:

```csharp
public T(XElement xElement)        // ctor restores state
public XElement ToXElement();      // writes state
```

The ctor is permissive (null-safe attribute reads, `TryParse` with `NumberStyles.Any`). `ToXElement()` writes attributes for scalars and child elements for nested types. Doubles use `"G17"` + `CultureInfo.InvariantCulture`. No SQLite, no JSON, no `BinaryFormatter`.

When one type holds another (e.g., `WeightedHazardFunction.HazardFunction`), serialization writes the child's full `ToXElement()` inline. **There is no name-based lookup during deserialization** — everything is self-contained. This is the headless replacement for the legacy `Project.GetInstance().FindByName(...)` pattern.

### 5.4 Async / cancellation / progress

`RiskAnalysis : AnalysisBase, IAnalysis` exposes:

```csharp
Task RunAsync(SafeProgressReporter? progressReporter = null, CancellationToken ct = default);
void CancelAnalysis();
event EventHandler<CancelEventArgs>? AnalysisStarting;
event EventHandler<AnalysisRunCompletedEventArgs>? AnalysisCompleted;
bool IsEstimated { get; }
```

`SafeProgressReporter` lives in `Numerics.Utilities` (per BestFit's `IAnalysis`); confirmed headless-clean. The legacy `async void` is rewritten to `Task` for proper exception flow. Cancellation flows through `CancellationToken` rather than `CancellationTokenSource` mutated externally.

### 5.5 Canonical hashing and content-based seeding

This is the single biggest behavioral change vs. v1. **Read carefully — it changes every concrete model type's contract.**

#### 5.5.1 The v1 reproducibility bug

Legacy [`RiskDiagram.RefreshSystemComponents()`](RMC-TotalRisk/RMC.TotalRisk.IO/Project/Elements/Risk%20Analysis/Support/Diagram/RiskDiagram.cs) sorts nodes by `(TopPosition, LeftPosition)`. The dictionary preserves insertion order. [`RiskAnalysis.Estimate()`](RMC-TotalRisk/RMC.TotalRisk.IO/Project/Elements/Risk%20Analysis/RiskAnalysis.cs) iterates components in that order, handing each one a seed via `prng.Next()`. **Drag a node on the canvas → reorder → different seed → different MC realizations.**

Verbatim from the user: *"a user can create a risk analysis with the exact same input, but change locations in the DAG, and because of the seed dependency, they will get slightly different results."*

A Guid-based fix (one `ComponentGuid` per `SystemComponent`) would resolve canvas-reorder, but two components with identical compute parameters would still get different seeds (different Guids). The user's true ask is stronger: **same numerical/functional inputs → same seed → same results, regardless of all metadata**. The fix below delivers that.

#### 5.5.2 Content-based canonical hashing — the fix (v0.6: XML canonicalization)

> **v0.6 change.** v0.5 specified a per-class `WriteCanonical(BinaryWriter)` on every model type (~35 hand-written binary writers). Hydrologics implemented this doc's v0.5 *semantics* with a leaner *mechanism* — one central, audited canonicalization pass over each type's existing `ToXElement()` — and verified it at stochastic scale (`IdentityInvarianceVerification`). v0.6 adopts that mechanism back. Semantics are unchanged; only the byte source changes. See Hydrologics `docs/requirements/identity-and-seeding.md` and [SHARED_FUNCTIONS_STRATEGY.md](SHARED_FUNCTIONS_STRATEGY.md) D3.

The canonical hash is SHA-256 over a type's `ToXElement()` output after a **canonicalization pass**. `RiskFunctionBase` exposes (v0.8 naming; `SystemComponent`/`FailureMode` implement the same one-liner directly):

```csharp
public byte[] CanonicalHash()
    => CanonicalContentHasher.Hash(ToXElement(), CanonicalizationRules.ModelRules);
```

`CanonicalContentHasher` (adapted from `C:\GIT\Hydrologics\src\Hydrologics\Core\CanonicalContentHasher.cs`):

1. applies the audited strip rules, removing **non-compute** attributes/elements: `Name`, `Description`, `NameOnDisk`, `Guid`, `LeftPosition`, `TopPosition`, unit labels (`SpecifiedHazard`, `HazardUnit`, ...), `ChartSettings` — only content that drives the math survives;
2. encodes the surviving tree with an injective, length-prefixed binary encoding (attributes ordinally sorted; owned-child order preserved as semantic);
3. hashes with SHA-256.

Element names play the role v0.5 assigned to type tags (two different types with identical numeric values cannot collide). Doubles are already bit-stable in XML via the `"G17"` + `CultureInfo.InvariantCulture` convention (§5.7), which round-trips ±0, NaN, infinity, and denormals exactly.

**Discipline this imposes**: `ToXElement()` is now the identity surface. Renaming a serialized attribute, reordering owned children, or changing numeric formatting **moves every affected hash and re-rolls seeds**. Serialization is therefore append-only. Every new model property must be classified at landing time — compute-relevant (hashed) or metadata (added to `CanonicalizationRules`) — and covered by the kitchen-sink rename/reorder invariance test (the Hydrologics landing-checklist pattern).

#### 5.5.3 Per-cluster canonical content

Architecture is contract. The tables below enumerate each type's **compute-relevant content**. Under v0.6 they are read as: "typeTag" → the `ToXElement()` element name; each listed field → an attribute/child *retained* by the canonicalization pass; everything else on the element is stripped. Each cluster's port-PR lands its `CanonicalizationRules` entries and hash-invariance tests matching this:

**Transforms**:

| Type | Canonical fields (in declared order) |
|---|---|
| `LinearTransform` | typeTag, Alpha, Beta, IsUncertain, [Sigma if uncertain], Minimum, Maximum |
| `PowerTransform` | typeTag, Alpha, Beta, Xi, IsUncertain, [Sigma if uncertain], IsInverse, Minimum, Maximum |
| `TabularTransform` | typeTag, HazardTransform, TransformTransform, SortOrder, ordinate count, per-ordinate (x, dist-type tag, dist params) |
| `CompositeTransform` | typeTag, CompositeFunctionType, weighted-list count, per entry (weight, sub.CanonicalHash) |
| `BestFitTransform` | typeTag, imported `RatingCurve` posterior bytes |
| `WeightedTransformFunction` | typeTag, weight, transformFunction.CanonicalHash |

**Univariate hazards**:

| Type | Canonical fields |
|---|---|
| `ParametricUnivariateHazard` | typeTag, fitted `Results.UncertaintyAnalysisResults` posterior parameter sets |
| `BestFitUnivariateHazard` | typeTag, source-analysis-type tag, imported posterior bytes |
| `NonparametricHazard` | typeTag, EffectiveRecordLength, HazardTransform, ProbabilityTransform, ordinate count, ordinate parameters |
| `TabularHazard` | typeTag, transforms, ordinate count, ordinate parameters |
| `RFAHazard` | typeTag, regional dataset hash, parametric model parameters |
| `CompositeHazard` | typeTag, DependencyType, weighted-list count, per entry (weight, sub.CanonicalHash) |
| `WeightedHazardFunction` | typeTag, weight, hazardFunction.CanonicalHash |

**Bivariate hazards** (new in v1.1.0):

| Type | Canonical fields |
|---|---|
| `ParametricBivariateHazard` | typeTag, MarginalX.CanonicalHash, MarginalY.CanonicalHash, copulaTypeTag, copulaParams, SecondaryIntegrationBins |
| `BestFitBivariateHazard` | typeTag, imported BivariateAnalysis posterior bytes, SecondaryIntegrationBins |
| `BestFitTabularHazard` | typeTag, imported (X, Y, Z) coincident-frequency table bytes |

**Responses**:

| Type | Canonical fields |
|---|---|
| `ParametricResponse` / `TabularResponse` / `NonFailResponse` | analogous to corresponding hazard types |
| `BivariateResponse` | typeTag, surface ordinates, PrimaryHazardType, SecondaryHazardType |
| `EventTreeResponse` | typeTag + post-order DAG traversal: per node, type tag + parameters; children sorted by canonical hash |
| `FaultTreeResponse` | (v2 placeholder; recipe deferred — Q-O) |
| `CompositeResponse` | typeTag, DependencyType, weighted-list count, per entry (weight, sub.CanonicalHash) |
| `WeightedResponseFunction` | typeTag, weight, responseFunction.CanonicalHash |

**Consequences**:

| Type | Canonical fields |
|---|---|
| `TabularConsequence` | typeTag, ordinates |
| `LifeSimConsequence` | typeTag, imported `UncertainOrderedPairedData` ordinates |
| `ParametricConsequence` | typeTag, Alpha, Beta, Threshold, UpperBound, IsUncertain, [SigmaAlpha, SigmaBeta if uncertain] |
| `CompositeConsequence` | typeTag, **CompositeFunctionType**, weighted-list count, per entry (effective weight — coerced to 1 under Additive — and sub.CanonicalHash) — a **projected identity form** (the second instance of the ratified `SystemComponent` exception): the persisted form is never the composite's hash surface, so serialization mode and child metadata cannot move the hash. *(Amended at implementation, 2026-07-21: the original row omitted the combine mode, but Additive vs Average vs Mixture changes results and must hash; entry order is hashed — declared order is semantic.)* |
| `WeightedConsequenceFunction` | (no hash surface of its own — the owning composite projects weight + child hash per entry) |

**Risk analysis**:

| Type | Canonical fields |
|---|---|
| `FailureMode` | typeTag, HazardBinding enum, ConsequenceHazardBinding enum, HazardToResponse list, ResponseToConsequence list, ResponseFunction.CanonicalHash, ConsequenceFunction.CanonicalHash, MultipleConsequences flag |
| `SystemComponent` | typeTag, HazardFunction.CanonicalHash, HazardThreshold, FailureModeMethod, JointConsequences, FailureModeDependency, CorrelationMatrix bytes, FailureModes count, [FailureModes[i].CanonicalHash in declared order] |
| `RiskAnalysisOptions` | typeTag, Realizations, PRNGSeed, SamplingScheme, EstimateMeanRiskOnly, ConfidenceIntervalWidth, LECOutputLength, ConsequenceThreshold, Alpha, SystemRiskMethod |

#### 5.5.4 Seed derivation: the independence/stability paradox

Naively combining `PRNGSeed` with `component.CanonicalHash()` alone is INCORRECT. It satisfies cross-analysis stability (rename-invariant, reorder-invariant) but breaks **within-analysis independence**: two components with identical compute parameters in the same analysis would receive identical seeds, giving perfectly positively-correlated knowledge-uncertainty draws. That is wrong — the statistical model assumes independent uncertainty per function instance.

The two requirements appear contradictory:

| Requirement | Implies |
|---|---|
| Two analyses with the same multiset of components → same MC results | Seed must depend on content alone |
| Two identical-content components in the same analysis → independent samples | Seed must NOT depend on content alone |

They are reconciled by introducing a third axis: the **occurrence index within the multiset of identical-content components**.

For each component in the analysis, assign an `OccurrenceIndex` defined as:

> *the number of OTHER components in this analysis that have the same `CanonicalHash` and appear before this component in the canonical ordering (sort by `CanonicalHash`, then by declared array index for ties).*

Equivalently: for each unique canonical hash `h`, the `n_h` components with that hash get occurrence indices `0, 1, …, n_h − 1` in stable-sorted order.

Each component's seed is then:

```csharp
componentSeed = SeedHelpers.HashCombine(
    Options.PRNGSeed,
    component.CanonicalHash(),
    component.OccurrenceIndex);
```

```csharp
// In RiskAnalysis.RunAsync(), before any sampling:
AssignOccurrenceIndices(Components);  // populates each component's OccurrenceIndex

foreach (var component in Components)
{
    int componentSeed = SeedHelpers.HashCombine(
        Options.PRNGSeed, component.CanonicalHash(), component.OccurrenceIndex);
    component.CreatePRNGs(componentSeed, Options.Realizations);
}

private static void AssignOccurrenceIndices(IReadOnlyList<SystemComponent> components)
{
    var byHash = new Dictionary<string, int>();  // hash hex → next occurrence
    var sorted = components
        .Select((c, declaredIdx) => (c, declaredIdx))
        .OrderBy(t => t.c.CanonicalHash(), ByteArrayComparer.Instance)
        .ThenBy(t => t.declaredIdx)
        .ToArray();
    foreach (var (c, _) in sorted)
    {
        var key = Convert.ToHexString(c.CanonicalHash());
        var occ = byHash.TryGetValue(key, out var v) ? v : 0;
        c.OccurrenceIndex = occ;
        byHash[key] = occ + 1;
    }
}

public static int HashCombine(int globalSeed, byte[] componentHash, int occurrenceIndex)
{
    Span<byte> tail = stackalloc byte[8];
    BinaryPrimitives.WriteInt32LittleEndian(tail[..4], globalSeed);
    BinaryPrimitives.WriteInt32LittleEndian(tail[4..], occurrenceIndex);
    using var sha = SHA256.Create();
    sha.TransformBlock(tail.ToArray(), 0, 8, null, 0);
    sha.TransformFinalBlock(componentHash, 0, componentHash.Length);
    return BinaryPrimitives.ReadInt32LittleEndian(sha.Hash!.AsSpan(0, 4));
}
```

`OccurrenceIndex` is a runtime-computed property on `SystemComponent` (not persisted to XElement; recomputed at the start of every `RunAsync`). It does not enter the canonical hash itself — it modifies seeding, not identity.

#### 5.5.5 Why this works

**Within-analysis independence** — Two components A1, A2 with identical `CanonicalHash` get distinct occurrence indices (0 and 1) → distinct master seeds → independent SHA256-derived sub-seeds for posterior draws. Knowledge uncertainty samples have correlation ≈ 0 in expectation, matching v1 behavior.

**Cross-analysis stability** — Two analyses with the same multiset of canonical hashes (e.g., both contain `{A, A, B}`) produce the same multiset of `(hash, occurrence)` tuples — `{(A,0), (A,1), (B,0)}` — so the same set of three seeds. The seeds may be assigned to differently-named components in different declared orders, but since identical-content components are mathematically indistinguishable, the total MC contribution is identical.

Worked example. Project P1 declares `[A1, A2, B]`; Project P2 declares `[B, A2′, A1′]` where all four A's have identical content:

| Project | Canonical-sort + declared-tiebreak | (hash, occ) tuples | Seeds |
|---|---|---|---|
| P1 | `[A1, A2, B]` | `(A,0)→A1, (A,1)→A2, (B,0)→B` | `s_A0, s_A1, s_B0` |
| P2 | `[A2′, A1′, B]` | `(A,0)→A2′, (A,1)→A1′, (B,0)→B` | `s_A0, s_A1, s_B0` |

Identical seed sets. Total contribution `Σ MC(A, s_A0) + MC(A, s_A1) + MC(B, s_B0)` is the same in both projects.

**Robust under canvas/rename/description edits** — none of these enter the canonical hash or the occurrence index. Identical seeds, identical results.

**Robust under unrelated additions** — adding a new component with a *different* canonical hash leaves all existing `(hash, occurrence)` tuples untouched. Existing components keep their seeds. Only the new component's contribution is added.

**Adding/removing a duplicate is structurally meaningful** — adding a second copy of an existing component bumps the occurrence indices for all existing peers with the same hash. Their seeds change. This is correct: the analysis is now genuinely different (more weight on that risk path).

**Joint-risk combination matrix** (`_eCombos`) — column `k` corresponds to the component at canonical-sorted-then-declared-tiebreak position `k`. Identical-content components occupy adjacent columns; their inclusion-exclusion roles are interchangeable, so the integration result is invariant under their swap.

**Within a component**, the existing `_prng.Next()` cascade through failure modes / transforms / leaf samplers in declared order is preserved. Two failure modes with identical content within the same component already get distinct sub-seeds via consecutive `prng.Next()` calls — that within-component independence does not require an occurrence-index scheme.

#### 5.5.6 Caveat: failure-mode order within a component

The component's canonical hash includes failure modes **in declared order** (per recipe table in §5.5.3). For `FailureModeMethod = CompetingFailures` / `MutuallyExclusive`, declared order is mathematically meaningful (drives correlation-matrix indexing and exclusion logic). For `FailureModeMethod = JointFailures`, FM order is mathematically irrelevant — but this scheme treats `[F1, F2]` and `[F2, F1]` as different content. See open question Q-I.

#### 5.5.7 Properties guaranteed

| Edit | Within-analysis result |
|---|---|
| Drag node on canvas, change `LeftPosition`/`TopPosition` | identical |
| Rename a component or any function | identical |
| Edit Description | identical |
| Reorder the components array in the analysis | identical |
| Add an unrelated component (different canonical hash) | existing components' contributions identical |
| Re-save and reload the project file | identical |
| Edit a numerical parameter (Alpha, Sigma, an ordinate value) | different (correctly — math changed) |
| Toggle `IsUncertain` | different (structural change in sampling) |
| Add a duplicate of an existing component | existing peer with same hash gets a new seed (its occurrence index moved); correct, the analysis changed |
| Delete one of two identical components | surviving peer's seed changes; correct, the analysis changed |

| Cross-analysis comparison | Result |
|---|---|
| Two analyses, same multiset of canonical hashes, any declared order | identical |
| Two analyses, same hashes but different `Realizations` or `PRNGSeed` | different (correctly — run options changed) |

| Within-analysis independence check | Behavior |
|---|---|
| Two components with identical canonical hash, occurrence 0 and 1 | independent posterior draws (correlation ≈ 0 across 10k+ realizations) |
| Two failure modes with identical content within the same component | independent draws via existing `_prng.Next()` cascade |
| Two transforms with identical content in the same `HazardToResponse[]` chain | independent draws via existing `prng.NextDouble()` cascade |

#### 5.5.8 Trade-off acknowledged

Editing a single numeric parameter changes the canonical hash → changes the MC seed → changes the realization noise on top of the parameter sensitivity. At ≥10k realizations this is negligible vs. the parameter-driven signal. A future architectural extension (out of Phase 2 scope) could expose a "seed-stable" mode that pins the seed across small parameter perturbations for sensitivity studies.

#### 5.5.9 Verification

Every cluster's `RMC.TotalRisk.Verification` parity test asserts:

1. **Reproducibility under metadata edits**: same compute inputs + same `PRNGSeed` + arbitrary irrelevant edits (rename, reorder, position change) → bit-identical results.
2. **Within-analysis independence**: an analysis with two identical-content components → posterior parameter draws across the two components have empirical correlation ≈ 0 (within 10k-realization noise band).
3. **Cross-project equivalence**: two `RiskAnalysis` instances with structurally identical component multisets but different project files / different declared orders / different names → identical aggregate results.
4. **Sensitivity to compute inputs**: change one numeric parameter → results differ in expected direction.
5. **Legacy parity tolerance**: scenario-by-scenario, results match legacy VB `Test_TotalRisk` outputs within agreed tolerance (relative, not bit-exact, since legacy seeds will differ — the parity is statistical convergence at large `Realizations`).

### 5.6 File header

v0.7 (2026-07-20): **No per-file license headers.** Files start with `using` directives; the USACE notice, conditions, and disclaimer live in the repo `LICENSE` file only; every class carries the Authors block in its XML `<remarks>`. This adopts the Hydrologics/Numerics convention and reverses the earlier ruling.

> *(v0.5, superseded)*: every `.cs` file opened with the 29-line USACE notice — "TotalRisk's mission is life-safety; the legal disclaimer rides with every file." The disclaimer now rides with the distribution via `LICENSE`, per the redistribution conditions themselves.

### 5.7 Numeric formatting

- `CultureInfo.InvariantCulture` for parse and format (no locale drift).
- `"G17"` format specifier for doubles in `ToXElement()` (round-trip-exact).
- `BitConverter.DoubleToInt64Bits` for canonical-hash byte encoding (handles ±0, NaN, infinity).

### 5.8 Sampling: per-function LHS via `SetupSampler`

Legacy v1 draws every knowledge-uncertainty percentile from a `Random` instance — independent uniform Monte Carlo. At N=1000–10000 realizations the standard error scales as N⁻¹ᐟ². Latin Hypercube Sampling stratifies each marginal and typically cuts variance 5–50× at the same N. LHS at N=1000 generally matches MC at N=10000 for risk integrals.

The Numerics library exposes LHS at [`Numerics/Sampling/LatinHypercube.cs`](file://C:/GIT/numerics/Numerics/Sampling/LatinHypercube.cs):

```csharp
public static double[,] LatinHypercube.Random(int sampleSize, int dimension, int seed = -1);
public static double[,] LatinHypercube.Median(int sampleSize, int dimension, int seed = -1);
```

Each column is independently Fisher–Yates-shuffled, so columns are uncorrelated. The MersenneTwister seed makes the matrix fully reproducible.

#### 5.8.1 Design principle

**Each function owns its sampler.** Composites, event trees, and leaf functions all expose a uniform `SetupSampler(N, seed, scheme)` method that pre-allocates the function's own `N × D` percentile matrix, where `D` is the function's intrinsic sampling dimension. At realization time, `SampleFunction(int realizationIndex)` reads row `realizationIndex` of that matrix.

This replaces the per-component `PercentileQueue` model from earlier drafts. Three benefits:

1. Composites and event trees get LHS variance reduction *inside* their internal sampling, not just at the boundary.
2. No queue threading or dimension-count bookkeeping at the component level.
3. Each function manages its own state, keeping sampling logic local to where the math lives.

#### 5.8.2 Interface contract

Every function interface — `IHazardFunction`, `ITransformFunction`, `IResponseFunction`, `IConsequenceFunction` — gains:

```csharp
/// <summary>Number of independent uniform draws this function consumes per realization.</summary>
int SamplingDimensions { get; }

/// <summary>
/// Pre-allocate the per-realization sampler. Idempotent; safe to call before every analysis run.
/// Allocates an N×D percentile matrix for this function and recursively sets up sub-function samplers.
/// </summary>
void SetupSampler(int sampleSize, int seed, SamplingScheme scheme);

/// <summary>
/// Returns the realization-index-th sampled function. SetupSampler() must be called first.
/// </summary>
IUnivariateFunction SampleFunction(int realizationIndex);
```

The legacy `SampleFunction()` (mean) and `SampleFunction(double percentile)` overloads are preserved for sensitivity analysis and ad-hoc queries; they don't require `SetupSampler`.

#### 5.8.3 Base implementation

The shared base `Models/Support/RiskFunctionBase.cs` (v0.8 naming) allocates the matrix uniformly across schemes:

```csharp
public abstract class RiskFunctionBase : IRiskFunction
{
    protected double[,]? _percentiles;     // null when SamplingDimensions == 0

    public abstract int SamplingDimensions { get; }

    public virtual void SetupSampler(int N, int seed, SamplingScheme scheme)
    {
        int D = SamplingDimensions;
        _percentiles = D == 0 ? null : scheme switch
        {
            SamplingScheme.LatinHypercube       => LatinHypercube.Random(N, D, seed),
            SamplingScheme.LatinHypercubeMedian => LatinHypercube.Median(N, D, seed),
            SamplingScheme.MonteCarlo           => IndependentUniform(N, D, seed),
            _ => throw new NotSupportedException()
        };
    }

    protected double Percentile(int realizationIndex, int dimension)
        => _percentiles![realizationIndex, dimension];
}
```

`MonteCarlo` uses the same matrix shape but fills it with independent uniform draws — preserves API uniformity, gives legacy behavior on opt-in.

#### 5.8.4 Per-function dimensions

| Function | D | Notes |
|---|---|---|
| `LinearTransform`, `PowerTransform` | 1 if `IsUncertain` else 0 | sigma draw |
| `TabularTransform` | 1 | uncertain ordinate percentile |
| `ParametricHazard`, `BestFitHazard`, `ParametricResponse` | 0 | bootstrap index lookup; posterior pre-computed |
| `NonparametricHazard`, `TabularHazard`, `RFAHazard` | 1 | percentile-driven ordinate |
| `TabularResponse`, `NonFailResponse` | 1 | percentile lookup |
| `BivariateResponse` | 0 | deterministic |
| `EventTreeResponse` | direct-chance-node count + unique-response-function count | see §5.8.6 |
| `TabularConsequence`, `LifeSimConsequence` | 1 | percentile-driven `UncertainOrderedPairedData` |
| `CompositeHazard`, `CompositeConsequence` | sub-function count (+ 1 if Mixture mode) | one mixture-selector per sub |
| `CompositeResponse` | sub-function count + (1 if Mixture mode) | per the legacy implementation |

Bootstrap-driven hazards (`ParametricHazard`, `BestFitHazard`) and the bootstrap response (`ParametricResponse`) use `SampleFunction(int idx)` to look up the idx-th pre-computed posterior parameter set. Their `SamplingDimensions` is 0; their `SetupSampler` records `N` for index-bound checks but allocates no matrix.

#### 5.8.5 Composite recursion

Each composite recursively initializes its sub-functions. Sub-seeds derive from `(parentSeed, ordinal, sub.CanonicalHash)` so identical-content siblings get different seeds:

```csharp
public override int SamplingDimensions
    => (CompositeFunctionType == CompositeFunctionType.Mixture ? 1 : 0)
       + HazardFunctions.Sum(w => 0);   // composite's own dims; subs counted separately

public override void SetupSampler(int N, int seed, SamplingScheme scheme)
{
    base.SetupSampler(N, seed, scheme);   // own mixture-selector dim if any
    for (int i = 0; i < HazardFunctions.Count; i++)
    {
        int childSeed = SeedHelpers.HashCombine(
            seed, i, HazardFunctions[i].HazardFunction.CanonicalHash());
        HazardFunctions[i].HazardFunction.SetupSampler(N, childSeed, scheme);
    }
}

public IUnivariateDistribution SampleFunction(int idx)
{
    var subs = HazardFunctions.Select(w => w.HazardFunction.SampleFunction(idx)).ToList();
    return BuildMixture(subs, GetWeights(), MixtureSelector(idx));
}

private double? MixtureSelector(int idx)
    => CompositeFunctionType == CompositeFunctionType.Mixture
       ? Percentile(idx, dimension: 0)
       : null;
```

#### 5.8.6 EventTreeResponse: LHS-driven traversal

The legacy `SampleFunction(int seed)` does `new Random(seed)` and walks the tree, calling `rnd.NextDouble()` once per direct chance-node child of the initiating node and once per unique referenced `ResponseFunction`. Both kinds of draws fit cleanly into a single LHS matrix.

```csharp
public override int SamplingDimensions
    => CountDirectChanceNodes() + CountUniqueResponseFunctionReferences();

public override void SetupSampler(int N, int seed, SamplingScheme scheme)
{
    base.SetupSampler(N, seed, scheme);

    int sub = 0;
    foreach (var rf in UniqueResponseFunctionsReferenced())
    {
        int childSeed = SeedHelpers.HashCombine(seed, sub++, rf.CanonicalHash());
        rf.SetupSampler(N, childSeed, scheme);
    }
}

public IUnivariateDistribution SampleFunction(int idx)
{
    var clone = _eventTree.CloneTree();
    int dim = 0;
    foreach (var node in clone.DirectChanceChildren())
        node.SamplePercentile = Percentile(idx, dim++);
    foreach (var rfRef in clone.UniqueResponseFunctionReferences())
        rfRef.SamplePercentile = Percentile(idx, dim++);
    return clone.ResponseFunction(idx);     // tree traversal now uses pre-set percentiles
}
```

The tree's internal traversal (in `ChanceNode.SampleNode`, `InitiatingNode.ResponseFunctionProbabilities`, etc.) reads the pre-set `SamplePercentile` on each node instead of calling into a `Random`. Variance reduction extends through the entire event tree.

#### 5.8.7 Component-level orchestration

`SystemComponent` walks its function tree once at the start of `RunAsync`, assigning a structural ordinal to each function and deriving each function's seed from the component's master seed (§5.5.4):

```csharp
public void SetupSamplers(int N, int componentSeed, SamplingScheme scheme)
{
    int ord = 0;
    HazardFunction.SetupSampler(N, FunctionSeed(componentSeed, ord++, HazardFunction), scheme);
    foreach (var fm in FailureModes)
        ord = fm.SetupSamplers(N, componentSeed, ord, scheme);
}

private static int FunctionSeed(int componentSeed, int ordinal, IModelElement fn)
    => SeedHelpers.HashCombine(componentSeed, ordinal, fn.CanonicalHash());
```

`FailureMode.SetupSamplers` calls `SetupSampler` on each of its transforms, its response function, and its consequence function, threading the ordinal counter so each function gets a unique seed.

Per realization, the component samples without any percentile threading:

```csharp
public SampledComponent Sample(int idx, FailureMode? nfMode)
{
    var hazard = HazardFunction.SampleFunction(idx);
    var sampled = FailureModes.Select(fm => fm.Sample(idx, nfMode)).ToList();
    return new SampledComponent(this, hazard, sampled);
}
```

#### 5.8.8 Reproducibility

Same `PRNGSeed` + same `SamplingScheme` + same component canonical hashes → bit-identical LHS matrices at every level → bit-identical results. The §5.5 guarantees (rename-invariant, position-invariant, occurrence-distinguished) carry through unchanged. Switching `SamplingScheme` is the only edit that changes results without changing the underlying math; expose it explicitly via `RiskAnalysisOptions.SamplingScheme` so the user opts in consciously.

```csharp
public enum SamplingScheme
{
    /// <summary>Independent uniform draws; legacy v1 behavior. Variance ∝ 1/√N.</summary>
    MonteCarlo,
    /// <summary>Default. LHS with random placement within bins (unbiased).</summary>
    LatinHypercube,
    /// <summary>LHS with median bin centers; deterministic per seed, useful at very small N.</summary>
    LatinHypercubeMedian,
}
```

Default: `LatinHypercube`.

#### 5.8.9 Verification

`RMC.TotalRisk.Verification` adds:

1. **Variance reduction**: same scenario at N=1000 with `MonteCarlo` vs. `LatinHypercube`, 50 repeated runs each. LHS empirical standard error of LEC mean ≥ 3× lower than MC.
2. **Reproducibility**: same seed + same scheme → bit-identical results.
3. **MC parity**: `SamplingScheme.MonteCarlo` reproduces legacy v1 statistical convergence at large N within tolerance.
4. **Dimension-count audit**: for every cluster parity scenario, assert `function.SamplingDimensions` matches actual `_percentiles` consumption in `SampleFunction(idx)`. Off-by-one in any cluster's port surfaces here.

## 6. Cluster architecture

### 6.1 Hazard Function — cluster #1

Path: `Models/HazardFunctions/`. **Cluster #1 of the migration** per user instruction. Split into univariate and bivariate sub-clusters.

#### 6.1.1 Univariate hazards

Six concrete types. The two imported/posterior types are renamed for clarity:

- `ParametricUnivariateHazard` (renamed from `ParametricHazard`) — user-defined parameters bootstrapped to a posterior.
- `BestFitUnivariateHazard` (renamed from `BestFitHazard`) — posterior-import type for any BestFit univariate analysis (was previously locked to `BayesianEstimation`). v0.6: holds Numerics artifacts (`ParentDistribution` + `ParameterSet[]` / `UncertaintyAnalysisResults`) passed in already-parsed — no `RMC.BestFit.dll` reference; `.rmcbf` reading (SQLite) is a UI-layer concern.
- `NonparametricHazard` — empirical CDF + Weibull extrapolation.
- `TabularHazard` — pure tabular paired data.
- `RFAHazard` — Regional Frequency Analysis.
- `CompositeHazard` — weighted mixture or competing risks over `WeightedHazardFunction[]` of any univariate types.

**Recommendation declined**: do NOT collapse `Parametric*` and `BestFit*` into a single `UnivariateHazard` with a flag. The two have different lineage (user-defined parameters vs. imported posterior bytes), different validation rules, different XElement schemas, and different canonical-hash recipes (parameters vs. posterior bytes). Unifying via a flag would create branching everywhere. They share `UnivariateHazardBase` for genuine commonality (both produce a posterior-indexed `IUnivariateDistribution` from `SampleFunction(int idx)`). Mirrors BestFit's own split between `UnivariateDistribution` (model) and the analyses that estimate it.

#### 6.1.2 Bivariate hazards (new in v1.1.0)

Three concrete types support compound-hazard analyses (e.g., flood depth × flood duration, wind speed × wind direction, primary flow × tributary contribution):

- `ParametricBivariateHazard` — user picks marginal X + marginal Y from existing univariate hazard types, manually sets copula type and parameters. Mirrors BestFit's `BivariateAnalysis`.
- `BestFitBivariateHazard` — posterior-import of a fitted `BivariateAnalysis` (Numerics marginals + copula + `ParameterSet[]` posterior; the UI layer reads the `.rmcbf`).
- `BestFitTabularHazard` — imports a `CoincidentFrequencyAnalysis` result as primitive (X, Y, Z[i,j]) arrays + posterior bounds. Stores the coincident-frequency table directly; no internal integration needed.

Common contract `IBivariateHazardFunction`:

```csharp
public interface IBivariateHazardFunction : IHazardFunction
{
    /// <summary>The marginal X (primary) hazard distribution.</summary>
    IUnivariateHazardFunction MarginalX { get; }

    /// <summary>The marginal Y (secondary) hazard distribution.</summary>
    IUnivariateHazardFunction MarginalY { get; }

    /// <summary>Number of integration bins for Y given X. Default 50.</summary>
    int SecondaryIntegrationBins { get; set; }

    /// <summary>
    /// Returns the conditional Y | X discretization at the given X hazard level for the
    /// realization-index-th sampled copula. Each entry is (Y value, conditional probability weight).
    /// Weights sum to 1 across the returned array.
    /// </summary>
    IReadOnlyList<(double Y, double Weight)> SampleConditionalYGivenX(int realizationIndex, double xHazardLevel);
}
```

`IHazardFunction.SampleFunction(int idx)` continues to return the X marginal as `IUnivariateDistribution`. The risk-analysis integrator switches to nested integration when `hazard is IBivariateHazardFunction` (see §7.4).

**`ParametricBivariateHazard`** — user wires existing univariate hazards as marginals, picks a copula from `Numerics.Distributions.Copulas` (Gaussian, Gumbel, Clayton, Frank, Student-t, etc.), supplies copula parameters. The copula is sampled via parameter uncertainty (if any); marginals propagate their own uncertainty. The realization index drives all three samplers together.

**`BestFitBivariateHazard`** — imports fitted marginals + copula + MCMC posterior from BestFit. The realization index looks up the i-th MCMC sample.

**`BestFitTabularHazard`** — imports the (X, Y, Z) coincident-frequency table. `SampleConditionalYGivenX(idx, x)` interpolates the Z column at x to get the Y | X distribution; uncertainty comes from MCMC sample bounds on Z. Suitable when an external coincident analysis has already been done in BestFit.

#### 6.1.3 Original interface

Contract for the cross-cutting `IHazardFunction`:

```csharp
public interface IHazardFunction : IModelElement
{
    string SpecifiedHazard { get; set; }
    string HazardUnit { get; set; }
    bool IsDeterministic { get; }

    // Sampling contract — see §5.8
    int SamplingDimensions { get; }
    void SetupSampler(int sampleSize, int seed, SamplingScheme scheme);
    IUnivariateDistribution SampleFunction(int realizationIndex);

    // Mean and ad-hoc percentile sampling — used for sensitivity / mean-only runs
    IUnivariateDistribution SampleFunction();
    IUnivariateDistribution SampleFunction(double percentile);

    double MinHazard(bool meanOnly);
    double MaxHazard(bool meanOnly);
}
```

#### 6.1.4 Decoupling moves (every cluster)

- Strip `[Browsable]` / `[DisplayName]` / `[Category]` / `Bitmap ElementImage` / `ChartSettings`.
- Replace `ElementBase` inheritance → derive from `HazardFunctionBase : ModelElementBase`.
- Delete `Open()` / `Save()` / `Delete()` / `CopyFromExternal()` SQLite paths → `ToXElement()` / ctor-from-`XElement` only.
- Replace `Messenger` calls + `BasicMessageItem` collection with `Validate()` returning `(bool, List<string>)`.
- Remove `Project.GetInstance()` references; pass instances directly via ctor / property.

What stays in UI: bitmap resources, PropertyGrid metadata attributes, validation message UI surfacing, file-import dialogs.

### 6.2 Transform Function — cluster #2

Path: `Models/TransformFunctions/`. Five concrete types.

> **v0.6**: this cluster is a set of **thin wrappers over the expanded `Numerics.Functions` toolkit** (Phase 2.0 — [SHARED_FUNCTIONS_STRATEGY.md](SHARED_FUNCTIONS_STRATEGY.md) §4): `LinearTransform` → `LinearFunction`, `PowerTransform` → `PowerFunction`, `TabularTransform` → `TabularFunction`, `CompositeTransform` → `CompositeFunction`, `BestFitTransform` → `SegmentedPowerFunction` + `ParameterSet[]` posterior (`EnsembleFunction`). The wrappers add domain labels, validation, serialization glue, and hash identity — zero math.

Concrete types:
- **`LinearTransform`** — `y = α + βx`, optional Gaussian uncertainty.
- **`PowerTransform`** — `y = α(x − ξ)^β`, log-space uncertainty, optional inversion.
- **`TabularTransform`** — paired-data interpolation with optional log axes.
- **`CompositeTransform`** (NEW) — weighted average or mixture over `WeightedTransformFunction[]`. Mirrors `CompositeHazard`: each sub-transform participates with a weight, output is the weighted combination. Useful when a transform is uncertain across multiple expert-elicited or fitted forms (e.g., several rating curves with credibility weights).
- **`BestFitTransform`** (NEW) — posterior-import of a fitted `RatingCurveAnalysis`: a Numerics `SegmentedPowerFunction` + `ParameterSet[]` posterior. Each realization index looks up the corresponding posterior parameter set; produces an `IUnivariateFunction` representing stage→discharge (or whatever the rating curve is). Same import pattern as `BestFitUnivariateHazard` — no `RMC.BestFit.dll`.

`WeightedTransformFunction` (new in `Support/`) follows the same shape as `WeightedHazardFunction`: holds a sub-transform reference + a weight + bubbled `PropertyChanged`. Serialized as an XElement with the sub's full `ToXElement()` inline.

Contract:

```csharp
public interface ITransformFunction : IModelElement
{
    string SpecifiedHazard { get; set; }
    string HazardUnit { get; set; }
    string TransformedHazard { get; set; }
    string TransformedHazardUnit { get; set; }
    bool IsDeterministic { get; }

    int SamplingDimensions { get; }
    void SetupSampler(int sampleSize, int seed, SamplingScheme scheme);
    IUnivariateFunction SampleFunction(int realizationIndex);

    IUnivariateFunction SampleFunction();
    IUnivariateFunction SampleFunction(double percentile);

    double MinHazard();
    double MaxHazard();
    double MinTransformedHazard(bool meanOnly);
    double MaxTransformedHazard(bool meanOnly);
}
```

### 6.3 Response Function — cluster #3

Path: `Models/ResponseFunctions/`. Six concrete response types plus 7 event-node types (the most complex cluster).

Contract:

```csharp
public interface IResponseFunction : IModelElement
{
    string SpecifiedHazard { get; set; }
    string HazardUnit { get; set; }
    bool IsDeterministic { get; }

    int SamplingDimensions { get; }
    void SetupSampler(int sampleSize, int seed, SamplingScheme scheme);
    IUnivariateDistribution SampleFunction(int realizationIndex);
    OrderedPairedData SampleResponseFunction(int realizationIndex);

    OrderedPairedData SampleResponseFunction();
    OrderedPairedData SampleResponseFunction(double percentile);
    IUnivariateDistribution SampleFunction();
    IUnivariateDistribution SampleFunction(double percentile);

    bool IsMonotonic();
    double MinHazard();
    double MaxHazard();
    double MinProbability();
    double MaxProbability();
}
```

Concrete types: `ParametricResponse`, `TabularResponse`, `BivariateResponse`, `NonFailResponse`, `EventTreeResponse`, `CompositeResponse`, `FaultTreeResponse` (v2 placeholder) + `WeightedResponseFunction`.

Event-node sub-hierarchy under `EventTreeResponse`: `IEventNode`, `EventNodeBase`, `ChanceNode`, `InitiatingNode`, `RemainderNode`, `SecondaryHazardNode`, `WeightedHazardLevel`, `EventNodeExtensions`. Pure DAG of probabilistic branches; canonical hash via post-order traversal with children visited in their own canonical-hash order.

#### 6.3.1 BivariateResponse extended for bivariate hazards

Today `BivariateResponse` is wired only to a primary hazard dimension. The v1.1.0 update connects it to a parent component's `IBivariateHazardFunction`:

```csharp
public class BivariateResponse : ResponseFunctionBase
{
    /// <summary>The hazard label this response's primary axis aligns to (must equal MarginalX.SpecifiedHazard on the parent component).</summary>
    public string PrimaryHazardType { get; set; }
    public string PrimaryHazardUnit { get; set; }

    /// <summary>The hazard label this response's secondary axis aligns to (must equal MarginalY.SpecifiedHazard on the parent component).</summary>
    public string SecondaryHazardType { get; set; }
    public string SecondaryHazardUnit { get; set; }

    /// <summary>The 2D failure-probability surface P(F | X, Y).</summary>
    public BivariateSurface Surface { get; set; }

    public double SurfaceProbability(double x, double y);  // bilinear interpolation
}
```

Validation in `SystemComponent`: the FM's BivariateResponse is allowed only when the SC's hazard is bivariate, and the four `(Primary|Secondary)HazardType` strings must match `MarginalX.SpecifiedHazard` and `MarginalY.SpecifiedHazard` exactly.

The owning `FailureMode` exposes `ConsequenceHazardBinding ∈ { Primary, Secondary }` to indicate which dimension feeds the consequence function (which is itself univariate). See §6.5 and §7.4.

#### 6.3.2 FaultTreeResponse (v2 placeholder)

```csharp
/// <summary>
/// V2 PLACEHOLDER — full design deferred to a post-v1.1.0 release. Provides a structural contract
/// for fault-tree analysis: AND/OR/XOR/k-of-N gates, basic-event probabilities, minimal-cut-set
/// enumeration. Will mirror EventTreeResponse's pattern (declarative tree + traversal at SampleFunction).
/// LHS-driven via SetupSampler once the dimension-counting recipe is locked.
/// Throws NotImplementedException from SetupSampler in v1.1.0.
/// </summary>
public class FaultTreeResponse : ResponseFunctionBase
{
    public FaultTreeRootNode Root { get; set; }
    // ...
}
```

Tracked design as Q-O. The placeholder lets the UI offer "Add Fault Tree Response" with a stub editor that warns "v2 only".

### 6.4 Consequence Function — cluster #4

Path: `Models/ConsequenceFunctions/`. Four concrete types + helpers.

Concrete types:
- **`TabularConsequence`** — paired-data hazard→consequence with optional uncertainty per ordinate.
- **`LifeSimConsequence`** — imported from a separate LifeSim simulation; treated as a `TabularConsequence` with imported `UncertainOrderedPairedData`.
- **`ParametricConsequence`** (NEW; landed 2026-07-21 — named without the `Function` suffix for cluster consistency with `TabularConsequence`/`ParametricResponse`) — closed-form power model per USACE ER 1110-2-1156 / HEC-FDA conventions:

  ```csharp
  // C(h) = clamp(Alpha * max(h - Threshold, 0)^Beta, 0, UpperBound)
  public double Alpha { get; set; }       // scale
  public double Beta { get; set; }        // exponent
  public double Threshold { get; set; }   // h₀ — no consequence below
  public double UpperBound { get; set; }  // saturation cap
  public bool IsUncertain { get; set; }
  public double SigmaAlpha { get; set; }  // log-space stddev on Alpha
  public double SigmaBeta { get; set; }   // log-space stddev on Beta
  ```

  `SamplingDimensions = IsUncertain ? 2 : 0` (two independent uncertain coefficients when uncertain: α_i = α·e^{σ_α·Z₁}, β_i = β·e^{σ_β·Z₂}). Suitable when a tabular function would over-fit sparse damage data, or when expert elicitation gives parametric form directly. Implementation notes (2026-07-21): the mean-curve overload returns the nominal (median) curve because with exponent scatter and no cap the analytic mean diverges for hazards more than one unit above the threshold (validation warns); sigmas serialize only while `IsUncertain` (recipe-literal conditional hashing); evaluation runs through the exact `ClampedPowerFunction` adapter (threshold-zero and saturation semantics that Numerics `PowerFunction` does not provide).
- **`CompositeConsequence`** (landed 2026-07-21) — weighted mixture / weighted average / additive sum over `WeightedConsequenceFunction[]` (`CompositeFunctionType { Additive, Average, Mixture }`, default Mixture per v1.0). Children are live references to stored functions (the BestFit `CompositeAnalysis` pattern): the stored `ByReference` form carries only (Id, Name, Weight) per entry — no duplicated child content — while `SelfContained` embeds children inline for storeless contexts; unresolvable references keep their weighted entries and are reported by `Validate()`. Nesting is allowed with a circular-reference validation error (deliberate divergence from BestFit, which forbids nesting: pointwise consequence combines are well-defined recursively, and Mixture-over-Additive is a real modeling need).

Contract:

```csharp
public interface IConsequenceFunction : IModelElement
{
    string SpecifiedHazard { get; set; }
    string HazardUnit { get; set; }
    string SpecifiedConsequence { get; set; }
    string ConsequenceUnit { get; set; }
    bool IsDeterministic { get; }

    int SamplingDimensions { get; }
    void SetupSampler(int sampleSize, int seed, SamplingScheme scheme);
    IUnivariateFunction SampleFunction(int realizationIndex);

    IUnivariateFunction SampleFunction();
    IUnivariateFunction SampleFunction(double percentile);

    // v0.13 (Phase 4): mean-only exposure branches. Non-composite types return a single
    // (1.0, meanCurve) entry; CompositeConsequence in Mixture mode returns its child mean
    // curves with their weights (nested composites flatten with multiplied weights).
    IReadOnlyList<(double Weight, IUnivariateFunction Function)> SampleExposureBranches();

    double MinHazard();
    double MaxHazard();
}
```

#### 6.4.1 Mixture consequences under the mean-only path (v0.13, Phase 4)

The v1.0 mean-only compute path calls the parameterless `SampleFunction()`, and
`CompositeConsequence.SampleFunction()` flattens a **Mixture** into its weighted-mean curve
`Σ wᵢ fᵢ(h)` (legacy `CompositeConsequence.vb:951-984`; v1.1 `CompositeConsequence.cs:391`). The
mean annualized risk is then correct — `Σ wᵢ P_F fᵢ = P_F Σ wᵢ fᵢ` — but the LEC that the integrand
records is the *mean curve's* LEC, so the variance, VaR, CVaR and F-N tail all collapse. The
day/night life-loss composite is the canonical failure: the high-consequence night branch is
averaged into the mean instead of surfacing as its own exceedance branch.

Fix: in the mean-only path, do **not** collapse a Mixture. `SampleExposureBranches()` returns the
weighted branches; `SampledFailureMode.ComputeRisk` emits **one risk-point entry per branch** with
probability mass `P_F(h)·wᵢ` and consequence `fᵢ(h)`. No results-container change is needed —
`RiskPoint` already carries parallel `ResponseProbabilities`/`Consequences` lists and
`Curve.CreateCurve` already loops over them. `Additive`/`Average` composites return a single
collapsed curve (those are genuine pointwise sums, not exposure states). A failure mode with
multiple ordered consequence types (the Phase-3 `ConsequenceFunctions` list) takes the **cross
product** of branches across types with product weights, matching the full-MC path where each
composite draws its branch from its own independently seeded dimension; warn above 64 combined
branches, error above 1024 (see open question Q-W for a future *shared exposure state*). This mirrors
into the full-MC path per open question Q-V.

### 6.5 SystemComponent dimensional binding (bivariate hazard support)

Each `FailureMode` declares which hazard dimension it operates on. For univariate hazards this is a no-op; for bivariate hazards it routes the FM's transform/response/consequence chain to either the X or Y marginal.

```csharp
public enum HazardDimension { Primary = 0, Secondary = 1 }

public class FailureMode
{
    /// <summary>
    /// Which hazard dimension feeds this FM's transform → response → consequence chain.
    /// Ignored when the parent component's hazard is univariate (treated as Primary).
    /// </summary>
    public HazardDimension HazardBinding { get; set; } = HazardDimension.Primary;

    /// <summary>
    /// Which hazard dimension feeds the consequence function. Used when ResponseFunction is a
    /// BivariateResponse (the response evaluates at (X, Y) jointly but the consequence is 1D).
    /// Ignored otherwise.
    /// </summary>
    public HazardDimension ConsequenceHazardBinding { get; set; } = HazardDimension.Primary;

    // ... existing fields
}
```

`SystemComponent.Validate()` adds:

| Hazard kind | Constraint |
|---|---|
| Univariate | All FMs must have `HazardBinding == Primary`. UI auto-coerces. |
| Bivariate, FM with non-bivariate response | `HazardBinding ∈ {Primary, Secondary}`; FM transform/response/consequence units must align with the bound marginal's hazard type and unit. |
| Bivariate, FM with `BivariateResponse` | Both response axes must align: `BivariateResponse.PrimaryHazardType == hazard.MarginalX.SpecifiedHazard` and `SecondaryHazardType == hazard.MarginalY.SpecifiedHazard`. `ConsequenceHazardBinding` is required and feeds the consequence function with the chosen marginal. |

Canonical hash of `FailureMode` adds the two binding enums; canonical hash of `SystemComponent` already includes its hazard's canonical hash, which differs between univariate and bivariate types. No additional field needed at the SC level.

## 7. Risk Analysis engine

Path: `Analyses/RiskAnalysis/` (orchestrator) + `Models/RiskAnalysis/` (data + sampled state).

### 7.1 Type relationships

```
RiskAnalysis (Analyses/RiskAnalysis/) : AnalysisBase, IAnalysis
  ├── SystemComponent[] Components             ← injected via ctor
  ├── RiskAnalysisOptions Options              ← Realizations, PRNGSeed, ...
  └── EnsembleResults? RiskResults             ← populated by RunAsync

SystemComponent (Models/RiskAnalysis/Components/)
  ├── IHazardFunction HazardFunction
  ├── ObservableCollection<FailureMode> FailureModes
  ├── HazardThreshold, FailureModeMethod, JointConsequences, FailureModeDependency, CorrelationMatrix
  ├── byte[] CanonicalHash()                    ← stable content identity
  ├── int OccurrenceIndex { get; internal set; } ← assigned per-RunAsync; not persisted
  ├── void SetupSamplers(N, componentSeed, scheme) ← walks tree, calls SetupSampler on each function (§5.8.7)
  └── SampledComponent Sample(int idx)          ← reads each function's pre-allocated row

FailureMode (Models/RiskAnalysis/Components/)
  ├── List<ITransformFunction> HazardToResponse
  ├── List<ITransformFunction> ResponseToConsequence
  ├── IResponseFunction ResponseFunction
  ├── IConsequenceFunction ConsequenceFunction
  ├── int SetupSamplers(N, componentSeed, ordinal, scheme) ← returns next ordinal
  └── SampledFailureMode Sample(int idx, FailureMode? nfMode)
```

Per realization, sampling is index-driven and queue-free:

```csharp
SampledComponent sampled = component.Sample(idx, nfMode);
// Inside: each function reads its own pre-allocated _percentiles[idx, :] row.
```

`SamplingScheme.MonteCarlo` uses the same index-driven flow but the matrices contain independent uniform draws instead of LHS-stratified ones — preserves the same call shape.

### 7.2 Topology decoupling

The model lib accepts `SystemComponent[]` directly. **No DAG.dll dependency.** Headless callers (REST, agentic) build the array programmatically. *(v0.9: each component now owns its topology as a `ComponentGraph` — the formal element DAG inside the model library — and projects its `FailureMode` chains from it; the UI's `RiskDiagram : DAG.Graph` controls become a visualization bound to the model graph rather than the source of a conversion.)*

The future `RMC.TotalRisk.UI` (Phase 3) references `DAGControls` (which references `DAG`) for visual editing. At the analysis boundary, the UI converts its `RiskDiagram : DAG.Graph` → flat `SystemComponent[]` and hands it to `new RiskAnalysis(components)`.

```
┌───────────────────────────────────────────────────────────────────────┐
│ RMC.TotalRisk.UI.dll  (net10.0-windows; Phase 3)                       │
│   References: DAGControls (WPF), DAG (model), RMC.TotalRisk            │
│                                                                        │
│   ┌──────────────────────┐    boundary    ┌──────────────────────┐     │
│   │ RiskDiagram :        │                │ SystemComponent[]    │     │
│   │   DAG.Graph          │ ─── adapt ────▶│   (flat, content-    │     │
│   │ + HazardNode etc.    │                │    hashed identity)  │     │
│   └──────────────────────┘                └──────────────────────┘     │
└──────────────────────────────────────────────┬─────────────────────────┘
                                               │
                       ┌───────────────────────▼──────────────────────┐
                       │ RMC.TotalRisk.dll  (net10.0)                 │
                       │   References: Numerics only (v0.6)           │
                       │   No DAG / DAGControls / FlowGraph           │
                       │                                              │
                       │   new RiskAnalysis(components)               │
                       │       .RunAsync(progress, ct)                │
                       └──────────────────────────────────────────────┘
```

### 7.3 Monte Carlo loop

```csharp
public override async Task RunAsync(SafeProgressReporter? progress = null, CancellationToken ct = default)
{
    var startEv = new CancelEventArgs();
    OnAnalysisStarting(startEv);
    if (startEv.Cancel) return;

    var (isValid, messages) = Validate();
    if (!isValid)
        throw new InvalidOperationException(string.Join("\n", messages));

    var token = ResetCancellationToken();    // fresh CTS, joined with caller's ct

    // Content-based per-component seeds with occurrence-index for within-analysis independence.
    // See §5.5.4.
    AssignOccurrenceIndices(Components);
    foreach (var component in Components)
    {
        int componentSeed = SeedHelpers.HashCombine(
            Options.PRNGSeed, component.CanonicalHash(), component.OccurrenceIndex);

        // Walk the component's function tree and pre-allocate per-function LHS matrices.
        // Each function gets a unique seed derived from the component seed + structural ordinal
        // + function canonical hash. See §5.8.
        component.SetupSamplers(Options.Realizations, componentSeed, Options.SamplingScheme);
    }

    if (Options.EstimateMeanRiskOnly)
    {
        var single = Compute(prngSeed: -1, index: -1, progress);
        // wrap in EnsembleResults with a single SystemRiskResults
    }
    else
    {
        var random = new Random(Options.PRNGSeed);
        int[] randomSeeds = random.NextIntegers(Options.Realizations);

        var realizations = new SystemRealization[Options.Realizations];
        var perRealizationResults = new SystemRiskResults[Options.Realizations];

        Parallel.For(0, Options.Realizations, new ParallelOptions { CancellationToken = token }, idx =>
        {
            var computed = Compute(randomSeeds[idx], idx, progress);
            realizations[idx] = computed;
            perRealizationResults[idx] = new SystemRiskResults(computed);
            // update ensemble min/max via Interlocked
        });

        // PostProcessUncertainty across the ensemble
        RiskResults = new EnsembleResults(perRealizationResults, realizations);
    }

    IsEstimated = true;
    OnAnalysisCompleted(new AnalysisRunCompletedEventArgs(/* ... */));
}
```

The `Compute(seed, idx)` per-realization method ports the legacy structure — an adaptive 1D integral per component for the additive/single-component path, `Vegas` for joint risk — but with the v0.13 corrections below. Behavioral changes vs. legacy:

1. Seed source: derived from canonical hashing (§5.5.4).
2. Component ordering for `_eCombos` columns: canonical-hash-sorted, not canvas-position-sorted.
3. Progress reporting via `SafeProgressReporter` (was already there in legacy).
4. Cancellation via `CancellationToken` (was via internal CTS).
5. Bivariate-aware integration: when `component.HazardFunction is IBivariateHazardFunction`, the integrand performs nested Y | X integration (§7.4).
6. **1D integrator is `AdaptiveGaussKronrod`, not `AdaptiveSimpsonsRule`** (v0.13; §7.7). Same `Integrate(List<StratificationBin>)` surface, same p-domain `[1e-16, 1−1e-16]`, same 50 `Stratify` hazard bins, tol `1e-8`, `MaxDepth` 100, `MaxEvaluations` 1e6; set `MinDepth ≥ 2`. As in v1.0 the integrator is used as an *adaptive sampler* — its returned value is discarded and only its evaluation points populate the risk-point set — so the choice of integrand (item 8) changes only where points are placed.
7. **LEC construction is exact, not histogrammed** (v0.13; §7.7): probability mass comes from the Kronrod weight (interim: the deduplicated midpoint-trapezoid fallback with a `Σ mass = 1 ± 1e-9` assertion, until Numerics item N7 lands the weight-exposing overload); moments use weighted Welford; `LECOutputLength` thins the output only.
8. **`Options.RiskIntegrand`** (default `MeanTotalRisk`) selects the adaptive refinement objective (§4, §7.7). Discontinuous integrands (`TailConditionalRisk`, `ThresholdExceedanceProbability`) inject their discontinuity `p` as an extra stratification-bin boundary.
9. **System aggregation is rebuilt** (v0.13; §7.8): additive assumes strict independence and convolves component LECs via FFT (producing a real system LEC v1.0 never built); joint enumerates true component failure/non-failure combinations and exposes the Vegas power transform.

### 7.4 Bivariate hazard integration

When a component's hazard is bivariate, the outer integration over X probability is unchanged. At each X integration point, the bivariate hazard is asked for `SecondaryIntegrationBins` discretized (Y, weight) pairs from the conditional Y | X distribution. Each FM contributes based on its `HazardBinding`:

```csharp
double IntegrateAtX(SampledComponent sc, int idx, double pX)
{
    bool bivariate = sc.SystemComponent.HazardFunction is IBivariateHazardFunction biv;
    double hX = sc.Hazard.InverseCDF(pX);
    var yBins = bivariate
        ? biv!.SampleConditionalYGivenX(idx, hX)
        : new[] { (Y: hX, Weight: 1.0) };  // univariate: Y collapses to a single point

    double total = 0;
    foreach (var fm in sc.FailureModes)
    {
        if (fm.SystemComponent.ResponseFunction is BivariateResponse br)
        {
            // Bivariate response: evaluate failure surface at (X, Y) pairs
            foreach (var (yVal, w) in yBins)
            {
                double pF = br.SurfaceProbability(hX, yVal);
                double hForC = fm.SystemComponent.ConsequenceHazardBinding == HazardDimension.Primary ? hX : yVal;
                double cF = fm.Consequences.Function(ApplyTransforms(fm.ResponseToConsequence, hForC));
                total += w * pF * cF;
            }
        }
        else
        {
            // Univariate response on either dimension
            double hIn = fm.SystemComponent.HazardBinding == HazardDimension.Primary ? hX : 0;
            if (fm.SystemComponent.HazardBinding == HazardDimension.Secondary)
            {
                foreach (var (yVal, w) in yBins)
                {
                    double th = ApplyTransforms(fm.HazardToResponse, yVal);
                    double pF = fm.Response.CDF(th);
                    double cF = fm.Consequences.Function(ApplyTransforms(fm.ResponseToConsequence, th));
                    total += w * pF * cF;
                }
            }
            else
            {
                double th = ApplyTransforms(fm.HazardToResponse, hX);
                double pF = fm.Response.CDF(th);
                double cF = fm.Consequences.Function(ApplyTransforms(fm.ResponseToConsequence, th));
                total += pF * cF;   // weight = 1 (Y dimension not used)
            }
        }
    }
    return total;
}
```

Univariate hazards collapse to the same code path: `yBins` holds a single `(hX, 1.0)` entry, and `BivariateResponse` is rejected at validation. Performance: bivariate analyses do `SecondaryIntegrationBins` × outer-integrator-evaluation work, typically 30–100× more than univariate. `SecondaryIntegrationBins` defaults to 50; tunable per-hazard.

Joint-risk inclusion-exclusion across multiple bivariate components composes the same way: each component contributes its own integrated risk profile; combinatorial assembly happens at the system level as today.

### 7.5 Results pipeline

`Curve` / `Curves` / `RiskPoint` / `Ensemble` / `*Realization` / `*Results` are pure data containers. Behavioral changes vs. legacy:

- **Strip `[Serializable]` and `BinaryFormatter`** in `SystemRealization` and `EnsembleResults`. **v0.8: replace with System.Text.Json** — results containers are redesigned with explicit public serializable state (v1.0's `Curve` hid moments/bin parameters in private fields) and expose `ToJson()`/`FromJson()` plus compressed-bytes overloads (in-memory only; persistence is a caller concern). Model definition types keep `ToXElement()` (the canonical-hash identity surface); results are JSON. v1.0 BLOBs are not readable — old projects re-run (v1.0 `Open()` already degraded unreadable results to `IsEstimated=false`).
- `Curve.ComputeCentralMoments()` and `ComputeRiskMeasures()` lazy-evaluation logic stays.
- `OrderedPairedData` (Numerics) inputs/outputs preserved.
- Memory cleanup pattern (`DumpMemory()` clearing `RiskPoints` and `Bins` post-aggregation) preserved.

### 7.6 Event lifecycle

Mirrors BestFit `IAnalysis`:

```csharp
ra.AnalysisStarting += (s, e) => { /* last-minute checks; set e.Cancel = true to abort */ };
ra.AnalysisCompleted += (s, e) =>
{
    if (e.Error != null) { /* ... */ }
    else if (e.Cancelled) { /* ... */ }
    else { /* read ra.RiskResults */ }
};
await ra.RunAsync();
```

Events fire on the same thread that invokes `RunAsync()`. WPF callers wrap with their own dispatcher invocation if needed.

### 7.7 1D integration, LEC construction, and risk measures (v0.13, Phase 4)

Normative summary; full math and every legacy `file:line` in
[../technical-reference/risk-integration.md](../technical-reference/risk-integration.md) and
[../technical-reference/loss-exceedance-curves.md](../technical-reference/loss-exceedance-curves.md).

**Integrator.** Replace `AdaptiveSimpsonsRule` with `AdaptiveGaussKronrod` (G10K21) at both 1D call
sites — the per-component risk integral (legacy `RiskAnalysis.vb:2852-2889`) and the CVaR integral
over `[1e-16, α]` of the log-log LEC quantile (legacy `Curve.vb:547-552`). The surface is identical;
give the CVaR integral explicit tol/eval caps (legacy used library defaults). The integrator's
returned value is not the risk result — its adaptively placed evaluation points are recorded as
`RiskPoint`s, and the LECs are built from those. G10K21 nodes are strictly interior, so no two
adjacent stratification bins share a `p` (the risk-point set stays duplicate-free).

**`RiskIntegrand` — the refinement objective** (`Core.Enums`, a hashed `RiskAnalysisOptions` field;
per §4). It changes only where the adaptive integrator concentrates evaluations; all five risk-type
LECs and every risk measure are produced regardless of the choice. Members, integrands (functions of
the hazard non-exceedance probability `p`), and what each refines:

| Member | Integrand | Concentrates points where |
|---|---|---|
| `MeanTotalRisk` **(default)** | `P_F·E[C_F] + P_NF·C_NF` | v1.0 behavior; mean annualized total consequence |
| `MeanIncrementalRisk` | `P_F·E[(C_F − C_NF)⁺]` (Excess) | the reducible risk lives |
| `TotalProbabilityOfFailure` | `P_F(p)` | the fragility is steep; pairs with `RiskAnalysisMode.Reliability` |
| `TailConditionalRisk` | `P_F·E[C_F]·1{p ≤ α}`, α = `Options.Alpha` | the α-tail — VaR / CVaR / F-N tail |
| `ThresholdExceedanceProbability` | `P(C > ConsequenceThreshold ∣ p)` | the assurance / tolerable-risk decision |
| `SecondMoment` | `E[C² ∣ p]` | the LEC variance |
| `Balanced` | normalized sum of MeanTotalRisk + SecondMoment + TailConditionalRisk | every measure converges together; the sensible default for API callers reading all measures |

The last two integrands are discontinuous in `p`; inject the crossing (`p = α`, resp. the threshold)
as a stratification-bin boundary so it lands on a bin edge (the `Integrate(List<StratificationBin>)`
overload already supports this).

**LEC construction** (both 1D and system paths converge on one algorithm):
1. Probability mass is the quadrature weight, not a re-derivation. Interim until Numerics item N7:
   keep a *deduplicated* midpoint-trapezoid fallback and assert `Σ mass = 1 ± 1e-9`.
2. Build the exceedance curve **exactly** from sorted `(mass, consequence)` pairs (descending
   consequence, exact reverse-cumulative mass); thin to `LECOutputLength` ordinates for output only,
   always retaining the extreme-tail points. The v1.0 200-bin log10 histogram plotted at bin
   midpoints is dropped — `LECOutputLength` is an output knob, not a compute knob.
3. Central moments via **weighted Welford**, not raw power sums (v1.0's `sqrt(u2 − u1²)` and expanded
   4th-moment forms catastrophically cancel when the mean dominates the spread — the usual life-loss
   case). Documented as an `improve-on-port` case in the type's `<remarks>`.
4. Raise a validation **Warning** when `|Σ mass − 1| > 1e-6` on an exhaustive curve instead of
   silently clamping.

**Risk-measure catalog** (all built from the finished LEC; defects fixed): `TotalProbability`
(= annualized P(failure) on the `Fail` curve), `Mean` (= EAD / mean annualized risk),
`ConditionalMean` (`Mean/TotalProbability`), `StandardDeviation`, `Skewness`, `Kurtosis`,
`ConsequenceThresholdProbability` (assurance), `HazardThresholdProbability`, `ValueAtRisk`,
`ConditionalValueAtRisk`, plus the `LEC` (F-N) and the `HazardFrequency` / `HazardvsCEN` profiles.
Two fixes: `ValueAtRisk` returns **0** (not the minimum consequence) when `α > TotalProbability`; and
the uncertainty percentile `Total` curve is read from the Total LEC, not reconstructed as
`fAEP + nfAEP` (legacy `RiskAnalysis.vb:3369`).

### 7.8 System risk aggregation (v0.13, Phase 4b)

> **Landed 2026-07-23 with the v0.15 implementation errata** (see the status log): the additive
> convolution is an exact lattice via `Fourier.FFT` (`SystemConvolution`; `EmpiricalDistribution.Convolve`
> cannot represent the zero atoms — N8 extended), the automatic γ target comes from a
> deterministic per-component quadrature probe (the warm-up harvest was unimplementable),
> recording accumulates five self-normalized passes, and the defective system stream
> probabilities keep the v1.0 system-state semantics. The text below is the v0.13 design intent;
> where it conflicts with the v0.15 items, v0.15 wins.

Full math in [../technical-reference/loss-exceedance-curves.md](../technical-reference/loss-exceedance-curves.md) §System aggregation.

**Additive method — strict independence + FFT convolution.** The additive method is **redefined to
assume components are strictly independent** (ratified). Validation: `SystemRiskMethod = Additive`
with `ComponentHazardDependency ≠ Independent` or a non-identity `HazardCorrelationMatrix` is an
**Error**; the correlation matrix applies to the joint method only. The v1.0 additive path combined
only the first two moments and produced **no system LEC** — v1.1 builds a true system LEC for all
five risk types by FFT:
- The `Fail`/`Excess`/`NonFail` curves are *defective* (`TotalProbability < 1`). Make each proper by
  adding an atom at consequence 0 with mass `1 − TotalProbability` (the "component did not fail ⇒
  contributes zero" branch). **Convolving the D zero-inflated distributions is exactly the
  enumeration of all 2^D component failure/non-failure combinations**, in O(n log n) rather than
  2^D, and reproduces the full tail rather than a conditional mean.
- Per risk type, build `EmpiricalDistribution(xValues, pValues)` from each component curve
  (X ascending consequence, P = 1 − exceedance) and call
  `EmpiricalDistribution.Convolve(IList<EmpiricalDistribution>, numberOfPoints)`. `Background` is
  already exhaustive; convolve component `Total` curves for the system `Total`.
- **Grid hazard:** `Convolve` samples PDFs on a *linear* grid over `[Σmin, Σmax]`. Life-loss ranges
  span orders of magnitude, so add option `SystemConvolutionPoints` (default 4096, min 4096); assert
  the convolved mean equals `Σ` component means to 1e-6 relative (this is the exact v1.0 additive
  answer — a free regression gate); gate the phase on a brute-force MC tail cross-check. Log-spaced
  convolution is Numerics item N8. System `pF = Probability.IndependentUnion(pfs)`.

**Joint method — Vegas power transform + real combination enumeration.**
- **Expose the power transform.** `RiskAnalysisOptions` gains `VegasTailFocusMode { None, Automatic,
  Manual }` (default `Automatic`) and `VegasTailFocusParameter` (γ, default 1.0 = v1.0-identical,
  valid [1, 20]). `None` pins γ = 1 for v1.0 comparability; `Manual` uses the supplied γ.
- **γ heuristic (`Automatic`).** Harvest `pTarget` from the warm-up pass (which already runs with
  `recordOutput = false` and currently discards everything but the grid): accumulate observed
  per-component failure probabilities, set `pTarget = clamp(min_i P̂_f,i · Alpha, 1e-12, 1e-2)`, then
  call `Vegas.ConfigureForRareEvents(pTarget)` before the recording pass. Zero extra cost,
  deterministic, adapts to actual fragility.
- **Jacobian audit.** In TotalRisk the Vegas `wgt` *is* the LEC probability mass, so the
  power-transform Jacobian must be folded into `wgt` or every LEC ordinate is biased even when the
  integral is correct. Verify (Numerics item N9) before enabling γ > 1 by default.
- **Record more than one final pass** — accumulate LEC points across `IndependentEvaluations > 1`
  recording passes and scale `FinalEvaluations` with D in `SetIntegrationDefaults` (a single 10,000-
  eval pass is far too sparse for a D-dimensional tail ordinate).
- **Enumerate real combinations.** Activate the parallel `ResponseProbabilities` /
  `FailureConsequences` / `ExcessConsequences` lists on `ComponentRiskOutput` (the legacy
  `ComponentRiskOutput.vb:39` TODO) so the Vegas integrand enumerates the true within-component
  consequence distribution instead of convolving per-component conditional means. Fix the
  double-increment of `tPF` (legacy `RiskAnalysis.vb:3117` and `:3129`).

## 8. Layer boundaries & consumer contract

**v0.11 (2026-07-20).** Normative for every consumer of `RMC.TotalRisk.dll`: the future
`RMC.TotalRisk.UI` element/persistence layer, the `RMC-TotalRisk` App, `RMC.TotalRisk.Api`, and
headless/agentic callers. The shape follows `RMC.BestFit`, whose model → UI → App split is the
in-house precedent this library is meant to slot into.

### 8.1 What owns what

| Concept | Owned by | Persisted as |
|---|---|---|
| Input functions (`TabularHazard`, `TabularResponse`, …) | The **consuming layer**, one stored item each | Its own row; the function's `ToXElement()` in one column |
| `SystemComponent` (graph + options) | Its owning **`RiskAnalysis`** — components are *not* independently creatable in the UI/App | A column on the analysis's row, functions written `ByReference` |
| `RiskAnalysis` config | Itself | `RiskAnalysis.ToXElement()` — **options and `IsEstimated` only** |
| Results | Themselves | Separate columns; JSON (per v0.8 §4) |

The analysis rule is BestFit's, verbatim: `UnivariateAnalysis.ToXElement()` writes config only and
documents that it excludes the underlying model and the computed results; the model is stored in
its own column and comes back through `new UnivariateAnalysis(dist, xElement, mcmcResults, …)`.
`RiskAnalysis` will follow that constructor shape — components and results in, config from XML.
This is what lets a single component be shared across the alternatives of a
`CostBenefitAnalysis`, and what keeps the analysis blob from becoming an all-in-one document.

### 8.2 Referencing functions: `Id`, not name

`IRiskFunction.Id` (a `Guid`) is the persistent reference key. It is serialized and **stripped by
`CanonicalizationRules.ModelRules`**, so identity can never perturb content or a seed.
`AssignNewId()` is what a "duplicate this function" flow calls; a deep copy keeps the id, because
a copy is the same logical function.

BestFit references elements by **name**, resolved by linear scan with defensive type checks — its
own retrospective flags this as the thing to reconsider, since a rename or a name collision can
silently re-resolve to a different type. The shared framework's `NodeBase.NodeGuid` is the
counter-example, and `IRiskElement.Id` already followed it. Names remain serialized alongside ids
as a lenient fallback for hand-authored and legacy forms.

### 8.3 The two serialization modes

`RiskSerializationMode` governs how a graph writes the functions its elements wrap:

- **`SelfContained`** (the default; the no-arg `ToXElement()` delegates to it) — function content
  inline. The form stands alone. Headless callers, verification oracles, and the REST/MCP API use
  this and are unaffected by anything in this section.
- **`ByReference`** — each wrapped function becomes a `<FunctionReference Id="…" Name="…"/>`.
  Reading such a form requires an `IRiskFunctionResolver`, which **must return the live stored
  instance, not a copy**. That is the entire point: a graph and the store it was loaded from
  observe the same object, so an edit in one is seen in the other.

`IRiskFunctionResolver` mirrors `RiskElementResolver` exactly — an id is authoritative and throws
when stale (a dangling persistent reference means the stored form is inconsistent); a name-only
reference is lenient and surfaces through validation as an unresolved reference naming it, which is
deliberately distinguished from "no function assigned".

**Invariant (tested):** the mode cannot move a canonical hash. `SystemComponent.CanonicalHash()`
hashes the projected `FailureMode` XML, and `FailureMode`/`ResponseStage` always serialize their
functions inline regardless of mode. A component seeds identically however it was stored — if this
ever breaks, a project's Monte Carlo results would depend on how the project was saved.

### 8.4 Change propagation

Elements subscribe to their wrapped functions' `PropertyChanged` and re-raise it; `ComponentGraph`
forwards element changes; `SystemComponent` subscribes to its graph. So an edit made where a
function is stored reaches the analyses that consume it, and a consuming layer can invalidate
stale results — the role `UnivariateAnalysis.Model_PropertyChanged` plays in BestFit.

Ordered collections of model objects are `ObservableCollection<T>`, and their owner reconciles
per-item subscriptions on every membership change — `ConsequenceElement.Functions` follows the
sibling BestFit `CompositeAnalysis.Analyses` pattern exactly. Reconciliation is against a shadow
set of what is currently subscribed, **not** against the event's `OldItems`/`NewItems`, because
`Clear()` raises a Reset that carries no removed items; handling only the event payload leaks a
subscription on every clear. This also makes a duplicated entry subscribe once, so it notifies
once. WPF binds these collections directly.

### 8.5 Authoring surface for a graph editor

The DAG control wires nodes and hands the model layer the inner `IRiskFunction`s. It should use:

- `RiskElementFactory.CreateForFunction(function)` / `Create(RiskElementType)` — never its own
  cluster→element mapping, which is the mapping most likely to drift as clusters land.
- `IRiskElement.TryAssignFunction(function, out error)` — reports a cluster mismatch instead of
  throwing, because dropping the wrong function on a node is ordinary user error.
- `ComponentGraph.GetUniqueName` / `TryRenameElement` — the name authority.
- `ComponentGraph.GetAvailableHazardSources(element)` — the binding picker, reused by validation
  so picker and validator agree by construction.
- `SystemComponent.GetReferencedFunctions()` — the save-time dependency set, and the answer to
  "this function is used by N components — delete anyway?".

Canvas position lives on the element (`LeftPosition`/`TopPosition`, stripped from hashing), so the
editor needs no parallel layout store.

### 8.6 What the model library still refuses

Unchanged from §2: no WPF, no `System.Windows.*`, no SQLite, no file I/O, no `RMC.BestFit`
reference, no DAG.dll. `IElement` and the wrapper vocabulary stay in the UI layer; the model layer
speaks `IRiskElement`. A user must be able to build a system and run an analysis from the model
library alone — asserted by a model-only end-to-end test with no store, no resolver, and no
consuming layer in the call path.

---

## 9. Dependency graph

v0.6: `RMC.TotalRisk.dll` references **Numerics only** — the same red line Hydrologics enforces. BestFit fitted results arrive as *data* (Numerics artifacts), not through a DLL reference; see [SHARED_FUNCTIONS_STRATEGY.md](SHARED_FUNCTIONS_STRATEGY.md) §5.

```
Numerics.dll                              (always — math, RNGs, distributions, integrators,
   │                                       and the Phase 2.0 Numerics.Functions toolkit)
   ▲
RMC.TotalRisk.dll  (net10.0)
   │
   │  no RMC.BestFit.dll (v0.6 — BestFit posteriors imported as Numerics artifacts)
   │  no DAG.dll
   │  no DAGControls.dll
   │  no FlowGraph
   │  no ProjectInterfaces / DatabaseManager / SQLiteManager
   │  no RMC-framework UI DLLs
   │
   ▲
RMC.TotalRisk.UI.dll  (net10.0-windows; Phase 3)
       References: RMC.TotalRisk + DAGControls (which references DAG) + RMC.BestFit
       (the UI reads `.rmcbf` SQLite columns and hands parsed Numerics artifacts to the model lib)
       Translates RiskDiagram : DAG.Graph ⇄ SystemComponent[] at the boundary
```

`RMC.TotalRisk.csproj` `<HintPath>` for the sibling repo (interim — switches to the `RMC.Numerics` PackageReference from the local feed `C:\GIT\numerics\packages` once 2.2.0 ships; see strategy D5):

```xml
<Reference Include="Numerics">
  <HintPath>..\..\..\..\numerics\Numerics\bin\Debug\net10.0\Numerics.dll</HintPath>
</Reference>
```

Expected sibling-repo layout on dev machines:
```
C:\GIT\RMC-TotalRisk-Dev\        ← this repo
C:\GIT\numerics\                  ← https://github.com/USACE-RMC/Numerics (shared functions home)
C:\GIT\rmc-bestfit\               ← https://github.com/USACE-RMC/RMC-BestFit (conventions template;
                                     referenced only by the UI layer in Phase 3)
C:\GIT\Hydrologics\               ← sibling consumer of the shared Numerics.Functions toolkit
                                     (no cross-reference in either direction)
C:\GIT\Wpf-framework\             ← contains DAG; only RMC.TotalRisk.UI (Phase 3) references this
```

## 10. Migration plan

> **v0.8: SUPERSEDED by [../ROADMAP.md](../ROADMAP.md)** — the ratified phase order is: 1 kernel foundation → 2 core input functions (tabular ×4 + parametric hazard/response + non-fail) → 3 components + JSON results → 4 analysis foundation + engine + ReliabilityAnalysis → 5–6 verification → 7–13 backfill (closed-form functions, Numerics expansion, composites, event trees, bivariate/BestFit/LifeSim, hardening, release) → 14 REST API + MCP server. The sub-phase text below is retained for its per-cluster task detail only; where it conflicts with ROADMAP.md or the v0.8 status entry, those win.

Original (v0.6) order notes follow.

### Phase 2.0 — Numerics.Functions expansion (v0.6; prerequisite)

Executed in the `C:\GIT\numerics` repo (branch `bug-fixes-and-enhancements`); full work plan in [SHARED_FUNCTIONS_STRATEGY.md](SHARED_FUNCTIONS_STRATEGY.md) §4:

- N1 — `UnivariateFunctionType` enum + `ToXElement()` on `IUnivariateFunction` and all concretes + `UnivariateFunctionFactory` (mirrors the existing `LinkFunctionFactory`).
- N2 — `SegmentedPowerFunction` (BestFit BaRatin rating-curve form; `ParameterSet`-compatible parameter layout).
- N3 — `CompositeFunction` (weighted-average + mixture modes).
- N4 — `EnsembleFunction` posterior sampling (pure `Sample(int)` / `Sample(double)` — thread-safe).
- N5 — `EmpiricalDistribution` XElement round-trip fix (+ `KernelDensity` check).
- N6/N7 — tests + `docs/functions/` guide page; release `RMC.Numerics 2.2.0` to the local feed.
- TotalRisk consumes via sibling `<HintPath>` until 2.2.0 ships, then switches to the PackageReference.

### Phase 2.1 — Hazard cluster (cluster #1)

Includes the v1.1.0 expansion to bivariate hazards. Larger than the legacy port alone.

- ~~Add `RMC.BestFit.dll` reference to `RMC.TotalRisk.csproj`.~~ **v0.6: no BestFit reference.** `BestFit*` types are posterior-import types constructed from already-parsed Numerics artifacts (`UncertaintyAnalysisResults`, `ParameterSet[]`, marginals + copula, X/Y/Z arrays).
- Port and rename Support types: `IHazardFunction`, `IUnivariateHazardFunction`, `IBivariateHazardFunction`, `HazardFunctionBase`, `UnivariateHazardBase`, `BivariateHazardBase`, `WeightedHazardFunction`.
- Port 6 univariate types: `ParametricUnivariateHazard`, `BestFitUnivariateHazard`, `NonparametricHazard`, `TabularHazard`, `RFAHazard`, `CompositeHazard`.
- Add 3 bivariate types: `ParametricBivariateHazard`, `BestFitBivariateHazard`, `BestFitTabularHazard`.
- `BestFitUnivariateHazard` extended to import any analysis from `RMC.BestFit.Analyses.Univariate.*`.
- `CanonicalizationRules` entries + hash-invariance tests on each, matching §5.5.3.
- Unit tests per concrete: XElement round-trip, `Validate()`, `SampleFunction()` smoke, bivariate `SampleConditionalYGivenX` smoke.
- ≥1 MC parity test reproducing a legacy `Test_TotalRisk` univariate hazard scenario within tolerance.
- ≥1 MC parity test cross-checking a `BestFitTabularHazard` against a directly-computed coincident-frequency analysis.
- DoD: `dotnet build` clean (0 warnings), all tests pass.

### Phase 2.2 — Transform cluster

Small after Phase 2.0 — the types are thin wrappers over `Numerics.Functions` (§6.2).

- Port `ITransformFunction`, `TransformFunctionBase`, `WeightedTransformFunction`.
- Port 3 existing types: `LinearTransform`, `PowerTransform`, `TabularTransform`.
- Add 2 new types: `CompositeTransform`, `BestFitTransform` (`SegmentedPowerFunction` + posterior import).
- `CanonicalizationRules` entries + hash-invariance tests on each.
- Unit tests + parity tests.

### Phase 2.3 — Response cluster

- Port `IResponseFunction`, `ResponseFunctionBase`, `WeightedResponseFunction`.
- Port 6 concrete response types + 8 event-node types.
- Extend `BivariateResponse` with bivariate hazard wiring (`PrimaryHazardType`, `SecondaryHazardType`, alignment validation against parent component).
- Add `FaultTreeResponse` v2 placeholder (throws from `SetupSampler`; canonical-hash recipe deferred to Q-O).
- `CanonicalizationRules` entries + hash-invariance tests on each (event nodes via post-order traversal; children sorted by canonical hash).
- Unit tests + parity tests, including a bivariate-response + bivariate-hazard end-to-end scenario.

### Phase 2.4 — Consequence cluster

- Port `IConsequenceFunction`, `ConsequenceFunctionBase`, `WeightedConsequenceFunction`, `LifeSimResult`.
- Port 3 existing types: `TabularConsequence`, `LifeSimConsequence`, `CompositeConsequence`.
- Add 1 new type: `ParametricConsequence` (power form per ER 1110-2-1156; landed pre-Phase-4, 2026-07-21, along with `CompositeConsequence` + `WeightedConsequenceFunction`).
- `CanonicalizationRules` entries + hash-invariance tests on each.
- Unit tests + parity tests.

### Phase 2.5 — Risk Analysis engine + supporting
- Port `Analyses/Support/` (`IAnalysis`, `AnalysisBase`, `AnalysisRunCompletedEventArgs`).
- Port `Models/RiskAnalysis/Components/` (`SystemComponent`, `FailureMode`, `SampledComponent`, `SampledFailureMode`, `ComponentRiskOutput`, enums).
- Port `Models/RiskAnalysis/Results/` (`Curve`, `Curves`, `RiskPoint`, `Ensemble`, `*Realization`, `*Results`).
- Port `Analyses/RiskAnalysis/RiskAnalysis.cs` — wire content-based seeding, `Parallel.For` MC loop, `RunAsync` lifecycle.
- Strip `[Serializable]` + `BinaryFormatter`; XElement round-trip for `SystemRealization` / `EnsembleResults`.
- **Drop `RiskDiagram` entirely** (UI concern; future Phase 3).
- **Drop all `Project.GetInstance()` references**; pass instances directly.
- ≥3 MC parity tests covering AdditiveRisk + JointRisk + EstimateMeanRiskOnly.
- 1 bivariate end-to-end parity test: a component with a `ParametricBivariateHazard`, two FMs (one bound to Primary, one to Secondary), and a `BivariateResponse` on a third FM with `ConsequenceHazardBinding` set. Verify nested integration produces expected joint-risk profile.
- 1 reproducibility regression test asserting that a `SystemComponent` graph with shuffled order produces bit-identical results vs. canonical order (proves the v1 bug fix).
- 1 LHS variance-reduction test: same scenario at N=1000 with `MonteCarlo` vs. `LatinHypercube` across 50 repeated runs; LHS empirical standard error of LEC mean is ≥3× lower than MC (loose threshold to avoid flakiness; expected reduction is 10×+).
- 1 LHS K-count assertion: `CountKnowledgeUncertaintyDimensions()` matches actual `PercentileQueue` consumption across all parity scenarios (Debug.Assert in code; explicit unit test ensures no off-by-one in any cluster's port).

### Phase 2.6 — Cleanup
- Retire `BuildInfo.cs` smoke seed + its `InternalsVisibleTo` declaration.
- Fix `RMC.TotalRisk.IO.csproj` `<AssemblyName>` collision (remove the override; let it default to `RMC.TotalRisk.IO`).
- Update [ROADMAP.md](ROADMAP.md) to reflect actual port order.
- Append Phase 2 decision log to [MEMORY.md](MEMORY.md).

### Definition of done (whole Phase 2)

Per ROADMAP §Phase 2:
- Every pure-compute class in the triage table is in `RMC.TotalRisk` with full XML docs, test coverage in `.Tests`, and parity coverage in `.Verification`.
- `RMC.TotalRisk.Tests` ≥90% line coverage on `RMC.TotalRisk.dll`.
- `dotnet build RMC.TotalRisk.csproj` succeeds on Linux (one-off container check; proves no Windows-only dep leaked in).

Plus added by this architecture:
- v1 reproducibility-bug regression test passes for every cluster's parity scenario (shuffle/rename/reorder all reproduce bit-identical results).

## 11. Open decisions / tracked questions

Living section. Append entries as we go. Once an item is resolved, move it under `## Resolved` with the resolution date.

### Open

- **Q-B**: `EventTreeResponse` post-order traversal — children sorted by their own canonical hash (proposed). Confirm this matches the math; if event-tree mathematics depend on declared order (e.g., probability normalization across siblings), we keep declared order and accept the metadata leak.
- **Q-D**: `LifeSimConsequence` is a heavy type that wraps a separate simulation. Audit during Phase 2.4: confirm there's no hidden file I/O in its compute path.
- **Q-E**: Threading audit — `Parallel.For` is straightforward, but are any `SampledComponent` / `SampledFailureMode` operations not thread-safe today? Audit during Phase 2.5.
- **Q-F**: `BasicMessageItem` rich metadata — Phase 2 drops severity/code/source/property-name. If the future REST API or agentic clients need structured error codes, revisit this in v1.x with a `ValidationIssue` record.
- **Q-H**: Numerics's `Random.NextIntegers(int)` — confirm it's part of public `Numerics.Utilities` API; if not, inline equivalent.
- **Q-I** *(failure-mode half resolved 2026-07-20, v0.9; consequence-composite half resolved 2026-07-21)*: Failure-mode order within a component is **always declared order** — now grounded structurally: projected FM order is the consequence-element order in the graph's `Elements` list, which is serialized, canvas-free, and user-controllable; reordering terminals is a deliberate semantic edit (pinned by test). `CompositeConsequence` resolves its half the same way: **declared entry order is semantic** — it drives the child sampler ordinals and the hashed entry order, so reordering entries is a compute edit (pinned by test). `CompositeHazard`/`CompositeResponse` follow the same rule when they land (Phase 9).
- **Q-J** *(answered for the consequence composite 2026-07-21)*: Should the `OccurrenceIndex` ALSO be applied within composites (e.g., a `CompositeHazard` containing two identical sub-hazards with different weights)? `CompositeConsequence.SetupSampler` answers it structurally: each child's seed is `HashCombine(seed, child.CanonicalHash(), ordinal)`, so identical-content siblings get independent draws from the list ordinal — no occurrence-index machinery and no dependence on distinguishing weights (pinned by test: two identical-content children draw independently). `CompositeHazard`/`CompositeResponse` adopt the same recipe in Phase 9.
- **Q-K**: ~~Refactor `EventTreeResponse` to consume k percentiles…~~ **Resolved 2026-04-30**: incorporated into §5.8.6. Event trees are LHS-driven from day one of the Phase 2.3 port.
- **Q-L**: Default value of `RiskAnalysisOptions.SamplingScheme` — `LatinHypercube` (proposed; gives the variance-reduction win out of the box) vs. `MonteCarlo` (legacy parity, opt-in). Recommended `LatinHypercube`. Confirm before Phase 2.5.
- **Q-M**: Bootstrap posterior size vs. Realizations count. `ParametricHazard.SampleFunction(int idx)` looks up the idx-th posterior parameter set. The legacy bootstrap stores M ∈ [100, 100000] samples; risk analysis runs N ∈ [1000, 10000+] realizations. If M < N, indices currently wrap modularly. Two options: keep the index-based path (simpler, parity with legacy) or convert to a `SamplingDimensions = 1` percentile-based path (`posterior[(int)(p * M)]`) so the bootstrap participates in LHS. Decide during Phase 2.1; for the initial port, preserve index-based.
- **Q-N** *(amended 2026-07-20, v0.9)*: Fail vs. non-fail consequence coupling. Legacy `SampledFailureMode` draws ONE percentile r per realization and uses it for BOTH the failure consequence and a parallel sample of the parent non-failure consequence on the same FM, so excess = fail − non-fail is sampled coherently at the realization level. With per-function `SetupSampler`, fail and non-fail samplers are independent by default — the coupling is lost. Default proposal: have `FailureMode.SetupSamplers` allocate one shared consequence percentile that drives both functions via `SampleFunction(double percentile)` instead of `SampleFunction(int idx)`. v0.9 amendment: under multi-consequence lists the coupling applies **per paired position** (the k-th failure consequence shares its draw with the k-th non-failure consequence — pairing is positional). Design with the engine phase's sampled machinery. **RESOLVED 2026-07-22, v0.14**: the technical reference grounds the shared draw (C_F and C_NF perfectly correlated within a mode); landed as the failure mode's N×max(K,1) coupling matrix driving both sides of each positional pair through `SampleExposureBranches(double percentile)` — consequence functions are excluded from the per-function sampler walk (see the v0.14 status block, item 2).
- **Q-O**: `FaultTreeResponse` design. v2 placeholder shipped in v1.1.0; full design (gate types, basic-event probabilities, minimal-cut-set enumeration, LHS dimension recipe) deferred. Pre-design discussion before v2 starts.
- **Q-P**: `BestFitTabularHazard` import shape. BestFit's `CoincidentFrequencyAnalysis` produces an X × Y × Z table with MCMC sample bounds. Decide what gets stored in canonical hash: the full Z[i,j] grid, or a compressed posterior-summary representation. Affects file size on save and canonical-hash bytes; doesn't affect math. Resolve during Phase 2.1.
- **Q-Q**: Copula sampling under LHS for `ParametricBivariateHazard`. The copula's parameter uncertainty (if present) needs to be one LHS dimension; the marginals each have their own. `SamplingDimensions` for a parametric bivariate hazard is `MarginalX.SamplingDimensions + MarginalY.SamplingDimensions + (copula uncertain ? 1 : 0)`. Confirm during Phase 2.1.
- **Q-R**: `BivariateResponse` surface uncertainty. The existing legacy `BivariateResponse` is deterministic (D=0 per the explore agent's report). For v1.1.0, do we add knowledge uncertainty on the surface itself (e.g., uncertainty per surface ordinate)? Default proposal: keep deterministic for the initial port; revisit in v2 if users request it. Tracked.
- **Q-S** *(added 2026-07-19)*: Phase 2.0 design details tracked in [SHARED_FUNCTIONS_STRATEGY.md](SHARED_FUNCTIONS_STRATEGY.md) §9: `SegmentedPowerFunction` parameter-layout verification vs BestFit `RatingCurve.cs` (S-1); `EnsembleFunction` index-wrap/percentile policy (S-2 — interacts with Q-M for imported posteriors); `KernelDensity` round-trip (S-3); `CanonicalContentHasher` upstreaming to `Numerics.Utilities` (S-4); `UncertainOrderedPairedData` extension needs (S-5).
- **Q-T** *(added 2026-07-20, v0.9)*: `ProfileHazardFunction` shape. In v1.0 it is a results-reporting selector typed `IElement` (the primary hazard or any transform point) driving risk profiles and the assurance threshold. Its v1.1 shape — a chain-position binding like the consequence binding, an element reference on the component, or an analysis-level option — should be designed with the results containers it labels (engine phase). Until then `HazardThreshold` lands without its companion selector. **Deferral ratified 2026-07-22 (v0.14)**: the profile remap and the legacy `Sensitivity()` surface both move to the risk-diagnostics/sensitivity sessions; Phase 4 profiles report on the driving hazard axis.
- **Q-U** *(added 2026-07-20, v0.9)*: Engine semantics of `MultipleConsequences` under response fan-out. The projection sets the flag when a path's last response feeds ≥ 2 terminals (each terminal still projects its own failure mode). Confirm in the engine phase that the v1.0 joint-consequence handling of fanned-out consequence sets is reproduced (or deliberately revised) when those modes share one response outcome per realization. **Interim ratified 2026-07-22 (v0.14)**: risk math and LECs consume the primary consequence (`ConsequenceFunctions[0]`) only; later positions stay coupled through the coupling-matrix shape but are unrecorded until the multi-axis results design lands.
- **Q-V** *(added 2026-07-21, v0.13; **RESOLVED 2026-07-22, v0.14 — ratified YES**)*: mixture-branch exposure enumeration applies in the full-MC path too. Mixture weights are aleatory exposure, so every realization's LEC carries the mixture spread and the ensemble stays purely epistemic. Landed: `SampledFailureMode` is branch-aware in both paths, `CompositeConsequence.SamplingDimensions` is 0, and the standalone per-realization mixture surface keeps its pre-Q-V stream through an internal selector matrix (see the v0.14 status block, item 1).
- **Q-W** *(added 2026-07-21, v0.13)*: *Shared exposure state* across consequence types. §6.4.1 takes the cross product of mixture branches across a failure mode's ordered consequence types with product weights, which assumes the types draw their branch independently. In reality a single day/night draw should drive **all** consequence types on that mode at once (economic and life loss share the same exposure state). v1.0 has no concept of this. Design a per-mode shared-exposure declaration in a later phase; until then the independent cross product is the documented behavior (and the 64/1024-branch guardrails — landed in `FailureMode.Validate()` and `SystemComponent.Validate()` at v0.14 — bound its cost).
- **Q-X** *(added 2026-07-22, v0.14)*: Multi-stage response composition semantics. The Phase-3 grammar `T* (R T*)* C` authors chains with two or more response stages, but v1.0 had exactly one response and no oracle covers composition. **Ratified deferral**: end users are expected to want event-tree branch semantics (each stage a chance node spawning branches with separate consequences), not a stage-probability product, so the engine gate (`RiskAnalysis.Validate()`) rejects multi-stage modes until the event-tree phase designs the composition; `SampledFailureMode`'s constructor throw is the placeholder seam. Authoring and serialization of multi-stage modes are untouched.

### Resolved

- **Q-G** *(resolved 2026-07-20, v0.9)*: `CorrelationMatrix` serializes and hashes the **full matrix, row-major** (G17; rows `;`-separated, values `,`-separated), and only under `DependencyType.CorrelationMatrix` — the automatic modes derive their matrices and never persist them (a lazily built MVN could otherwise perturb the hash). v1.0's braced format is not read: its parse loop discarded every value, so no legacy file ever round-tripped a matrix.
- **Q-A** *(resolved 2026-07-19, v0.6)*: Ordinate-distribution hashing. Under XML canonicalization each ordinate's Y-distribution serializes via Numerics `UnivariateDistributionBase.ToXElement()`, which carries the stable `Type` attribute (`UnivariateDistributionType` enum — confirmed present and append-only in Numerics). No separate tag table needed.
- **Q-C** *(resolved 2026-07-19, v0.6)*: `UncertaintyAnalysisResults` is a **Numerics** type with `ToXElement()` / `FromXElement` (plus JSON `ToByteArray`/`FromByteArray`); BestFit already persists results through exactly these methods in dedicated `.rmcbf` columns (`AnalysisPersistenceHelper`). No adapter needed — and no `RMC.BestFit.dll` reference at all (v0.6, strategy D2).

---

## Appendix A — Canonicalization rules (v0.6)

*(Replaces the v0.5 type-tag enum — under XML canonicalization the `ToXElement()` element name serves as the type tag.)*

The audited strip-rule set lives in `Models/Support/CanonicalizationRules.cs`; the hasher in `Models/Support/CanonicalContentHasher.cs`. Both adapt `C:\GIT\Hydrologics\src\Hydrologics\Core\CanonicalContentHasher.cs` / `CanonicalizationRules.cs`.

```csharp
namespace RMC.TotalRisk.Core
{
    public sealed class CanonicalizationRules
    {
        /// <summary>
        /// The audited rule set (v0.8 name: ModelRules) — identity, display, and presentation
        /// metadata that must never perturb Monte Carlo seeds. APPEND-ONLY: every new
        /// non-compute property lands here AND in the kitchen-sink invariance test.
        /// </summary>
        public static CanonicalizationRules ModelRules { get; } = new(
            strippedAttributes: new[]
            {
                "Name", "Description", "NameOnDisk", "Guid",
                "LeftPosition", "TopPosition",
                "SpecifiedHazard", "HazardUnit", "TransformedHazard", "TransformedHazardUnit",
                "SpecifiedConsequence", "ConsequenceUnit",
                "ChartSettings",
            },
            strippedElements: Array.Empty<string>(),
            rewriters: Array.Empty<Action<XElement>>());
    }
}
```

`CanonicalContentHasher.Hash(XElement, CanonicalRuleSet)` strips per the rules, encodes the surviving tree with an injective length-prefixed binary encoding (attributes ordinally sorted; owned-child order preserved as semantic), and returns the SHA-256 digest.

Landing checklist for every new model property (the Hydrologics pattern):

1. Classify: compute-relevant (stays in the hash) vs metadata (add to `ModelElementRules`).
2. Extend the kitchen-sink rename/reorder invariance test in `RMC.TotalRisk.Tests`.
3. Never rename or reorder existing serialized attributes/children — hashes are contract.

## Appendix B — Seed helper types (semantics unchanged from v0.5)

The occurrence-index seed derivation of §5.5.4 is untouched by the v0.6 hashing-mechanism change.

```csharp
// In Models/Support/SeedHelpers.cs
public static class SeedHelpers
{
    public static int HashCombine(int globalSeed, byte[] componentHash, int occurrenceIndex)
    {
        Span<byte> tail = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(tail[..4], globalSeed);
        BinaryPrimitives.WriteInt32LittleEndian(tail[4..], occurrenceIndex);
        using var sha = SHA256.Create();
        sha.TransformBlock(tail.ToArray(), 0, 8, null, 0);
        sha.TransformFinalBlock(componentHash, 0, componentHash.Length);
        return BinaryPrimitives.ReadInt32LittleEndian(sha.Hash!.AsSpan(0, 4));
    }
}

// In Models/Support/ByteArrayComparer.cs
public sealed class ByteArrayComparer : IComparer<byte[]>
{
    public static readonly ByteArrayComparer Instance = new();
    public int Compare(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return x.AsSpan().SequenceCompareTo(y);
    }
}

// On RiskFunctionBase (v0.8; SystemComponent/FailureMode implement the same directly):
public byte[] CanonicalHash()
    => CanonicalContentHasher.Hash(ToXElement(), CanonicalizationRules.ModelRules);
```

---

End of architecture document. Append revision dates inline as the doc evolves.
