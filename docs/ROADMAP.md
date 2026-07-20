# RMC-TotalRisk v1.1 Roadmap

> Phased development of the v1.1 model library, tests, and verification suite. **One phase per working session.** Every phase exits with: a zero-warning build, the fast test suite green (`dotnet test -c Release`), `validate-code-xml-docs.ps1` green, the Ported Types Matrix in CLAUDE.md updated, `docs/PROGRESS.md` updated, and AGENTS.md regenerated if CLAUDE.md changed. **Every ported compute type ships with unit tests in `RMC.TotalRisk.Tests`, and gains verification coverage in `RMC.TotalRisk.Verification` by the phase that converts its legacy oracle scenarios.**

Porting sources in order of authority: (1) the partial C# port `C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\RMC.TotalRisk.IO\Project\Elements\`; (2) the legacy VB engine `...\RMC.TotalRisk\`; (3) the `Test_TotalRisk` Monte Carlo oracles (verification only); (4) the normative specs in [requirements/](requirements/) — where the spec deliberately departs from legacy (content-based seeding, LHS sampling, no-BestFit imports), the spec wins.

| Phase | Scope | Status |
|---|---|---|
| 0 | Repo bootstrap: structure, projects, process machinery, docs | Complete (2026-07-20) |
| 1 | Model support layer: hashing, seeding, sampling, serialization base | Not started |
| 2 | Hazard functions — tabular + parametric | Not started |
| 3 | Transform functions — linear, power, tabular | Not started |
| 4 | Response + consequence functions — tabular + parametric | Not started |
| 5 | Risk components + results containers | Not started |
| 6 | RiskAnalysis engine | Not started |
| 7 | Verification I — single-component oracle families | Not started |
| 8 | Verification II — system risk + NFIP assurance | Not started |
| 9 | Numerics.Functions expansion (numerics repo) + package switch | Not started |
| 10 | Composites + remaining univariate hazards | Not started |
| 11 | Event trees | Not started |
| 12 | Bivariate + BestFit import + LifeSim | Not started |
| 13 | Hardening + release prep (`v1.1.0-alpha` of the model lib) | Not started |
| Future | UI layer, desktop App, REST API, FDA importer — planned when the model lib stabilizes | Placeholder |

---

## Phase 0 — Repo bootstrap

**Scope:** Reconcile the local folder with the official GitHub repo on branch `v1.1-development`; restructure to `docs/`, `examples/`, `src/`; scaffold `RMC.TotalRisk` + `RMC.TotalRisk.Tests` + `RMC.TotalRisk.Verification` (MSTest.Sdk/3.6.4, Verification excluded from Release); port the development-process machinery (CLAUDE.md/AGENTS.md, validation + sync scripts, Directory.Build/Packages.props, NuGet.config, global.json, LICENSE); seed the docs tree; import the architecture + shared-functions specs into `requirements/`.

**Exit criteria:** solution builds 0-warning; 2 smoke tests pass in Release; Verification produces no Release output; doc script green; docs tree live; work committed on `v1.1-development`.

## Phase 1 — Model support layer

**Scope:** `Models/Support`: `IModelElement`, `ModelElementBase` (INPC + `ToXElement` contract + `CanonicalHash`), `CanonicalContentHasher` + `CanonicalizationRules` (mechanism adapted from Hydrologics `src/Hydrologics/Core/`), `SeedHelpers.HashCombine`, `ByteArrayComparer`, `SampledModelElement` + `SamplingScheme` enum (per-function N×D percentile matrices — LHS default, MC legacy), serialization utilities (G17/InvariantCulture helpers, curve wrapping), `FunctionHelpers.ForceMonotonic`. Normative spec: [requirements/MODEL_LIBRARY_ARCHITECTURE.md](requirements/MODEL_LIBRARY_ARCHITECTURE.md) §5.

**Unit tests:** hash canonicalization (strip rules; rename/reorder invariance kitchen-sink scaffold that later phases extend), `HashCombine` determinism, sampler matrix shapes for LHS/MC schemes, serialization round-trips, INPC raises.

**Exit criteria:** all support types P/T; the kitchen-sink invariance test runs green and is extendable per cluster.

## Phase 2 — Hazard functions (tabular + parametric)

**Scope:** `IHazardFunction`, `HazardFunctionBase`, `UnivariateHazardBase`; **`TabularHazard`** (per-ordinate uncertain paired data; None/Hazard/Probability uncertainty modes; bootstrap mean-curve assembly) and **`ParametricUnivariateHazard`** (fitted distribution + `BootstrapAnalysis` posterior; index-driven sampling). Numerics-only — no upstream changes needed (`UncertainOrderedPairedData`, `EmpiricalDistribution`, `BootstrapAnalysis`, `UncertaintyAnalysisResults`, `Stratify` all exist).

