# RMC-TotalRisk v1.1 Roadmap

> Phased development of the v1.1 model library, tests, and verification suite. **One phase per working session.** Every phase exits with: a zero-warning build, the fast test suite green (`dotnet test -c Release`), `validate-code-xml-docs.ps1` green, the Ported Types Matrix in CLAUDE.md updated, `docs/PROGRESS.md` updated, and AGENTS.md regenerated if CLAUDE.md changed. **Every ported compute type ships with unit tests in `RMC.TotalRisk.Tests`, and gains verification coverage in `RMC.TotalRisk.Verification` by the phase that converts its legacy oracle scenarios.**
>
> **Ordering driver (2026-07-20):** the model library and REST API are needed by the web-based **Dam Screening Tool**, which uses only the tabular input functions and mean-only risk compute. Phases 1–6 therefore deliver the minimal function surface → risk components → engine → verification, before the remaining function options are backfilled (7–13). The REST API + MCP server is Phase 14; it depends only on Phases 1–6 and can be executed any time after Phase 6.

Porting sources in order of authority: (1) the partial C# port `C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\RMC.TotalRisk.IO\Project\Elements\`; (2) the legacy VB engine `...\RMC.TotalRisk\`; (3) the `Test_TotalRisk` Monte Carlo oracles (verification only); (4) the normative specs in [requirements/](requirements/) — where the spec deliberately departs from legacy (content-based seeding, LHS sampling, no-BestFit imports, JSON results), the spec wins.

**v1.0 API preservation (ratified 2026-07-20):** the input-function domain surface is preserved verbatim from v1.0 (property names/types/defaults, `SampleFunction` overload shapes and returns, `Min/Max*` shapes, the `Estimate()` lifecycle, legacy enum names) plus the ratified sampler additions. The analysis layer instead adopts a growth foundation (`RiskAnalysisOptions` extraction, a `RiskAnalysisMode` reliability option, future `CostBenefitAnalysis`); the future UI layer maps v1.0 projects onto the new analysis API on import. Risk results are the sanctioned exception: v1.0 BinaryFormatter BLOBs are replaced by redesigned System.Text.Json containers, and v1.0 projects re-run their analyses in v1.1.

| Phase | Scope | Status |
|---|---|---|
| 0 | Repo bootstrap: structure, projects, process machinery, docs | Complete (2026-07-20) |
| 1 | Model kernel foundation: `IRiskFunction`/`RiskFunctionBase`, hashing, seeding, sampling, serialization support | Not started |
| 2 | Core input functions: tabular hazard/transform/response/consequence + parametric hazard/response + non-fail response | Not started |
| 3 | Risk components + results containers (JSON results redesign) | Not started |
| 3.5 | Layer boundary seams: function `Id`, serialization modes, function resolver, change propagation | Complete (2026-07-20) |
| 4 | Analysis foundation + RiskAnalysis engine (mean-only first-class) + reliability mode | Not started |
| 5 | Verification I — single-component oracle families | Not started |
| 6 | Verification II — system risk + NFIP assurance | Not started |
| 7 | Remaining closed-form functions: linear/power transforms, parametric consequence, nonparametric hazard | Not started |
| 8 | Numerics.Functions expansion (numerics repo) + RMC.Numerics 2.2.0 package switch | Not started |
| 9 | Composites + RFA hazard + weighted wrappers + BestFit composite imports | Not started |
| 10 | Event trees | Not started |
| 11 | Bivariate + BestFit import + LifeSim | Not started |
| 12 | Hardening: coverage gate, Linux check, examples, getting-started | Not started |
| 13 | Release prep — `v1.1.0-alpha` tag | Not started |
| 14 | REST API + MCP server (`RMC.TotalRisk.Api`) | Not started (executable any time after Phase 6) |

---

## Phase 0 — Repo bootstrap

**Scope:** Reconcile the local folder with the official GitHub repo on branch `v1.1-development`; restructure to `docs/`, `examples/`, `src/`; scaffold `RMC.TotalRisk` + `RMC.TotalRisk.Tests` + `RMC.TotalRisk.Verification` (MSTest.Sdk/3.6.4, Verification excluded from Release); port the development-process machinery (CLAUDE.md/AGENTS.md, validation + sync scripts, Directory.Build/Packages.props, NuGet.config, global.json, LICENSE); seed the docs tree; import the architecture + shared-functions specs into `requirements/`.

