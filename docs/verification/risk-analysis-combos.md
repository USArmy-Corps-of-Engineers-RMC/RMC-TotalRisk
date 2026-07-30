# Risk-Analysis Combos

**Test class:** `RiskAnalysisCombosVerification` · **Tests:** 5 · **Run of record:** 2026-07-23, isolated run, ✅ all passed

The conversion of the legacy `Test_RiskAnalysis` N-element/N-failure-mode combination
oracles — the scenarios the other converted families do not already cover: the 3- and 4-PFM
single-component joint groups (between the joint-failures 2- and 5-PFM families), the 3- and
4-component system groups (between the system matrix's 2- and 5-component families), and the
suite's only negative-correlation-matrix scenario. Every method in the legacy class is
accounted for below.

## Converted scenarios

| Test | Legacy source (body) | Model | Seeds / N |
|---|---|---|---|
| `Test_1Comp3Pfm_PerfectlyNegative_AllRules_VsOracle` | `Test_1Element_3PFM` (line 481) | 1 component, 3 joint PFMs, perfectly negative (r = −1/2 + √ε), all four rules consolidated from the legacy additive draws | MT(12345) hazards + MVN(12345) capacities, N = 10⁶ (native) |
| `Test_1Comp4Pfm_PerfectlyNegative_AllRules_VsOracle` | `Test_1Element_4PFM` (582) | 1 component, 4 joint PFMs, perfectly negative (r = −1/3 + √ε), all four rules | same, N = 10⁶ (native) |
| `Test_3Comp1Pfm_PerfectlyNegative_VsOracle` | `Test_3Element_1PFM` (1182) | 3 components (PFM-1/2/3), perfectly negative hazards, additive + average rules | MVN(12345) hazards + MT(12345) capacities, N = 10⁶ (native) |
| `Test_4Comp1Pfm_PerfectlyNegative_VsOracle` | `Test_4Element_1PFM` (1290) | 4 components (PFM-1..4), perfectly negative hazards, additive + maximum rules | same |
| `Test_2Comp_NegativeQuarterCorrelation_Average_VsOracle` | `Test_5Element_1PFM_2` (1743) | 2 components under a NEGATIVE user correlation matrix (r = −0.25), average rule — the mislabeled body drives only the first two columns of its 5-dimensional equicorrelated draw, so the realized pair correlation is −1/4 | MVN(12345, 5-dim) + MT(12345) columns 0–1, N = 10⁶ (native) |

Single-component groups assert the full single-component catalog against the consolidated
oracle (five means, union + complement, σ, conditional mean, assurance, two curve probes, VaR,
CVaR — the engine path is deterministic). System groups assert the joint-method
catalog (five means with the reported VEGAS error, union and probes at combined binomial
errors, mass balance, and the additive-rule component-mean identity — exact at D = 2,
bounded by the documented `IndependentExclusive` enumeration tolerance above).

**Documented deviations:** (1) legacy negative equicorrelations use ε_mach offsets; the port
uses the engine's √ε constants (an approved deviation class, statistically indistinguishable
and Cholesky-stable). (2) The 3-/4-element
legacy bodies accumulate the RUNNING failure total into the increment (`iC += fC − nfC(j)` —
a typo the 2- and 5-element bodies do not have); the port uses the per-component excess
convention every other legacy system body and the engine use. (3) The r = −0.25 engine matrix
is the exact −0.25 (the oracle realizes −0.25 + √ε — identical to floating precision at the
assert tolerances).

## Disposition of every legacy `Test_RiskAnalysis` method

