# Cost-benefit analysis

**Test class:** `CostBenefitVerification` · **Tests:** 7 · **Run of record:** 2026-09-07, isolated run, ✅ all passed

## Scope and status

The greenfield family for the cost-benefit study's kernel, model, and parity surfaces: the
v1.0 plan-economics reference constants, the cost-stream present-value oracle, the null
study's exact zeros, the stationary bridge onto hand closed forms with the
configuration-query cross-check, the two-epoch benefit closed form pairing capital and
benefit discounting, grid-refinement inertness with shared-grid deterioration re-aging, and
the monetization identities with the economic/monetized aggregate split. The
decision-framework surfaces (the cost-per-life-saved family, the ε-constraint sweep, the
frontier, and the strategy catalog) are verified by this family's later fixtures as those
surfaces land.

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~CostBenefitVerification"
```

Observed 2026-09-07: **7/7 passed** (≈ 2 s).

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
| `Test_NullStudy_ExactZeros` | A costless, planless twin of the baseline shares one trajectory evaluation (reference-equal) and publishes exact zeros in every reduction row and signed reduction column, with an undefined (NaN) benefit-cost ratio at zero cost | ✅ exact |
| `Test_StationaryBridge_ClosedFormAndConfigurationParity` | The year-zero repair's monetized benefit is 270·A(20) on the closed-form means 550 and 280 (1e-9 rel), net present value and benefit-cost ratio follow by hand, and the year-zero epoch entries agree with `MeasureConfigurationRisk` with no delta — both queries run the same clone machinery | ✅ within documented tolerances |
| `Test_TwoEpochBenefit_ClosedFormConventionPairing` | A year-ten repair pairs capital at (1 + r)^−10 (1e-12 rel) with benefits whose first term is (1 + r)^−11: the per-year power-form loop over years 11–20 equals the annuity-segment form Δm·(A(20) − A(10)) (1e-12 rel) and the published benefit (1e-9 rel, recomposed from the trajectories with no delta); the absorbing benefit equals differenced per-year survival loops (1e-9 rel) | ✅ within documented tolerances |
| `Test_GridAlignment_TelescopingAndSharedReAging` | Refining a deterioration-free study's epoch grid re-associates the same telescoping sums — measured coarse-versus-fine differences of a few units in the last place (4.6e-13 absolute at magnitude 115, ≈ 4e-15 relative), pinned at 1e-13 relative — while a deteriorating baseline re-ages at the repair alternative's plan year purely because the grid is shared, strictly moving its aggregate against a standalone coarse evaluation | ✅ within documented tolerances |
| `Test_Monetization_SplitIdentities` | Life-only monetization prices exactly V·(discounted lives saved) with an exactly zero economic aggregate; adding the identity-priced dollar type recomposes the monetized aggregate from both published reductions with no delta; an empty monetized set publishes NaN monetary aggregates with the advisory warning; the lives-saved column reads the Excess stream (0.045·Δp equivalent-annual lives, 1e-9 rel) | ✅ within documented tolerances |

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
