# Risk Profile Verification

**Test class:** `RiskProfileVerification` · **Tests:** 5 · **Run of record:** 2026-07-24, isolated run, ✅ all passed

The family for the per-component profile hazard selection (a **seed-inert** element
reference whose transform chain remaps every recorded hazard coordinate onto the selected axis)
and the expanded risk-profile catalog: the **Cumulative Failure Probability by Hazard**
(the ascending cumulate of recorded probability mass — the honest form of the plot practice
mislabels "cumulative APF"; its terminal ordinate is the annualized failure probability and the
curve is the distribution of the failure-causing hazard, NOT the failure probability at a
hazard level), the **Cumulative Expected Annual Consequence by Hazard** (terminal ≡ the stream
mean) on all five streams and every consequence type, and the **System Response Probability
profile** plotted against annual exceedance probability (the normalized, transform-independent
axis — a hazard-axis response profile is ill-posed when failure modes respond to different
transformed signals; this profile is deliberately never remapped by the profile selector). The
same design restored the v1.0 five-stream profile banding scope (the Total-only banding was a
parity gap) and added mode-scope profiles on the mean pass.

## Scenarios

**R — remap (deterministic):** flow frequency (0.999 → 0 cfs, 0.5 → 50,000 cfs, 0.001 →
100,000 cfs) → Flow→Stage rating T(q) = q/200 → stage fragility (250 → 0, 450 → 1) → stage
damages (0 → 0, 500 → 1,000), with a transform-free non-failure path. The profile element is
the rating.

**C — catalog (deterministic two-mode joint):** stage frequency from Normal(100, 20) quantiles
on a 65-knot z-grid (±8, step 0.25); fragility A (100 → 0, 180 → 1), fragility B
(120 → 0, 200 → 1); damages c_A (60 → 0, 200 → 1,000), c_B (60 → 0, 200 → 2,000); non-failure
(60 → 0, 200 → 100); `JointFailures`, independent capacities, **Additive** consequence rule.
The full-uncertainty variant carries a triangular-ordinate fragility A at 200 LHS realizations.

## Oracle

An independent dense-trapezoid quadrature (200,001 ordinates over non-exceedance
u ∈ [1e-16, 1 − 1e-16]) over the oracle's **own** linear interpolators of the same tables —
no engine code in the oracle path. The Additive rule's linearity gives the failure mean in
closed integrand form, Σ pathways·c = p_A·c_A + p_B·c_B; the union is 1 − (1−p_A)(1−p_B); the
per-knot response and exceedance coordinates are exact interpolation chains on both sides.

## Checks

| # | Test | Verifies | Tolerance | Result |
|---|---|---|---|---|
| 1 | `Test_ProfileRemap_PushforwardExactAtKnots` | Every profile hazard ordinate is the exact rating pushforward T(h) of the raw-axis run; every profile Y array and every non-profile scalar (APF, mean, σ, CVaR) is **bit-identical**; the response profile's exceedance axis is untouched by the remap | 1e-9 relative on the pushforward; 0 (bit) elsewhere | ✅ |
| 2 | `Test_ProfileRemap_HazardThreshold_KnotEquivalence` | A hazard threshold at a recorded raw knot h reads the same probability as the profile-axis threshold at T(h) — the selector re-expresses the threshold without changing its meaning at knots (between knots the reads differ only by log-log segment curvature, documented) | 1e-9 relative | ✅ |
| 3 | `Test_ProfileCatalog_TwoModeJoint_VsQuadratureOracle` | Terminal identities (A-terminal ≡ Fail `MassBalance`, B-terminal ≡ stream `Mean` — exact bookkeeping); terminals vs the oracle integrals; interior cumulative ordinates at the quartiles vs partial oracle integrals; per-knot response ordinates ≡ the exact union and exceedance coordinates | 1e-12 rel (identities); 1e-4 rel (oracle terminals — the quadrature mass-accounting residual envelope); 2e-3 of terminal (interior — the partial-sum discretization allowance); 1e-9 rel (per-knot response) | ✅ |
| 4 | `Test_ProfileCatalog_ReliabilityMode_CumulativePresent` | Reliability mode builds the cumulative failure profile (there it is the headline profile) with terminal ≡ the reported annualized failure probability, plus the response profile | 1e-12 relative | ✅ |
| 5 | `Test_ProfileCatalog_FullUncertainty_FiveStreamBands_AndReproducibility` | All four band trees carry five-stream frequency + cumulative-consequence profiles (the v1.0 parity restoration); Lower ≤ Median ≤ Upper ordering and hazard monotonicity on the banded cumulate; the response band rides the log exceedance grid in [0, 1]; repeated runs reproduce the new arrays bit-for-bit | ordering/bit asserts | ✅ |

## Tolerance derivations

- **Identity asserts (1e-12 relative):** the ascending cumulate's terminal and the stored
  `MassBalance`/`Mean` sum the same recorded values in different orders — pure floating-point
  association, no statistics.
- **Oracle terminals (1e-4 relative):** the quadrature mass-accounting residual envelope
  (measured ≈ 3e-6 on means in the EAD family under the earlier midpoint-trapezoid partition,
  since replaced by the recorded-mass ledger — the bound holds a fortiori) plus the
  dense-trapezoid oracle's own O(h²) error (≈ 1e-9 at 200,001 ordinates).
- **Interior cumulative probes (2e-3 of the terminal):** the engine's ascending cumulate at
  recorded knot j is a partial sum of the recorded per-abscissa masses, representing the
  integral only to within the local inter-node spacing — a half-interval discretization
  allowance at the mean pass's recorded density (~10³ evaluations).
- **Per-knot response (1e-9 relative):** both sides are the same exact interpolation chain
  (engine tables vs the oracle's re-implementation) — agreement to floating-point association.

## Engine-inertness gates

The no-profile default is a single null check: fixture F1's results-JSON SHA-256 reproduced the
then-current byte-gate hash `b88a49f3…` **exactly** when the selector landed, and the
`NfipAssuranceVerification` (5/5) and `EngineReproducibilityVerification` (3/3) families passed
unchanged as the landing gates. The catalog's new serialized arrays are a documented byte-gate
re-pin (`scripts/perf/RESULTS.md`); `EngineReproducibilityVerification` re-ran green (3/3) on
the extended payload. The `b88a49f3…` hash above is that landing's record, not a live pin — the
current F1 hash is in `scripts/perf/RESULTS.md` (re-pinned for the quadrature mass
ledger). Run of record 2026-07-24: `RiskProfileVerification` 5/5 passed
(~22 s wall); the fast suite at that date passed in full (447/447, Debug and Release).
