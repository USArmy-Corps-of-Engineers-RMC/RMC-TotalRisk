using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the risk-measure members: names, order, and values are serialized contract — the
/// tolerable-risk criteria and the cost-benefit metric selectors persist the measure by name.
/// </summary>
[TestClass]
public class RiskMeasureTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(new[]
        {
            "TotalProbability", "ConditionalMean", "Mean", "StandardDeviation", "Skewness",
            "Kurtosis", "ConsequenceThresholdProbability", "HazardThresholdProbability",
            "ValueAtRisk", "ConditionalValueAtRisk",
        }, Enum.GetNames<RiskMeasure>());
        Assert.AreEqual(0, (int)RiskMeasure.TotalProbability);
        Assert.AreEqual(1, (int)RiskMeasure.ConditionalMean);
        Assert.AreEqual(2, (int)RiskMeasure.Mean);
        Assert.AreEqual(3, (int)RiskMeasure.StandardDeviation);
        Assert.AreEqual(4, (int)RiskMeasure.Skewness);
        Assert.AreEqual(5, (int)RiskMeasure.Kurtosis);
        Assert.AreEqual(6, (int)RiskMeasure.ConsequenceThresholdProbability);
        Assert.AreEqual(7, (int)RiskMeasure.HazardThresholdProbability);
        Assert.AreEqual(8, (int)RiskMeasure.ValueAtRisk);
        Assert.AreEqual(9, (int)RiskMeasure.ConditionalValueAtRisk);
    }
}
