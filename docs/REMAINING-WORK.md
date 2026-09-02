# Remaining v1.1 Work

> The authoritative map of what stands between the current state and full v1.1, maintained
> alongside [ROADMAP.md](ROADMAP.md) (phases and exit gates) and [PROGRESS.md](PROGRESS.md)
> (session log). Updated 2026-08-28 after v2.0-program session 3; update whenever an item lands or
> a ruling changes scope.

> **Program decision (2026-08-27):** the capability roadmap beyond v1.1 is approved; the final
> TotalRisk release is **v2.0.0** — all tier A–C work ships in it (the old v1.2/v1.3 mapping is
> superseded) — and the program closes with a dedicated session that publishes the NuGet package
> and makes the code public. The v1.1 sequence below still runs first and is unchanged except as
> noted. Program progress: session 1 (the Numerics 2.2.0 tier A/B slate), session 2 (**A4 —
> the weighted epistemic ensemble**, the foundation primitive for B2/B1/B3/C1/C3, plus the
> **A7 tolerable-risk confidence** stretch — serialized `TolerableRiskCriteria` on the options
> per the 2026-08-27 ruling, the conditional-presence hash identity pinned, and no
> `RiskMeasureOptions` flag so the recorded `"All"`-serialization trap never engages), and
> session 3 (**B2 — value of information**, output-first with the given-data weighted EVPPI
> conditioning and the A7 guideline-movement framing; plus the quick wins **A3** given-data
> sensitivity measures, **A9** retained realizations with byte-exact post-hoc weighted
> re-banding, and **A2** the secondary-bin Richardson diagnostic; A10-TotalRisk skip-recorded
> on a discovered upstream gap; the B1 design draft written to
> `~/.claude/plans/b1-bayesian-updating-design-draft.md` for Haden's design session), and
> session 4 (**A10 — the extrapolation policy end to end across both repos**: the upstream
> `Extrapolation` wrappers riding the 2.2.0 release plus the TotalRisk `ExtrapolationPolicy`
> with the Error guards; the quick wins **A6** epistemic conditioning via fractile pins, **A5**
> exposure-period/life-cycle conversions, **A1** exact fault-tree importance; and the full A8
> stretch — the runtime joint convergence certificate and, per the ratified attempt, the
> scrambled-Sobol `SamplingScheme` member plus the opt-in seeded-Sobol joint driver, with
> `Vegas.SobolSeed` also riding 2.2.0) are complete.

## The sequence to `v1.1.0-alpha`

| # | Item | Owner / gate | What it entails |
|---|---|---|---|
| 1 | **Numerics 2.2.0 release** (remediation task N4) | **User-gated** — Haden cuts the release | The branch push is done (origin in sync at `dc5b17c`, observed 2026-08-27): the tier A/B slate is upstream (`AdaptiveGaussKronrod2D`, weighted statistics, `ExtrapolationSides`, scrambled Sobol, `UnionSingleFactor` + `Tools.Expm1`, `GlobalSensitivity`, the lookup binary-compatibility overloads). Remaining: `dotnet pack /p:Version=2.2.0` to the local feed `C:\GIT\numerics\packages` — **the previously packed nupkg is stale**: per the 2026-08-28 release-train ruling the A10 extrapolation wrappers (`91ccaf9`) and `Vegas.SobolSeed` (`fa91884`) ride this release, and the draft carries both items with re-pack reminders. Release notes are **drafted** at `~/.claude/plans/numerics-2.2.0-release-notes-draft.md`, covering all 127 commits since the v2.1.4 merge incl. the ratified rulings (D1, D2, D4/D5, D6, D7), the MCMC changes and their BestFit impact, the Frank θ > 0 conditional-inversion correction, and the Gumbel/Joe boundary saturation. The F6 byte-gate re-pin ruling is **executed** (approved and re-pinned to `74af2e95…` 2026-08-27; attribution and evidence in PROGRESS 2026-08-27) |
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
| Engine AGK `AbsoluteTolerance` audit — acceptance is absolute-OR-relative, so a small integral can be governed by the absolute criterion (the `UnionSingleFactor` lesson, 2026-08-27); confirm the engine's small-EAD runs are not exposed | Item 4 candidate |
| Q-E — threading audit of `SampledComponent`/`SampledFailureMode` | Item 4 candidate |
| Q-F — structured `ValidationIssue` error codes for API/agentic clients | Item 6 trigger |
| Q-M — bootstrap posterior size vs `Realizations` (index-wrap vs percentile path) | Open (arch §11) |

Closed 2026-08-27 (v2.0-program session 1): the `Network._nodeCount` row was stale — the ruling
was executed 2026-08-03 (six commits, four ratified decisions, 46 tests); Q-H — `NextIntegers`
confirmed public API (three `System.Random` extension overloads); N15 closed as unneeded (the CIF
`bins` knob exists and the strict-output wrinkle keeps its engine-side rebuild); N16 closed by the
weighted statistics landed for 2.2.0. Closed 2026-08-27 (session 2): the F6 re-pin ruling —
approved and executed (`74af2e95…`, its own commit).

## Unscheduled items needing future ratification (not blocking alpha)

- **KernelDensity extrapolation surface (observation recorded 2026-08-28, from the A10 close;
  not scheduled).** A10 landed end to end on 2026-08-28 — the upstream `Extrapolation` property
  on the two evaluation wrappers (`TabularFunction`/`EmpiricalDistribution`, numerics
  `bug-fixes-and-enhancements`, riding the 2.2.0 release) and the TotalRisk
  `ExtrapolationPolicy` on the five tabular/nonparametric function types with the Error guards
  (`ExtrapolationVerification`). One adjacent surface was deliberately left outside the ruled
  two-type upstream scope: `KernelDensity` does **not** compose `EmpiricalDistribution` — it
  duplicates the lookup pattern (its own `OrderedPairedData`, transforms, and hard CDF support
  gates) — so it carries no extrapolation surface today. If KDE-backed curves ever need the
  policy (the post-v1.1 KDE-smoothed assurance banding is the plausible consumer), that is a
  new upstream item, not a TotalRisk wiring gap; a no-regression pin
  (`Test_KernelDensity_EmpiricalUnderTheHood_NoRegression`) documents the relationship.

- **Consequence fractile pinning via the coupling columns (recorded 2026-08-28, from the A6
  close; not scheduled).** The epistemic conditioning surface pins functions that own a
  percentile matrix (hazard, transforms, responses, profile transforms); consequence functions
  draw from each failure mode's coupling matrix instead, so a consequence pin is a validated
  no-effect warning today. Extending pins to the coupling columns means overriding column k of
  every consuming mode's matrix — which necessarily pins the paired failure/non-failure
  consequences of that type together (the pairing is the column's purpose) — a small, separate
  ratification when a consequence cross-tab is actually wanted.
- `CompositeTransform` **aleatory `Mixture`** mode — still waits on an engine transform-branch
  analog of the consequence exposure branches (within-realization enumeration). The explicit
  epistemic mixture mode landed 2026-09-02 (arch Q-Y closure): `EpistemicMixture` on all four
  composite clusters with named shared epistemic variables (`EpistemicVariable`,
  conditional-presence hashed; run-scope selector columns overwritten after seeding — zero
  walk-ordinal movement), branch attribution, the mean-only Error/Warning gates, and the
  `EpistemicMixtureVerification` family. The epistemic reading needed no branch-enumeration
  surface — one branch per realization chains like any sampled function — which is why it
  shipped while the aleatory transform mixture stays deferred.
- **Shared epistemic variables on `CompositeConsequence` (recorded 2026-09-02, from the
  epistemic-mixture landing; not scheduled).** An epistemic consequence composite selects from
  its failure mode's coupling draw (consequences are never walked), so it has no per-function
  selector seat for a shared variable to overwrite — binding one is a validation-visible
  non-feature (the property is deliberately absent). Extending sharing there rides the same
  future ratification as consequence fractile pinning via the coupling columns (above): both
  need a ruled seat for per-function conditioning of coupling-driven draws.
- **A first-class shared-epistemic-variable object (recorded 2026-09-02; not scheduled).** The
  landed sharing identity is the name string on each binder — cheap, mode-portable, and exact.
  If authoring UX ever wants one place to declare a variable (description, weight-vector
  defaults, discoverability before any binder exists), that is a new Id-linked object paying the
  full registration bill (factory/resolver cases, both serialization modes, its own hash
  surface); the run mechanics underneath would not change.
- S-4 — upstreaming `CanonicalContentHasher` to `Numerics.Utilities` (deferred; TotalRisk copies
  the pattern).
- Arch §7.9.9 cascade deferrals: multi-group claimed non-failure states, competing over else-chain
  failure states, cross-group `ExclusivePCM` coupling, numeric `InverseSRP` for cascades.
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
