# Engine Performance Measurements (Phase 6.5)

Wall-clock measurements from `PerfHarness`, recorded per optimization commit. **One fixture
per invocation** (the session workflow rule — measurement rounds stay in the minutes):

```bash
dotnet run -c Release --project scripts/perf/PerfHarness -- F1
```

The SHA-256 is the byte gate: Group-1 optimizations (commits C1–C6) must reproduce the
baseline hash bit-for-bit on every fixture; Group-2 (C7 closed-form CVaR, C8 ensemble budget)
moves values deliberately and re-pins. The harness defaults to a single rep — results are
deterministic, so the hash needs one run and the timing signal at fixture scale (tens of
seconds) resolves the targeted multiples; `--reps 3` is reserved for the committed
baseline/final rows.

Fixtures:

- **F1** — trivial 1D single-component fixture (uncertain triangular fragility), N = 1000 full
  uncertainty. The PROGRESS-recorded pre-optimization reference measured ≈ 54 s on the
  session machine of 2026-07-23 (Debug-adjacent conditions); the Release baseline below is
  the working reference.
- **F4** (added Phase 8.5) — a **dependent** competing-risks component, 4 failure modes under a
  perfectly-negative dependency, N = 200. The one shape whose cost lives in
  `SampledComponent`'s constructor rather than in the risk integral: a dependent competing
  configuration routes `CompetingRisks.CumulativeIncidenceFunctions` onto its Genz
  multivariate-normal branch, one rectangle integral per unit per hazard level over 201
  levels, per realization.
- **F2** — two-component joint (VEGAS) system, N = 200 at a reduced VEGAS budget (warm-up
  1000 × 2 cycles, 2000 final evaluations × 5 recording passes) — the default budget runs
  ~110k evaluations per realization, far too heavy for an iteration fixture, and optimization
  deltas are relative on identical code paths.
- **F3** — F1 carrying a second consequence type (the Phase 6.5 axis; measures the ×K cost).

Each fixture also reports the process-wide allocated-bytes delta and GC collection counts of
the final full run (`GC.GetTotalAllocatedBytes(precise)`) — the direct signal for the
allocation-elimination commits, where wall time alone is a noisy proxy.

## Measurements (machine HADEN, 22 logical processors, Release)

| Commit | F1 mean-only (s) | F1 full (s) | F1 alloc (GB) | F2 full (s) | F2 alloc (GB) | F3 full (s) | F3 alloc (GB) | Notes |
|---|---|---|---|---|---|---|---|---|
| baseline (post-B3, `a2d174d`) | 0.065 | 31.458 | 38.97 | 24.082 | 24.77 | 66.501 | 75.21 | F1 median-of-3; F2/F3 single rep; F3 ≈ 2.1 × F1 (the K = 2 cost) |
| C1 workspace | 0.082 | 29.485 | 31.62 | 19.875 | 20.10 | 58.514 | 62.16 | hashes identical on all three fixtures; the remaining allocations are the recording path (lists a RiskPoint adopts), which shrink with the C8 evaluation-count cut |
| C2+C3 probes/joint | 0.071 | 27.949 | 31.62 | 20.051 | 19.67 | 57.423 | 62.46 | hashes identical; Balanced scales in one 51-eval pass (was 153 + three bin builds); joint latent transform in place off a cached Cholesky factor. Convolution buffer reuse (planned C4) consciously skipped: the additive multi-component convolution runs ~10 allocations per realization — no fixture shows it, not a hot path |
| C5+C6 percentiles | 0.067 | 25.835 | 25.12 | 19.678 | 16.95 | 54.962 | 48.37 | hashes identical; merge-walk log-log interpolator (0-ulp pinned vs OrderedPairedData) with transposed loops replaces per-ordinate binary searches and the Ordinate-view allocations; profile sort → strictness-gated reverse. SingleComponentUncertainty pinned percentile literals unmoved |
| C7+C8 CVaR + budget | 0.065 | **3.898** | **3.67** | 20.160 | 16.95 | **7.810** | **7.07** | the value-moving batch: exact closed-form CVaR (more exact than the 1e-8 AGK it retires) + relaxed ensemble discipline (EnsembleTolerance 1e-4 / EnsembleMinDepth 0 defaults; mean pass, probes, and mean-only runs stay 1e-8/2). **F1 8.1×, F3 8.5× vs baseline**; F2 is VEGAS-budget-bound by design. Zero pinned verification constants moved — the exact-anchored families (SingleComponentUncertainty, NfipAssurance, MeanParity kernel-identity, LhsVarianceReduction) pin their discipline in-test with documented rationale; the relaxed default is exercised by MultiConsequence + EngineReproducibility |