| Legacy method | Disposition |
|---|---|
| `Test_1Element_3PFM`, `Test_1Element_4PFM` | Converted here |
| `Test_3Element_1PFM`, `Test_4Element_1PFM` | Converted here (increment typo corrected) |
| `Test_5Element_1PFM_2` | Converted here (as the 2-component r = −0.25 average scenario its body computes) |
| `Test_1Element_2PFM` (negative/minimum body) | Stream-identical duplicate of a `JointFailuresVerification` scenario (same seeds 12345/12345, same tables) — not re-ported |
| `Test_1Element_5PFM` (independent/additive body) | Stream-identical duplicate of the joint-failures 5-PFM independent group — not re-ported |
| `Test_2Element_2PFM` | Byte-for-byte the `Test_MC_SystemRisk` 2-comp/2-PFM independent additive body (seeds 78910/12345/45678) — covered by `SystemRiskMatrixVerification` |
| `Test_2Element_1PFM`, `Test_2Element_1PFM_New`, `Test_5Element_1PFM` | The same scenarios as the system matrix's 2-comp independent additive / independent minimum / 5-comp negative additive groups (the last at identical seeds; the first two at alternate seed layouts of the same model) — covered by `SystemRiskMatrixVerification` |
| `Test_1Element_2PFM_Adaptive`, `Test_5Element_1PFM_Adaptive`, the `TotalRisk_*_Sum` functions | Inert integration workbenches (mostly commented out, debugger-print only, some referencing the legacy engine's own types) — not oracles; the exact conditional-mean integrand they exercised is pinned by the mean-parity and consistency families |
| `Test_Composite`, `Test_Composite_Uncertainty`, `Test_Composite_Consequence_Mixture` | Covered by `CompositeEngineVerification`, `CompositeHazardVerification`, and `CompositeConsequenceVerification` |
| `Test_EAD` | Converted in `EadVerification` |
| `Test_NFIP_Assurance_TOL_50/55/70` | Converted in `NfipAssuranceVerification` |
| `Test_NFIP_Assurance_TOL_60/65` | Covered in `NfipAssuranceVerification` using the active workbench ramp/21-knot fragility, source-ordered bootstrap seeds, and independent conditional quadrature |
| `Test_NFIP_Assurance_TOL_65_FDA` | Obsolete by technical-authority decision; intentionally not ported |

## Results

| Scenario (oracle / engine) | Fail mean | Total mean | Union (AFP) | Notes |
|---|---|---|---|---|
| 3-PFM PerfectlyNegative Additive | 5.29205 / 5.31197 | 6.15090 / 6.17268 | 0.078628 / 0.078810 | σF 51.30/51.92, VaR 93.53/93.18, CVaR 370.5/372.7 |
| 3-PFM PerfectlyNegative Minimum | 3.03335 / 3.03619 | 3.89220 / 3.89690 | 0.078628 / 0.078810 | consolidated from the legacy additive draws |
| 4-PFM PerfectlyNegative Additive | 6.44933 / 6.46115 | 7.04989 / 7.06277 | 0.191978 / 0.191930 | σF 57.46/57.90, CVaR 420.5/422.0 |
| 4-PFM PerfectlyNegative Average | 3.50338 / 3.50438 | 4.10393 / 4.10599 | 0.191978 / 0.191930 | consolidated |
| 3C1P PerfectlyNegative Additive (joint) | 5.27575 / 5.31600 | 8.71754 / 8.75692 | 0.085375 / 0.085339 | D = 3 VEGAS |
| 4C1P PerfectlyNegative Additive (joint) | 6.46241 / 6.46965 | 10.7502 / 10.7598 | 0.209401 / 0.209780 | D = 4 VEGAS |
| 2C1P r = −0.25 Average (joint) | 2.61557 / 2.60211 | 3.78471 / 3.77095 | 0.069205 / 0.069307 | the negative-correlation-matrix scenario |

Single-component groups (deterministic engine path) sit within ≈ 0.4% of their oracles on the
means and ≈ 1.2% on dispersion/tail measures — every assert within its documented k·SE. The
system groups' joint-path means sit within ≈ 0.8%, unions within 0.2%.

## Notes

The 3/4-PFM and 3/4-component counts close the dimension ladder: with the joint-failures 2/5-PFM
families and the system matrix's 2/5-component families, every failure-mode count and
component count from 2 through 5 now carries an engine-versus-oracle anchor, and the joint
VEGAS path is exercised at D = 2, 3, 4, and 5.
