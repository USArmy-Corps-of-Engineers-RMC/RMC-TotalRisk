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

Baseline byte-gate hashes:

- F1 `7a88638cf38c5ee38a3091fabd933e747dd9dc43ea14d44b0addbb465c12f500`
- F2 `dbc8dd66faf06c8de4ee434bf9bb04d321b812e5d26b3db1ae899ee72e34ec80`
- F3 `06a1456315cc3a6458850e1a22611fc72dc7529a188b36c5efa073169fb0d785`