Post-C8 byte-gate hashes (the re-pinned baseline for any further bit-inert work):

- F1 `b88a49f3343788ef2cef5c5b99e44615dc286e38536bd63f65f755d2c46c257e`
- F2 `c50f427116123f8016c4da2512ec9d860dec84e5f7575709da20d8813a7c6a51`
- F3 `16de774a4fdafc506a0c631cd00c6d5309482b1cc8d1fafc8badde1944c9251c`

## Phase 6.6 measurements

**Stage 1 (Q-T profile remap, commit `c115f79`):** bit-inert by construction with no profile
selected — F1 reproduced the post-C8 hash `b88a49f3…` exactly (verified in-session 2026-07-24).

**Stage 2 (profile catalog + five-stream banding parity):** a deliberate value-moving batch —
the results JSON gains the catalog arrays (`CumulativeFailureProbabilities`,
`CumulativeExpectedConsequences`, `SystemResponseExceedanceProbabilities`/`Probabilities`) and
the banding restores the v1.0 five-stream scope, so the byte gate re-pins:

- F1 `6cbe160902cb804e7bab710f2faeddfa6ae0683b58a0ba31d9c42d8c254717fd` (stable across
  single-rep and median-of-3 runs — determinism held).

Wall-clock on the 2026-07-24 session machine drifted well above the 6.5-session regime
(the committed 6.5-identical state measured 5.0–6.9 s across the session vs the recorded
3.898 s), so the honest comparison is the same-conditions matched pair at `--reps 3`:
committed reference 6.907 s / 3.67 GB vs stage 2 6.595 s / **3.93 GB** — wall delta inside
session noise; the deterministic signal is the **+0.26 GB (+7%) allocation** for the
per-realization catalog arrays plus the 15 additional banded profile families (a bisect
attributed ≈ 0.35 s to the extra banding assemblies, ≈ 0.1 s each to the exceedance
coordinate and the ascending pass under low-noise conditions). Mean-only wall is unchanged
(0.093 committed vs 0.101 stage-2 at matched conditions — the mode-scope profiles cost one
pass over recorded points).

**Stage 4 (% contribution):** another append-only results-JSON extension (the
`RiskContribution` members at mode/component scope), so the byte gate re-pins:

- F1 `d98a11f9a5041a7cc4556e3dcc5eda92f4eab3ea33dd47d76d09c51758c00c4f` (single rep,
  5.991 s / **4.09 GB** under the same noisy session regime).