**Exit criteria:** solution builds 0-warning; 2 smoke tests pass in Release; Verification produces no Release output; doc script green; docs tree live; work committed on `v1.1-development`. **Complete 2026-07-20.**

## Phase 1 — Model kernel foundation

**Scope:** `Models/Support` — the model library's domain-named kernel (no "element" vocabulary; that is UI-layer lingo from the wpf-framework `IElement` world, and no root `IModel` abstraction; rationale in [requirements/MODEL_LIBRARY_ARCHITECTURE.md](requirements/MODEL_LIBRARY_ARCHITECTURE.md) v0.8):

- `IRiskFunction` — THE kernel contract every input function implements: `INotifyPropertyChanged`, `Name`/`Description` (identity metadata, never hashed), `SpecifiedHazard`/`HazardUnit` axis labels, `IsDeterministic`, `SamplingDimensions`, `SetupSampler(sampleSize, seed, scheme)`, `ComputeUncertaintyResults(confidenceIntervalWidth)`, `Validate()`, `ToXElement()`, `CanonicalHash()`.
- `RiskFunctionBase` — the one shared implementation base: INPC scaffolding, label backing, `CanonicalHash()` pipeline, and the per-function N×D percentile sampler machinery (LHS default, MC legacy, LHS-median).
- `CanonicalContentHasher` + `CanonicalizationRules` (`ModelRules` audited strip list) — SHA-256 canonical content hashing over `ToXElement()`, adapted from Hydrologics `src/Hydrologics/Core/`.
- `SeedHelpers` (`HashCombine`, `IndependentUniform`), `ByteArrayComparer`, `SamplingScheme` enum, `SerializationUtilities` (G17/InvariantCulture helpers), `FunctionHelpers.ForceMonotonic`.

**Unit tests:** hash canonicalization (strip rules; rename/reorder invariance kitchen-sink scaffold that later phases extend), `HashCombine` determinism, sampler matrix shapes for LHS/MC schemes, serialization helpers (±0/NaN/∞/denormal, culture safety), INPC raises.

**Exit criteria:** all support types P/T; the kitchen-sink invariance test runs green and is extendable per cluster.

## Phase 2 — Core input functions

**Scope:** the minimal function surface the Dam Screening Tool and the engine phases need, with the v1.0 domain surface preserved verbatim:

- Cluster contracts on the Phase 1 kernel: `IHazardFunction`/`IUnivariateHazardFunction`/`HazardFunctionBase`/`UnivariateHazardBase`, `ITransformFunction`/`TransformFunctionBase`, `IResponseFunction`/`ResponseFunctionBase`, `IConsequenceFunction`/`ConsequenceFunctionBase`, `FunctionUncertainty` enum (standalone file, legacy name).
- **`TabularHazard`** — None/Hazard/Probability uncertainty modes, three `UncertainOrderedPairedData` tables (`NoUncertaintyFunction` — spelling fixed from the v1.0 typo, `HazardUncertainFunction`, `ProbabilityUncertainFunction`), mode-switched mean-curve assembly and percentile sampling, `ForceMonotonic` on invalid samples.
- **`ParametricUnivariateHazard`** (renamed from `ParametricHazard`) — `ParentDistribution` + bootstrap posterior via `Estimate()` (v1.0 lifecycle), **plus the posterior-injection overload `Estimate(IList<ParameterSet>)`** so the future UI importer passes BestFit posteriors (UnivariateAnalysis/Bulletin17C/PointProcess) as already-parsed Numerics artifacts.
- **`TabularTransform`** — single uncertain table over Numerics `TabularFunction`; co-monotonic `ConfidenceLevel` sampling.
- **`TabularResponse`** — co-monotonic `CurveSample` fragility sampling, `IsMonotonic()` legacy algorithm.
- **`ParametricResponse`** — LnNormal default, non-exceedance ordinates, bootstrap + the same `Estimate(IList<ParameterSet>)` injection overload.
- **`NonFailResponse`** — the engine's non-failure branch; instantiable type (no singleton), type-test identification.
- **`TabularConsequence`** — uncertain table, `AllowNegativeYValues = false` clamp.
- **Uncertainty-results contract:** every function implements `ComputeUncertaintyResults(confidenceIntervalWidth)` → Numerics `UncertaintyAnalysisResults`. Tabular types evaluate **exact co-monotonic percentile curves** (deterministic — no simulation); parametric types surface their stored bootstrap/imported posterior. This replaces the v1.0 app-layer code-behind visualization math and is the same method the Phase 4 uncertainty options and Phase 14 API consume.

