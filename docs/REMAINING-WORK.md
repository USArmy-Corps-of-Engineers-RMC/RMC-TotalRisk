# Remaining v1.1 Work

> The authoritative map of what stands between the current state and full v1.1, maintained
> alongside [ROADMAP.md](ROADMAP.md) (phases and exit gates) and [PROGRESS.md](PROGRESS.md)
> (session log). Updated 2026-08-17 from the full-repo review; update whenever an item lands or a
> ruling changes scope.

## The sequence to `v1.1.0-alpha`

| # | Item | Owner / gate | What it entails |
|---|---|---|---|
| 1 | **Numerics 2.2.0 release** (remediation task N4) | **User-gated** — Haden pushes and cuts the release | Push the numerics branch (one unpushed commit, `5ea77d3`); `dotnet pack /p:Version=2.2.0` to the local feed `C:\GIT\numerics\packages`; release notes covering the ratified rulings (D1, D2, D4/D5, D6, D7), the MCMC changes and their BestFit impact, the Frank θ > 0 conditional-inversion correction, and the Gumbel/Joe conditional-inverse boundary saturation |
| 2 | **Package switch** (remediation task T8; closes Phase 8) | One session after (1) | Swap all **four** `<HintPath>` references (library, both test projects, `scripts/perf/PerfHarness`) to the `RMC.Numerics` PackageReference already declared in `Directory.Packages.props`; update CLAUDE.md's interim dependency note and the README build prerequisites; re-run all eight perf byte gates (expected bit-identical) |
| 3 | **Phase 9 residue + Phase 11B — external imports and LifeSim** | One or two sessions | `RFAHazard` (owner: 11B — the tabular import of RMC-RFA results); `CompositeHazard` parameter-set import (+ resolve whether parametric posterior injection supersedes the planned `BestFitUnivariateHazard`); `CompositeConsequence` → Numerics `CompositeFunction` migration; `BestFitBivariateHazard` (θ-posterior import — copula-parameter uncertainty arrives here), `BestFitTabularHazard` (resolves arch Q-P: the import hash shape), `BestFitTransform`; `LifeSimConsequence` + `LifeSimResult` (arch Q-D: audit for hidden file I/O; needs the numerics `Network._nodeCount` ruling — LifeSim consumes that surface). Verification: a BestFit import contract test (deserialize with Numerics alone → construct → evaluate) plus RFA/LifeSim oracle families. **Exit: full v1.1 input-function surface P/T/V** |
| 4 | **Phase 12 — hardening** | One session | The ≥ 90% coverage gate script already exists and passes; the real delta is the one-off Linux `dotnet build` container check, `docs/getting-started.md`, a headless code example in `examples/`, and the BenchmarkDotNet micro-suite over the hot kernels. Candidate: the arch Q-E threading audit. Standing perf note: the F8 recording-path allocation profile (38.25 GB staging lists) |
| 5 | **Phase 13 — release prep** | One session; user tags | Recorded full-suite verification run, release notes, version stamping, the `v1.1.0-alpha` tag (user pushes/tags) |
| 6 | **Phase 14 — `RMC.TotalRisk.Api`** (REST + MCP) | Post-alpha, inside v1.1 (ruling 2026-08-17) | `src/RMC.TotalRisk.Api` + in-process test suite on the confirmed `RMC.BestFit.Api` template (store/services/mappers/DTOs, OpenAPI, stateless MCP at `/mcp`), plus `docs/api.md`. Containerization and an auth scheme are net-new scope decisions when deployment demands them. Unblocked since Phase 6; deliberately sequenced after the alpha tag |

## Rulings recorded 2026-08-17

1. **Authors-block scope**: required in the library and Verification projects; test classes are
   exempt (the ~23 test files that carry it are harmless). Enforced by
   `scripts/validate-code-xml-docs.ps1`.
2. **Phase 14 (API)** is inside v1.1 and ships **after** the `v1.1.0-alpha` tag — the alpha is the
   model-library milestone.
3. **`RMC.TotalRisk.Systems` root** (`SystemModel`, the Hydrologics `BasinModel` analog) is
   **post-v1.1**: the namespace reservation stays; `RiskAnalysis`-owns-components remains the v1.1
   system representation.

## Open questions that resolve inside the items above

| Question | Where it resolves |
|---|---|
| Q-P — `BestFitTabularHazard` import hash shape (full grid vs compressed posterior summary) | Item 3 |
| Q-D — `LifeSimConsequence` hidden-file-I/O audit | Item 3 |
| `Network._nodeCount` defect ruling in numerics `Dynamic/` (fix vs triage before release) | Item 1/3 |
| Q-E — threading audit of `SampledComponent`/`SampledFailureMode` | Item 4 candidate |
| Q-F — structured `ValidationIssue` error codes for API/agentic clients | Item 6 trigger |
| Q-H — confirm Numerics `Random.NextIntegers(int)` is public API | Any numerics session |
| Q-M — bootstrap posterior size vs `Realizations` (index-wrap vs percentile path) | Open (arch §11) |
| N15 (`CompetingRisks` CIF knobs) and N16 (weighted moments) | Open upstream (numerics) |

## Unscheduled items needing future ratification (not blocking alpha)

- `CompositeTransform` **Mixture** mode and the explicit epistemic mixture mode — both wait on an
  engine transform-branch analog of the consequence exposure branches (arch Q-Y).
- S-4 — upstreaming `CanonicalContentHasher` to `Numerics.Utilities` (deferred; TotalRisk copies
  the pattern).
- Arch §7.9.9 cascade deferrals: multi-group claimed non-failure states, competing over else-chain
  failure states, cross-group `ExclusivePCM` coupling, numeric `InverseSRP` for cascades.
- Exact BDD-based Birnbaum/criticality importance measures for fault trees.
- The Archimedean `ConditionalCDF` boundary accuracy limitation (documented, deliberately
  untouched under the algorithm-change rule).
- Joint-path independence-only polynomial shortcuts (a performance idea; not to be attempted
  without a ratified re-verification pass).

## Explicitly post-v1.1 (the roadmap's Future-phases block)

UI layer (`RMC.TotalRisk.UI`: element wrappers, `.tra`/`.rmcbf` reading, v1.0-project import,
TRG-line comparison, KDE-smoothed assurance banding, batch orchestration) · Desktop App (WPF
shell) · `CostBenefitAnalysis` (incl. `PlanRow.EquivalentAnnual` and the TR App. H
equivalent-annual math) · FDA importer + datasets (the six `BlockedExternalData` traceability
rows) · Hydrologics cross-engine parity · the `SystemModel` system root (ruling 3) · the shared
`Hydrologics.Risk` assembly.

## Standing facts worth restating

- The source tree carries **zero** in-code debt markers (no TODO/FIXME/HACK/NotImplementedException
  across 407 files); every remaining item is tracked here, in the roadmap, or in the architecture
  spec's §11.
- The traceability matrix maps 116 applicable legacy methods to current tests; the only
  blocked rows are the six FDA importer workflows awaiting external datasets (post-v1.1).
