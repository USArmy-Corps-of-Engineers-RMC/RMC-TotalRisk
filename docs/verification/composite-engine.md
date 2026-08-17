# Composite engine scenarios

**Test class:** `CompositeEngineVerification` · **Tests:** 5 · **Run of record:** 2026-07-27, isolated run, ✅ all passed

This family closes the four executable configurations in legacy `Test_Composite.vb` and the
composite-mixture consistency body in `Test_RiskAnalysis.vb`. Unlike the function-level
composite families ([composite-hazard](composite-hazard.md),
[composite-response](composite-response.md),
[composite-consequence](composite-consequence.md)), these tests place the composite behind a
complete `RiskAnalysis` graph — hazard → response → consequence — and assert the engine's
unconditional Total-stream mean.

## Fixtures (the legacy configurations)

Every scenario reuses the legacy `Test_Composite.vb` building blocks, constructed as typed
v1.1 model objects:

- **Hazards** — deterministic `ParametricUnivariateHazard` over natural-log lognormals:
  `LnNormal(85, 20)` for the single-hazard scenarios; the two-branch pair `LnNormal(85, 5)` /
  `LnNormal(65, 20)` for the composite-hazard scenarios.
- **Fragilities** — deterministic `ParametricResponse` over Normal capacities:
  `Normal(140, 30)`, plus `Normal(160, 10)` as the second composite-response child.
- **Consequences** — the legacy five-point piecewise-linear loss table
  x = {60, 100, 140, 200, 250}, y = {0, 10, 100, 1000, 1500}; the night child scales the
  ordinates by 0.5. The day weight is the legacy w = 0.45 (night carries the complement).
- **Uncertain variant** — each positive loss ordinate carries symmetric
  `Triangular(0.8v, v, 1.2v)` knowledge uncertainty (mean exactly v, so the ensemble centers
  on the deterministic answer).

## Oracle

The legacy 10M Monte Carlo workbench is replaced by a stronger independent reference: a fixed
256-interval × Gauss–Legendre-20 compensated-summation quadrature of
∫ P(F|h(p)) · C(h(p)) dp over hazard non-exceedance probability, assembled directly from the
Numerics distributions and the legacy loss table. The oracle never calls the engine. The
deterministic scenarios run mean-only and carry **no Monte Carlo seed or SE** — engine-versus-
oracle differences are purely the two deterministic pipelines' discretization allowances.

## Tests, tolerances, and derivations

| Current test | Legacy configuration | Independent reference | Tolerance (derivation) |
|---|---|---|---|
| `Test_CompositeConsequence_EngineVsIndependentQuadrature` | Day/night weighted consequence (Mixture, w = 0.45) | Quadrature of the analytically collapsed mixture (0.45 + 0.5·0.55)·C(h) | 2e-4 relative — deterministic allowance covering the engine's adaptive-quadrature tolerance and loss-exceedance output thinning; no Monte Carlo term exists |
| `Test_CompositeConsequenceBootstrap_FullEngineReproducibleAndCentered` | Uncertain day/night consequence | Full 100-realization ensemble engine run, twice | Repeated-run JSON bit-identity; 3% relative centering of the ensemble mean on the deterministic symmetric-triangular answer (100-realization noise band) |
| `Test_CompositeHazard_EngineVsIndependentQuadrature` | Weighted lognormal hazards (Mixture, w = 0.45) | The w-weighted sum of the two single-hazard quadratures | 2e-3 relative — the composite hazard represents the analytic mixture on its established empirical grid before the engine integrates it (the `Mixture.CreateEmpiricalCDF()` ~200-bin interpolation, ≈3e-3 of exceedance probability on inversions); stays below the verification report's 1% "very good" criterion |
| `Test_CompositeResponse_EngineVsIndependentQuadrature` | Weighted response functions (Mixture, w = 0.45) | Quadrature of the pointwise-weighted fragility 0.45·F₁(h) + 0.55·F₂(h) | 2e-4 relative — same deterministic derivation as the consequence check |
| `Test_CompositeHazard_MixtureIdentityBehindEngine` | Risk-analysis mixture consistency (the legacy `Test_RiskAnalysis` composite workbench) | The w-weighted means of the two child-engine runs (engine versus engine) | 2e-3 relative — both sides carry the composite hazard's empirical-grid allowance |

These are verification assertions only and do not change an engine default, integration
formula, seed, or production tolerance.

The exhaustive source disposition is maintained in
[legacy-traceability.csv](legacy-traceability.csv).
