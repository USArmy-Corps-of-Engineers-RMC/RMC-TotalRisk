# Engine Performance Measurements

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

## Current byte-gate pins

The standing gates, one row per committed fixture. A close-out byte-gate round runs every
fixture as a single invocation and each must reproduce its pin bit-exactly; the dated sections
below are the measurement history and pin provenance (superseded pins remain as audit trail).

| Fixture | Current pin (SHA-256) |
|---|---|
| F1 | `b2e6ea8861b3306ff783ad4e9b096a789bbd1bc53941fa237ef84f552f1843af` |
| F2 | `ac35a7fa8e93d542a876ca5b3b17ad673d7a102aeace8e2dba6e2f89d071763a` |
| F3 | `e46763eff9139854b9ba30ecf219c35260da3feda6fa5276e5e30e5bc0560e33` |
| F4 | `8a3a8b522b92aae893902ebc9aa785bbf02efa58de660b5a98219241dd8ea212` |
| F5 | `2ae3925bfb7488cbfa4bd516cc2d4eb7d71c6f84bfff9f4891873a2bfd811349` |
| F6 | `bd4a26e80972540f5247effb4521c26b94bfeb8998f99817cf7f352ca13d044b` |
| F7 | `f985ca0228952ac0ba66cc29b39cf9006247ea3965264b966ab785fa74317084` |
| F8 | `c9599e0a057ff511020d38a585669a56e8a13c8cf73fbfb4eed147f0ad184e2b` |

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
- **F5** (added Phase 10A) — a large `EventTreeResponse` with 24 independent external-link
  occurrences of one 46-node deep/wide target subtree. Its compiled form has 1,105 occurrence
  instructions, 1,104 edges, and 745 exhaustive branches over 33 hazard ordinates. It measures
  sampler setup plus 32 indexed exhaustive-branch reads at N = 64; the byte gate hashes every
  final branch classification and probability ordinate.
- **F6** (added 2026-07-30) — a four-child mixture `CompositeHazard` over bootstrap posteriors
  (LnNormal scenario parents, method of moments, 500 replications each) with an uncertain
  day/night mixture `CompositeConsequence` behind a deterministic parametric fragility, N = 500.
  The one shape whose per-realization cost is the combined-distribution rebuild: every
  realization re-tabulates `Mixture.CreateEmpiricalCDF()` (~200 bins over the K = 4 sampled
  children) before the risk integrand inverts the interpolated CDF at every quadrature node.
- **F7** (added 2026-07-31) — a shared-event `FaultTreeResponse`: twenty pooled basic events
  repeated three to seven times through shared-logical transfers across twenty crossing AND
  trains and two wide k-of-n voting gates (one counting crossing pool pairs), plus forty
  train-local events with sources alternating between scalars and 33-knot uncertain tables.
  The authored tree has 191 nodes (49 gates, 60 basic events, 82 transfers); the expansion
  unifies 60 Boolean variables and freezes a 23,344-node exact decision diagram. It measures
  sampler setup plus 32 indexed top-event curve reads at N = 64; the byte gate hashes every
  hazard/probability ordinate of the final indexed curve in G17/invariant form.
- **F8** (added 2026-08-06; re-pinned 2026-09-05) — a single bivariate component: an
  independence-copula `BivariateHazard` over uncertain tabular marginals at the default 20
  conditional bins, carrying a Secondary-bound uncertain pool-fragility failure mode and a
  joint `BivariateResponse` surface failure mode plus a primary background path, N = 1000.
  The shape whose per-realization cost is the two-dimensional adaptive conditional interior:
  each realization runs one `AdaptiveGaussKronrod2D` pass per stratification strip over
  (u, probit z) steered by the balanced surrogate, then replays one staged combination-kernel
  evaluation per distinct committed abscissa over its merged conditional column, with the
  fixed-slice adaptive sweep serving the endpoint columns.

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

## Phase 10A - event-tree compiled-plan cache (2026-07-28)

F5 was run alone in Release with `--reps 3` on HADEN (22 logical processors). The baseline is
commit `cd48c768` with the new fixture applied but before compiled-plan reuse; the final row is the
immutable instance-scoped occurrence/evaluation cache, complete dependency invalidation, and the
primitive-index linear evaluator. No acceptance threshold was introduced; these are reproducible
characterization measurements under the existing harness convention.