**Unit tests:** ctor/default matrices (every preserved v1.0 default asserted), validation matrices, XElement round-trips, hash-invariance registrations, `SampleFunction` known-point checks, sampler smoke, Min/Max bounds, `Estimate` bootstrap vs injection, `ComputeUncertaintyResults` exactness.

**Exit criteria:** all seven concrete types + contracts P/T with hash-invariance coverage.

## Phase 3 — Risk components + the structured component graph — **COMPLETE (2026-07-20)**

*(Re-scoped at session start, ratified by the user: the risk setup is formalized as a structured DAG **within** each `SystemComponent`; results containers and the sampled machinery move to Phase 4. Arch doc v0.9 records the amendment.)*

**Scope (landed):** `Models/RiskAnalysis/Components`: `SystemComponent`, `FailureMode` (concrete classes — no interfaces; v1.0 option surface preserved incl. ctor defaults, `FailureModeDependency`/`CorrelationMatrix double[,]` with Cholesky validation, MVN builds with `1−√εmach` / `−1/(D−1)+√εmach` off-diagonals, CommonCause/MutuallyExclusive dependency coercion), `ResponseStage` (v1.1 response chains — grammar `T* (R T*)* C`; v1.0 members survive as views over stage 0), ordered multi-type `ConsequenceFunctions` (index 0 = primary for integration; positional fail/non-fail pairing), and the legacy-named enums `DependencyType`, `FailureModeMethod`, `JointConsequenceType`, `RiskType` + new `HazardDimension` (standalone files; `SystemRiskType` stays Phase 4/Analyses). Occurrence-index assignment (`AssignOccurrenceIndices`, arch doc §5.5.4). Name-based `FromXElement(XElement, IProject)` resolution replaced by self-contained inline child serialization (`RiskFunctionFactory`).

