using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the economics-metric catalog: names, order, and values are serialized contract.
/// </summary>
[TestClass]
public class EconomicMetricTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[]
            {
                "PresentValueOfTotalCost", "EquivalentAnnualCost", "TotalExpectedAnnualCost",
                "NetPresentValue", "NetAnnualBenefit", "BenefitCostRatio",
                "CostPerStatisticalLifeSavedUnadjusted", "CostPerStatisticalLifeSavedAdjusted",
                "EquityWeightedAdjustedCostPerStatisticalLifeSaved",
                "CostPerStatisticalFailurePrevented",
                "AbsorbingAdjustedCostPerStatisticalLifeSaved", "DisproportionalityRatio",
                "AnnualizedFailureProbability", "AnnualizedFailureProbabilityReduction",
                "MonetizedPresentValueBenefit",
            },
            Enum.GetNames<EconomicMetric>());
        Assert.AreEqual(0, (int)EconomicMetric.PresentValueOfTotalCost);
        Assert.AreEqual(1, (int)EconomicMetric.EquivalentAnnualCost);
        Assert.AreEqual(2, (int)EconomicMetric.TotalExpectedAnnualCost);
        Assert.AreEqual(3, (int)EconomicMetric.NetPresentValue);
        Assert.AreEqual(4, (int)EconomicMetric.NetAnnualBenefit);
        Assert.AreEqual(5, (int)EconomicMetric.BenefitCostRatio);
        Assert.AreEqual(6, (int)EconomicMetric.CostPerStatisticalLifeSavedUnadjusted);
        Assert.AreEqual(7, (int)EconomicMetric.CostPerStatisticalLifeSavedAdjusted);
        Assert.AreEqual(8, (int)EconomicMetric.EquityWeightedAdjustedCostPerStatisticalLifeSaved);
        Assert.AreEqual(9, (int)EconomicMetric.CostPerStatisticalFailurePrevented);
        Assert.AreEqual(10, (int)EconomicMetric.AbsorbingAdjustedCostPerStatisticalLifeSaved);
        Assert.AreEqual(11, (int)EconomicMetric.DisproportionalityRatio);
        Assert.AreEqual(12, (int)EconomicMetric.AnnualizedFailureProbability);
        Assert.AreEqual(13, (int)EconomicMetric.AnnualizedFailureProbabilityReduction);
        Assert.AreEqual(14, (int)EconomicMetric.MonetizedPresentValueBenefit);
    }
}
