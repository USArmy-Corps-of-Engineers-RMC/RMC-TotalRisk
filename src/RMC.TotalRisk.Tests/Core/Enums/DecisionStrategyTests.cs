using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the decision-strategy members: names, order, and values become append-only serialized
/// contract when the results body is serialized.
/// </summary>
[TestClass]
public class DecisionStrategyTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[]
            {
                "ExpectedValue", "MeanPlusDispersion", "ConditionalValueAtRisk",
                "PartitionedConditionalMean", "CertaintyEquivalent", "TotalExpectedAnnualCost",
                "NetPresentValue", "BenefitCostRatio", "CostPerLifeSaved",
                "AnnualizedFailureProbability", "ConstrainedSelection", "MultiCriteriaScore",
                "Laplace", "WaldMaximin", "Maximax", "Hurwicz", "QuantileRegret",
                "ChanceConstrainedSelection", "MinimaxRegret", "ExpectedRegret",
            },
            Enum.GetNames<DecisionStrategy>());
        Assert.AreEqual(0, (int)DecisionStrategy.ExpectedValue);
        Assert.AreEqual(1, (int)DecisionStrategy.MeanPlusDispersion);
        Assert.AreEqual(2, (int)DecisionStrategy.ConditionalValueAtRisk);
        Assert.AreEqual(3, (int)DecisionStrategy.PartitionedConditionalMean);
        Assert.AreEqual(4, (int)DecisionStrategy.CertaintyEquivalent);
        Assert.AreEqual(5, (int)DecisionStrategy.TotalExpectedAnnualCost);
        Assert.AreEqual(6, (int)DecisionStrategy.NetPresentValue);
        Assert.AreEqual(7, (int)DecisionStrategy.BenefitCostRatio);
        Assert.AreEqual(8, (int)DecisionStrategy.CostPerLifeSaved);
        Assert.AreEqual(9, (int)DecisionStrategy.AnnualizedFailureProbability);
        Assert.AreEqual(10, (int)DecisionStrategy.ConstrainedSelection);
        Assert.AreEqual(11, (int)DecisionStrategy.MultiCriteriaScore);
        Assert.AreEqual(12, (int)DecisionStrategy.Laplace);
        Assert.AreEqual(13, (int)DecisionStrategy.WaldMaximin);
        Assert.AreEqual(14, (int)DecisionStrategy.Maximax);
        Assert.AreEqual(15, (int)DecisionStrategy.Hurwicz);
        Assert.AreEqual(16, (int)DecisionStrategy.QuantileRegret);
        Assert.AreEqual(17, (int)DecisionStrategy.ChanceConstrainedSelection);
        Assert.AreEqual(18, (int)DecisionStrategy.MinimaxRegret);
        Assert.AreEqual(19, (int)DecisionStrategy.ExpectedRegret);
    }
}