| F5 state | Setup median (s) | Setup allocation (MB) | 32 indexed reads (s) | Read allocation (MB) | Published plans |
|---|---:|---:|---:|---:|---:|
| uncached baseline (`cd48c768` + fixture) | 0.086353 | 51.70 | 1.157486 | 1,695.75 | n/a |
| compiled-plan cache | **0.031395** | **20.55** | **0.239866** | **99.77** | **1** |
| Phase 10A close (`--reps 3`) | **0.035477** | **20.55** | **0.283314** | **99.79** | **1** |

That matched pair is 2.75x faster with 2.52x less allocation during setup and 4.83x faster with
17.00x less allocation across repeated indexed reads. Evaluation is one parent-before-child pass
over the 1,104 compiled edges for each hazard ordinate. The measurement deliberately includes
public immutable branch-sample construction; the remaining 99.77 MB is predominantly the 745 x 33
public output matrix repeated 32 times, not occurrence compilation or graph search.

The phase-close row is the mandatory isolated invocation
`dotnet run -c Release --project scripts/perf/PerfHarness -- --reps 3 F5`. Its ordinary wall-clock
movement remains within the workstation variation described above; allocations, the single-plan
publication count, expanded shape, and result hash are the deterministic regression signals.

The byte gate is identical before and after caching:

- F5 `2ae3925bfb7488cbfa4bd516cc2d4eb7d71c6f84bfff9f4891873a2bfd811349`

The unchanged hash demonstrates bit-identical branch classifications and probabilities. Fast tests
separately pin aggregate curves, per-leaf curves, canonical hashes, seeded indexed samples, stable
ports, concurrent read-only publication, mutation invalidation, and rollback state.

## F4 re-pin — the boundary-clipping commit reached the dependent path after its last measurement (2026-07-29)

A routine byte-gate round found F4 at `846234f1…` against the recorded `31bef59f…`, with F1, F2,
F3, and F5 reproducing their pins bit-exactly. Bisecting with fixed engine states isolated the
movement to the numerics commit `eb6718b` (2026-07-27) — the final commit of the numerical-safety
batch, which applies the ratified probability boundary clipping to the joint/union/conditional
paths and routes `UnionPCM` through the lazy kernel. The dependent competing-risks configuration
is the one fixture crossing those sites, so its results moved within the clipping envelope. The
close-out isolated verification families ran against that numerics state and passed; the F4
measurement earlier in the same session predated the commit and was not repeated after it. The
prior engine/numerics pair reproduces the old hash bit-exactly today, exonerating the toolchain,
and the current pair reproduces the new hash across repeated runs. Re-pinned with approval
2026-07-29:

- F4 `846234f17c71ffef1239e50caee0f897c7e7d95bcf85489917b8ef7bca92eb99` (4.62 GB)

**Standing rule:** a byte-gate round at any close-out runs every committed fixture (currently
F1–F7), and a fixture measured before a session's final upstream commit is not a gate — re-run
after the last commit that can reach the compute path.

## F6 — the composite CreateEmpiricalCDF fixture (2026-07-30)

The dedicated performance session added F6 so the composite input-function shape has a committed
reference before any optimization of it is considered. Per realization the fixture pays four
posterior parameter lookups, one `Mixture` construction, one `CreateEmpiricalCDF()` tabulation
(~200 bins × 4 child CDF evaluations each), the interpolated `InverseCDF` at every accepted
quadrature node, and the two-branch day/night consequence expansion. The committed row (isolated
invocation, Release, `--reps 3`):

| Fixture | Mean-only median (s) | Full median (s) | Allocated (GB) | GC gen0/1/2 | SHA-256 |
|---|---:|---:|---:|---|---|
| F6 | 0.159 | 6.633 | 3.10 | 282/275/5 | `c183d28f83f41db40a4921542225382e30dc72cafc5df13ffc6ebe76be2d9132` |

The hash is stable across single-rep and median-of-3 invocations. The fixture addition touches no
engine code path; F1–F5 reproduced their recorded pins bit-exactly in the same session after the
session's last library commit, and the close-out round re-runs all six.

## F7 — the shared-event fault-tree fixture (2026-07-31)

