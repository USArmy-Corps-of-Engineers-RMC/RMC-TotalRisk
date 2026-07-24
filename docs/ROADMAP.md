# RMC-TotalRisk v1.1 Roadmap

> Phased development of the v1.1 model library, tests, and verification suite. **One phase per working session.** Every phase exits with: a zero-warning build, the fast test suite green (`dotnet test -c Release`), `validate-code-xml-docs.ps1` green, the Ported Types Matrix in CLAUDE.md updated, `docs/PROGRESS.md` updated, and AGENTS.md regenerated if CLAUDE.md changed. **Every ported compute type ships with unit tests in `RMC.TotalRisk.Tests`, and gains verification coverage in `RMC.TotalRisk.Verification` by the phase that converts its legacy oracle scenarios.**
>
> **Ordering driver (2026-07-20):** the model library and REST API are needed by the web-based **Dam Screening Tool**, which uses only the tabular input functions and mean-only risk compute. Phases 1–6 therefore deliver the minimal function surface → risk components → engine → verification, before the remaining function options are backfilled (7–13). The REST API + MCP server is Phase 14; it depends only on Phases 1–6 and can be executed any time after Phase 6.

Porting sources in order of authority: (1) the partial C# port `C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\RMC.TotalRisk.IO\Project\Elements\`; (2) the legacy VB engine `...\RMC.TotalRisk\`; (3) the `Test_TotalRisk` Monte Carlo oracles (verification only); (4) the normative specs in [requirements/](requirements/) — where the spec deliberately departs from legacy (content-based seeding, LHS sampling, no-BestFit imports, JSON results), the spec wins.

**v1.0 API preservation (ratified 2026-07-20):** the input-function domain surface is preserved verbatim from v1.0 (property names/types/defaults, `SampleFunction` overload shapes and returns, `Min/Max*` shapes, the `Estimate()` lifecycle, legacy enum names) plus the ratified sampler additions. The analysis layer instead adopts a growth foundation (`RiskAnalysisOptions` extraction, a `RiskAnalysisMode` reliability option, future `CostBenefitAnalysis`); the future UI layer maps v1.0 projects onto the new analysis API on import. Risk results are the sanctioned exception: v1.0 BinaryFormatter BLOBs are replaced by redesigned System.Text.Json containers, and v1.0 projects re-run their analyses in v1.1.

| Phase | Scope | Status |
|---|---|---|
| 0 | Repo bootstrap: structure, projects, process machinery, docs | Complete (2026-07-20) |
| 1 | Model kernel foundation: `IRiskFunction`/`RiskFunctionBase`, hashing, seeding, sampling, serialization support | Complete (2026-07-20) |
| 2 | Core input functions: tabular hazard/transform/response/consequence + parametric hazard/response + non-fail response | Complete (2026-07-20) |
| 3 | Risk components + the structured component graph (results containers moved to Phase 4) | Complete (2026-07-20) |
| 3.5 | Layer boundary seams: function `Id`, serialization modes, function resolver, change propagation | Complete (2026-07-20) |
| 4 | Analysis foundation + RiskAnalysis engine core (1D): AGK integrator, exact LEC + risk measures, `RiskIntegrand`, mixture-exposure mean-only | Complete (2026-07-22) |
| 4b | Multi-dimensional system risk: additive lattice convolution (strict independence), joint Vegas tail focus + real combination enumeration | Complete (2026-07-23) |
| 4c | `RiskAnalysisMode.Reliability` | Complete (2026-07-23) |
| 5 | Verification I — single-component oracle families | Complete (2026-07-23) |
| 6 | Verification II — system risk + NFIP assurance | Complete (2026-07-23) |
| 6.5 | Multi-consequence axis (Q-U closure) + engine performance + cascade design ratification | Complete (2026-07-23) |
| 6.6 | Risk measures, diagnostics & sensitivity | Not started |
| 6.7 | Cascading response end states (the event tree in the risk diagram) | Not started (design ratified 2026-07-23, arch doc v0.16) |
| 7 | Remaining closed-form functions: linear/power transforms, parametric consequence, nonparametric hazard | Not started (`ParametricConsequence` pulled forward 2026-07-21) |
| 8 | Numerics.Functions expansion (numerics repo) + RMC.Numerics 2.2.0 package switch | Not started |
| 9 | Composites + RFA hazard + weighted wrappers + BestFit composite imports | Not started (`CompositeConsequence` + `WeightedConsequenceFunction` pulled forward 2026-07-21) |
| 10 | Event trees | Not started |
| 11 | Bivariate + BestFit import + LifeSim | Not started |
| 12 | Hardening: coverage gate, Linux check, examples, getting-started | Not started |
| 13 | Release prep — `v1.1.0-alpha` tag | Not started |
| 14 | REST API + MCP server (`RMC.TotalRisk.Api`) | Not started (executable any time after Phase 6) |

