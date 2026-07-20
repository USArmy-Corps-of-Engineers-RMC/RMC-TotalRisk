# RMC-TotalRisk

## Project Overview

Monte-Carlo-based quantitative risk analysis framework for dam and levee safety, developed by the USACE Risk Management Center. **This software is used for life-safety flood risk assessments worldwide.** Code quality is paramount.

- **Version:** 1.1.0 (in development on branch `v1.1-development`; v1.0 is the released desktop product — see the README)
- **Language:** C# — model library first (no UI, no IO frameworks); UI/App layers come in later phases
- **Framework:** .NET 10
- **Primary consumers:** the future RMC-TotalRisk desktop app, USACE practitioners, U.S. Federal agencies, consultants, academics, AWS/agentic headless callers
- **Porting source:** the legacy repos remain at `C:\GIT\RMC-TotalRisk-Dev` (VB.NET v1.0 engine, partial C# port `RMC.TotalRisk.IO`, and the `Test_TotalRisk` Monte Carlo oracle suite). This repo is the clean v1.1 home; the Dev repo is reference-only.

## Headless-Compute Philosophy (non-negotiable)

`RMC.TotalRisk.dll` is a headless compute library: typed system definition + seed in → reproducible typed results out. Callable from AWS workers, web services, and agentic AI tools. Every design decision serves that:

- **No UI frameworks.** No WPF, no `System.Windows.*`, no `Dispatcher`. Target `net10.0`, never `net10.0-windows`.
- **No singletons.** No `Project.Current` global state. Analyses take inputs via constructor/method args and return pure result objects.
- **No direct file I/O.** Callers pass already-parsed data. In-memory serialization only: model *definition* types use `ToXElement()` / ctor-from-`XElement` (the canonical-hash identity surface); *results* containers use System.Text.Json (`ToJson()`/`FromJson()` + compressed-bytes overloads) — v1.0's BinaryFormatter BLOBs are not ported, and v1.0 projects re-run their analyses in v1.1. No SQLite, no `File.*` in the model lib.
- **Deterministic entry points.** Explicit seeds, typed inputs/outputs, no dialogs. Same inputs + same seed → bit-identical results at any thread count.
- **Content-based seed identity.** Monte Carlo seeds derive from SHA-256 canonical content hashes (XML canonicalization over `ToXElement()` through audited strip rules) plus occurrence indices — renaming, canvas moves, or reordering never change results. Normative spec: `docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md` §5.5. **Landing checklist for every new model property:** classify it compute-relevant (hashed) or metadata (add to `CanonicalizationRules`), extend the kitchen-sink rename/reorder invariance test, and never rename or reorder existing serialized attributes — `ToXElement()` is the identity surface and hashes are contract.
- **`INotifyPropertyChanged` is allowed.** It is a passive contract; headless callers don't subscribe. It gives the future WPF UI layer a clean data-binding story.

**Dependency rule (MANDATORY):** `RMC.TotalRisk.dll` references only `RMC.Numerics`. Never `RMC.BestFit`, never UI/IO frameworks, never SQLite. BestFit fitted results are imported as **already-parsed Numerics artifacts** (`UnivariateDistributionBase`, `ParameterSet[]`, `UncertaintyAnalysisResults`, `UncertainOrderedPairedData`) — reading `.rmcbf` files is a UI-layer concern. The validation script fails on any `RMC.BestFit`, `System.Windows`, or SQLite reference. See `docs/requirements/SHARED_FUNCTIONS_STRATEGY.md`.

## Project Architecture

```
Numerics.dll (RMC.Numerics)         ← distributions, uncertain paired data, functions,
    ↑                                 sampling (LHS/stratification/bootstrap/MCMC), math
RMC.TotalRisk.dll                   ← model library (input functions, risk components,
    ↑                                 Monte Carlo risk engine, results)
Future consumers                    ← RMC.TotalRisk.UI → RMC-TotalRisk App; REST API; agents
```

**Interim dependency note:** all three csproj files reference the sibling build `..\..\..\numerics\Numerics\bin\Debug\net10.0\Numerics.dll` via `<HintPath>`; switch to the `RMC.Numerics` PackageReference (already declared in `Directory.Packages.props`, local feed `C:\GIT\numerics\packages`) once a release ≥ 2.2.0 lands (roadmap Phase 9).

**Namespace map** (folders mirror namespaces; no types in the bare `RMC.TotalRisk` root namespace). Authoritative layout: `docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md` §3. Summary:

**No "element" vocabulary and no root `IModel` abstraction in the model lib** — "element" is wpf-framework UI lingo reserved for the future UI layer (the RMC-BestFit separation template: `RMC.BestFit.UI\Elements\`); the kernel contract is `IRiskFunction` because TotalRisk's engine consumes functions by role, never "any model" (arch doc v0.8 status entry has the full rationale).

| Namespace | Contents |
|---|---|
| `RMC.TotalRisk.Models` | `BuildInfo` seed (Phase 0; retired when real content lands) |
| `RMC.TotalRisk.Models.Support` | `IRiskFunction`, `RiskFunctionBase`, `CanonicalContentHasher`, `CanonicalizationRules` (`ModelRules`), `SeedHelpers`, `ByteArrayComparer`, `SamplingScheme`, `SerializationUtilities`, `FunctionHelpers` (Phase 1) |
| `RMC.TotalRisk.Models.HazardFunctions` | Hazard (frequency-distribution) input functions (Phase 2+) |
| `RMC.TotalRisk.Models.TransformFunctions` | Transform input functions (Phase 2+) |
| `RMC.TotalRisk.Models.ResponseFunctions` | Response (fragility) input functions (Phase 2+) |
| `RMC.TotalRisk.Models.ConsequenceFunctions` | Consequence input functions (Phase 2+) |
| `RMC.TotalRisk.Models.RiskAnalysis` | System components, failure modes (concrete classes — no interfaces), results containers (Phase 3+) |
| `RMC.TotalRisk.Analyses` | `IAnalysis`/`AnalysisBase` support, `RiskAnalysis` engine + `RiskAnalysisOptions`, `ReliabilityAnalysis` (Phase 4+) |

## Test Project Architecture

Three projects split by scope and speed:

```
RMC.TotalRisk                 ← the model library
RMC.TotalRisk.Tests           ← fast programmatic unit tests (seconds)
RMC.TotalRisk.Verification    ← Monte Carlo verification vs legacy oracles (minutes)
                                NOT built in Release (omitted .sln Build.0 line)
                                [assembly: TestCategory("Verification")]
```

### Test classification rule

- **Programmatic unit tests → `RMC.TotalRisk.Tests`.** Constructor validation, property round-trips and change notification, `Validate()` matrices, XML serialization round-trips, canonical-hash invariance, simple calculation checks at known points, exception throws. Small inline fixtures only.
- **Computational verification → `RMC.TotalRisk.Verification`.** Monte Carlo oracle parity (converted from legacy `Test_TotalRisk`), engine-vs-oracle statistical asserts, reproducibility-at-scale, LHS variance reduction. See the Verification Workflow below and `docs/verification.md`.

### Commands (verified on the .NET 10 SDK)

| Intent | Command |
|---|---|
| Fast PR gate / dev loop | `dotnet test -c Release` (Verification not built in Release) |
| Fast loop, Debug binaries | `dotnet test src/RMC.TotalRisk.Tests` |
| Verification suite (deliberate, per change area) | `dotnet test src/RMC.TotalRisk.Verification` |
| Everything, Debug | `dotnet test` |
| Doc/namespace validation | `.\scripts\validate-code-xml-docs.ps1 -Configuration Debug` |
| Regenerate AGENTS.md after editing this file | `py scripts/sync-agents-md.py` |

> **⚠ .NET 10 `dotnet test` gotcha:** the SDK drives MSTest.Sdk projects through Microsoft.Testing.Platform. `dotnet test --filter "TestCategory!=Verification"` is **silently ignored** (the classic VSTest argument is not forwarded). Passing the filter after `--` works per-assembly but fails the run when an assembly matches zero tests. Therefore: **scope by configuration or by project path** as in the table above; do not rely on category filters at the CLI. The assembly-level `[TestCategory("Verification")]` remains useful for IDE/explorer filtering.

### Shared MSTestSettings.cs

`[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` lives in `src/RMC.TotalRisk.Verification/MSTestSettings.cs` and is **linked** into `RMC.TotalRisk.Tests`. Tests must be independent of execution order and share no mutable static state.

### Coverage targets

- `RMC.TotalRisk.dll` — >90% line coverage from `RMC.TotalRisk.Tests` alone (the fast suite).
- **Every new public class must have a corresponding `<ClassName>Tests.cs`** in `RMC.TotalRisk.Tests`, in a folder mirroring the source folder. A new class without tests is not ready to commit.
- **Every ported compute type must additionally gain verification coverage** (its oracle family in `RMC.TotalRisk.Verification`) by the phase that converts its legacy scenarios. Track gaps in the Ported Types Matrix below.

### Regression check on every library change (MANDATORY)

```bash
dotnet build          # zero warnings required
dotnet test -c Release
```

If the change touches a verified family, also run its verification class (scope by project path + class): `dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~JointFailuresVerification"`.

## Repository Layout

```
RMC-TotalRisk/                      ← repo root (github.com/USACE-RMC/RMC-TotalRisk)
├── RMC-TotalRisk.sln               ← solution; Core/Tests/Verification solution folders
├── CLAUDE.md                       ← this file (Claude Code guidance; source of truth)
├── AGENTS.md                       ← Codex sibling — GENERATED from CLAUDE.md, do not edit by hand
├── README.md                       ← public product README (v1.0 downloads + v1.1 development notes)
├── LICENSE                         ← USACE-RMC notice, conditions, and disclaimer
├── global.json                     ← pins .NET 10 SDK (latestFeature roll-forward)
├── Directory.Build.props           ← RepositoryRoot + opt-in EnforceXmlDocumentation
├── Directory.Packages.props        ← central package versions (RMC.Numerics 2.*)
├── NuGet.config                    ← local feed C:\GIT\numerics\packages + nuget.org
├── docs/
│   ├── index.md, references.md    ← doc map + IEEE-numbered bibliography
│   ├── ROADMAP.md                  ← the phased roadmap (single source of truth for phases)
│   ├── PROGRESS.md                 ← session progress log — update every session
│   ├── verification.md             ← oracle-conversion strategy + tolerance policy
│   ├── requirements/               ← normative specs (architecture + shared-functions strategy)
│   ├── technical-reference/        ← per-family math docs (grows per phase)
│   └── verification-requests/      ← user-executed reference-run specs (as needed)
├── examples/                       ← v1.0 example projects (.tra) + future headless examples
├── scripts/
│   ├── validate-code-xml-docs.ps1  ← doc coverage + namespace/culture/dependency guards
│   └── sync-agents-md.py           ← regenerates AGENTS.md from CLAUDE.md
└── src/
    ├── RMC.TotalRisk/              ← the model library
    ├── RMC.TotalRisk.Tests/        ← fast unit tests
    └── RMC.TotalRisk.Verification/ ← Monte Carlo verification tests
```

## Roadmap and Priorities

The phased roadmap lives in `docs/ROADMAP.md` — one phase per working session, exit gates listed there. Current status is tracked in `docs/PROGRESS.md` (newest first; update every session).

**Porting sources (in order of authority):**
1. `C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\RMC.TotalRisk.IO\Project\Elements\` — the partial VB→C# port of the input-function clusters and engine. Primary source for class shapes and math bodies.
2. `C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\RMC.TotalRisk\` — the legacy VB.NET v1.0 engine. Cross-check when the C# port is ambiguous or incomplete.
3. `C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\Test_TotalRisk\` — ~128 hand-rolled Monte Carlo oracle methods (fixed seeds, no asserts). Source for verification tests, NOT for model code.
4. Normative design specs: `docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md` (v0.6+) and `docs/requirements/SHARED_FUNCTIONS_STRATEGY.md`. Where the spec deliberately departs from legacy (seeding, sampling, no-BestFit imports), the spec wins.

**Porting rule (amended 2026-07-20):** preserve the v1.0 **API surface** (signatures, defaults, lifecycle) and its **reference results**, but actively improve the implementations — fix numerical fragility, imprecision, and inefficiency wherever v1.0 is demonstrably flawed (example: v1.0 `ForceMonotonic` nudged by an absolute machine epsilon that is unrepresentable at hazard magnitudes ≥ 1 and was swallowed by the tolerance-equal ordinate setter — v1.1 uses a scale-aware nudge). Every improvement is documented in the type's XML `<remarks>` and the session's PROGRESS entry, and covered by a test. Verification oracles (Phases 5–6) remain the arbiter that improvements did not change reference results beyond statistical tolerance. Larger deliberate behavioral changes (content-based seeding, LHS sampling, JSON results) are those ratified in the requirements docs.

## Ported Types Matrix

Status legend: — planned · P ported · T unit-tested · V verification coverage. Update as work lands.

| Cluster | Type | Status | Verification anchor |
|---|---|---|---|
| Support | IRiskFunction / RiskFunctionBase / CanonicalContentHasher / CanonicalizationRules / SeedHelpers | P/T | hash-invariance + seeding unit tests (Phase 1 — landed 2026-07-20) |
| Hazard | TabularHazard | P/T | NFIP assurance oracles (Phase 6) |
| Hazard | ParametricUnivariateHazard | P/T | NFIP assurance oracles (Phase 6) |
| Transform | TabularTransform | P/T | rating-curve oracles (Phase 6) |
| Response | TabularResponse | P/T | joint/competing/common-cause oracles (Phase 5) |
| Response | ParametricResponse | P/T | joint/competing/common-cause oracles (Phase 5) |
| Response | NonFailResponse | P/T | engine scenarios (Phase 5) |
| Consequence | TabularConsequence | P/T | joint-failures oracles (Phase 5) |
| Risk | SystemComponent / FailureMode / Sampled* / results | — | engine scenarios (Phases 5–6) |
| Engine | RiskAnalysis + RiskAnalysisOptions / ReliabilityAnalysis | — | full oracle families (Phases 5–6) + seed-bug regressions (Phase 4) |
| Backfill | LinearTransform / PowerTransform / ParametricConsequenceFunction / NonparametricHazard | — | closed-form checks + deferred EAD/NFIP scenarios (Phase 7) |
| Later | RFA/Composite hazards, composites, event trees, bivariate, BestFit imports, LifeSim | — | Phases 9–11 |

## Critical Quality Standards

Zero tolerance: **no compiler errors, no compiler warnings, no empty catch blocks, proper exception propagation** (never silently swallow).

**XML documentation is MANDATORY on ALL types and methods — public AND private.** Required tags as applicable: `<summary>`, `<param>`, `<returns>`, `<exception>`, `<remarks>`. Do not suppress the CS1570–CS1591 doc warnings to pass a build; fix the docs. Every class carries the Authors block in `<remarks>`:

```csharp
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// </remarks>
```

**No file-header banners.** Files start with `using` directives. The USACE notice lives in `LICENSE` only.

**Never hand-roll numerics that Numerics provides.** Interpolation → `Numerics.Data.Interpolation.Linear`/`Bilinear`; distributions → `Numerics.Distributions`; sampling → `LatinHypercube`/`Stratify`/`BootstrapAnalysis`; root finding → `Brent`; integration → `AdaptiveSimpsonsRule`/`Vegas`. **Single exception:** verification oracles in `RMC.TotalRisk.Verification` intentionally re-implement engine math from Numerics primitives — that independence is what makes them oracles.

**Validation contract:** every model type implements `public (bool IsValid, List<string> ValidationMessages) Validate()`. Messages start `"Error: ..."` (invalidating) or `"Warning: ..."` (advisory); `IsValid` is false only on errors.

**XML serialization contract:** element name `nameof(ClassName)`; doubles written `value.ToString("G17", CultureInfo.InvariantCulture)`; reads via `double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, ...)` with null-safe attribute access. Every serializable class gets a round-trip unit test. **`ToXElement()` is also the canonical-hash identity surface** — serialized attribute names and owned-child order are append-only contract (see the landing checklist above).

**Property change notification:** model types implement `INotifyPropertyChanged` directly (`RaisePropertyChange(nameof(...))` after mutation). No WPF dependency.

**Numerical safety:** `double.NegativeInfinity` for impossible log-likelihoods (not `double.MinValue`); guard `Math.Log(x)` with `x > 0`.

**Namespaces:** library code uses block namespaces; test code uses file-scoped namespaces. No types in the bare `RMC.TotalRisk` root namespace. The legacy flat `TotalRisk` namespace must not survive a port.

## Verification Workflow

The legacy `Test_TotalRisk` suite is a set of **hand-rolled Monte Carlo oracles with fixed seeds and no asserts** (results were printed to the debugger). Conversion pattern, per scenario (full policy: `docs/verification.md`):

1. Port the oracle math to C# in `RMC.TotalRisk.Verification`, preserving the legacy fixed seeds (`MersenneTwister(12345)`/`(45678)`; MVN seeds 12345/67891/78910) at **1,000,000 realizations** (legacy used 10M; the tolerance scales).
2. Run the **new engine** on the same typed scenario.
3. `Assert` engine vs oracle within a statistical tolerance derived from the Monte Carlo standard error (SE ≈ σ/√N; at 1M ≈ 0.1% relative on means; document the k·SE factor per output). Engine seeds are content-based, so engine-vs-oracle is **statistical, never bit-exact**.
4. Pin engine reproducibility separately: same seed → bit-identical; shuffle/rename/reorder → bit-identical (the v1 seed-dependency bug regression).
5. Oracle values may additionally be pinned as constants once captured (exact at fixed seed + N) for regression.

Run verification **deliberately**: the family you touched after touching it; the full suite on demand. It is excluded from Release builds and from the fast PR gate by construction.

## Numerics API Gotchas

Verified traps to remember (extend as discovered):

- **`LogNormal` is base-10.** The natural-log lognormal is `LnNormal` — the legacy VB engine and oracles use `LnNormal`.
- `LatinHypercube.Random(n, d, seed)`: a seed ≤ 0 falls back to wall-clock — always pass an explicit positive seed.
- `UnivariateDistributionFactory.CreateDistribution` is a closed switch (`Mixture`/`CompetingRisks` reconstruct via their own `FromXElement`).
- `EmpiricalDistribution` does not round-trip its X/P tables through the base `ToXElement()` (fix planned upstream in Phase 9 — until then serialize the underlying paired data).
- Verification data files (when they exist) are read relative to `AppContext.BaseDirectory`, never the repo path, and parsed with `CultureInfo.InvariantCulture`.

## Code Conventions

- Each public class in its own `.cs` file; file name matches class name; folders mirror namespaces; enums one per file.
- Comments explain *why*, not *what*. No commented-out code.
- No LINQ or allocations inside per-realization compute paths; pre-allocate in setup, index in the loop.
- PascalCase types/properties, `_camelCase` private fields, camelCase locals/parameters.
- `#region` grouping when a class is large: `Construction`, `Members`, `IModel Methods`, `Private Helpers`, `Serialization`.
- Tests: files `<ClassName>Tests.cs` mirroring `src/`; methods `Test_<Scenario>_<ExpectedResult>`; `// Arrange / // Act / // Assert` comments; explicit deltas on floating-point asserts.

## Build Commands

```bash
dotnet build                                        # full solution, Debug — zero warnings required
dotnet test -c Release                              # fast PR gate (Verification skipped)
dotnet test src/RMC.TotalRisk.Tests                 # fast loop, Debug
dotnet test src/RMC.TotalRisk.Verification          # verification suite (deliberate)
powershell -File scripts/validate-code-xml-docs.ps1 # doc coverage + namespace/dependency guards
py scripts/sync-agents-md.py                        # regenerate AGENTS.md after CLAUDE.md edits
```

## Permissions and Approval (MANDATORY)

**Claude MUST NOT perform code changes without explicit user approval.** Present a plan first and wait for approval. Never start a major refactor autonomously. Small, clearly-scoped tasks may proceed after confirming scope. If in doubt, ask.

## Session Workflow

1. User assigns a phase or task (one roadmap phase per session).
2. Claude reads `docs/PROGRESS.md` + `docs/ROADMAP.md`, presents a plan, and waits for approval.
3. Work follows the porting loop: port → unit tests → verification (when the phase includes it) → `dotnet build` (0 warnings) → `dotnet test -c Release` → `validate-code-xml-docs.ps1` → update the Ported Types Matrix + `docs/PROGRESS.md` → regenerate AGENTS.md if CLAUDE.md changed → commit.
4. User compiles/reviews; fix; repeat.

## Git Workflow

- All v1.1 work lands on **`v1.1-development`**. `main` remains the v1.0-era public face until v1.1 ships; the user coordinates merges and pushes.
- **Committing is mandatory** after a successful validation run — commit before reporting the work complete. Stage only files belonging to the change. If validation fails, do not commit.
- **No AI breadcrumbs in commits**: no generated-by text, no co-authored-by AI trailers, no references to AI assistants or agents by name.
- **Do not push unless explicitly asked.** Never push to `main`.

## Progress Tracking

Track completed work in `docs/PROGRESS.md` (newest first: **Goal / Landed / Verified / Next**, with exact gate counts and user decisions recorded). Update the Ported Types Matrix in this file as types reach P/T/V status.

## Files to Ignore

Never commit: `**/bin/`, `**/obj/`, `**/.vs/`, `**/TestResults/`, `*.user`, `*.suo`, local NuGet caches.

## Future Feature Enhancements

Planned follow-on projects, in rough order (each gets its own roadmap phases when the model library stabilizes):

- **`RMC.TotalRisk.Api`** — **now roadmap Phase 14** (executable any time after Phase 6): one ASP.NET Core project hosting REST controllers + the MCP server (`ModelContextProtocol.AspNetCore`, stateless HTTP at `/mcp`), mirroring the confirmed `RMC.BestFit.Api` template (store/services/mappers/DTOs, synchronous runs, OpenAPI via `AddOpenApi()`). Containerization and auth are net-new work on top (BestFit ships neither).
- **`RMC.TotalRisk.UI`** (net10.0-windows) — WPF wrapper layer: project model, element wrappers (the "element" vocabulary lives here, never in the model lib), `.tra`/`.rmcbf` file reading (SQLite lives here too), v1.0-project import mapping onto the v1.1 analysis API, BestFit posterior importers feeding `Estimate(IList<ParameterSet>)`, data binding onto the model lib's INPC surface.
- **`RMC-TotalRisk` App** (net10.0-windows WinExe) — the desktop shell, ported from the legacy VB WPF app.
- **`CostBenefitAnalysis`** — composite analysis owning a `List<RiskAnalysis>` of alternatives; the Phase 4 self-contained-analysis foundation is its design driver.
- **FDA importer** — port of the legacy `FDAImporter_143` (HEC-FDA 1.4.3 → TotalRisk objects) plus committed FDA verification datasets via a `docs/verification-requests/` request.

## Unit Test API Gotchas (CRITICAL)

- **`dotnet test` on .NET 10 (Microsoft.Testing.Platform):** `--filter` before `--` is silently ignored for MSTest.Sdk projects; scope by project path or configuration. A filter that matches zero tests in an assembly FAILS that assembly.
- Verification tolerances are documented per test with their derivation (k·SE); never widen a tolerance to make a test pass without recording why in the test's XML docs.
- MSTest method-level parallelization is on: no shared mutable statics, no order dependence, no `Thread.Sleep` timing assumptions.