**Unit tests:** ctor/validation matrices, serialization round-trips, hash invariance entries, `SampleFunction()` smoke at mean and fixed percentiles, min/max hazard bounds.

**Verification:** distribution-level sanity (bootstrap mean curve vs direct Numerics computation). Full oracle parity lands in Phases 7–8.

**Exit criteria:** both types P/T with hash-invariance coverage.

## Phase 3 — Transform functions (linear, power, tabular)

**Scope:** `ITransformFunction`, `TransformFunctionBase`; **`LinearTransform`**, **`PowerTransform`**, **`TabularTransform`** — thin wrappers over the existing `Numerics.Functions` trio (`LinearFunction`, `PowerFunction`, `TabularFunction`) per [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md); domain labels, validation, serialization, hash identity — zero math in the wrappers.

**Unit tests:** evaluation at known points vs closed forms (incl. uncertainty at fixed percentile, inverse power form, clamping), serialization/hash coverage, transformed-hazard bounds.

**Exit criteria:** three types P/T.

## Phase 4 — Response + consequence functions (tabular + parametric)

**Scope:** `IResponseFunction`, `ResponseFunctionBase`; **`TabularResponse`**, **`ParametricResponse`**, **`NonFailResponse`** (engine's non-failure branch). `IConsequenceFunction`, `ConsequenceFunctionBase`; **`TabularConsequence`**, **`ParametricConsequenceFunction`** (new: `C(h) = clamp(α·max(h−h₀,0)^β, 0, U)` per ER 1110-2-1156). Resolve open question Q-N (fail/non-fail consequence percentile coupling) before the consequence sampler lands.

**Unit tests:** fragility curve sampling (co-monotonic `CurveSample`), `IsMonotonic`, probability bounds, parametric consequence closed-form checks, serialization/hash coverage.

**Exit criteria:** five types P/T; Q-N resolved and recorded.

## Phase 5 — Risk components + results containers

**Scope:** `Models/RiskAnalysis/Components`: `SystemComponent`, `FailureMode`, `SampledComponent`, `SampledFailureMode`, `ComponentRiskOutput`, enums (`FailureModeMethod`, `JointConsequencesType`, `FailureModeDependency`, `HazardDimension`); occurrence-index assignment (`AssignOccurrenceIndices`, §5.5.4). `Models/RiskAnalysis/Results`: `Curve`, `Curves`, `RiskPoint`, `Ensemble`, `ComponentRealization`/`FailureModeRealization`/`SystemRealization`, `ComponentResults`/`EnsembleResults`/`FailureModeResults`/`SummaryRiskResults`/`SystemRiskResults` (XElement round-trip; no BinaryFormatter).

**Unit tests:** occurrence-index scheme (identical-content components get 0..n−1 in canonical order; cross-analysis stability tuples), component/FM validation matrices incl. dimensional binding, results container round-trips, correlation-matrix serialization (Q-G resolution).

**Exit criteria:** component + results layers P/T; occurrence-index behavior pinned by tests.

## Phase 6 — RiskAnalysis engine

**Scope:** `Analyses/Support` (`IAnalysis`, `AnalysisBase`, run-completed events), `RiskAnalysisOptions` (+`SamplingScheme`, `SystemRiskMethod`), `RiskAnalysis.RunAsync`: validation gate, content-based per-component seeds, per-function `SetupSampler` walk, `Parallel.For` realization loop, additive risk (AdaptiveSimpson), joint risk (Vegas), mean-only path, cancellation/progress/lifecycle events. May split 6a (sampling orchestration + mean-only) / 6b (full MC + joint risk) if the session overruns.

**Unit tests:** engine smoke on tiny scenarios; cancellation; validation failures throw; event lifecycle.

**Verification:** the v1 seed-bug regression suite — shuffle components / rename everything / move canvas metadata → **bit-identical** results; same seed → bit-identical across thread counts.

**Exit criteria:** engine P/T with reproducibility pinned.

## Phase 7 — Verification I: single-component oracle families

**Scope:** Convert the Bucket-1 legacy oracles to asserted C# verification classes at **1,000,000 realizations** (policy: [verification.md](verification.md)): `Test_MC_JointFailures` (1-component, 2- and 5-PFM, {Independent/Positive/Negative/Correlation} × {Additive/Average/Maximum/Minimum}), `Test_MC_CompetingFailures`, `Test_MC_CommonCause`, `Test_MC_MutuallyExclusive`, `Test_EAD`. Engine-vs-oracle statistical asserts on the five summary outputs (incremental/irreducible/total/failure/non-failure means) + failure probability; FN-curve spot checks.

**Exit criteria:** all four combination-rule families + EAD verified; tolerances documented per test with k·SE derivation; suite runtime recorded.

## Phase 8 — Verification II: system risk + NFIP assurance

**Scope:** Convert `Test_MC_SystemRisk` (2-component/2-PFM and 5-component/1-PFM across the correlation × aggregation matrix — port from method *bodies*, several legacy names are mislabeled), the `Test_RiskAnalysis` N-element/N-PFM combos, and NFIP Assurance TOL 50/55/70 (LP3 flow frequency → rating transform → fragility → AEP). Add the LHS variance-reduction test (MC vs LHS at N=1k over repeated runs). Finalize the tolerance policy in [verification.md](verification.md).

**Exit criteria:** multi-component system risk + NFIP verified; tabular+parametric clusters and the engine all carry V status in the matrix.

## Phase 9 — Numerics.Functions expansion (numerics repo)

**Scope:** Executed in `C:\GIT\numerics` (branch `bug-fixes-and-enhancements`) per [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md) §4: N1 function serialization + `UnivariateFunctionFactory`; N2 `SegmentedPowerFunction` (BestFit BaRatin rating form, `ParameterSet`-compatible layout); N3 `CompositeFunction`; N4 `EnsembleFunction` posterior sampling; N5 `EmpiricalDistribution` XElement round-trip fix; N6 tests + `docs/functions/` guide. Release **RMC.Numerics 2.2.0** to the local feed; switch this repo's three csprojs from the HintPath to the PackageReference (Hydrologics does the same on its side).

**Exit criteria:** 2.2.0 on the feed; this repo builds green on the package.

## Phase 10 — Composites + remaining univariate hazards

**Scope:** `NonparametricHazard`, `RFAHazard`, `CompositeHazard` + `WeightedHazardFunction`; `CompositeTransform` + `WeightedTransformFunction`; `CompositeResponse` + `WeightedResponseFunction`; `CompositeConsequence` + `WeightedConsequenceFunction` — composite math on Numerics `CompositeFunction`/`Mixture`/`CompetingRisks`. Resolve Q-I (weighted-list ordering in the canonical hash) and Q-J (occurrence index within composites).

**Verification:** `Test_Composite` (incl. its built-in mixture consistency cross-check), `Test_Composite_Uncertainty`, `Test_Composite_Consequence_Mixture`, NFIP TOL 60/65 (hazard bootstrap variants).

**Exit criteria:** composite family P/T/V.

## Phase 11 — Event trees

**Scope:** `IEventNode`, `EventNodeBase`, `ChanceNode`, `InitiatingNode`, `RemainderNode`, `SecondaryHazardNode`, `WeightedHazardLevel`, `EventNodeExtensions`; `EventTreeResponse` with LHS-driven traversal (§5.8.6); post-order canonical hashing (Q-B resolution).

**Verification:** port the legacy `Test_EventTree` serialization round-trip (the suite's only genuinely asserted legacy test) + its product oracle.

**Exit criteria:** event-tree family P/T/V.

## Phase 12 — Bivariate + BestFit import + LifeSim

**Scope:** Bivariate hazards (`ParametricBivariateHazard`, `BestFitBivariateHazard`, `BestFitTabularHazard` — posterior-import types holding Numerics artifacts only), `BivariateResponse` + the §7.4 nested Y|X integration, `BestFitUnivariateHazard` + `BestFitTransform` (posterior imports; no `RMC.BestFit.dll`), `LifeSimConsequence` + `LifeSimResult`, `FaultTreeResponse` v2 placeholder.

**Verification:** `Test_BivariateRisk` (legacy 100M → 1M with widened, documented tolerance), `Test_DAMRAE` (bilinear surfaces); BestFit import contract test (deserialize `MCMCResults`/`UncertaintyAnalysisResults` with Numerics alone → construct → evaluate).

**Exit criteria:** full v1.1 input-function surface P/T/V.

## Phase 13 — Hardening + release prep

**Scope:** ≥90% line-coverage gate on `RMC.TotalRisk.dll` from the fast suite; one-off Linux `dotnet build` container check (proves no Windows-only dependency); `examples/` documentation (the two v1.0 `.tra` projects described + a headless model-lib code example); `docs/getting-started.md`; `docs/REMAINING-WORK.md`; tag `v1.1.0-alpha` of the model library.

**Exit criteria:** coverage + Linux gates green; alpha tag cut (user pushes/tags).

## Future phases (placeholders — planned when the model lib stabilizes)

- **UI layer** — `RMC.TotalRisk.UI` (net10.0-windows): project model, element wrappers, `.tra` (SQLite) + `.rmcbf` reading, data binding on the model lib's INPC surface.
- **Desktop App** — the WPF shell ported from the legacy VB app.
- **REST API** — `RMC.TotalRisk.Api`: containerized, AWS-deployable, OpenAPI for agentic clients.
- **FDA importer + datasets** — port `FDAImporter_143`; bring the HEC-FDA comparison datasets in-repo via a `docs/verification-requests/` request; convert the FDA integration oracles.
- **Cross-engine parity** — the levee "same results" test against Hydrologics' event-based risk layer, in a verification-only project referencing both (see [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md) §7).