---

## Phase 0 — Repo bootstrap

**Scope:** Reconcile the local folder with the official GitHub repo on branch `v1.1-development`; restructure to `docs/`, `examples/`, `src/`; scaffold `RMC.TotalRisk` + `RMC.TotalRisk.Tests` + `RMC.TotalRisk.Verification` (MSTest.Sdk/3.6.4, Verification excluded from Release); port the development-process machinery (CLAUDE.md/AGENTS.md, validation + sync scripts, Directory.Build/Packages.props, NuGet.config, global.json, LICENSE); seed the docs tree; import the architecture + shared-functions specs into `requirements/`.

**Exit criteria:** solution builds 0-warning; 2 smoke tests pass in Release; Verification produces no Release output; doc script green; docs tree live; work committed on `v1.1-development`. **Complete 2026-07-20.**

## Phase 1 — Model kernel foundation — **COMPLETE (2026-07-20)**

**Scope:** `Models/Support` — the model library's domain-named kernel (no "element" vocabulary; that is UI-layer lingo from the wpf-framework `IElement` world, and no root `IModel` abstraction; rationale in [requirements/MODEL_LIBRARY_ARCHITECTURE.md](requirements/MODEL_LIBRARY_ARCHITECTURE.md) v0.8):

- `IRiskFunction` — THE kernel contract every input function implements: `INotifyPropertyChanged`, `Name`/`Description` (identity metadata, never hashed), `SpecifiedHazard`/`HazardUnit` axis labels, `IsDeterministic`, `SamplingDimensions`, `SetupSampler(sampleSize, seed, scheme)`, `ComputeUncertaintyResults(confidenceIntervalWidth)`, `Validate()`, `ToXElement()`, `CanonicalHash()`.
- `RiskFunctionBase` — the one shared implementation base: INPC scaffolding, label backing, `CanonicalHash()` pipeline, and the per-function N×D percentile sampler machinery (LHS default, MC legacy, LHS-median).
- `CanonicalContentHasher` + `CanonicalizationRules` (`ModelRules` audited strip list) — SHA-256 canonical content hashing over `ToXElement()`, adapted from Hydrologics `src/Hydrologics/Core/`.
- `SeedHelpers` (`HashCombine`, `IndependentUniform`), `ByteArrayComparer`, `SamplingScheme` enum, `SerializationUtilities` (G17/InvariantCulture helpers), `FunctionHelpers.ForceMonotonic`.

**Unit tests:** hash canonicalization (strip rules; rename/reorder invariance kitchen-sink scaffold that later phases extend), `HashCombine` determinism, sampler matrix shapes for LHS/MC schemes, serialization helpers (±0/NaN/∞/denormal, culture safety), INPC raises.

**Exit criteria:** all support types P/T; the kitchen-sink invariance test runs green and is extendable per cluster.

## Phase 2 — Core input functions — **COMPLETE (2026-07-20)**

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

## Phase 4 — Analysis foundation + RiskAnalysis engine core (1D)

> **v0.13 (2026-07-21):** the single Phase 4 was split into 4 / 4b / 4c and the "port the v1.0 compute exactly" instruction was replaced. The legacy engine is demonstrably wrong on LEC tails and system aggregation, and Numerics has since gained the tools to fix it. Normative detail: [requirements/MODEL_LIBRARY_ARCHITECTURE.md](requirements/MODEL_LIBRARY_ARCHITECTURE.md) §6.4.1, §7.3, §7.7, §7.8 (v0.13); math: [technical-reference/risk-integration.md](technical-reference/risk-integration.md), [technical-reference/loss-exceedance-curves.md](technical-reference/loss-exceedance-curves.md).

**Scope:** the Phase-3 deferrals first — `SampledComponent`/`SampledFailureMode` (Q-N per-pair shared-draw coupling; and Q-V, ratified at session start, on whether mixture-branch enumeration also applies in the full-MC path), `ComponentRiskOutput` (**with the parallel `ResponseProbabilities`/`FailureConsequences`/`ExcessConsequences` lists live from the start — the legacy `ComponentRiskOutput.vb:39` TODO — since Phase 4b needs them**), `SetupSamplers`/`Sample` on `SystemComponent`/`FailureMode`, and the `RMC.TotalRisk.Results` JSON-first containers (see the Phase 3 moved-scope list) — then `Analyses` (`IAnalysis` in `Core.Interfaces`, `AnalysisBase`, `AnalysisRunCompletedEventArgs` — BestFit mirror) and **`RiskAnalysisOptions`** (ratified extraction; v1.0 option names/defaults preserved: `EstimateMeanRiskOnly=true`, `Realizations=1000` [100–10000], `PRNGSeed=12345`, `LECOutputLength=200` [50–1000], `ConfidenceIntervalWidth=0.9`, `Alpha=0.01`, `ConsequenceThreshold=0`, `SystemRiskMethod`/`JointConsequences`/`ComponentHazardDependency`/`HazardCorrelationMatrix`, integration options + `UseDefaults`/`SetIntegrationDefaults`; new `SamplingScheme`; **new `RiskIntegrand` (default `MeanTotalRisk`)**). The options' `ConfidenceIntervalWidth` drives per-function uncertainty summaries through the Phase 2 `ComputeUncertaintyResults` contract.