`Models/RiskAnalysis/Graph` — the formal component topology (arch doc v0.9): `IRiskElement`/`RiskElementBase`, `HazardElement` (single root) / `TransformElement` / `ResponseElement` / `ConsequenceElement`, `RiskConnection` (consumer-stored typed inputs; dual Id+Name+port serialization), `RiskElementFactory`/`RiskElementResolver`, `HazardSourceOption`, and **`ComponentGraph`** (name authority, Kahn sort + cycle detection, derived fan-out, upstream paths, `GetAvailableHazardSources` discoverability, the structural validation catalog, three-pass load, clone-map re-linking). `SystemComponent.Graph` is the persisted truth; `FailureModes` is a fresh deterministic projection (declared terminal order); `AddFailureMode` expands chain-style modes losslessly; `CanonicalHash()` hashes the identity form (options + hazard + projected FMs) so element renames/ids/canvas moves can never perturb seeds. Structural label-free consequence binding (`HazardSource` → `(ConsequenceHazardDimension, ConsequenceHazardPosition)`, resolved-on-write default = last response's input); bivariate output/input ports reserved now (functions Phase 11, zero shape breaks).

**Unit tests (landed; suite 240):** occurrence-index scheme (identical-content components get 0..n−1 in canonical order; cross-analysis stability; metadata-inert), component/FM/stage/element/graph validation matrices, correlation-matrix G17 round-trip (Q-G resolved), element-identity hash inertness + equal-content hash equality, projection determinism (levee acceptance scenario: consequence bound to raw peak flow ⇒ position 0), expansion round-trip (bit-identical hash), topology (sort/cycles/paths/fan-out), three-pass serialization incl. stale-Id throws and unknown-type policies.

**Moved to Phase 4:** `SampledComponent`, `SampledFailureMode` (Q-N per-pair shared-draw coupling — designed with the compute loop), `ComponentRiskOutput`, `SetupSamplers`/`Sample`, and all of `Models/RiskAnalysis/Results` (the JSON-first redesign: `Curve`, `Curves`, `RiskPoint`, `Ensemble`, `ComponentRealization`/`FailureModeRealization`/`SystemRealization`, `ComponentResults`/`FailureModeResults`/`SummaryRiskResults`/`SystemRiskResults`/`EnsembleResults` — explicit public state, `ToJson()`/`FromJson()` + compressed-bytes overloads, same computed outputs; v1.0 BLOBs unreadable, old projects re-run). `ProfileHazardFunction` deferred to the results design (Q-T).

**Exit criteria: met** — component + graph layers P/T; occurrence-index behavior pinned by tests; identity-form hashing pinned (the v1 canvas-position seed bug is structurally unreachable).

## Phase 3.5 — Layer boundary seams

Inserted as a fractional phase (rather than renumbering 4–13) after reviewing `Systems.Components`
against the `C:\GIT\rmc-bestfit` model → UI → App layering. Prepares the library for the UI/App
layers without changing anything a headless caller sees. Normative outcome:
[MODEL_LIBRARY_ARCHITECTURE.md §8](requirements/MODEL_LIBRARY_ARCHITECTURE.md#8-layer-boundaries--consumer-contract) (v0.11).

**Scope:** `IRiskFunction.Id`/`AssignNewId` as the rename-proof reference key (stripped from
hashing); `RiskSerializationMode { SelfContained, ByReference }` with additive `ToXElement(mode)`
overloads; `IRiskFunctionResolver`/`RiskFunctionResolver` mirroring `RiskElementResolver`;
wrapped-function change propagation through element → graph → component (with
`ConsequenceElement.Functions` an `ObservableCollection` whose owner reconciles per-item
subscriptions, per the BestFit `CompositeAnalysis.Analyses` precedent); authoring surface for a
DAG editor (`RiskElementFactory.CreateForFunction`/`Create`, `IRiskElement.TryAssignFunction`,
`GetReferencedFunctions`, unresolved-reference validation).

**The problem it solves:** elements owned and inlined their functions, so a component blob carried
a full copy of every function. Once functions are stored items in their own right, that copy would
win on load and silently discard edits made where the function is stored.

**Landed 2026-07-20.** `dotnet build` 0 warnings; `dotnet test -c Release` 265/265; docs validation
green. Headline pin: a component's canonical hash is byte-identical across both serialization
modes, so results can never depend on how a project was saved. Also pinned: by-reference round-trip
re-attaches the *same* function instances (`Assert.AreSame`); stale ids throw, missing names are
reported by validation; and a model-only end-to-end test builds, validates, and hashes a system
with no store, no resolver, and no consuming layer in the call path.

**Exit criteria: met.**

## Phase 4 — Analysis foundation + RiskAnalysis engine + reliability mode

**Scope:** the Phase-3 deferrals first — `SampledComponent`/`SampledFailureMode` (Q-N per-pair shared-draw coupling), `ComponentRiskOutput`, `SetupSamplers`/`Sample` on `SystemComponent`/`FailureMode`, and the `RMC.TotalRisk.Results` JSON-first containers (see the Phase 3 moved-scope list) — then `Analyses` (`IAnalysis` in `Core.Interfaces`, `AnalysisBase`, `AnalysisRunCompletedEventArgs` — BestFit mirror) and **`RiskAnalysisOptions`** (ratified extraction; v1.0 option names/defaults preserved: `EstimateMeanRiskOnly=true`, `Realizations=1000` [100–10000], `PRNGSeed=12345`, `LECOutputLength=200` [50–1000], `ConfidenceIntervalWidth=0.9`, `Alpha=0.01`, `ConsequenceThreshold=0`, `SystemRiskMethod`/`JointConsequences`/`ComponentHazardDependency`/`HazardCorrelationMatrix`, integration options + `UseDefaults`/`SetIntegrationDefaults`; new `SamplingScheme`). The options' `ConfidenceIntervalWidth` drives per-function uncertainty summaries through the Phase 2 `ComputeUncertaintyResults` contract.

`RiskAnalysis.RunAsync`: validation gate, content-based per-component seeds + occurrence indices (replacing the v1.0 canvas-order master-PRNG cascade — the seed-dependency bug), per-function `SetupSampler` walk, then the v1.0 compute preserved exactly: mean-only path (`Compute(-1,-1)`-equivalent, every function mean-sampled, same integration) and full-MC path (`Parallel.For`, per-realization `SystemRealization` + compact `SystemRiskResults`, `PostProcessUncertainty` percentile curves). Integration constants preserved: AdaptiveSimpson over `p∈[1e-16,1−1e-16]` with 50 stratified hazard bins, tol 1e-8, MaxDepth 100, MaxEvaluations 1e6; Vegas warmup/final cycles for joint risk; competing-failures 200-bin CIF pre-processing; LEC via log10 consequence bins. Lifecycle: `Task RunAsync(progress, ct)`, cancellation, `AnalysisStarting`/`AnalysisCompleted`. `RiskAnalysis` owns its components (they are not independently creatable in the UI/App) so future composite analyses (`CostBenefitAnalysis` over a `List<RiskAnalysis>` of alternatives) can own instances. Per §8 (v0.11) its `ToXElement()` serializes **options + `IsEstimated` only** — components and results arrive through the constructor, the BestFit `new UnivariateAnalysis(dist, xElement, results)` shape — and the results containers land in `RMC.TotalRisk.Results`.

**`RiskAnalysisMode.Reliability`** (v0.10 — replaces the planned `ReliabilityAnalysis` sibling) — a mode of the one `RiskAnalysis`: failure probabilities / annualized failure probability per FM/component/system, no consequence functions required (relaxed FailureMode validation), reusing the sampled-component machinery with a reduced integrand and its own results shape. One graph traversal serves both modes. May split to a 4b session if the engine session overruns.

**Unit tests:** engine smoke on tiny scenarios (mean-only + full MC); cancellation; validation failures throw; event lifecycle; options round-trip + hash recipe.

**Verification (pinned here, expanded in Phases 5–6):** the v1 seed-bug regression — shuffle components / rename everything / edit metadata → **bit-identical** results; same seed → bit-identical across thread counts.

**Exit criteria:** foundation + engine P/T with reproducibility pinned; the reliability mode P/T (or explicitly split to 4b).

## Phase 5 — Verification I: single-component oracle families

**Scope:** Convert the Bucket-1 legacy oracles to asserted C# verification classes at **1,000,000 realizations** (policy: [verification.md](verification.md)): `Test_MC_JointFailures` (1-component, 2- and 5-PFM, {Independent/Positive/Negative/Correlation} × {Additive/Average/Maximum/Minimum}), `Test_MC_CompetingFailures`, `Test_MC_CommonCause`, `Test_MC_MutuallyExclusive`, `Test_EAD`. Engine-vs-oracle statistical asserts on the five summary outputs (incremental/irreducible/total/failure/non-failure means) + failure probability; FN-curve spot checks. If a legacy scenario requires a `LinearTransform`/`PowerTransform` as a typed model object, pull that thin wrapper forward from Phase 7 (zero-math wrappers over existing `Numerics.Functions`).

**Exit criteria:** all four combination-rule families + EAD verified; tolerances documented per test with k·SE derivation; suite runtime recorded.

## Phase 6 — Verification II: system risk + NFIP assurance

**Scope:** Convert `Test_MC_SystemRisk` (2-component/2-PFM and 5-component/1-PFM across the correlation × aggregation matrix — port from method *bodies*, several legacy names are mislabeled), the `Test_RiskAnalysis` N-element/N-PFM combos, and NFIP Assurance TOL 50/55/70 (LP3 flow frequency → rating transform → fragility → AEP). Add the LHS variance-reduction test (MC vs LHS at N=1k over repeated runs). Finalize the tolerance policy in [verification.md](verification.md).

**Exit criteria:** multi-component system risk + NFIP verified; tabular+parametric clusters, the engine, and the reliability mode all carry V status in the matrix.

## Phase 7 — Remaining closed-form functions

**Scope:** **`LinearTransform`** (`y = α + βx`, optional Gaussian uncertainty), **`PowerTransform`** (`y = α(x − ξ)^β`, log-space uncertainty, optional inversion) — thin wrappers over the existing `Numerics.Functions` `LinearFunction`/`PowerFunction` per [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md); **`ParametricConsequenceFunction`** (new: `C(h) = clamp(α·max(h−h₀,0)^β, 0, U)` per ER 1110-2-1156); **`NonparametricHazard`** (empirical CDF + Weibull extrapolation). Domain labels, validation, serialization, hash identity, `ComputeUncertaintyResults` — zero math in the wrappers.

**Unit tests:** evaluation at known points vs closed forms (incl. uncertainty at fixed percentile, inverse power form, clamping), serialization/hash coverage, transformed-hazard bounds. Resolve open question Q-N (fail/non-fail consequence percentile coupling) formally if not already pinned by Phase 3.

**Exit criteria:** four types P/T; any Phase 5/6 deferred scenarios that needed these types converted.

## Phase 8 — Numerics.Functions expansion (numerics repo) + package switch

**Scope:** Executed in `C:\GIT\numerics` (branch `bug-fixes-and-enhancements`) per [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md) §4: N1 function serialization + `UnivariateFunctionFactory`; N2 `SegmentedPowerFunction` (BestFit BaRatin rating form, `ParameterSet`-compatible layout); N3 `CompositeFunction`; N4 `EnsembleFunction` posterior sampling; N5 `EmpiricalDistribution` XElement round-trip fix; N6 tests + `docs/functions/` guide. Release **RMC.Numerics 2.2.0** to the local feed; switch this repo's three csprojs from the HintPath to the PackageReference (Hydrologics does the same on its side).

**Exit criteria:** 2.2.0 on the feed; this repo builds green on the package.

## Phase 9 — Composites + RFA hazard + weighted wrappers

**Scope:** `RFAHazard`, `CompositeHazard` + `WeightedHazardFunction`; `CompositeTransform` + `WeightedTransformFunction`; `CompositeResponse` + `WeightedResponseFunction`; `CompositeConsequence` + `WeightedConsequenceFunction` — composite math on Numerics `CompositeFunction`/`Mixture`/`CompetingRisks`. **`CompositeHazard` gains the parameter-set import option** (mirroring the Phase 2 parametric injection) so BestFit competing-risks/mixture/composite results import via the UI layer as Numerics artifacts. Resolve whether the injection path supersedes the planned `BestFitUnivariateHazard` type. Resolve Q-I (weighted-list ordering in the canonical hash) and Q-J (occurrence index within composites).

**Verification:** `Test_Composite` (incl. its built-in mixture consistency cross-check), `Test_Composite_Uncertainty`, `Test_Composite_Consequence_Mixture`, NFIP TOL 60/65 (hazard bootstrap variants).

**Exit criteria:** composite family P/T/V.

## Phase 10 — Event trees

**Scope:** `IEventNode`, `EventNodeBase`, `ChanceNode`, `InitiatingNode`, `RemainderNode`, `SecondaryHazardNode`, `WeightedHazardLevel`, `EventNodeExtensions`; `EventTreeResponse` with LHS-driven traversal (arch doc §5.8.6); post-order canonical hashing (Q-B resolution).

**Verification:** port the legacy `Test_EventTree` serialization round-trip (the suite's only genuinely asserted legacy test) + its product oracle.

**Exit criteria:** event-tree family P/T/V.

## Phase 11 — Bivariate + BestFit import + LifeSim

**Scope:** Bivariate hazards (`ParametricBivariateHazard`, `BestFitBivariateHazard`, `BestFitTabularHazard` — posterior-import types holding Numerics artifacts only), `BivariateResponse` + the §7.4 nested Y|X integration, `BestFitTransform` (posterior import; no `RMC.BestFit.dll`), `LifeSimConsequence` + `LifeSimResult`, `FaultTreeResponse` v2 placeholder. Revisit `BestFitUnivariateHazard` here only if Phase 9 concluded the parametric injection path does not fully supersede it.

**Verification:** `Test_BivariateRisk` (legacy 100M → 1M with widened, documented tolerance), `Test_DAMRAE` (bilinear surfaces); BestFit import contract test (deserialize `MCMCResults`/`UncertaintyAnalysisResults` with Numerics alone → construct → evaluate).

**Exit criteria:** full v1.1 input-function surface P/T/V.

## Phase 12 — Hardening

**Scope:** ≥90% line-coverage gate on `RMC.TotalRisk.dll` from the fast suite; one-off Linux `dotnet build` container check (proves no Windows-only dependency); `examples/` documentation (the two v1.0 `.tra` projects described + a headless model-lib code example); `docs/getting-started.md`; `docs/REMAINING-WORK.md`.

**Exit criteria:** coverage + Linux gates green; docs live.

## Phase 13 — Release prep

**Scope:** final full-suite verification run (recorded), release notes, version stamping, tag **`v1.1.0-alpha`** of the model library (user pushes/tags).

**Exit criteria:** alpha tag cut.

## Phase 14 — REST API + MCP server

> Depends only on Phases 1–6 (the tabular + mean-only surface); can be executed any time after Phase 6 to unblock the Dam Screening Tool.

**Scope:** `src/RMC.TotalRisk.Api` + `src/RMC.TotalRisk.Api.Tests`, mirroring `RMC.BestFit.Api` (confirmed template: one ASP.NET Core `Microsoft.NET.Sdk.Web` net10.0 project hosting **both** surfaces):

- **Packages:** `ModelContextProtocol.AspNetCore`, `Microsoft.AspNetCore.OpenApi`, `Microsoft.OpenApi` (central package management; nuget.org).
- **Layering (all DI singletons; state in the store):** `Store/` (`IResourceStore`/`InMemoryResourceStore`: GUID-keyed concurrent dictionaries, capacity cap, per-resource `RunLock`, no eviction) → `Services/` (stateless facades shared verbatim by controllers and MCP tools; run orchestration: per-resource lock → 409, global `SemaphoreSlim(MaxConcurrentRuns)` throttle, typed exceptions, run-state machine) → `Mappers/` (model/Numerics → flat DTOs; multi-dim arrays decomposed to parallel lists; NaN→null diagnostics) → `DTOs/` (`Create*Request`; responses derive from a shared `ResponseBase`: success/errorMessage/validationErrors/validationWarnings/computationTimeMs/timestamp/nonFiniteFindings) → thin attribute-routed controllers over `ApiControllerBase.ExecuteAsync` (timing, finite-audit rejecting ±Infinity → 500, exception→status mapping 400/404/409/499/500).
- **Endpoints per resource kind** (input functions, system components/failure modes, risk + reliability analyses): `POST` create / `POST {id}/run` (synchronous; CancellationToken wired to `CancelAnalysis`) / `GET` list / `GET {id}` / `GET {id}/results` / `GET {id}/validate` / `DELETE {id}`; function uncertainty previews via `ComputeUncertaintyResults`; plus metadata/health/`/api/info`; OpenAPI at `/openapi/v1.json` via `AddOpenApi()`.
- **JSON:** camelCase, `JsonStringEnumConverter(camelCase)`, ignore-null, `AllowNamedFloatingPointLiterals` (NaN legal; ±Infinity rejected by the finite auditor).
- **MCP:** `AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<...>()` + `app.MapMcp("/mcp")`; one `[McpServerToolType]` class per area; snake_case tool names; flat primitive params with `[Description]`; `McpJson` helper mirroring the REST JSON options; consider collapsed `run_analysis`/`get_analysis_results` dispatching on stored kind.
- **Tests:** `WebApplicationFactory<Program>` in-process integration suite + raw JSON-RPC `tools/list` smoke against `/mcp`; unit tests per DTO/mapper/controller/service. **Docs:** `docs/api.md` in BestFit's structure (concepts → endpoint tables → MCP section with `claude mcp add` line + tool list + agent chains).
- **Known gaps to plan as net-new work when deployment demands:** BestFit ships no Dockerfile/AWS artifacts and configures no auth scheme — containerization and auth are additive tasks for the Dam Screening Tool deployment.

**Exit criteria:** API + MCP green in-process test suite; OpenAPI served; `docs/api.md` live.

## Future phases (placeholders — planned when the model lib stabilizes)

- **UI layer** — `RMC.TotalRisk.UI` (net10.0-windows): project model, element wrappers (the "element" vocabulary lives here), `.tra` (SQLite) + `.rmcbf` reading, v1.0-project import mapping onto the v1.1 analysis API, data binding on the model lib's INPC surface.
- **Desktop App** — the WPF shell ported from the legacy VB app.
- **`CostBenefitAnalysis`** — composite analysis owning a `List<RiskAnalysis>` of alternatives (the Phase 4 self-contained-analysis foundation is its design driver).
- **FDA importer + datasets** — port `FDAImporter_143`; bring the HEC-FDA comparison datasets in-repo via a `docs/verification-requests/` request; convert the FDA integration oracles.
- **Cross-engine parity** — the levee "same results" test against Hydrologics' event-based risk layer, in a verification-only project referencing both (see [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md) §7).