The deterministic signal is again the allocation: +0.16 GB over stage 2 for the
per-(mode, type) contribution rows (~32 B per recording evaluation per mode, freed with the
realization's memory dump) and the finalize lists. The kernels add one array store per mode
per recording evaluation (per-mode methods reuse the products the expected-value chains
already compute) and an O(|participants|) split per joint tuple, in accumulation chains kept
separate from every pinned floating-point sequence — `EngineReproducibilityVerification` and
`JointFailuresVerification` passed unchanged as the stage gates.

**Phase close (stages 5–7, commits `7e26c3e`…`767bf95`):** one more append-only
results-JSON extension — stage 5 serializes `EnsembleResults.Summary` (the scalar-CI +
convergence block) — so the byte gate re-pins once more:

- F1 `d1faad62cc114ede085ce4578cc04699e883f41e3d7ff47ef8efa75f9bab5824` (single rep,
  6.797 s / **4.09 GB**, 2026-07-24 phase-close run).

Allocations are byte-identical to stage 4 (the Summary reduction is grid-scale work at
ensemble end, not per-realization) and wall stayed inside the session's noisy regime. The
stage-6 sensitivity engine and the stage-7 seed maps are run-path- and JSON-inert by
construction (unpersisted API objects / runtime-only state) — this run is their perf gate:
had either leaked into the run path, the hash or the allocation counter would have moved
beyond the Summary block. Existing-field byte equality across the re-pin is carried by the
append-only load tests (pre-6.6 payloads deserialize with null blocks) and by the zero moved
pins across the regression families.

**Phase 6.7 Stage 1 (the BranchPolarity/ResponseNodes hash event, 2026-07-24):** every
`ResponseStage` now serializes its resolved `BranchPolarity` and the component identity form
annotates each projected mode's response-element topology (`ResponseNodes`), so every
component canonical hash — and therefore every content-derived Monte Carlo seed — moves
once, deliberately. No engine file changed in this stage (projection, serialization, and
graph validation only), so for the sampled fixtures the hash movement is pure seed
relocation; the deterministic bit-pin (`Test_Deterministic_BitPin_CascadePhases`, seed-free
by construction) and the relational `EngineReproducibilityVerification` +
`SingleComponentUncertaintyVerification` families passed without any re-capture. Wall/alloc
figures below are single-rep on a loaded session machine — non-comparative; this entry
exists to re-pin the byte gates, which are the Stage 2/3 bit-identity baselines (the
state-group rework must reproduce them exactly).

**Phase 6.7 Stage 3 (state-group layer + Q3 end-state labels, 2026-07-24):** the group
kernels landed with a mechanical inertness proof — F1/F2/F3 reproduced the Stage 1 hashes
**bit-exactly** with the whole state-group layer active (every pre-6.7 layout is trivial and
the unit-space arithmetic reduces to the pre-cascade operations exactly; F1 allocations
4.09 → 4.10 GB from the per-realization layout builds in the combination-cache getters).
The gates then re-pinned once for the Q3 labeling: `FailureModeRealization`/
`FailureModeResults` gained the stamped `Name` and the append-only `PathLabel` — a
JSON-only movement (labels are display metadata; every numeric surface had just been proven
bit-identical).

Baseline byte-gate hashes (re-pinned at the 6.7 Stage 3 Q3 labeling):

- F1 `917ff3a52dc6b18c9fed374f56b75490649b6086e8b36c8f795de280b4c20ecc`
- F2 `f5de82ea7e028abff4144a7427fb4d57baf7a9bf1ec3abb15477771b4ddc8397`
- F3 `f2714852569e72c8883d6936265f2ddcd099711e644be5a869f7496db661b015`

Prior baselines (6.7 Stage 1 hash event; Stage 3 kernels reproduced these bit-exactly
before the labeling re-pin):

- F1 `193189449835d8ab34d877cc8aedb28be309313d03c17ee31d45b9f3088ca93a` (reps 1: full-MC 7.535 s / 4.09 GB)
- F2 `d59c57713eb0cd037488b2076df79622917878b96fa0f2062ccd61aab99e79f6` (reps 1: full-MC 37.028 s / 17.93 GB)
- F3 `8c97caf0e6fa4f431e295e8839ab4da49484d7868ea3c92e765cdb8add3bee26` (reps 1: full-MC 15.767 s / 7.84 GB)

Prior baselines (post-6.6, superseded by the 6.7 hash event):

- F1 `7a88638cf38c5ee38a3091fabd933e747dd9dc43ea14d44b0addbb465c12f500`
- F2 `dbc8dd66faf06c8de4ee434bf9bb04d321b812e5d26b3db1ae899ee72e34ec80`
- F3 `06a1456315cc3a6458850e1a22611fc72dc7529a188b36c5efa073169fb0d785`

## Phase 8.5 Stage 1 — bit-inert cleanups (2026-07-26)

Every Stage 1 sub-stage is gated on reproducing the 6.7 Stage 3 hashes **bit-for-bit**. They
all did. Session reference conditions (machine HADEN, 22 logical processors, Release, single
rep) — wall-clock on this machine drifts by ±30% between runs, so the deterministic signals
are the hash and the allocation counter:

| Stage | F1 full (s) | F1 alloc (GB) | F2 alloc (GB) | F3 alloc (GB) | Hashes |
|---|---|---|---|---|---|
| baseline (`f186989`) | 5.619 | 4.10 | 17.93 | 7.85 | all three reproduce the 6.7 Stage 3 values |
| 1a Numerics helpers | 5.328 | 4.10 | 17.93 | 7.85 | bit-identical |
| 1b PERT-percentile-Z determinism | 5.328 | 4.10 | — | — | bit-identical (branch unreachable from the fixtures) |
| 1c combination-cache hygiene | 5.646 | **4.09** | 17.93 | **7.84** | bit-identical |
| 1d competing-risk CIF sharing | 5.615 | 4.09 | 17.93 | 7.84 | bit-identical |
| 1e pooled exclusive-combination buffers | 5.4 | **3.88** | **16.38** | **7.64** | bit-identical |

The 1c allocation drop is the eliminated double re-projection (`CombinationUnitCount()` rebuilt
the end-state layout twice per component per realization); the per-method matrix saving does
not show on these fixtures because they are single-mode.

The 1e drop is the pooled `IndependentExclusive` outputs replacing the allocating overload at
both call sites (the component pathway decomposition and the joint system integrand). It is
largest on **F2, −1.55 GB (−8.6%)**, because the joint integrand runs the enumeration tens of
thousands of times per realization; F1/F3 gain the component-scope half only. Cumulative Stage 1
allocation: F1 4.10 → 3.88 GB, F2 17.93 → 16.38 GB, F3 7.85 → 7.64 GB, all bit-identical.

One constraint the pooled overload imposes, checked at both sites before adopting it: it reuses
indicator row arrays in place, so a caller must not retain a row past its evaluation. Neither
does — the joint integrand extracts participating indices into its own list before the odometer
runs, and the component kernel reads rows within the same evaluation.

### F4 — the competing-risks measurement, and why its hash is not a gate

Measured with the cache force-disabled versus enabled, same fixture, same session:

| F4 | full-MC (s) | allocated (GB) |
|---|---|---|
| CIF rebuilt per realization (pre-1d) | 22.284 | 20.36 |
| CIF data shared across the run (1d) | 5.487 | 4.77 |

**≈ 4.1× faster, ≈ 4.3× less allocated**, on a fixture that is 200 realizations; the ratio grows
with realization count because the skipped work is per-realization and constant.

## Phase 8.5 Stage 2 — new capability (2026-07-26)

The optional-measure flags and the adjusted failure-mode curves both default to preserving
today's behaviour, so no number moved — but both add fields to the results JSON, so the byte
gates re-pin once. Proven JSON-only rather than numeric by dumping F1's payload
(`--dump <path>`, added to the harness for exactly this) and stripping the four added keys —
`AdjustedExcess`, `AdjustedFail`, `AdjustedCurves`, `AdditionalAdjustedCurves` — which
reproduces the previous hash `917ff3a5…` **exactly**.

Baseline byte-gate hashes (re-pinned at Stage 2):

- F1 `8169cce116c5c7a247a52514bdb07c5d431f8bcc3f8d12c996f130b3d34053fb`
- F2 `4efa12fd62c5f5cfb90367424ac062ace1ff2dd5de746c48df199a8c7aaee17e`
- F3 `383bbe0028414d6ae01d5b3decac71cebe648259bcce305782da3ec5d8bba540`
- F4 `ab80eab10d2fceae00e1f0d17771cbab9c19f4b650682e687a4fe89ff1174687`

Allocations are unchanged from Stage 1 (F1 3.88 GB, F2 16.52 GB, F3 7.64 GB, F4 4.68 GB): the
new fields are null or empty unless the options are set.

Prior baselines (Stage 1, superseded by the Stage 2 field additions):

- F1 `917ff3a52dc6b18c9fed374f56b75490649b6086e8b36c8f795de280b4c20ecc`
- F2 `f5de82ea7e028abff4144a7427fb4d57baf7a9bf1ec3abb15477771b4ddc8397`
- F3 `f2714852569e72c8883d6936265f2ddcd099711e644be5a869f7496db661b015`
- F4 `af2348d1b2dfa20afb0bedf9b1d732189a40848ba38f254eb48ed3a59921e46b`

### N17 — the reproducibility defect F4 exposed, and its fix

Building F4 immediately showed its results SHA-256 changing on **every run**:
`237b694e…`, `a37864ab…`, `603f565e…`. That was **pre-existing**, not introduced by Stage 1.
`MultivariateNormal` initialized its quasi-Monte-Carlo generator as `new MersenneTwister()`
(`MultivariateNormal.cs:73`), whose parameterless constructor seeds from
`DateTime.UtcNow.Ticks`. `MVNDST` draws from it, so every Genz interval evaluation — and
therefore every dependent competing-risks incidence curve — was clock-seeded.

It went unnoticed because no fixture exercised dependent competing risks before F4, and
`CompetingFailuresVerification` asserts statistically at k·SE tolerances that absorb Genz's
≈1e-4 error. The joint and additive system paths were never affected: the joint hazard uses an
in-place Cholesky transform, not the MVN CDF, and `Probability.ExclusiveMVN`/`UnionMVN` have no
engine callers. Note also that the multivariate normal reaches the randomized lattice rule only
above two dimensions — at two it uses a closed bivariate form — so the exposure begins at three
combination units.

Fixed in two parts: Numerics defaults `MVNUNI` to a fixed seed and `CompetingRisks` gained a
`PRNGSeed` that seeds it at construction; TotalRisk sets that from the component's content-derived
run seed. The realization index is deliberately excluded, because the run-shared pre-processing
below publishes whichever realization finishes first — a realization-dependent seed would make
the shared result depend on thread scheduling.

**F4 byte gate (pinned 2026-07-26, reproduced twice consecutively):**

- F4 `af2348d1b2dfa20afb0bedf9b1d732189a40848ba38f254eb48ed3a59921e46b`

### Thread-safety constraints on the shared pre-processing

Two findings from the same work, both load-bearing for the cache's correctness:

- **Only plain `double[]` is shared.** `OrderedPairedData`'s two-list constructor copies each
  pair into its own ordinate list rather than retaining or sorting the inputs, which is what
  makes concurrent reads of the shared arrays safe.
- **Distributions and bins are rebuilt per realization.** A Numerics interpolator writes its
  `SearchStart` on every lookup (`UseSmartSearch` defaults true), so one `EmpiricalDistribution`
  read by parallel realizations is a data race — and on the non-strict incidence curves, where
  ties admit more than one valid bracket, a corrupted search start can return a *different
  ordinate*, not merely a slower lookup. `StratificationBin.Weight` is publicly settable, so the
  bins are rebuilt too; at 200 bins that is free beside the integration being skipped.

## Phase 8.5 stage 3a — quadrature mass ledger (N7 adoption)

The 1D LEC probability mass now comes from `AdaptiveGaussKronrod.Recorder` (the acceptance-aware
`(x, weight, f)` flush) collected by `RMC.TotalRisk.Results.QuadratureMassLedger`, replacing the
v1.0 midpoint-trapezoid re-derivation of mass from the recorded abscissas. This is a deliberate
value-moving change; the byte gates are re-pinned.

### Accuracy, measured

The A/B was taken through the `RMCTR_LEGACY_MASS` environment switch against a 4,000,000-point
dense reference on fixture F1's component:

| Mass source | Relative error vs the dense reference |
|---|---|
| Midpoint trapezoid (v1.0) | 5.664E-007 |
| Quadrature ledger | 3.996E-011 |

### Byte gates, re-pinned at `--reps 3`

- F1 `23a0f30ea40cff8c05025c4e0a7e9acefa66c40800b8b60d35f7fa1843548a49`
- F2 `9dbec059b6f10d6f8dcde5b6abaa4bdaa777b390cfe2293061d818b4b6f54f4f`
- F3 `8c8494416786fef6185a80e46327c7c30b6b631119e5b542e371f8a14753353d`
- F4 `31bef59fda695643aa576ae3d172f8dd39bb243a8c5f51176e4ee774baf24b46`

**F2 was expected to hold and did not — the cause is understood and is not a leak.** F2 is the
joint/VEGAS path, which the ledger does not touch. It moved because the same commit extends the
end stratification bins in `BuildHazardBins` to the integration domain, and `BuildHazardBins`
also feeds `ProbeAnnualFailureProbability`, whose result sets the VEGAS tail-focus γ. A different
γ redistributes the VEGAS samples, so every joint ordinate moves. The ledger itself is confined to
the 1D path.

### Why the end bins were extended

The ledger's domain-partition gate compares `Σ weights` against `Σ bin widths`, and the bins
covered only the hazard's tabulated support (0.998 wide) rather than the stated integration domain
`[1e-16, 1−1e-16]`. Normalizing the recorded mass to close that gap was tried first and rejected:
scaling every mass by 1/0.998 spreads the uncovered tail mass *proportionally across the whole
curve*, a 0.5% systematic bias, when the missing mass belongs at the tails. Extending the first and
last bins puts the quadrature on the domain it claims, and the gate then holds without a correction
factor.

### Allocation cost

The ledger retains one `(abscissa, weight)` row per accepted evaluation for the life of a
component integration:

| Fixture | Before | After |
|---|---|---|
| F1 | 3.88 GB | 4.54 GB |
| F3 | 7.64 GB | 8.81 GB |

In-place compaction in `Curve.ApplyRecordedMass` (overwriting the kept points and trimming once,
rather than building a second list) took F1 from 4.61 GB to 4.54 GB and F3 from 8.95 GB to 8.81 GB.

## Phase 8.5 stage 3b–3d — the rest of the value-moving batch

Three changes landed after the ledger, and **none of the four byte gates moved**:

- **3b** — the parametric hazard and response summarize their posterior into an
  `UncertaintyAnalysisResults` directly instead of calling `BootstrapAnalysis.Estimate`. The
  fixtures use tabular functions (estimated parametric functions are excluded from the byte gates
  by the reproducibility note), so the gates are silent on it; the equivalence is pinned upstream
  and the estimation-touching verification families re-ran green.
- **3d** — `Math.Pow(x, 2d)` → `Tools.Sqr` at the four remaining sites. This was deferred out of
  the bit-inert stage because the two are **not** identical: 1,560 differences per 3,000,000
  random inputs, all one ulp. The sites are `NonparametricHazard`'s order-statistic standard
  errors (kept out of the byte-gate fixtures) and the sensitivity R², which is not serialized.
- The `RMCTR_LEGACY_MASS` A/B scaffold and the retired `ProcessHazardProbabilities` chain are
  **deleted** — the phase exit gate. That removed 117 lines across five files.

Byte gates confirmed unchanged after all three:

- F1 `23a0f30ea40cff8c05025c4e0a7e9acefa66c40800b8b60d35f7fa1843548a49` (4.54 GB)
- F2 `9dbec059b6f10d6f8dcde5b6abaa4bdaa777b390cfe2293061d818b4b6f54f4f` (16.52 GB)
- F3 `8c8494416786fef6185a80e46327c7c30b6b631119e5b542e371f8a14753353d` (8.81 GB)
- F4 `31bef59fda695643aa576ae3d172f8dd39bb243a8c5f51176e4ee774baf24b46` (4.63 GB)

### 3c was audited and declined

`SystemConvolution` does **not** migrate onto N8's `EmpiricalDistribution.ConvolveDiscrete`. The
upstream kernel is pairwise and derives its lattice step from its two operands' spans, so an N-way
fold through it re-bins the running result at a step that changes on every fold, and its
two-node atom deposit smears once per re-bin. The engine bins every component once onto a single
lattice sized to the summed support and folds by integer shift, which is exact by comparison. The
reason is recorded in the type's remarks so the next sweep does not re-litigate it.
## Phase 8.6 — endpoint mass, lazy enumeration, and allocation recovery (2026-07-27)

The approved endpoint rectangles and result manifest deliberately move result bytes; the scalar
risk-point storage and curve-workspace reductions that follow are representation-only and reproduce
the Phase 8.6 hashes exactly. Final runs were isolated, Release, three repetitions per fixture on
HADEN (22 logical processors):

| Fixture | Full median (s) | Allocated (GB) | Pre-ledger allocation (GB) | Phase 8.6 SHA-256 |
|---|---:|---:|---:|---|
| F1 | 5.669 | **3.09** | 3.88 | `4c1472d2a3c1c1abdb5b01f05db91bba00b5867b23776fee170ac8372d100d00` |
| F2 | 34.183 | **12.22** | 16.52 | `d19f56c5bcb4c8dcc0b9ab13e724d2fd1568ae9f8a65d91a9d2478adb8c72852` |
| F3 | 10.757 | **6.03** | 7.64 | `6469666ef207b436263434e793dc5bd8ec2990e2b26f9cf822de961019206281` |

Allocation recovery came from pooled ledger/thinning storage, in-place equal-abscissa and
exact-ordinate workspaces, eliminating redundant adopted-list construction, and an inline
single-entry `RiskPoint` representation whose public list surface materializes only when inspected.
The general multi-entry path and every numerical reduction retain their existing order. F1 is 20.4%
below its pre-ledger allocation and its 5.669 s median is within 5% of the recorded 5.4 s reference;
wall-clock variation on this workstation remains materially noisier than the deterministic allocation
and hash signals.

### Forensic closure paired rerun (2026-07-27)

The lifecycle and verification closure was timed against safety-gate commit `3a03371` from a
temporary archive, alternating current and baseline invocations on the same machine. This controls
for the workstation's observed wall-clock drift more honestly than comparing with a morning run.
Each reported value is a three-repetition Release median, with one fixture per invocation:

| Fixture | `3a03371` full (s) | Current full (s) | Current allocation (GB) | Current SHA-256 |
|---|---:|---:|---:|---|
| F1 | 8.145 | **7.763** | 3.10 | `4c1472d2a3c1c1abdb5b01f05db91bba00b5867b23776fee170ac8372d100d00` |
| F2 | 29.173 | **26.839** | 12.23 | `876ce063bc6777d54154728e747d279058611e1a1521e3f0d2cd4b6aa6155c73` |
| F3 | 10.554 | **10.503** | 6.03 | `6469666ef207b436263434e793dc5bd8ec2990e2b26f9cf822de961019206281` |

F1 and F3 retain the `3a03371` hashes exactly. F2 intentionally moves because the deterministic
failure-probability probe now shares production integration's endpoint-rectangle support service;
that changes the automatic VEGAS focus and therefore redistributes samples, while definition
hashes and seeds remain unchanged. The allocation signals remain below the pre-ledger baselines.

During the alternating round, current-code wall medians also ranged to 30.843 s for F2 and
13.387 s for F3 before immediate repeats returned 26.839 s and 10.503 s; hashes and allocations
were stable. The paired results are retained so future gates can distinguish deterministic
regression signals from this workstation's timing noise.