**Engine core (1D / single-component / additive-per-component path):**
- `RiskAnalysis.RunAsync`: validation gate, content-based per-component seeds + occurrence indices (replacing the v1.0 canvas-order master-PRNG cascade — the seed-dependency bug), per-function `SetupSampler` walk, then the mean-only path (`Compute(-1,-1)`-equivalent) and full-MC path (`Parallel.For`, per-realization `SystemRealization` + compact `SystemRiskResults`, `PostProcessUncertainty` percentile curves). Lifecycle: `Task RunAsync(progress, ct)`, cancellation, `AnalysisStarting`/`AnalysisCompleted`. `RiskAnalysis` owns its components; per §8 (v0.11) `ToXElement()` serializes **options + `IsEstimated` only** — components and results arrive through the constructor.
- **1D integrator = `AdaptiveGaussKronrod`** (G10K21), not `AdaptiveSimpsonsRule`, at both call sites (per-component risk integral and CVaR). Same `Integrate(List<StratificationBin>)` surface; p-domain `[1e-16,1−1e-16]`, 50 stratified hazard bins, tol 1e-8, MaxDepth 100, MaxEvaluations 1e6, `MinDepth ≥ 2`. Item 1 of §7.3/§7.7.
- **`RiskIntegrand`** enum (`Core.Enums`, a hashed option) selects the adaptive-refinement objective; default `MeanTotalRisk` reproduces v1.0 point placement. Discontinuous integrands inject their crossing as a bin boundary. §7.7.
- **Exact LEC construction + stable moments + the risk-measure catalog** (§7.7): probability mass from the quadrature weight (interim deduped midpoint-trapezoid fallback with a `Σ mass=1±1e-9` assertion); exact exceedance curve from sorted `(mass, consequence)` pairs thinned to `LECOutputLength` for output only; weighted-Welford central moments; `ValueAtRisk`→0 when `α>TotalProbability`; Total percentile curve read from the Total LEC. Competing-failures CIF pre-processing preserved.
- **Mean-only mixture exposure** (§6.4.1): `SampleExposureBranches()` on the consequence contract; the mean-only path enumerates Mixture branches as weighted exposure states instead of flattening to the mean curve, so σ/VaR/CVaR/tail are correct while the mean is algebraically unchanged. Non-composite types return a single unit-weight branch.

**Unit tests:** engine smoke on tiny scenarios (mean-only + full MC); cancellation; validation failures throw; event lifecycle; options round-trip + hash recipe (incl. `RiskIntegrand`); AGK vs a closed-form 1D integral; exact-LEC vs analytic exceedance on a known consequence distribution; weighted-Welford moments vs closed form; mixture-exposure mean parity + non-degenerate σ.

**Verification (pinned here, expanded in Phases 5–6):** the v1 seed-bug regression — shuffle components / rename everything / edit metadata → **bit-identical** results; same seed → bit-identical across thread counts. Mean parity vs a legacy 1-component oracle (means are unchanged by the LEC/mixture fixes — the free regression gate; tails validated by new MC oracles per [verification.md](verification.md)).

**Exit criteria:** foundation + engine core P/T with reproducibility pinned; single-component mean-only and full-MC both green; mean parity vs the legacy 1-component oracle.

## Phase 4b — Multi-dimensional system risk

> **Complete (2026-07-23)** with the v0.15 implementation errata (architecture doc status log):
> the additive convolution is an **exact lattice** via `Fourier.FFT` (`Analyses/SystemConvolution`,
> public) — `EmpiricalDistribution.Convolve` samples continuous PDFs and cannot represent the zero
> atoms, so **N8 is extended** to an atom-aware convolution; the automatic γ target comes from a
> **deterministic per-component AGK failure-probability probe** (the warm-up harvest both wiped the
> importance grid via the `NumberOfBins` reset and could not see rare failures at γ = 1); recording
> accumulates **five self-normalized passes**; defective system stream probabilities keep the v1.0
> system-state semantics (failure union / complement); the convolution and the VEGAS seed base fold
> in canonical-hash component order (additive reorder + rename bit-inert; joint rename bit-inert,
> reorder statistically equivalent by construction). Exit gates met: system LEC for both methods,
> convolved mean == Σ means by construction, brute-force MC tail cross-checks green
> ([verification/system-risk.md](verification/system-risk.md)), γ-audit green.