The static fault-tree landing added F7 so the exact decision-diagram shape has a committed
reference: repeated shared events are what grow a reduced ordered diagram, and this fixture's
crossing trains and wide undecided thresholds keep many partial obligations live under any
canonical variable order. Setup pays occurrence expansion, unification, the 23,344-node diagram
build/freeze, and 34-dimension Latin hypercube preparation at N = 64; each indexed read pays 33
hazard ordinates × (60 source evaluations + one linear pass over the frozen diagram). The
committed row (isolated invocation, Release, `--reps 3`):

| Fixture | Decision nodes | Variables | Plans | Setup median (s) | Setup alloc (MB) | 32 reads (s) | Read alloc (MB) | SHA-256 |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| F7 | 23,344 | 60 | 1 | 0.091965 | 62.18 | 0.604993 | 574.74 | `f985ca0228952ac0ba66cc29b39cf9006247ea3965264b966ab785fa74317084` |

Read time is dominated by uncertain-table source evaluation (each read re-samples 34 table
curves per hazard pass), not by the frozen diagram, whose linear evaluation is allocation-free
by construction; the recorded read allocation is predominantly the per-read public curve and
source-curve materialization. The fixture addition touches no engine code path, and the
close-out round runs all seven fixtures.

## Upstream Numerics micro-measurements (2026-07-30, record-only)

Two upstream costs flagged by the full review were measured (never changed — any fix is an
upstream proposal for the pre-release Numerics batch). Single-threaded Stopwatch medians of 5 on
HADEN, bench compiled Release, measured against **both** the sibling Debug Numerics build (the
configuration every committed fixture rides) and a Release Numerics build (the shipped
configuration). Bench design: K equal-weight `LinearFunction` children evaluated 2,000,000 times
at K = 2 and 500,000 times at K = 8 over a cycling abscissa set, results consumed into a
checksum; each cost is isolated as a paired difference against a control path with identical
arithmetic.

**A. `CompositeFunction`'s ConditionalWeakTable+lock, per non-deterministic child per evaluation.**
Every evaluation of a non-deterministic child resolves a lock object from a static
`ConditionalWeakTable` and takes it (deterministic children bypass both). Two independent
estimates — (uncertain-children mean path − deterministic-children path) and (locked draw path −
a no-lock save/restore replica) — agree:

| Numerics build | K = 2, mean path | K = 2, draw path | K = 8, mean path | K = 8, draw path |
|---|---:|---:|---:|---:|
| Release | 11.5 ns | 18.8 ns | 16.7 ns | 16.2 ns |
| Debug | 8.9 ns | 41.7 ns | 9.7 ns | 31.4 ns |

Whole-evaluation context (Release, K = 2): direct weighted sum 29.7 ns; composite over
deterministic children 22.3 ns; uncertain mean path 45.2 ns; uncertain draw path 58.8 ns.
TotalRisk's one call site is `CompositeTransform.BuildCombined` (mean-convention branch): the
mean pass wraps mean-sampled curves flagged deterministic and pays nothing; the per-realization
ensemble path wraps uncertain sampled curves flagged non-deterministic and pays ~12–19 ns × K
children per integrand evaluation — single-digit µs per realization at AGK evaluation counts,
minor against the realization budget. The locks are per-child-instance and each realization
wraps freshly sampled children, so engine contention is nil; the static table is the only
cross-thread surface.

**B. `EnsembleFunction.Sample`'s XML parse per draw.** Each `Sample(int)` reconstructs the
template through `UnivariateFunctionFactory.CreateFromXElement(XElement.Parse(_templateXml))`;
the constructor also builds one validation instance per parameter set through the same chain.

| Measurement (Release Numerics) | Time |
|---|---:|
| `Sample(int)` + one evaluation, linear template (142-char XML) | 1,575 ns/draw |
| `XElement.Parse` alone, linear template | 1,146 ns |
| Parse + factory, linear template | 1,592 ns |
| `XElement.Parse` alone, 50-row tabular template (5,183-char XML) | 18,557 ns |
| Parse + factory, 50-row tabular template | 29,930 ns |
| Constructor, 1,000 parameter sets (validation parse per set) | 4.1 ms |

Parse + factory is effectively the entire per-draw cost, and it scales with template size (the
50-row tabular template pays ~19× the linear template; Debug Numerics reads 1,946 ns/draw and
48.5 µs respectively). TotalRisk has no `EnsembleFunction` call site today (it is the
BestFit-import vehicle), so no committed fixture moves on it; a 10,000-draw ensemble over a
tabular-template posterior would pay ~0.3 s of pure reconstruction overhead per function.

