using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Verification.Analyses;

/// <summary>
/// Cost-benefit verification, greenfield: the v1.0 plan-economics parity constants, the
/// cost-stream present-value oracle, the null study's exact zeros, the stationary bridge onto
/// hand closed forms with the configuration-query cross-check, the two-epoch benefit closed
/// form pairing capital and benefit discounting, grid-refinement inertness with deterioration
/// re-aging on the shared grid, and the monetization identities with the economic and
/// monetized aggregate split.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// <b>Scenario tables.</b> The flat model drives a five-ordinate deterministic
/// stage-frequency hazard (exceedance 0.999 to 0.001 over stages 60 to 260) through the flat
/// OR(AND(house, 0.375), 0.2) fault tree — failure probability exactly 0.2 with the house
/// event false and 0.5 with it true — with a flat failure consequence of 1000 dollars over a
/// flat non-failure background of 100, so every stream mean is closed-form: Fail = 1000p,
/// Total = 1000p + 100(1 − p), Excess = 900p. The life-safety variant declares a second
/// consequence type with flat failure life loss 0.05 and background 0.005, so the Total life
/// mean is 0.05p + 0.005(1 − p) and the Excess life mean 0.045p. Studies designate the
/// broken (house-true) condition as the baseline and repair it through a year-zero or
/// year-ten plan intervention on the shared system instance.
/// </para>
/// <para>
/// <b>Equivalence contract.</b> Every quantification is mean-only on deterministic fixtures,
/// so the study's trajectory evaluations are bit-reproducible and the per-alternative deltas
/// are exact twin differences. The year-zero epoch of a plan that flips the house event runs
/// the same clone machinery as the configuration-risk query, so their entries must agree with
/// no delta — the cross-anchor between the study's trajectory rows and the shipped
/// configuration query.
/// </para>
/// <para>
/// <b>Oracle mechanics.</b> Oracles are independent re-implementations: plan economics
/// re-derives its stream with a value-array loop, power-form discounting, and the capital
/// recovery factor; cost streams re-price with per-year Math.Pow sums that never touch the
/// annuity forms; benefits recompute from power-form annuity segments and per-year survival
/// loops on the closed-form stream means; monetized aggregates recompose from the published
/// trajectory arrays and declared factors.
/// </para>
/// <para>
/// <b>Tolerances.</b> Recompositions from published values assert bit-exact (no delta), and
/// the null study's deltas are exact zeros. Closed-form stream means carry 1e-9 relative
/// (the integrator's 1e-8 relative discipline is quadrature-exact on flat integrands,
/// leaving rounding), and derived money quantities inherit that bound. Independent
/// discounting arithmetic (power form against the engine's log-space form) carries 1e-12
/// relative. The v1.0 parity constants carry 1e-9 absolute against the recorded reference
/// values and 1e-12 relative against the in-test re-derivation. Grid-refinement inertness is
/// pinned at 1e-13 relative: refining a stationary trajectory's epoch grid re-associates the
/// same telescoping sums, which is exact arithmetic but not bit-identical addition; each
/// derivation is documented on its assert.
/// </para>
/// </remarks>
[TestClass]
public class CostBenefitVerification
{
    #region Fixtures

