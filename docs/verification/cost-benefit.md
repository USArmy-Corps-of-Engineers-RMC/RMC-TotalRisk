# Cost-benefit analysis

**Test class:** `CostBenefitVerification` · **Tests:** 26 · **Run of record:** 2026-09-10, isolated run, ✅ all passed

## Scope and status

The greenfield family for the cost-benefit study's kernel, model, parity, and
decision-framework surfaces: the v1.0 plan-economics reference constants, the cost-stream
present-value oracle, the null study's exact zeros with the frontier-tie and do-no-harm
closures, the stationary bridge onto hand closed forms with the configuration-query
cross-check and the total-expected-annual-cost identity, the two-epoch benefit closed form
pairing capital and benefit discounting, grid-refinement inertness with shared-grid
deterioration re-aging, the monetization identities with the economic/monetized aggregate
split, the Appendix L cost-per-life-saved family with the basis-invariance lemma, the
equity-weighted/failure-prevention/absorbing ratios against independent survival arithmetic,
the closed-form Haimes ε-constraint table and the conditional-tail discrimination table on
hand matrices, the tolerable-life-risk template end to end, the frontier and
incremental-analysis hand set with the trade-off unification, the multi-criteria
arithmetic, the study-level re-measurement against a directly-configured twin, the
do-no-harm screen policies, and the reliability-mode study subset — plus the
decision-strategy catalog (the `CostBenefitVerification.Strategies` partial): the
shared-state Savage regret machinery over genuinely enumerated ensembles with the
epistemic-alternative refusal pinned, the classical epistemic rule picks against
independent arithmetic, the exact epistemic tail averages, the stochastic-dominance
verdicts on analytic curve and sample pairs, the expected-utility closed forms with the
partition-to-CVaR bit pin, the chance-constraint parity with the published tolerable-risk
confidence, the designed decision-summary disagreement, study author-inertness with
run-to-run bit reproducibility of every strategy block, and the strategy validation and
diagnostic sweep.

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~CostBenefitVerification"
```

Observed 2026-09-10: **26/26 passed** (≈ 6 s).

## The oracles

Every quantification is mean-only on deterministic fixtures, so trajectory evaluations are
bit-reproducible and the per-alternative deltas are exact twin differences. The flat model
drives the OR(AND(house, 0.375), 0.2) fault tree — failure probability exactly 0.2 with the
house event false and 0.5 with it true — through a flat failure consequence of 1000 dollars
over a flat non-failure background of 100, so every stream mean is closed-form (Fail =
1000p, Total = 1000p + 100(1 − p), Excess = 900p); the life-safety variant adds flat life
loss (failure 0.05, background 0.005 lives). Studies designate the broken condition as the
baseline and repair it through a plan intervention on the shared system instance. Oracles
re-implement independently: value-array loops with power-form discounting and the capital
recovery factor for plan economics; per-year `Math.Pow` sums that never touch the annuity
forms for cost streams; power-form annuity segments and per-year survival loops on the
closed-form means for benefits; recompositions from the published trajectory arrays for
the monetized aggregates.

| Test | Independent expectation | Result |
|---|---|---|
| `Test_PlanEconomics_V10ParityConstants` | The five recorded v1.0 reference constants — 134.970911441500 (ramp-and-plateau), 108.287007110536 (silent beyond-horizon truncation), 160 (constant stream when the future year does not exceed the base year), 119.497368419047 (the 7%/30-year planning-default ramp), and 137.5 (the exact zero-rate average) — at 1e-9 absolute, each re-derived in-test at 1e-12 relative | ✅ within documented tolerances |
| `Test_CostStream_PresentValueOracle` | Capital {0: 1000, 10: 500, 20: −200}, operations 10/yr over (0, 50], operating −5/yr over (10, 50] at 3.5% — the study's per-kind present values against per-year power sums (1e-12 rel), the equivalent-annual identity, and the undiscounted cumulative | ✅ within documented tolerances |
| `Test_NullStudy_ExactZeros` | A costless, planless twin of the baseline shares one trajectory evaluation (reference-equal) and publishes exact zeros in every reduction row and signed reduction column, with an undefined (NaN) benefit-cost ratio at zero cost; the default objective vector ties exactly so the weak-dominance screen keeps both rows, doing nothing passes the do-no-harm screen with no offending types, and the undeclared life-safety axis leaves the whole cost-per-life-saved family NaN | ✅ exact |
| `Test_StationaryBridge_ClosedFormAndConfigurationParity` | The year-zero repair's monetized benefit is 270·A(20) on the closed-form means 550 and 280 (1e-9 rel), net present value and benefit-cost ratio follow by hand, the year-zero epoch entries agree with `MeasureConfigurationRisk` with no delta — both queries run the same clone machinery — and the total expected annual cost is 1000/A(20) + 280 against the costless baseline's 550, with the TEAC difference equal to −NPV/A(20) (the minimize-TEAC ≡ maximize-net-benefits identity, 1e-9 rel) | ✅ within documented tolerances |
| `Test_TwoEpochBenefit_ClosedFormConventionPairing` | A year-ten repair pairs capital at (1 + r)^−10 (1e-12 rel) with benefits whose first term is (1 + r)^−11: the per-year power-form loop over years 11–20 equals the annuity-segment form Δm·(A(20) − A(10)) (1e-12 rel) and the published benefit (1e-9 rel, recomposed from the trajectories with no delta); the absorbing benefit equals differenced per-year survival loops (1e-9 rel) | ✅ within documented tolerances |
| `Test_GridAlignment_TelescopingAndSharedReAging` | Refining a deterioration-free study's epoch grid re-associates the same telescoping sums — measured coarse-versus-fine differences of a few units in the last place (4.6e-13 absolute at magnitude 115, ≈ 4e-15 relative), pinned at 1e-13 relative — while a deteriorating baseline re-ages at the repair alternative's plan year purely because the grid is shared, strictly moving its aggregate against a standalone coarse evaluation | ✅ within documented tolerances |
| `Test_Monetization_SplitIdentities` | Life-only monetization prices exactly V·(discounted lives saved) with an exactly zero economic aggregate; adding the identity-priced dollar type recomposes the monetized aggregate from both published reductions with no delta; an empty monetized set publishes NaN monetary aggregates with the advisory warning; the lives-saved column reads the Excess stream (0.045·Δp equivalent-annual lives, 1e-9 rel) | ✅ within documented tolerances |
| `Test_ApplLFamily_HandValuesAndBasisInvariance` | The Appendix L hand values — annualized cost 120, economic reduction 30, operating reduction 10, life-loss reduction 0.004 — give the unadjusted ratio exactly 30,000 and the adjusted exactly 20,000; the negative-numerator proviso clamps to exactly zero; a non-positive life-loss reduction is NaN; and the present-value and equivalent-annual quotients agree bit-exactly under a power-of-two annuity surrogate (scaling is an exact exponent shift, so A(T) cancels and the clamp commutes) and at 1e-15 rel under a real annuity factor (each scaled term rounds once) | ✅ exact / bit-exact (surrogate) |
| `Test_EwacslsCsfpAacsls_Arithmetic` | The equity weight (max(r_base, IRL)/max(r_alt, IRL))^n with the floor engaging on the alternative side only, swept over n ∈ {0.5, 1, 2} against independent `Math.Pow` (1e-12 rel); behind the year-ten repair, the survival-equivalent annualized probabilities against an independent survival computation (1e-9 rel — the published probabilities inherit the flat-quadrature bound); the failure-prevention and equity-weighted columns recomposing from published values with no delta; and the absorbing adjusted ratio against independent per-year survival loops on the closed-form means (1e-9 rel) | ✅ within documented tolerances |
| `Test_EpsilonSweep_ThesisTableB1` | Ten alternatives at the analytic noninferior points of min f₁ = (x₁−2)² + (x₂−4)² + 5 s.t. f₂ = (x₁−6)² + (x₂−10)² + 6 ≤ ε (x₁(λ) = (2+6λ)/(1+λ), x₂(λ) = (4+10λ)/(1+λ), λ = √(52/(ε−6)) − 1) on the uniform ε grid 6→58: the per-ε selections walk the ten points in order, the noninferior set is the whole frontier, each adjacent trade-off ratio equals the same-order secant with no delta and lies between the analytic tangent multipliers at its endpoints, and the analytic multiplier at ε = 13.31 is 1.667 (the recorded upstream augmented-Lagrange anchor) | ✅ exact |
| `Test_CvarDiscrimination_ThesisTable21` | Four options with identical expected life loss and conditional tails {30, 67, 149, 577} on a descending cost axis {40, 30, 20, 10}: the expected-value screen keeps every option (ties never resolve), adding the tail axis collapses it to the lowest tail, the cost-tail pair is a complete frontier, and the tolerable tail limit of 100 admits options one and two with least cost selecting option two (the bound binding) | ✅ exact |
| `Test_TolerableLifeRiskTemplate_EndToEnd` | The template study (minimize total expected annual cost s.t. Excess annual life loss ≤ 0.01 in every epoch, conditional tail swept) over the broken baseline (0.0225 lives/yr — removed by the guideline), a year-zero repair (0.009), and a low-probability rebuild (0.00225): the tight bound is infeasible for everyone and binding, the admissible bound admits both compliant alternatives and selects the repair on the hand closed forms (1000/A + 280 against 5000/A + 145) without binding, and the baseline appears in no noninferior set | ✅ shape verified |
| `Test_FrontierIca_HandSet` | Strict and weak dominance, an exact tie kept on both rows, a NaN exclusion, mixed directions, and a zero-cost row; the cost-ranked incremental table's ratios recompose exactly (3 and 1, then NaN on the zero-increment steps), and the incremental cost-per-life-saved column equals the ε sweep's trade-off ratios with no delta — one shared helper on the same operands (the reversed walk direction is exact IEEE negation) | ✅ bit-exact |
| `Test_Mcda_Arithmetic` | Weights {2, 1, 1} normalize to exactly {0.5, 0.25, 0.25}; the leader normalizes to one on both live objectives and the constant objective contributes zero, so the scores are exactly 0.75 and 0; the NaN row is excluded with rank zero | ✅ exact |
| `Test_StudyAlpha_Reevaluation` | Tail measures at the study's declared 60% exceedance level, read from the retained epoch curves through the resolver's clone route, equal a directly-configured twin run at that level with no delta (the same thinned loss-exceedance arrays through the same measure computation), and genuinely differ from the run's own 1% level on the stepped flat model | ✅ bit-exact |
| `Test_DoNoHarm_Screen` | An alternative reducing Excess risk (180 → 60) while raising Total risk (280 → 460) through background growth: flagged with the dollar type named; Enforce excludes it from the ε selection it would otherwise win and marks it on the frontier while keeping it in every table; WarnOnly leaves it selectable and carries the advisory Warning; Off leaves the screen unevaluated | ✅ exact |
| `Test_ReliabilityMode_Study` | Two reliability-mode alternatives end to end: the survival-equivalent probabilities are the constant annual probabilities (1e-9 rel), the failure-prevention ratio recomposes with no delta, the reliability ε template selects the upgrade under the tight bound and the costless baseline under the loose one, the declared-vector frontier NaN-excludes every row with each consequence-dependent skip named once, the cost-versus-probability-reduction projection carries the live frontier, and a mixed-mode study is refused at validation | ✅ within documented tolerances |
| `Test_SavageRegret_EnumeratedSeamAndStudyRefusal` | Two flat systems sharing the epistemic fragility variable θ (three deterministic branches at exact weights 0.2/0.5/0.3), enumerated at K = 3, M = 4, N = 12: every realization's Total mean is the closed form 100 + 900·p of its branch (1e-9 rel) and the map weights are the normalized declared weights (1e-15 rel); the regret machinery driven at the engine seam — block means through the engine's scope-and-measure extraction, the regret matrices, aggregates, win counts, and ranking picks — is bit-equal to an independent oracle reading the published realizations directly with mirrored sequential loops; the deliberately unsorted repaired branches make the pairing matter, so the Tier-2 quantile regret (sorted weighted marginals, pairing forgotten) is a different number from the shared-state maximum regret; and the live study over these systems is refused at validation and at run with the epistemic-alternative error | ✅ bit-exact (seam) / exact (refusal) |
| `Test_EpistemicCriteria_ClassicalRulePicks` | Hand ensembles (a wide 10–40 and a tight 24–27 alternative at weights {1, 2, 3, 2}): the weighted means are exactly 210/8 and 203/8, the extremes are the raw sample extremes, and the Laplace, Wald maximin, maximax, mean + k·σ, and Hurwicz(½) picks match independent arithmetic (tight, tight, wide, tight, wide); Hurwicz at α = 1 and α = 0 reproduces the extremes bit-exactly | ✅ exact |
| `Test_EpistemicTailAverage_ExactDegenerates` | The weighted tail average's exact arithmetic: the boundary realization's partial weight completes exactly the tail share ((0.1·100 + 0.1·80)/0.2 = 90), a boundary landing on a weight edge consumes whole weights (88), the Maximize side reads the favorable tail, value ties break by realization index, and the single-realization and equal-value degenerates return the value itself at exact-target parameters — all with no tolerance | ✅ exact |
| `Test_StochasticDominance_AnalyticVerdicts` | Constructed loss-exceedance pairs: a uniform consequence doubling gives clean first-order dominance both ways and a twin is Identical; a concentrated 0.2·50 loss dominates a 0.1·92 + 0.1·10 spread at second order only (the exceedance curves cross between knots, exercising the interior crossing abscissa); a 0.1·60 + 0.1·2 spread with the clearly better stored mean but the worse tail is non-comparable; the quantile tail integral behind the stop-loss transform matches an independent closed-form segment integration (1e-12 rel); and the epistemic weighted-sample variant covers the pointwise, certain-versus-spread, and non-comparable cases | ✅ exact verdicts |
| `Test_ExpectedUtilityAndPartition_ClosedFormsAndCVaRPin` | The exponential certainty equivalent over a two-point loss (mass 0.2 at 100) reproduces (1/θ)·ln(0.8 + 0.2·e^{100θ}) and the power form reproduces (0.2·100^{1+γ})^{1/(1+γ)} (1e-12 rel); an expected-value tie (0.2·100 against 0.1·190 + 0.1·10) is broken toward the concentrated loss with the spread's equivalent matching its own closed form; and on a live repair study the partitioned (0, 0.01] region's conditional mean equals the retained year-zero curve's re-measured CVaR at 0.01 with no delta — one quantile-integral authority — while the certainty-equivalent ranking publishes finite values | ✅ within documented tolerances / bit-exact (pin) |
| `Test_ChanceConstraintParity_BitEqualToPublishedConfidence` | Two uncertain (triangular-fragility) alternatives run at full uncertainty with a configured tolerable-risk criterion and post-hoc weights: the study's raw threshold-exceedance fractions are bit-equal to each stored ensemble's weighted tolerable-risk confidence, the ≤ satisfaction is the exact complement of the published raw fraction, the designed threshold sits strictly inside both spreads, and the verdicts follow the declared confidence | ✅ bit-exact |
| `Test_DecisionSummary_DesignedDisagreement` | Four designed alternatives make the expected value (104.5 beats 109 beats 136), the 0.01-level conditional tail (the 400-dollar consequence bounds the tail), and the total expected annual cost (136 beats 149 and 149.5 under the flat operating costs) recommend three different alternatives; the risk-raising alternative fails the do-no-harm screen, is excluded from every recommendation, and collects a zero margin; and the cross-tabulation reconciles row-by-row against the rankings with recomputed margins | ✅ exact picks |
| `Test_Study_AuthorInertnessAndReproducibility` | Two uncertain alternatives with published 100-realization ensembles: Tier 2 runs live (four epistemic band rows over the benefit-stream mean and the declared standard-deviation criterion, the classical rankings, the quantile regret, the epistemic dominance screen, and the chance-constrained selection); the deferred shared-state block is pinned absent with its named diagnostic; the authors' published ensemble payloads and component hashes are byte-identical after the study; and a second study over the same alternatives publishes bit-identical strategy blocks (bit-level double comparison, NaN slots included) | ✅ byte-identical / bit-exact |
| `Test_Validation_StrategyGatesAndDiagnostics` | Verbatim-prefix and named-diagnostic sweep: the epistemic-alternative refusal, the mixed-mode refusal, the two tier-precondition advisories, the reliability-mode skips naming each consequence-dependent strategy, screen, and declared criterion, the missing-ensemble and missing-map block diagnostics, and the explicit empty objective vector skipping the constrained selection with its named notice | ✅ exact |

## Conventions and limitations

- Costs are commitments: the cost block is identical under both accounting conventions, so
  every benefit-cost ratio divides by the same cost present value; the ratio discipline
  (one convention, one annualization basis per ratio) is documented on the economics row.
- Grid-refinement inertness is exact arithmetic but not bit-identical floating-point
  addition: splitting a stationary epoch re-associates the telescoping annuity, span, and
  survival sums, and the measured movement is a few units in the last place. The 1e-13
  relative pin refuses any real movement while honestly admitting summation rounding.
- Monetization is opt-in: identity pricing (a declared type whose unit equals the monetary
  unit) engages only when a monetization map is declared, and an empty monetized set makes
  the monetary aggregates NaN — "not a monetary question", never zero.
- The v1.0 plan-economics anchor preserves the reference behaviors deliberately — the base
  year discounted one full period over exactly n terms, the silent beyond-horizon ramp
  truncation, and the constant stream when the future year does not exceed the base year —
  while the zero-rate case returns the exact limit the v1.0 capital recovery factor could
  not evaluate (a documented improvement).
- Two documented readings in the cost-per-life-saved family: the absorbing adjusted ratio's
  cost base is the capital plus operations-and-maintenance present value (the operating
  stream is accounted through its own reduction term — folding it into the cost base would
  count it twice), and its numerator carries the Appendix L zero clamp for family
  consistency, a choice its source does not state. Both are carried in the formulary's
  remarks.
- The basis-invariance lemma's floating-point form: multiplying every term by a power of
  two is exact, so the present-value and equivalent-annual quotients agree bit-for-bit
  under the surrogate; under a real annuity factor each scaled term rounds once and the
  quotients agree to a few units in the last place (pinned at 1e-15 relative).
- Sweep bounds set exactly at quadrature-derived metric values can sit one bit below them;
  the engine-backed fixtures carry slack on their explicit grids, and the closed-form
  Haimes table passes the points' own computed swept values as the grid so each bound
  admits its point to the last bit.
- The tolerable-life-risk template fixture is engine-backed through the full study — a
  superset of the design's hand-built reading, recorded here as the run-of-record
  interpretation.
- The trade-off unification is structural, not numerical: the ε sweep's ratio column and
  the incremental table's cost-per-life-saved column call one shared helper, so their
  agreement is bit-exact by construction wherever they pair the same adjacent alternatives.
- Under reliability mode the consequence-dependent metrics are refused deliberately (NaN
  with one named diagnostic each), never computed from empty consequence output; the
  failure-probability axis, costs, and cost per statistical failure prevented stay live.
- An epistemic-mixture alternative is refused at study validation: the study's life-cycle
  trajectories are mean-only quantifications, and a mean pass cannot select an epistemic
  branch (the same predicate behind the engine's mean-only refusal, through one shared
  authority). The shared-state regret machinery is therefore verified at the engine seam
  over genuinely enumerated published state — exactly what a future epistemic-capable
  study would read — and the live-study Tier-3 path stays deliberately absent, pinned with
  its named diagnostic.
- Quantile regret and shared-state regret are different objects by doctrine: the quantile
  view compares sorted weighted marginals and forgets the state pairing. The regret
  fixture's repaired branches are deliberately unsorted against the baseline's so the two
  numbers demonstrably differ.
- Expected utility and the partition means evaluate the design's discrete convention —
  the curve's loss-exceedance mass pairs, with segment conditional means from the shared
  quantile tail integral. A stored curve interpolates log-log between recorded atoms, so
  closed-form fixtures place masses on flat duplicated-knot segments, where the convention
  is exact.
- The chance parity surface is the raw strict-exceedance fraction, published before any
  complementation, so the study and the tolerable-risk confidence block can never drift by
  a rearranged subtraction.
