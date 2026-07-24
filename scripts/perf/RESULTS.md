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