**Scope:** the two multi-component system-risk methods (§7.8), each producing a true system LEC for all five risk types.

- **Additive → strict independence + FFT convolution.** Redefine the additive method to assume strictly independent components (ratified): error if a correlation is supplied under the additive method; delete the v1.0 correlation-matrix σ formula from the additive path; system `pF = Probability.IndependentUnion(pfs)`. Build the system LEC by zero-inflating each defective component curve (atom at 0 with mass `1−TotalProbability`) and convolving via `EmpiricalDistribution.Convolve(IList<EmpiricalDistribution>, numberOfPoints)` — this is exactly the 2^D failure/non-failure combination enumeration in O(n log n), and produces the system LEC v1.0 never built. New option `SystemConvolutionPoints` (default 4096, min 4096). Assert convolved mean == Σ component means to 1e-6 relative (the exact v1.0 additive answer). Grid caveat + log-spaced follow-up = Numerics item N8.
- **Joint → Vegas power transform + real combination enumeration.** Expose `VegasTailFocusMode { None, Automatic, Manual }` (default Automatic) and `VegasTailFocusParameter` (γ, default 1.0, [1,20]); the Automatic heuristic harvests `pTarget = clamp(min_i P̂_f,i·Alpha, 1e-12, 1e-2)` from the warm-up pass and calls `ConfigureForRareEvents`. Enumerate real component failure/non-failure combinations from the now-live per-pathway lists instead of convolving conditional means; accumulate LEC points across `IndependentEvaluations>1` recording passes; scale `FinalEvaluations` with D; fix the `tPF` double-increment. Audit the Vegas power-transform Jacobian is folded into `wgt` before enabling γ>1 (Numerics item N9).

**Unit tests:** additive-independence validation error; zero-inflation round-trip; convolved-mean == Σ means; Vegas γ heuristic determinism; multi-pass recording; combination-enumeration vs a small brute-force reference.

**Verification:** system LEC produced for **both** methods (v1.0 produced none for additive); brute-force MC cross-check of the system LEC tail at α = 0.01 within documented k·SE; mean parity vs the legacy joint-method oracle.

**Exit criteria:** additive + joint system risk P/T with the MC tail cross-check green; means parity held.

## Phase 4c — Reliability mode

> **Complete (2026-07-23).** The relaxation is a **mode-aware validation chain**
> (`Validate(RiskAnalysisMode)` overloads on `FailureMode` / `ConsequenceElement` /
> `ComponentGraph` / `SystemComponent`; consequence elements stay the structural terminals but
> need no functions), the engine **forces** the effective refinement objective to
> `TotalProbabilityOfFailure` in reliability mode (pinned by a bit-identity test across
> configured objectives), and the results shape is the **existing containers** — AFP =
> the Fail stream's `TotalProbability` at every level (mode/component/system; union under the
> system methods), consequence surface degenerate at zero, mass-balance warning suppressed.
> Multi-component reliability rides the Phase 4b machinery unchanged.

**`RiskAnalysisMode.Reliability`** (v0.10 — replaces the planned `ReliabilityAnalysis` sibling) — a mode of the one `RiskAnalysis`: failure probabilities / annualized failure probability per FM/component/system, no consequence functions required (relaxed FailureMode validation), reusing the sampled-component machinery with the **`RiskIntegrand.TotalProbabilityOfFailure`** integrand. One graph traversal serves both modes.

**Unit tests:** reliability-mode smoke (single + multi-component); consequence-free validation relaxation; AFP vs closed form; mode round-trips in options.

**Exit criteria: met** — reliability mode P/T (AFP vs dense reference at 1e-3; union AFP exact; forcing pinned).

## Phase 5 — Verification I: single-component oracle families

> **Complete (2026-07-23).** All five Bucket-1 families converted at N = 10⁶ (53 legacy
> methods → 27 scenario tests plus per-family reproducibility pins; sampling-identical rule
> variants consolidated per group, the
> broken legacy 5-PFM Competing-Positive body replaced by the correct r = 1 − √ε oracle, the
> `_CCA`/`_CommonCause_Independent` pairs merged — identical streams), with the 2024 report's
> published constants pinned for all 22 published scenarios and the v0.13 tails policy
> realized (each ported oracle doubles as the brute-force tail oracle for σ, LEC probes, VaR,
> CVaR, conditional means, and assurance). **Two new families beyond the roadmap scope**
> (ratified at session start): `SingleComponentUncertainty` (two-loop knowledge-uncertainty
> oracle with an exact inner integral; Q-N coupling counter-pin; the `ParametricResponse`
> posterior-injection anchor) and `CombinationMethodConsistency` (union invariance across
> methods, Fréchet bound ordering, background/`RiskIntegrand` invariance, reliability
> parity). Pre-flight verification exposed and fixed **two latent engine defects** before any
> test was written: the perfectly-negative dependency matrix never materialized on the run
> path (silent zero risk under Joint/CCA, a faulted run under Competing — corrected at the
> `SetupSamplers` freeze point, with the integrator failure-status guard added so swallowed
> integrand exceptions can never truncate curves into silent zeros again), and the
> common-cause perfectly-positive factor faulted against the Numerics overload's
> unconditional null-matrix check. No `LinearTransform`/`PowerTransform` pull-forward was
> needed (the legacy Bucket-1 scenarios use none). Results: [verification/](verification/README.md).

