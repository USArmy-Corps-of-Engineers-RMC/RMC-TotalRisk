# Extrapolation Policy Verification

**Test class:** `ExtrapolationVerification` · **Tests:** 5 · **Run of record:** 2026-08-28, isolated run, ✅ all passed

The per-function extrapolation policy extends a tabular chain's boundary segments linearly in
the configured transform spaces — or refuses out-of-range forward evaluation loudly under the
Error mode — while the default reproduces the historical endpoint hold bit-for-bit
([hazard-functions](../technical-reference/hazard-functions.md#extrapolation-policy)). This
family proves all three behaviors behind the full engine: the extended chain against an
independent quadrature oracle built from Numerics primitives only, the default's byte identity
at engine scale, the Error refusal surfacing through the integrators' exception absorption with
its full diagnostic, and the deliberate hash/seed/result movement of a configured policy.

## Scenario

A deterministic stage-frequency curve on the normal-Z probability axis — non-exceedance z
piecewise linear through (0 ft, Φ⁻¹(0.001)), (10 ft, 0), (30 ft, Φ⁻¹(0.999)) — a non-saturating
fragility (10 ft → 0.1, 20 ft → 0.8, linear probability axis), and a linear consequence
(0 ft → $0, 30 ft → $1000). Every extension is a hand-computed line: the hazard extends linearly
in z (its inverse tails widen the integration domain to the engine's 10⁻¹⁶ probability floors),
the fragility extends linearly in probability and clamps to [0, 1] (crossing 1 at 22.857 ft and
0 at 8.571 ft), and the consequence extends linearly with the negative tail clamped at zero.
The movement pins replace the deterministic consequence's top ordinate with Normal(1000, 100)
at N = 200 realizations.

## Oracle derivation

The oracle re-implements the extended chain from Numerics primitives only — the
piecewise-linear z(x) map and its inverse, the clamped extended fragility and consequence
lines — and integrates E = ∫ SRP(x(u))·C(x(u)) du by the trapezoid rule on a uniform
two-million-interval grid over [10⁻¹⁶, 1 − 10⁻¹⁶], plus the two endpoint rectangles of the
engine's exhaustive-mass construction. The integrand is bounded and piecewise smooth, so the
oracle's discretization error sits far below the asserted tolerance, which in turn dominates
the engine's 10⁻⁸ integration tolerance.

## Checks

| # | Test | Verifies | Tolerance | Result |
|---|---|---|---|---|
| 1 | `Test_ExtendedChain_MeanOnly_VsOracle` | Mean-only EAD and AFP against the oracle under both the endpoint hold and the extended chain; the engine agrees with the oracle on the movement's direction and the movement is material | 1e-5 relative (oracle-discretization dominated); direction and materiality exact | ✅ |
| 2 | `Test_ResponseClamp_ExtendedTails` | The [0, 1] response clamp on the extended tails: the AFP equals the oracle split at the clamp crossings, where the region above 22.857 ft contributes exactly its hazard mass | 1e-5 relative | ✅ |
| 3 | `Test_DefaultNone_ByteIdentical_EngineScale` | A never-configured model and a set-to-Both-then-cleared-to-None model publish byte-identical results JSON, mean-only and at ensemble scale (seeds, draws, every realization) | 0 (byte) | ✅ |
| 4 | `Test_ErrorMode_LoudSurfacing` | An Error-mode fragility narrower than the hazard domain stops the run with the integration-failure message carrying the function name and table range through the integrator's exception absorption | exact (message content) | ✅ |
| 5 | `Test_PolicyEdit_MovesHashSeedsAndResults` | A configured policy moves the analysis content hash, re-rolls the owning function's sampling stream (the normalized first-realization draw moves), and lands both ensemble means on their oracles | hash/stream exact; means at 4·σ/√N with σ = E·0.1 | ✅ |

A noteworthy measurement from check 1: on this chain the extension **reduces** risk — the held
fragility keeps its 0.1 plateau below the table while the extension descends through zero at
8.571 ft, shedding more low-stage mass than the upper tail gains. Extension is a modeling
statement, not a conservatism knob; the oracle carries the sign and the engine agrees.

Unit-level companions (fast suite): the enum member/value pins and the Error→None mapping pin;
per-type conditional-presence serialization (absent by default, byte-inert set-then-clear,
present by name, faithful round trips), hash-movement pins, sampled-wrapper side wiring on the
mean and percentile products, the widened inverse tails, the Error guards' diagnostics and
forward-only rule, the fault-scope capture/consume semantics, and the upstream wrappers' own
default-None bit pins, sided extensions, clamps, descending-orientation mapping, and
serialization (Numerics `Test_Functions`, `Test_EmpiricalDistribution`, and the
Mixture/CompetingRisks/KernelDensity no-regression pins).

Run of record 2026-08-28: `ExtrapolationVerification` 5/5 passed (7.3 s wall — four mean-only
runs, four N = 200 ensembles, and five two-million-node oracle integrations), isolated
invocation `dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~ExtrapolationVerification"`.