**Upstream proposals recorded for the pre-release batch (decision pending; nothing changed
this session):** (1) `CompositeFunction` — resolve each child's lock object once at
construction into a per-instance array, removing the `ConditionalWeakTable.GetValue` from every
evaluation; optionally skip the lock on the configured-state mean path, which mutates no child
state. (2) `EnsembleFunction` — retain the parsed template `XElement` and hand the factory a
`new XElement(template)` deep copy instead of re-parsing per draw, and hoist the constructor's
per-set validation instance out of the loop.

## F8 — the bivariate conditional-bin fixture (2026-08-06)

The conditional-bin engine landing added F8 so the (bins + 1)× evaluation shape has a committed
reference: an independence-copula `BivariateHazard` over uncertain tabular marginals at the
default 20 bins, carrying a Secondary-bound uncertain pool fragility and a joint
`BivariateResponse` surface mode plus a primary background path, N = 1000. Every hazard
evaluation fills the 21 conditional trapezoid nodes from the frozen per-realization snapshot
and runs the failure-mode combination kernels once per node; all sampling stays in the sampled
constructors (the bin loop evaluates already-sampled functions at deterministic points only).
The committed row (isolated invocation, Release, `--reps 3`):

| Fixture | Mean-only median (s) | Full median (s) | Full alloc (GB) | SHA-256 |
|---|---:|---:|---:|---|
| F8 | 2.652 | 76.001 | 38.25 | `5f4d4c397f43f76f551a6d29ea046bb351be4da6befbee2f8059b346c3e450b3` |

The full-run cost sits near F1's pre-C8 scale despite the lean two-mode shape — the expected
conditional-bin factor at 21 nodes per evaluation. The allocation is dominated by the recording
path: the 1D objective records at every accepted evaluation, and a recording bivariate
evaluation stages fresh entry lists per mode and per stream that the committed risk points
adopt (the same allocate-what-the-point-adopts contract as the univariate multi-entry path,
paid once per stream instead of once per bin). Recorded for a future dedicated perf session
per the no-preoptimization rule; the close-out round this session ran all seven prior fixtures
bit-exact alongside this baseline, F1 being the univariate zero-overhead proof for the
`ComputeRisk` dispatch branch.

## F6 re-pin — upstream accuracy corrections reached the bootstrap-MLE surface (2026-08-27)

A close-out byte-gate round found F6 at `74af2e95…` against the recorded `c183d28f…`, with the
other seven fixtures reproducing their pins bit-exactly. A detached-worktree bisect over fixed
engine states isolated the movement to two numerics commits in the pre-session review batch —
`4aab2e9` (product-moment power sums accumulated about a shifted origin) and `4f0d4f0` (relative
accuracy held in the far normal and complement error function tails) — both ratified accuracy
corrections on exactly the bootstrap maximum-likelihood surface F6 alone exercises (the only
fixture that fits distributions; no other fixture crosses those paths). The same session's own
commits were exonerated: the hash is identical immediately before and after them. Magnitude
evidence: `CompositeEngineVerification` 5/5, `CompositeHazardVerification` 11/11 (including the
bit-reproducibility pin), and `CompositeConsequenceVerification` 9/9 against the final DLL — the
movement sits inside every statistical tolerance. The session baseline of 2026-08-27 (numerics
`dc5b17c`, clean tree) reproduces the new hash bit-exactly. Re-pinned with approval 2026-08-27:

- F6 `74af2e959503f4a96f36c78b1dfce96dab591016c46882c3db96d072bcb9525e` (3.12 GB)

## Six-gate re-pin — the 2.2.0 assembly-version stamp in the results manifest (2026-08-27)

A session-start byte-gate round against numerics `8e4f08a` found the six analysis fixtures moved
— F1–F4, F6, and F8 — while the two tree fixtures (F5, F7, which hash plan evaluations rather
than results JSON) reproduced their pins bit-exactly. A detached-worktree bisect attributed the
whole movement to the single release-preparation commit `8e4f08a` (the version metadata bump):
rebuilt one commit earlier at `7d7d6aa`, **all six fixtures reproduce their prior pins
bit-exactly**, exonerating the fourteen substantive upstream commits in between (including the
percentile-cancellation guard, the ordered-paired-data hardening, and the deterministic mean
reductions — all proven bit-inert on the engine surface). The mechanism is the results manifest
telling the truth: an `--dump` payload diff between the two builds contains exactly one changed
field — `NumericsAssemblyVersion` `"2.1.4.0"` → `"2.2.0.0"` in each of the five hashed result
containers — with the analysis content hash and **every numeric byte proven equal**, so no
verification families were run (byte-level identity is stronger evidence than statistical
tolerance). This is the JSON-only re-pin shape: values did not move; the provenance stamp did.
Re-pinned with approval 2026-08-27:

- F1 `b2e6ea8861b3306ff783ad4e9b096a789bbd1bc53941fa237ef84f552f1843af` (3.10 GB)
- F2 `ac35a7fa8e93d542a876ca5b3b17ad673d7a102aeace8e2dba6e2f89d071763a` (12.25 GB)
- F3 `e46763eff9139854b9ba30ecf219c35260da3feda6fa5276e5e30e5bc0560e33` (6.03 GB)
- F4 `8a3a8b522b92aae893902ebc9aa785bbf02efa58de660b5a98219241dd8ea212` (4.62 GB)
- F6 `53be64aac9a7b4f4ae3de042007e6b620818c84ea2157321d958472404fbbaf9` (3.12 GB)
- F8 `7833ad5f9db34f57fbf71c1cd0d3b6adecb2391e903beda8101fd10ed3d01350` (38.26 GB)

## F6 re-pin — the log-family CDF tail stabilization reached the stored bootstrap content (2026-09-02)

A session-start byte-gate round against numerics `2b57771` found F6 at `bd4a26e8…` against the
recorded `53be64aa…`, with the other seven fixtures reproducing their pins bit-exactly. A
detached-worktree bisect over the 43 upstream commits since `fa91884` isolated the movement to
the single commit `99db59a` (log-family lower-tail stabilization: `LnNormal`, `LogNormal`, and
the near-zero-skew `LogPearsonTypeIII` CDF rerouted from `0.5·(1 + erf(z/√2))` onto
`Normal.StandardCDF`, the tail-stable MVNPHI Φ): every commit before it reproduces the prior
pin bit-exactly and every commit from it through HEAD reproduces the new hash, exonerating the
other 42 commits. The mechanism differs from the 2026-08-27 version-stamp re-pin: an `--dump`
payload diff shows the `AnalysisContentHash` itself moved (the effective-options hash is
unchanged) — F6's bootstrap-estimated log-family content shifts at the ulp level under the
stabilized CDF, the stored model content moves, and the content-derived seeds legitimately
re-roll every downstream draw. Magnitude evidence against the final DLL:
`CompositeHazardVerification` 11/11 (including the bit-reproducibility pin),
`CompositeEngineVerification` 5/5, `NfipAssuranceVerification` 7/7 (the direct log-Pearson
surface), and the fast suite 1,225/1,225 — the movement sits inside every statistical
tolerance and breaks no pinned oracle constant. Re-pinned with approval 2026-09-02:

- F6 `bd4a26e80972540f5247effb4521c26b94bfeb8998f99817cf7f352ca13d044b` (3.12 GB)

## F8 re-pin — the two-dimensional adaptive conditional interior (2026-09-05)

The ruled value-moving landing: all bivariate hazards integrate their conditional axis
adaptively (Haden's ruling, 2026-09-05 — `AdaptiveGaussKronrod2D` over (u, z = Φ⁻¹(t)) for the
additive interior, per-slice adaptive sweeps at every fixed-u seat, no compatibility knob; the
fixed conditional trapezoid survives only as the discretization diagnostic's quarantined
instrument). Nothing new is serialized or hashed and no seed moves — **F1–F7 reproduced their
pins bit-exactly in the same close-out round**, F1 remaining the univariate zero-overhead proof
— while F8's recorded values move deliberately with the integration rule and re-pin. Accuracy
evidence at the landing (families isolated): `BivariateRiskVerification` 5/5 — the legacy
seismic SRP probe lands ≈ 3.7e-6 relative from the exact union-grid closed form at BOTH the
default and the 1000-bin configuration (the fixed grid measured 0.353 at the default and
1.98e-3 at 1000 bins), the per-slice sweep reproduces the closed form to ≈ 5.3e-12, and the
default-configuration engine now agrees with the 1000-bin configuration within 0.5% on the
full seismic run (the historical 2.42×/1.35× 20-bin overshoot regime is gone);
`CopulaDependenceVerification` 8/8 — all three convergence-study fixtures at quadrature scale
at every configured budget, the Gumbel/Joe θ = 8 robustness probe within ≈ 5e-12 of dense
closed forms; `EngineReproducibilityVerification` 7/7 — bit-identical repeats, metadata/mode
inertness, thread-count bit-identity, and pinned-seed perturbation all hold on the adaptive
path. The committed row (isolated invocation, Release, `--reps 3`):