**Scope:** Convert the Bucket-1 legacy oracles to asserted C# verification classes at **1,000,000 realizations** (policy: [verification.md](verification.md)): `Test_MC_JointFailures` (1-component, 2- and 5-PFM, {Independent/Positive/Negative/Correlation} × {Additive/Average/Maximum/Minimum}), `Test_MC_CompetingFailures`, `Test_MC_CommonCause`, `Test_MC_MutuallyExclusive`, `Test_EAD`. Engine-vs-oracle statistical asserts on the five summary outputs (incremental/irreducible/total/failure/non-failure means) + failure probability; FN-curve spot checks. If a legacy scenario requires a `LinearTransform`/`PowerTransform` as a typed model object, pull that thin wrapper forward from Phase 7 (zero-math wrappers over existing `Numerics.Functions`).

> **v0.13 parity policy (ratified 2026-07-21):** the Phase 4/4b/4c LEC/mixture/aggregation fixes leave the **means** unchanged (verify closely against the legacy oracles — the free regression gate), but move **σ / VaR / CVaR / F-N tails** deliberately. Those tail measures are verified against **new brute-force Monte Carlo oracles**, not v1.0 parity. Where a legacy oracle itself encodes the conditional-mean collapse or the mixture-mean flattening, port it for the mean only and add an independent MC oracle for the tail. See [verification.md](verification.md).

**Exit criteria:** all four combination-rule families + EAD verified; tolerances documented per test with k·SE derivation; suite runtime recorded.

## Phase 6 — Verification II: system risk + NFIP assurance

> **Complete (2026-07-23).** Four families landed: `SystemRiskMatrixVerification` (the 36
> legacy `Test_MC_SystemRisk` methods consolidated to 12 dependency-group tests — 2-comp/2-PFM
> additive, 2-comp/1-PFM and 5-comp/1-PFM across {Independent, PerfectlyPositive,
> PerfectlyNegative, CorrelationMatrix} × all four joint-consequence rules — with the 2024
> report's tables 77–103 pinned for every published scenario, the additive engine asserted on
> the full Phase 5 catalog and the joint engine on the 4b convention, plus the 5-component
> additive shuffle/rename bit-identity pin); `RiskAnalysisCombosVerification` (the
> `Test_RiskAnalysis` combos: 1-comp 3/4-PFM perfectly negative groups across all rules, the
> 3/4-component negative systems, the r = −0.25 average scenario the mislabeled
> `Test_5Element_1PFM_2` body computes, with every remaining legacy method's disposition
> documented — stream-identical duplicates, workbenches, and Phase 9 scope); the roadmap's
> "2-component/2-PFM and 5-component/1-PFM" reading of the legacy matrix was extended from the
> bodies (2-comp/1-PFM is the published bulk of the legacy file). `NfipAssuranceVerification`
> (TOL 50/55/70 API oracles at the legacy seed + an exact-quadrature reference + Table 104
> pins, both parametric-LP3 and dense-tabular hazard engine variants in reliability mode with
> the top-of-levee `HazardThreshold` mapped to exact rating knots; **plus the full-uncertainty
> assurance ensemble the TR NFIP appendix requires** — a 300-set injected LP3 posterior,
> per-realization API parity against exact quadrature, and the assurance fraction
> P(API ≤ 0.01) pinned exactly). `LhsVarianceReductionVerification` (MC vs LHS at N = 1k over
> 5 replicate pairs — variance ratio, unbiasedness, mean-only linearity anchor). One latent
> engine gap found and fixed: the additive system's failure union folded component
> probabilities in declaration order (reorder moved the union by 1–2 ulp against the 4b
> bit-inertness contract) — it now folds in the canonical-hash order the convolution uses.
> Tolerance policy finalized in [verification.md](verification.md).

**Scope:** Convert `Test_MC_SystemRisk` (2-component/2-PFM and 5-component/1-PFM across the correlation × aggregation matrix — port from method *bodies*, several legacy names are mislabeled), the `Test_RiskAnalysis` N-element/N-PFM combos, and NFIP Assurance TOL 50/55/70 (LP3 flow frequency → rating transform → fragility → AEP). Add the LHS variance-reduction test (MC vs LHS at N=1k over repeated runs). Finalize the tolerance policy in [verification.md](verification.md).

