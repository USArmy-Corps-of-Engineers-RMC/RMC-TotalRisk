using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Tests the study results root: guards, the trajectory-parallelism rule, and the
/// convention echoes.
/// </summary>
[TestClass]
public class CostBenefitResultsTests
{
    /// <summary>Builds a one-row results container.</summary>
    /// <param name="trajectoryCount">The trajectory count (1 = parallel to the single row).</param>
    /// <returns>The results.</returns>
    private static CostBenefitResults Build(int trajectoryCount = 1)
    {
        var row = new AlternativeEconomics("Baseline", string.Empty, isBaseline: true,
            0d, 0d, 0d, 0d, 0d, 0d, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
            double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
            1e-3, 0d, 1e-3, 0d, double.NaN);
        var epoch = new LifeCycleEpochRisk(0, 50, 0d, 0.05d,
            new LifeCycleEpochEntry("System", 1e-3, new[] { 100d }, new[] { 80d }, new[] { 20d }),
            Array.Empty<LifeCycleEpochEntry>(), Array.Empty<string>());
        var trajectory = new LifeCycleRiskResults(50, 0.035d, new[] { "Damages" }, new[] { "$" },
            new[] { epoch }, 0.05d, new[] { 0d }, new[] { 0d }, new[] { 0d }, new[] { 0d },
            new[] { 0d }, Array.Empty<string>());
        var trajectories = new LifeCycleRiskResults[trajectoryCount];
        for (int i = 0; i < trajectoryCount; i++) trajectories[i] = trajectory;
        return new CostBenefitResults(50, 0.035d, new[] { 0 }, RiskType.Total,
            LifeCycleAccounting.NonAbsorbing, new[] { 0.01d },
            new ConsequenceMonetization(new[] { new MonetizationFactor(0, 2d, "Price", "2026") }),
            -1, double.NaN, "vintage", AlarpProximity.JustBelowTolerableLimit, null,
            1e-4, 1d, DoNoHarmPolicy.Enforce, new[] { "Damages" }, new[] { "$" },
            new[] { row }, Array.Empty<ConsequenceReduction>(), Array.Empty<TrajectoryPoint>(),
            trajectories);
    }

    /// <summary>Verifies the trajectory-parallelism rule and a null guard.</summary>
    [TestMethod]
    public void Test_Ctor_Guards()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentException>(() => Build(trajectoryCount: 2));
    }

    /// <summary>Verifies the convention echoes.</summary>
    [TestMethod]
    public void Test_Ctor_ConventionEchoes()
    {
        // Act
        var results = Build();

        // Assert
        Assert.AreEqual(50, results.PeriodYears);
        Assert.AreEqual(0.035d, results.DiscountRate);
        Assert.AreEqual(1, results.EpochGridYears.Count);
        Assert.AreEqual(RiskType.Total, results.BenefitRiskType);
        Assert.AreEqual(LifeCycleAccounting.NonAbsorbing, results.Accounting);
        Assert.AreEqual(0.01d, results.AlphaLevels[0]);
        Assert.IsNotNull(results.Monetization);
        Assert.AreEqual("2026", results.Monetization.Factors[0].Vintage);
        Assert.AreEqual(-1, results.LifeSafetyConsequenceType);
        Assert.IsTrue(double.IsNaN(results.WillingnessToPay));
        Assert.AreEqual("vintage", results.WillingnessToPayVintage);
        Assert.AreEqual(AlarpProximity.JustBelowTolerableLimit, results.AlarpProximity);
        Assert.IsNull(results.AlarpBandThresholds);
        Assert.AreEqual(1e-4, results.IndividualRiskLimit);
        Assert.AreEqual(1d, results.EquityExponent);
        Assert.AreEqual(DoNoHarmPolicy.Enforce, results.DoNoHarm);
        Assert.AreEqual("Damages", results.ConsequenceLabels[0]);
        Assert.AreEqual("$", results.ConsequenceUnits[0]);
        Assert.AreEqual(1, results.Alternatives.Count);
        Assert.IsTrue(results.Alternatives[0].IsBaseline);
        Assert.AreEqual(1, results.Trajectories.Count);
    }
}