    /// <summary>Builds the deterministic five-ordinate stage-frequency hazard.</summary>
    /// <returns>The hazard.</returns>
    private static TabularHazard StageFrequency()
    {
        return new TabularHazard
        {
            Name = "Stage frequency",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            NoUncertaintyFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Deterministic(60d)),
                    new UncertainOrdinate(0.5d, new Deterministic(100d)),
                    new UncertainOrdinate(0.1d, new Deterministic(140d)),
                    new UncertainOrdinate(0.01d, new Deterministic(180d)),
                    new UncertainOrdinate(0.001d, new Deterministic(260d)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending,
                UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>
    /// Builds the flat OR(AND(house, 0.375), 0.2) fault-tree response: failure probability
    /// exactly 0.2 with the house event false and 1 − 0.8 · 0.625 = 0.5 with it true.
    /// </summary>
    /// <param name="houseState">The authored house state.</param>
    /// <param name="houseId">The house-event node id.</param>
    /// <returns>The response.</returns>
    private static FaultTreeResponse FlatFaultResponse(bool houseState, out Guid houseId)
    {
        var faultTree = new FaultTree();
        Guid gateId = faultTree.Add(faultTree.Root.Id,
            new FaultTreeGateNode("Outage impact", FaultTreeGateType.And));
        houseId = faultTree.Add(gateId, new FaultTreeHouseEventNode("Gate out of service", houseState));
        faultTree.Add(gateId, new FaultTreeBasicEventNode("Load exceedance", new ProbabilitySource(0.375d)));
        faultTree.Add(faultTree.Root.Id,
            new FaultTreeBasicEventNode("Structural failure", new ProbabilitySource(0.2d)));
        return new FaultTreeResponse(new[] { 0d, 1d }, faultTree)
        {
            Name = "Spillway fault tree",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
        };
    }

    /// <summary>Builds a flat deterministic consequence over the fixture's stage range.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="value">The flat consequence value.</param>
    /// <param name="label">The consequence-type label.</param>
    /// <param name="unit">The consequence-type unit.</param>
    /// <returns>The consequence.</returns>
    private static TabularConsequence FlatConsequence(string name, double value,
        string label = "Damages", string unit = "$")
    {
        return new TabularConsequence
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = label,
            ConsequenceUnit = unit,
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(40d, new Deterministic(value)),
                    new UncertainOrdinate(280d, new Deterministic(value)),
                },
                true, SortOrder.Ascending, false, SortOrder.None,
                UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>
    /// Builds the flat one-component system: the fault-tree failure mode over the 1000-dollar
    /// flat consequence plus the 100-dollar non-failure background, optionally carrying the
    /// life-loss type (failure 0.05, background 0.005 lives).
    /// </summary>
    /// <param name="houseState">The authored house state.</param>
    /// <param name="houseId">The house-event node id.</param>
    /// <param name="response">The fault-tree response (for intervention addressing).</param>
    /// <param name="includeLifeLoss">True to declare the life-loss consequence type.</param>
    /// <returns>The analysis.</returns>
    private static RiskAnalysis BuildFlatSystem(bool houseState, out Guid houseId,
        out FaultTreeResponse response, bool includeLifeLoss = false)
    {
        response = FlatFaultResponse(houseState, out houseId);
        var component = new SystemComponent { Name = "Dam" };
        component.HazardFunction = StageFrequency();
        var failureMode = new FailureMode(null, null, response,
            FlatConsequence("Failure damages", 1000d));
        var backgroundMode = new FailureMode(null, null, new NonFailResponse { Name = "Background" },
            FlatConsequence("Background damages", 100d));
        if (includeLifeLoss)
        {
            failureMode.ConsequenceFunctions.Add(
                FlatConsequence("Failure life loss", 0.05d, "Life Loss", "lives"));
            backgroundMode.ConsequenceFunctions.Add(
                FlatConsequence("Background life loss", 0.005d, "Life Loss", "lives"));
        }
        component.AddFailureMode(failureMode);
        component.AddFailureMode(backgroundMode);
        var author = new RiskAnalysis(new[] { component })
        {
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
        };
        if (includeLifeLoss)
        {
            author.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Life Loss", "lives"));
        }
        return author;
    }

    /// <summary>The independent power-form annuity factor.</summary>
    /// <param name="years">The horizon in years.</param>
    /// <param name="rate">The annual discount rate.</param>
    /// <returns>The annuity factor.</returns>
    private static double Annuity(int years, double rate)
    {
        return rate > 0d ? (1d - Math.Pow(1d + rate, -years)) / rate : years;
    }

    /// <summary>
    /// Builds and runs a broken-baseline repair study on one shared flat system: the
    /// designated baseline is the authored house-true condition and the repair alternative
    /// flips the house event through a plan intervention at the given year, carrying the
    /// given costs.
    /// </summary>
    /// <param name="options">The study declarations.</param>
    /// <param name="repairYear">The repair intervention year.</param>
    /// <param name="repairCosts">The repair alternative's costs.</param>
    /// <param name="includeLifeLoss">True to declare the life-loss consequence type.</param>
    /// <returns>The estimated study and the shared system.</returns>
    private static (CostBenefitAnalysis Study, RiskAnalysis System, FaultTreeResponse Response, Guid HouseId)
        RunRepairStudy(CostBenefitOptions options, int repairYear, CostStream? repairCosts,
            bool includeLifeLoss = false)
    {
        RiskAnalysis system = BuildFlatSystem(houseState: true, out Guid houseId,
            out FaultTreeResponse response, includeLifeLoss);
        var repairPlan = new LifeCyclePlan(new[]
        {
            new LifeCycleIntervention(repairYear,
                new[] { new HouseEventState(response.Id, houseId, false) }),
        });
        var baseline = new RiskReductionAlternative("Existing condition", system);
        var repair = new RiskReductionAlternative("Gate repair", system, repairCosts, repairPlan);
        var study = new CostBenefitAnalysis(options);
        study.Alternatives.Add(baseline);
        study.Alternatives.Add(repair);
        study.Baseline = baseline;
        study.RunAsync().GetAwaiter().GetResult();
        return (study, system, response, houseId);
    }

    #endregion

    /// <summary>
    /// The v1.0 plan-economics parity: the recorded reference constants for positive rates
    /// (the ramp-and-plateau case, the silent beyond-horizon truncation, the constant stream,
    /// and the long planning-default ramp), the exact zero-rate average, and an independent
    /// in-test re-derivation of every case with power-form discounting and the capital
    /// recovery factor.
    /// </summary>
    [TestMethod]
    public void Test_PlanEconomics_V10ParityConstants()
    {
        // Arrange — (baseEac, baseYear, futureEac, futureYear, rate, periodYears, reference).
        var cases = new (double BaseEac, int BaseYear, double FutureEac, int FutureYear,
            double Rate, int Period, double Reference)[]
        {
            (100d, 0, 160d, 2, 0.1d, 4, 134.970911441500d),
            (100d, 0, 160d, 10, 0.1d, 4, 108.287007110536d),
            (100d, 0, 160d, 0, 0.1d, 4, 160d),
            (100d, 2026, 200d, 2076, 0.07d, 30, 119.497368419047d),
            (100d, 0, 160d, 2, 0d, 4, 137.5d),
        };

        foreach (var scenario in cases)
        {
            // Act
            double actual = PlanEconomics.EquivalentAnnualConsequences(scenario.BaseEac,
                scenario.BaseYear, scenario.FutureEac, scenario.FutureYear, scenario.Rate,
                scenario.Period);

            // The independent oracle: build the year values, discount with power forms, and
            // apply the capital recovery factor (the zero-rate case averages the values —
            // the exact limit).
            var values = new double[scenario.Period];
            for (int i = 0; i < scenario.Period; i++)
            {
                int year = scenario.BaseYear + i;
                values[i] = year >= scenario.FutureYear
                    ? scenario.FutureEac
                    : scenario.BaseEac + (scenario.FutureEac - scenario.BaseEac)
                        * (year - scenario.BaseYear) / (double)(scenario.FutureYear - scenario.BaseYear);
            }
            double expected;
            if (scenario.Rate > 0d)
            {
                double presentValue = 0d;
                for (int i = 0; i < scenario.Period; i++)
                {
                    presentValue += values[i] * Math.Pow(1d + scenario.Rate, -(i + 1));
                }
                double growth = Math.Pow(1d + scenario.Rate, scenario.Period);
                expected = scenario.Rate * growth / (growth - 1d) * presentValue;
            }
            else
            {
                double total = 0d;
                for (int i = 0; i < scenario.Period; i++) total += values[i];
                expected = total / scenario.Period;
            }

            // Assert — the recorded reference at 1e-9 absolute (twelve recorded digits) and
            // the re-derivation at 1e-12 relative (independent expression order).
            Assert.AreEqual(scenario.Reference, actual, 1e-9d);
            Assert.AreEqual(expected, actual, Math.Abs(expected) * 1e-12d);
        }
    }

    /// <summary>
    /// The cost-stream present-value oracle: dated capital {0: 1000, 10: 500, 20: −200},
    /// operations 10 per year over (0, 50], operating −5 per year over (10, 50], at 3.5
    /// percent over fifty years — re-priced with per-year Math.Pow sums that never touch the
    /// annuity forms — plus the equivalent-annual identity.
    /// </summary>
    [TestMethod]
    public void Test_CostStream_PresentValueOracle()
    {
        // Arrange — the priced repair alternative over the shared flat system.
        var costs = new CostStream(
            new[]
            {
                new CapitalCostEntry(0, 1000d),
                new CapitalCostEntry(10, 500d),
                new CapitalCostEntry(20, -200d),
            },
            new[] { new RecurringCostSegment(0, 10d) },
            new[] { new RecurringCostSegment(10, -5d, 50) });
        (CostBenefitAnalysis study, _, _, _) = RunRepairStudy(
            new CostBenefitOptions(50, 0.035d), repairYear: 0, costs);

        // The independent oracle: every yearly amount discounted with Math.Pow — capital at
        // its own year, recurring amounts one year after their start through their end.
        double rate = 0.035d;
        double expectedCapital = 1000d + 500d * Math.Pow(1d + rate, -10) - 200d * Math.Pow(1d + rate, -20);
        double expectedOperations = 0d;
        for (int year = 1; year <= 50; year++) expectedOperations += 10d * Math.Pow(1d + rate, -year);
        double expectedOperating = 0d;
        for (int year = 11; year <= 50; year++) expectedOperating += -5d * Math.Pow(1d + rate, -year);
        double expectedTotal = expectedCapital + expectedOperations + expectedOperating;

        // Assert — the engine's log-space discounting against the per-year power sums at
        // 1e-12 relative, and the equivalent-annual identity against the independent annuity.
        AlternativeEconomics row = study.Results!.Alternatives[1];
        Assert.AreEqual(expectedCapital, row.CapitalPresentValue, Math.Abs(expectedCapital) * 1e-12d);
        Assert.AreEqual(expectedOperations, row.OperationsAndMaintenancePresentValue,
            Math.Abs(expectedOperations) * 1e-12d);
        Assert.AreEqual(expectedOperating, row.OperatingChangePresentValue,
            Math.Abs(expectedOperating) * 1e-12d);
        Assert.AreEqual(expectedTotal, row.TotalCostPresentValue, Math.Abs(expectedTotal) * 1e-12d);
        Assert.AreEqual(expectedTotal / Annuity(50, rate), row.EquivalentAnnualCost,
            Math.Abs(expectedTotal / Annuity(50, rate)) * 1e-12d);
        Assert.AreEqual(1000d + 500d - 200d + 10d * 50 - 5d * 40, row.CumulativeCost, 1e-12d);
    }

    /// <summary>
    /// The null study: a costless, planless twin of the baseline reuses the baseline's
    /// trajectory evaluation, publishes exact zeros in every reduction row and every signed
    /// reduction column, and reports an undefined benefit-cost ratio at zero cost.
    /// </summary>
    [TestMethod]
    public void Test_NullStudy_ExactZeros()
    {
        // Arrange — the twin shares the baseline's system with no plan and no costs; the
        // declared (empty) monetization map identity-prices the dollar type at one.
        RiskAnalysis system = BuildFlatSystem(houseState: true, out _, out _);
        var baseline = new RiskReductionAlternative("Existing condition", system);
        var twin = new RiskReductionAlternative("Do nothing", system);
        var study = new CostBenefitAnalysis(new CostBenefitOptions(20, 0.05d,
            monetization: new ConsequenceMonetization()));
        study.Alternatives.Add(baseline);
        study.Alternatives.Add(twin);
        study.Baseline = baseline;

        // Act
        study.RunAsync().GetAwaiter().GetResult();

        // Assert — one evaluation serves both rows, and the twin's deltas are exact zeros:
        // the study differences the same trajectory instance against itself.
        CostBenefitResults results = study.Results!;
        Assert.IsTrue(ReferenceEquals(results.Trajectories[0], results.Trajectories[1]));
        int twinRows = 0;
        for (int i = 0; i < results.ConsequenceReductions.Count; i++)
        {
            ConsequenceReduction row = results.ConsequenceReductions[i];
            if (!string.Equals(row.AlternativeName, "Do nothing", StringComparison.Ordinal)) continue;
            twinRows++;
            Assert.AreEqual(0d, row.PresentValueReduction, 0d);
            Assert.AreEqual(0d, row.EquivalentAnnualReduction, 0d);
            Assert.AreEqual(0d, row.CumulativeReduction, 0d);
            Assert.AreEqual(0d, row.AbsorbingPresentValueReduction, 0d);
            Assert.AreEqual(0d, row.AbsorbingCumulativeReduction, 0d);
        }
        Assert.AreEqual(3, twinRows, "One row per stream for the single declared type.");

        AlternativeEconomics twinRow = results.Alternatives[1];
        Assert.AreEqual(0d, twinRow.MonetizedPresentValueBenefit, 0d);
        Assert.AreEqual(0d, twinRow.EconomicPresentValueBenefit, 0d);
        Assert.AreEqual(0d, twinRow.NetPresentValue, 0d);
        Assert.AreEqual(0d, twinRow.AnnualizedFailureProbabilityReduction, 0d);
        Assert.AreEqual(0d, twinRow.YearZeroFailureProbabilityReduction, 0d);
        Assert.IsTrue(double.IsNaN(twinRow.BenefitCostRatio),
            "Cost per unit of nothing spent is undefined, never infinite.");
    }

    /// <summary>
    /// The stationary bridge: the year-zero repair of the broken baseline is stationary on
    /// both sides, so the monetized benefit is the closed form (m_broken − m_fixed)·A(T) on
    /// the flat stream means 550 and 280, the net present value and benefit-cost ratio
    /// follow by hand, and the year-zero epoch entries agree with the shipped
    /// configuration-risk query with no delta — the cross-anchor onto the clone machinery
    /// both queries share.
    /// </summary>
    [TestMethod]
    public void Test_StationaryBridge_ClosedFormAndConfigurationParity()
    {
        // Arrange / Act — repair at year zero for 1000 capital, at five percent over twenty
        // years, with the dollar type identity-monetized.
        var costs = new CostStream(new[] { new CapitalCostEntry(0, 1000d) });
        (CostBenefitAnalysis study, RiskAnalysis system, FaultTreeResponse response, Guid houseId) =
            RunRepairStudy(new CostBenefitOptions(20, 0.05d,
                monetization: new ConsequenceMonetization()), repairYear: 0, costs);

        // Assert — the hand closed form: broken Total mean 1000·0.5 + 100·0.5 = 550, fixed
        // 1000·0.2 + 100·0.8 = 280, so the stationary benefit is 270·A(20) and the
        // percentage tolerances inherit the flat-quadrature 1e-9 relative bound.
        CostBenefitResults results = study.Results!;
        AlternativeEconomics repair = results.Alternatives[1];
        double annuity = Annuity(20, 0.05d);
        double expectedBenefit = 270d * annuity;
        Assert.AreEqual(expectedBenefit, repair.MonetizedPresentValueBenefit,
            Math.Abs(expectedBenefit) * 1e-9d);
        Assert.AreEqual(expectedBenefit - 1000d, repair.NetPresentValue,
            Math.Abs(expectedBenefit) * 1e-9d);
        Assert.AreEqual(expectedBenefit / 1000d, repair.BenefitCostRatio,
            Math.Abs(expectedBenefit / 1000d) * 1e-9d);
        Assert.AreEqual(1000d, repair.TotalCostPresentValue, 0d);

        // The year-zero epochs against the configuration query, with no delta: both run the
        // same mean-only clone machinery over the same author state.
        ConfigurationRiskResults configuration = system.MeasureConfigurationRisk(
            new[] { new HouseEventState(response.Id, houseId, false) });
        LifeCycleRiskResults baselineTrajectory = results.Trajectories[0];
        LifeCycleRiskResults repairTrajectory = results.Trajectories[1];
        Assert.AreEqual(configuration.System.BaselineFailureProbability,
            baselineTrajectory.Epochs[0].System.FailureProbability);
        Assert.AreEqual(configuration.System.ConfiguredFailureProbability,
            repairTrajectory.Epochs[0].System.FailureProbability);
        Assert.AreEqual(configuration.System.BaselineExpectedConsequences[0],
            baselineTrajectory.Epochs[0].System.ExpectedConsequences[0]);
        Assert.AreEqual(configuration.System.ConfiguredExpectedConsequences[0],
            repairTrajectory.Epochs[0].System.ExpectedConsequences[0]);
    }

    /// <summary>
    /// The two-epoch benefit closed form: a year-ten repair pairs capital discounted at
    /// (1 + r)^−10 with benefits whose first term is (1 + r)^−11 — the study's end-of-year
    /// exposure convention — so the monetized benefit equals the per-year power-form loop
    /// over years eleven through twenty and the annuity-segment form Δm·(A(20) − A(10)),
    /// and the absorbing benefit equals the differenced per-year survival loops.
    /// </summary>
    [TestMethod]
    public void Test_TwoEpochBenefit_ClosedFormConventionPairing()
    {
        // Arrange / Act — repair at year ten for 1000 capital, at 3.5 percent over twenty
        // years, with the dollar type identity-monetized.
        double rate = 0.035d;
        var costs = new CostStream(new[] { new CapitalCostEntry(10, 1000d) });
        (CostBenefitAnalysis study, _, _, _) = RunRepairStudy(new CostBenefitOptions(20, rate,
            monetization: new ConsequenceMonetization()), repairYear: 10, costs);

        // Assert — the capital side of the pairing: exactly (1 + r)^−10.
        CostBenefitResults results = study.Results!;
        AlternativeEconomics repair = results.Alternatives[1];
        double expectedCapital = 1000d * Math.Pow(1d + rate, -10);
        Assert.AreEqual(expectedCapital, repair.CapitalPresentValue, Math.Abs(expectedCapital) * 1e-12d);

        // The benefit side: the closed forms on the flat means (broken 550, fixed 280) — the
        // per-year loop over years eleven through twenty and the annuity-segment form agree,
        // and the published benefit matches both at the flat-quadrature 1e-9 relative bound.
        double deltaMean = 550d - 280d;
        double perYearBenefit = 0d;
        for (int year = 11; year <= 20; year++)
        {
            perYearBenefit += deltaMean * Math.Pow(1d + rate, -year);
        }
        double segmentBenefit = deltaMean * (Annuity(20, rate) - Annuity(10, rate));
        Assert.AreEqual(perYearBenefit, segmentBenefit, Math.Abs(perYearBenefit) * 1e-12d);
        Assert.AreEqual(perYearBenefit, repair.MonetizedPresentValueBenefit,
            Math.Abs(perYearBenefit) * 1e-9d);

        // The published benefit recomposed from the published trajectories, with no delta.
        Assert.AreEqual(results.Trajectories[0].PresentValueOfExpectedConsequences[0]
            - results.Trajectories[1].PresentValueOfExpectedConsequences[0],
            repair.MonetizedPresentValueBenefit, 0d);

        // The absorbing benefit against differenced per-year survival loops on the closed
        // forms: the broken side survives at 0.5 throughout, the repaired side at 0.5 through
        // year ten and 0.2 after.
        static double AbsorbingPresent(double rate, Func<int, double> probability, Func<int, double> mean)
        {
            double survival = 1d;
            double present = 0d;
            for (int year = 1; year <= 20; year++)
            {
                present += survival * mean(year) * Math.Pow(1d + rate, -year);
                survival *= 1d - probability(year);
            }
            return present;
        }
        double expectedAbsorbingBenefit =
            AbsorbingPresent(rate, _ => 0.5d, _ => 550d)
            - AbsorbingPresent(rate, year => year <= 10 ? 0.5d : 0.2d, year => year <= 10 ? 550d : 280d);
        Assert.AreEqual(expectedAbsorbingBenefit, repair.AbsorbingMonetizedPresentValueBenefit,
            Math.Abs(expectedAbsorbingBenefit) * 1e-9d);
    }

    /// <summary>
    /// Grid alignment: refining a deterioration-free study's epoch grid re-associates the
    /// same telescoping sums, so every economics number agrees at summation rounding; and a
    /// deteriorating baseline re-ages at the repair alternative's plan year because the study
    /// grid is shared, moving its trajectory relative to a standalone coarse evaluation.
    /// </summary>
    [TestMethod]
    public void Test_GridAlignment_TelescopingAndSharedReAging()
    {
        // Arrange / Act — the deterioration-free pair: identical studies, one with extra
        // study-wide evaluation years.
        var costs = new CostStream(new[] { new CapitalCostEntry(0, 1000d) });
        (CostBenefitAnalysis coarse, _, _, _) = RunRepairStudy(new CostBenefitOptions(20, 0.05d,
            monetization: new ConsequenceMonetization()), repairYear: 0, costs);
        (CostBenefitAnalysis fine, _, _, _) = RunRepairStudy(new CostBenefitOptions(20, 0.05d,
            evaluationYears: new[] { 3, 7, 13 },
            monetization: new ConsequenceMonetization()), repairYear: 0, costs);

        // Assert — the refined grid quantifies identical stationary epochs, so the
        // aggregates re-associate the same telescoping sums: exact arithmetic, but not
        // bit-identical floating-point addition. The measured coarse-versus-fine difference
        // is a few units in the last place (4.6e-13 absolute on a net present value of
        // magnitude 115, about 4e-15 relative), so 1e-13 relative pins the identity with two
        // orders of headroom while refusing any real movement.
        AlternativeEconomics coarseRow = coarse.Results!.Alternatives[1];
        AlternativeEconomics fineRow = fine.Results!.Alternatives[1];
        Assert.AreEqual(4, fine.Results.EpochGridYears.Count);
        Assert.AreEqual(coarseRow.MonetizedPresentValueBenefit, fineRow.MonetizedPresentValueBenefit,
            Math.Abs(coarseRow.MonetizedPresentValueBenefit) * 1e-13d);
        Assert.AreEqual(coarseRow.NetPresentValue, fineRow.NetPresentValue,
            Math.Abs(coarseRow.NetPresentValue) * 1e-13d);
        Assert.AreEqual(coarseRow.AbsorbingMonetizedPresentValueBenefit,
            fineRow.AbsorbingMonetizedPresentValueBenefit,
            Math.Abs(coarseRow.AbsorbingMonetizedPresentValueBenefit) * 1e-13d);
        Assert.AreEqual(coarseRow.AnnualizedFailureProbability, fineRow.AnnualizedFailureProbability,
            Math.Abs(coarseRow.AnnualizedFailureProbability) * 1e-13d);

        // The shared-grid re-aging pin: a deteriorating baseline plus a planned repair
        // alternative on a second system — the baseline trajectory re-ages at the repair's
        // plan year purely because the grid is shared.
        var agingComponent = new SystemComponent { Name = "Levee" };
        agingComponent.HazardFunction = StageFrequency();
        agingComponent.AddFailureMode(new FailureMode(null, null, new DeterioratingResponse
        {
            Name = "Aging fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            BaseResponse = new TabularResponse
            {
                Name = "Fragility",
                SpecifiedHazard = "Stage",
                HazardUnit = "ft",
                UncertainOrderedPairedData = new UncertainOrderedPairedData(
                    new[]
                    {
                        new UncertainOrdinate(60d, new Deterministic(0d)),
                        new UncertainOrdinate(260d, new Deterministic(0.5d)),
                    },
                    true, SortOrder.Ascending, false, SortOrder.None,
                    UnivariateDistributionType.Deterministic),
            },
            DeteriorationLaw = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0d, new Deterministic(0d)),
                    new UncertainOrdinate(20d, new Deterministic(40d)),
                },
                true, SortOrder.Ascending, false, SortOrder.None,
                UnivariateDistributionType.Deterministic),
        }, FlatConsequence("Failure damages", 1000d)));
        var agingSystem = new RiskAnalysis(new[] { agingComponent })
        {
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
        };
        RiskAnalysis repairSystem = BuildFlatSystem(houseState: true, out Guid repairHouseId,
            out FaultTreeResponse repairResponse);
        var agingBaseline = new RiskReductionAlternative("Aging levee", agingSystem);
        var plannedRepair = new RiskReductionAlternative("Year-ten repair", repairSystem,
            costs, new LifeCyclePlan(new[]
            {
                new LifeCycleIntervention(10,
                    new[] { new HouseEventState(repairResponse.Id, repairHouseId, false) }),
            }));
        var sharedGrid = new CostBenefitAnalysis(new CostBenefitOptions(20, 0.05d));
        sharedGrid.Alternatives.Add(agingBaseline);
        sharedGrid.Alternatives.Add(plannedRepair);
        sharedGrid.Baseline = agingBaseline;
        sharedGrid.RunAsync().GetAwaiter().GetResult();