**Exit criteria: met** — multi-component system risk + NFIP verified; tabular+parametric clusters, the engine, and the reliability mode all carry V status in the matrix.

## Phase 6.5 — Multi-consequence axis + engine performance — **COMPLETE (2026-07-23)**

> The post-Phase-6 gap-audit session (plan ratified with five user decisions recorded in
> PROGRESS). Three deliverables:
>
> 1. **The Q-U closure.** The consequence-type axis is declared at the analysis level
>    (`ConsequenceTypeDescriptor` + `RiskAnalysis.AdditionalConsequenceTypes`, append-only
>    serialization) with strict bubble-down validation — counts and order always, labels/units
>    when both sides are non-blank — and the engine computes **every** declared type in one
>    pass: per-type Q-N coupling columns, shared probability structure, primary-driven
>    refinement, per-type curves/extents/percentile grids at every scope, `ConsequenceResults`
>    on the summary tree with the declared labels echoed. RNG-silent for fixed models (the full
>    pinned suite passed unchanged) and verified by the new `MultiConsequenceVerification`
>    family ([verification/multi-consequence.md](verification/multi-consequence.md)).
> 2. **The dedicated performance pass** (the PROGRESS-recorded ~10× regression brief):
>    realization-owned compute workspaces, single-pass balanced-objective scales, an in-place
>    cached-Cholesky joint latent transform, a zero-ulp merge-walk percentile interpolator
>    (`Curve.InterpolateLogLogDescending`), the exact closed-form CVaR (retiring the deepest
>    per-realization quadrature), and the relaxed ensemble integration budget
>    (`EnsembleTolerance` 1e-4 / `EnsembleMinDepth` 0 defaults; the mean pass, probes, and
>    mean-only runs keep 1e-8/MinDepth-2). **F1 fixture 31.5 → 3.9 s (8.1×), allocations
>    39 → 3.7 GB**; zero pinned verification constants moved (exact-anchored families pin
>    their discipline in-test). Measurements: [scripts/perf/RESULTS.md](../scripts/perf/RESULTS.md).
> 3. **The cascade design ratification** (arch doc v0.16): Q-X closes into the Phase 6.7
>    design below; Phase 6.5 landed the resilience hooks (per-terminal results scope, factored
>    SRP, reserved `ResponseStage` attribute space) without touching the two Q-X seams.
>
> Doc reconciliation rode along: ROADMAP Phase 1–3 status markers fixed, the
> SHARED_FUNCTIONS_STRATEGY N7 numbering collision resolved (release → N10), CLAUDE.md
> Phase-8/9 pointers corrected.

## Phase 6.6 — Risk measures, diagnostics & sensitivity

**Scope:** the v1.0 diagnostic surface never ported, plus the measure extensions scoped by the Phase 6.5 audit:

- **Sensitivity/tornado port**: `SensitivityMeasure` enum (`PearsonCorrelation`, `SpearmanCorrelation`, `SensitivityIndex` — legacy names), typed `RiskAtHazardLevel` on the sampled component (the labeled input dictionary), and `RiskAnalysis.Sensitivity(componentIndex, hazardLevel, measure, riskType)` — content-seeded (no wall-clock PRNG), default 100 realizations. Porting source: the Dev-repo partial C# port (`Risk Analysis/RiskAnalysis.cs:4364`, `Support/Components/SampledComponent.cs:253`) first, the VB (`RiskAnalysis.vb:3625`, `SampledComponent.vb:214`) as cross-check.
- **Q-T closure**: `ProfileHazardFunction` as a chain-position remap (the consequence-binding precedent) driving the risk profiles + the `HazardThreshold` companion selector.
- **Ensemble uncertainty on scalar measures**: percentile CIs on Mean/VaR/CVaR/APF and the rest of the catalog from the already-stored `EnsembleResults` (curves carry bands today; scalars do not).
- **Convergence diagnostics**: aggregated integrator standard-error/evaluation summaries, realization-adequacy indicators on the ensemble.
- **Seed-stable perturbation mode** (arch doc §5.5.8) for sensitivity studies.
- **Q-W shared-exposure declaration** (design; per-type marginals make it moot until cross-type joint statistics are wanted) + per-type `ConsequenceThresholds` (the Phase 6.5 primary-only interim).
- Evaluate: exact-pair excess entry lists (`ComponentRiskOutput` interim), TRG-line comparison data (UI-leaning — may defer to the UI layer).

**Exit criteria:** sensitivity family verified against a legacy-style oracle; remap round-trip + hash recipe pinned; scalar-CI family verified; diagnostics JSON append-only; Q-T/Q-W doc closures.

## Phase 6.7 — Cascading response end states

