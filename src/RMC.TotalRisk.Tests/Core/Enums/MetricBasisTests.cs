using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the metric time-basis members: names, order, and values are serialized contract.
/// </summary>
[TestClass]
public class MetricBasisTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "AnnualizedPerEpoch", "HorizonPresentValue", "HorizonEquivalentAnnual", "HorizonCumulative" },
            Enum.GetNames<MetricBasis>());
        Assert.AreEqual(0, (int)MetricBasis.AnnualizedPerEpoch);
        Assert.AreEqual(1, (int)MetricBasis.HorizonPresentValue);
        Assert.AreEqual(2, (int)MetricBasis.HorizonEquivalentAnnual);
        Assert.AreEqual(3, (int)MetricBasis.HorizonCumulative);
    }
}
