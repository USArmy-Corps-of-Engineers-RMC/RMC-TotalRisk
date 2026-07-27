# Composite engine scenarios

**Test class:** `CompositeEngineVerification` · **Status:** Verified

This family closes the four executable configurations in legacy `Test_Composite.vb` and the
composite-mixture consistency body in `Test_RiskAnalysis.vb`. Unlike the function-level
composite families, these tests place the composite behind a complete `RiskAnalysis` graph.

| Current test | Legacy configuration | Independent reference |
|---|---|---|
| `Test_CompositeConsequence_EngineVsIndependentQuadrature` | Day/night weighted consequence | Fixed 256 × Gauss–Legendre-20 integration of the legacy loss table |
| `Test_CompositeConsequenceBootstrap_FullEngineReproducibleAndCentered` | Uncertain day/night consequence | Repeated-run bit identity and independently derived centering |
| `Test_CompositeHazard_EngineVsIndependentQuadrature` | Weighted lognormal hazards | Fixed Gauss–Legendre integration of the analytic mixture |
| `Test_CompositeResponse_EngineVsIndependentQuadrature` | Weighted response functions | Fixed Gauss–Legendre integration of the weighted fragility |
| `Test_CompositeHazard_MixtureIdentityBehindEngine` | Risk-analysis mixture consistency | Weighted means of the two child-engine runs |

The consequence and response assertions use a 2e-4 relative deterministic allowance. Composite
hazards use 0.2% because the established hazard implementation represents the analytic mixture on
an empirical grid before the engine integrates it; 0.2% remains below the verification report's
1% “very good” criterion. These are verification assertions only and do not change an engine
default, integration formula, seed, or production tolerance.

The exhaustive source disposition is maintained in
[legacy-traceability.csv](legacy-traceability.csv).