**Scope:** the ratified event-tree-in-the-diagram design (arch doc v0.16 — user decisions: typed output ports; exact partition with auto-remainder; EventTreeResponse survives as a compact node):

- `ResponseElement.OutputCount` 1 → 2: **port 0 = Fail** (the default — every existing connection already targets it), **port 1 = Non-Fail**; polarity-aware path classification and validation (both-port fan-out legal; the response-free background path unchanged; consequence alignment per terminal unchanged).
- `ResponseStage.BranchPolarity` (append-only serialized attribute, resolved-on-write per the `ConsequenceHazardPosition` precedent — a deliberate, documented hash/re-pin event); `FailureMode` SRP(h) becomes ∏ᵢ (polarityᵢ = Fail ? pᵢ(h) : 1 − pᵢ(h)); multi-stage acceptance replaces the two Q-X seams (`RiskAnalysis.Validate` gate + `SampledFailureMode` ctor throw; `SetupSamplers` already walks all stages).
- End states ARE the projected failure modes (one per consequence terminal, carrying the full declared consequence-type axis); terminals sharing upstream response elements form a **mutually-exclusive state group** — within-group exact partition (no inclusion–exclusion), across groups the existing `FailureModeMethod`, remainder mass = 1 − Σ state weights → background (the engine's existing complement math). Shared response instances already share sampled draws, so branch probabilities stay coherent across sibling end states for free.
- Partial failures with partial damage states: consequences wired to Non-Fail-port continuations (e.g., R1-Fail → R2 "progresses to breach": R2-Fail terminal = full-breach consequences, R2-Non-Fail terminal = partial-damage consequences).

**Exit criteria:** hand-rolled two-stage cascade MC oracle family (including a partial-damage state); single-stage-equivalence checks (statistical — the polarity attribute moves hashes); graph round-trip + projection + polarity tests; arch-doc Q-X closure.

## Phase 7 — Remaining closed-form functions

> **Pulled forward (2026-07-21):** **`ParametricConsequence`** landed pre-Phase-4 (renamed from `ParametricConsequenceFunction` for cluster consistency — arch doc v0.12) with unit tests and a function-level verification family ([docs/verification/parametric-consequence.md](verification/parametric-consequence.md)). This phase's remaining scope is the three types below.

**Scope:** **`LinearTransform`** (`y = α + βx`, optional Gaussian uncertainty), **`PowerTransform`** (`y = α(x − ξ)^β`, log-space uncertainty, optional inversion) — thin wrappers over the existing `Numerics.Functions` `LinearFunction`/`PowerFunction` per [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md); **`NonparametricHazard`** (empirical CDF + Weibull extrapolation). Domain labels, validation, serialization, hash identity, `ComputeUncertaintyResults` — zero math in the wrappers.

**Unit tests:** evaluation at known points vs closed forms (incl. uncertainty at fixed percentile, inverse power form, clamping), serialization/hash coverage, transformed-hazard bounds. Resolve open question Q-N (fail/non-fail consequence percentile coupling) formally if not already pinned by Phase 3.

**Exit criteria:** remaining three types P/T; any Phase 5/6 deferred scenarios that needed these types converted.

## Phase 8 — Numerics.Functions expansion (numerics repo) + package switch

**Scope:** Executed in `C:\GIT\numerics` (branch `bug-fixes-and-enhancements`) per [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md) §4: N1 function serialization + `UnivariateFunctionFactory`; N2 `SegmentedPowerFunction` (BestFit BaRatin rating form, `ParameterSet`-compatible layout); N3 `CompositeFunction`; N4 `EnsembleFunction` posterior sampling; N5 `EmpiricalDistribution` XElement round-trip fix; N6 tests + `docs/functions/` guide. **Plus the v0.13 risk-engine follow-ups (raised by Phases 4/4b, non-blocking there because each has a documented interim):** N7 — an `AdaptiveGaussKronrod` integrand overload that hands the Kronrod weight to the callback (so LEC probability mass comes from the quadrature directly, retiring the midpoint-trapezoid fallback); N8 — `EmpiricalDistribution.Convolve` upgrades: a log-spaced / adaptive-grid option (the current linear grid starves order-of-magnitude consequence tails) **and an atom-aware discrete/mixed-distribution overload** (v0.15 finding: `Convolve` samples continuous PDFs, so a zero-inflation atom has no representation — the engine's exact lattice kernel `SystemConvolution` migrates onto it when it ships); N9 — Vegas power-transform Jacobian unit tests (integrate a known heavy-tail function at γ ∈ {1,4,10} to the same value; confirm `Σ wgt` = domain volume at every γ — the engine-level empirical audit is green in `SystemRiskVerification`, this is the upstream unit-test half). Release **RMC.Numerics 2.2.0** to the local feed; switch this repo's three csprojs from the HintPath to the PackageReference (Hydrologics does the same on its side).

**Exit criteria:** 2.2.0 on the feed; this repo builds green on the package.

## Phase 9 — Composites + RFA hazard + weighted wrappers

> **Pulled forward (2026-07-21):** **`CompositeConsequence` + `WeightedConsequenceFunction`** landed pre-Phase-4 (combine math in the model library until N3 lands here; BestFit `CompositeAnalysis`-pattern wiring; projected-identity hashing per arch doc v0.12) with unit tests and a function-level verification family reproducing the 2024 report's three scenarios ([docs/verification/composite-consequence.md](verification/composite-consequence.md)). Q-I's composite half and Q-J are resolved for the consequence composite (declared order semantic; ordinal-in-seed) — the hazard/response composites below adopt the same rules. This phase migrates the consequence combine onto Numerics `CompositeFunction` when N3 ships and converts the engine-level composite oracles.

**Scope:** `RFAHazard`, `CompositeHazard` + `WeightedHazardFunction`; `CompositeTransform` + `WeightedTransformFunction`; `CompositeResponse` + `WeightedResponseFunction` — composite math on Numerics `CompositeFunction`/`Mixture`/`CompetingRisks` (migrate `CompositeConsequence`'s in-library combine onto `CompositeFunction` at the same time). **`CompositeHazard` gains the parameter-set import option** (mirroring the Phase 2 parametric injection) so BestFit competing-risks/mixture/composite results import via the UI layer as Numerics artifacts. Resolve whether the injection path supersedes the planned `BestFitUnivariateHazard` type.

**Verification:** `Test_Composite` (incl. its built-in mixture consistency cross-check), `Test_Composite_Uncertainty`, `Test_Composite_Consequence_Mixture` (the engine-level day/night oracles), NFIP TOL 60/65 (hazard bootstrap variants).

**Exit criteria:** composite family P/T/V.

## Phase 10 — Event trees (reshaped 2026-07-23)

> **Reshaped by the Phase 6.5 cascade ratification:** graph-level event-tree behavior — branch
> semantics, partial end states, consequences on end states — is Phase 6.7's cascading
> response design. This phase ports `EventTreeResponse` as the **compact single-node authoring
> convenience** for chance-probability trees (per-node distributions, per-hazard-interval
> tables, node references — the v1.0 `ChanceSource` surface), sharing the cascade's end-state
> compute contract; per-leaf output ports (completing the dormant v1.0 `MultipleConsequences`
> scaffold) let a tree's failure leaves feed distinct consequence terminals. Final scope call
> happens here, informed by the landed cascade.

**Scope:** `IEventNode`, `EventNodeBase`, `ChanceNode`, `InitiatingNode`, `RemainderNode`, `EventNodeExtensions`; `EventTreeResponse` with LHS-driven traversal (arch doc §5.8.6); post-order canonical hashing (Q-B resolution); per-leaf output ports riding the Phase 6.7 port machinery. (`SecondaryHazardNode`/`WeightedHazardLevel` were dead v1.0 scaffolding — port only if the bivariate phase resurrects the need.)

**Verification:** port the legacy `Test_EventTree` serialization round-trip (the suite's only genuinely asserted legacy test) + its product oracle.

**Exit criteria:** event-tree family P/T/V.

## Phase 11 — Bivariate + BestFit import + LifeSim

**Scope:** Bivariate hazards (`ParametricBivariateHazard`, `BestFitBivariateHazard`, `BestFitTabularHazard` — posterior-import types holding Numerics artifacts only), `BivariateResponse` + the §7.4 nested Y|X integration, `BestFitTransform` (posterior import; no `RMC.BestFit.dll`), `LifeSimConsequence` + `LifeSimResult`, `FaultTreeResponse` v2 placeholder. Revisit `BestFitUnivariateHazard` here only if Phase 9 concluded the parametric injection path does not fully supersede it.

**Verification:** `Test_BivariateRisk` (legacy 100M → 1M with widened, documented tolerance), `Test_DAMRAE` (bilinear surfaces); BestFit import contract test (deserialize `MCMCResults`/`UncertaintyAnalysisResults` with Numerics alone → construct → evaluate).

**Exit criteria:** full v1.1 input-function surface P/T/V.

## Phase 12 — Hardening

**Scope:** ≥90% line-coverage gate on `RMC.TotalRisk.dll` from the fast suite; one-off Linux `dotnet build` container check (proves no Windows-only dependency); `examples/` documentation (the two v1.0 `.tra` projects described + a headless model-lib code example); `docs/getting-started.md`; `docs/REMAINING-WORK.md`; a BenchmarkDotNet micro-suite over the hot kernels (closed-form CVaR, `SystemConvolution`, the percentile merge-walk interpolator, the sampled compute kernels) as the standing perf-regression net — the Phase 6.5 macro harness (`scripts/perf/PerfHarness`) stays the whole-engine gate.

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