| Fixture | Mean-only median (s) | Full median (s) | Full alloc (GB) | SHA-256 |
|---|---:|---:|---:|---|
| F8 | 3.152 | 121.776 | 154.79 | `c9599e0a057ff511020d38a585669a56e8a13c8cf73fbfb4eed147f0ad184e2b` |

Against the 2026-08-06 fixed-grid row (2.652 / 76.001 / 38.25, pin `7833ad5f…` retained above
as audit trail): mean-only +19%, full-run +60%, allocations 4.0×. The wall cost buys the
accuracy table above — the fixed default was 35% wrong on the seismic class this fixture
represents. The allocation growth rides the committed structure: the adaptive interior commits
more distinct primary abscissas per realization than the fixed grid's evaluation set, and each
committed point stages and adopts its entry lists (the allocate-what-the-point-adopts
contract); the surrogate evaluation path itself is allocation-free (reused per-type scratch
buffers, cached slice state). Recorded for the future dedicated perf session per the
no-preoptimization rule: candidate reductions are pooling the per-abscissa staging objects and
reusing tensor-region storage upstream. The single-rep hash reproduced the `--reps 3` hash
bit-exactly.

## F6 movement note — upstream distribution repairs, session-local target, regression repaired (2026-09-09)

A session-start byte-gate round against the upstream distribution-hardening delta (numerics
`94d1713..62424a2`; the `bug-fixes-and-enhancements` branch) found F6 at
`1549355363204c1adb057f364d64e54e5056e2d5b5641d05158bfd7785ff8203` against the recorded
`bd4a26e8…`, with the other seven fixtures reproducing their pins bit-exactly. A
detached-clone DLL swap attributed the movement to the single upstream commit `b8bf912`
("Repair distribution tails, moments, and uncertainty"): DLLs built at `94d1713` and at
`d80bfa8` (the commit before it) reproduce the prior pin bit-exactly, and every state from
`b8bf912` forward reproduces the new hash stably. The mechanism is the `Mixture` rework F6
alone exercises (log-sum-exp CDF routing with repaired tails); magnitude evidence against
the moved DLL: `CompositeHazardVerification` 11/11, `CompositeEngineVerification` 5/5, and
the fast suite 1,589 + 73 — inside every statistical tolerance, no pinned oracle breaks.
Ruled session-local (Haden Smith, 2026-09-09): the working target is `15493553…` and the
table above deliberately retains `bd4a26e8…` until the formal re-pin.

The same round exposed a severe upstream allocation regression on the F6 path — 52.95 GB
and ~15–16 s full-MC against the recorded 3.12 GB / ~5.7 s — root-caused to per-call
canonical-configuration serialization (`Mixture.InverseCDF` rebuilt and discarded a
recursive per-component XML string on every call: 62,512 B and 11.6 µs per call measured)
plus per-call allocating validation. The repair campaign on the numerics branch (commits
`1b90450..7a80e35`: the generalized bitwise configuration snapshot, the validation
certificate with certified support bounds, the single-walk quantile path, and the
static log-Pearson routing) recovered and improved the fixture with the hash unchanged:

| F6 | Full-MC (s) | Full alloc (GB) | SHA-256 |
|---|---:|---:|---|
| Recorded pre-hardening row | ~5.7 | 3.12 | `bd4a26e8…` |
| Regressed (numerics `b8bf912..62424a2`) | 15.3–16.8 | 52.95–56.08 | `15493553…` |
| Repaired (numerics `7a80e35`) | 4.90 | 3.11 | `15493553…` |

Mixture `InverseCDF` per call (Debug assembly, the linked-DLL discipline): pre-hardening
144 B / 0.97 µs; regressed 62,512 B / 11.1 µs; repaired 96 B / 0.79 µs — better than the
pre-hardening baseline on both axes. The formal F6 re-pin to `15493553…` (with a fresh
`--reps 3` row) is a ruling for Haden Smith on this settled state.