        LifeCycleRiskResults agingTrajectory = sharedGrid.Results!.Trajectories[0];
        LifeCycleRiskResults standalone = agingSystem.MeasureLifeCycleRisk(new LifeCycleDefinition(20, 0.05d));

        // The baseline splits at the repair's plan year and ages across it, while the
        // standalone coarse evaluation holds age zero throughout — so the shared grid moves
        // the baseline's aggregate strictly upward.
        Assert.AreEqual(2, agingTrajectory.Epochs.Count);
        Assert.AreEqual(10d, agingTrajectory.Epochs[1].EvaluationAge);
        Assert.IsTrue(agingTrajectory.Epochs[1].System.FailureProbability
            > agingTrajectory.Epochs[0].System.FailureProbability,
            "Deterioration must raise the re-aged epoch's failure probability.");
        Assert.AreEqual(1, standalone.Epochs.Count);
        Assert.IsTrue(agingTrajectory.CumulativeExpectedConsequences[0]
            > standalone.CumulativeExpectedConsequences[0],
            "Re-aging on the shared grid must move the deteriorating baseline's aggregate.");
    }

    /// <summary>
    /// The monetization identities: with only the life-loss type priced, the monetized
    /// benefit is exactly the factor times the discounted lives saved and the economic
    /// aggregate is exactly zero; with the dollar type identity-priced alongside, the
    /// monetized aggregate recomposes from the published trajectory arrays with no delta;
    /// and an empty monetized set publishes undefined monetary aggregates with the
    /// advisory warning.
    /// </summary>
    [TestMethod]
    public void Test_Monetization_SplitIdentities()
    {
        // Arrange / Act — life-only monetization: the monetary unit differs from the dollar
        // type's unit, so only the declared life factor prices anything.
        double lifeValue = 7.5e6d;
        var costs = new CostStream(new[] { new CapitalCostEntry(0, 1000d) });
        var lifeOnlyOptions = new CostBenefitOptions(20, 0.05d,
            monetization: new ConsequenceMonetization(
                new[] { new MonetizationFactor(1, lifeValue, "Statistical life", "2026") }, "FY26$"),
            lifeSafetyConsequenceType: 1);
        (CostBenefitAnalysis lifeOnly, _, _, _) = RunRepairStudy(lifeOnlyOptions, repairYear: 0,
            costs, includeLifeLoss: true);

        // Assert — the monetized benefit is the factor times the discounted lives saved,
        // recomposed from the published trajectories with no delta; the economic aggregate
        // (which always excludes the life-safety type) is exactly zero because nothing else
        // is priced.
        CostBenefitResults lifeResults = lifeOnly.Results!;
        AlternativeEconomics lifeRepair = lifeResults.Alternatives[1];
        double discountedLivesSaved = lifeResults.Trajectories[0].PresentValueOfExpectedConsequences[1]
            - lifeResults.Trajectories[1].PresentValueOfExpectedConsequences[1];
        Assert.AreEqual(lifeValue * discountedLivesSaved, lifeRepair.MonetizedPresentValueBenefit, 0d);
        Assert.AreEqual(0d, lifeRepair.EconomicPresentValueBenefit, 0d);
        Assert.AreEqual(lifeValue * discountedLivesSaved - 1000d, lifeRepair.NetPresentValue, 0d);

        // The closed form beneath it: the Total life mean is 0.05p + 0.005(1 − p), so the
        // stationary lives-saved stream is (0.0275 − 0.014)·A(20) at the flat-quadrature
        // 1e-9 relative bound; the lives-saved column reads the Excess stream instead —
        // 0.045·(0.5 − 0.2) = 0.0135 equivalent-annual lives.
        double expectedLives = (0.0275d - 0.014d) * Annuity(20, 0.05d);
        Assert.AreEqual(expectedLives, discountedLivesSaved, Math.Abs(expectedLives) * 1e-9d);
        Assert.AreEqual(0.0135d, lifeRepair.LivesSavedEquivalentAnnual, 0.0135d * 1e-9d);

        // The split scenario: the dollar type identity-prices at one beside the life factor,
        // and the monetized aggregate recomposes from both published reductions exactly.
        var splitOptions = new CostBenefitOptions(20, 0.05d,
            monetization: new ConsequenceMonetization(
                new[] { new MonetizationFactor(1, lifeValue, "Statistical life", "2026") }),
            lifeSafetyConsequenceType: 1);
        (CostBenefitAnalysis split, _, _, _) = RunRepairStudy(splitOptions, repairYear: 0,
            costs, includeLifeLoss: true);
        CostBenefitResults splitResults = split.Results!;
        AlternativeEconomics splitRepair = splitResults.Alternatives[1];
        double dollarReduction = splitResults.Trajectories[0].PresentValueOfExpectedConsequences[0]
            - splitResults.Trajectories[1].PresentValueOfExpectedConsequences[0];
        double lifeReduction = splitResults.Trajectories[0].PresentValueOfExpectedConsequences[1]
            - splitResults.Trajectories[1].PresentValueOfExpectedConsequences[1];
        Assert.AreEqual(1d * dollarReduction + lifeValue * lifeReduction,
            splitRepair.MonetizedPresentValueBenefit, 0d);
        Assert.AreEqual(1d * dollarReduction, splitRepair.EconomicPresentValueBenefit, 0d);

        // The empty monetized set: a declared map pricing nothing publishes undefined
        // monetary aggregates and the advisory warning.
        var emptyOptions = new CostBenefitOptions(20, 0.05d,
            monetization: new ConsequenceMonetization(null, "FY26$"));
        (CostBenefitAnalysis empty, _, _, _) = RunRepairStudy(emptyOptions, repairYear: 0, costs);
        Assert.IsTrue(double.IsNaN(empty.Results!.Alternatives[1].MonetizedPresentValueBenefit));
        Assert.IsTrue(double.IsNaN(empty.Results.Alternatives[1].NetPresentValue));
        bool warned = false;
        (_, List<string> messages) = empty.Validate();
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].StartsWith(
                "Warning: No consequence types are monetized", StringComparison.Ordinal))
            {
                warned = true;
            }
        }
        Assert.IsTrue(warned, "An empty monetized set must advise, not silently publish NaN.");
    }
}
