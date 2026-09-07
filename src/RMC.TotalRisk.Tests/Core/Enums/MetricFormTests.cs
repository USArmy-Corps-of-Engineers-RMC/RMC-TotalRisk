using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the metric-form members: names, order, and values are serialized contract.
/// </summary>
[TestClass]
public class MetricFormTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "Level", "ReductionVsBaseline" },
            Enum.GetNames<MetricForm>());
        Assert.AreEqual(0, (int)MetricForm.Level);
        Assert.AreEqual(1, (int)MetricForm.ReductionVsBaseline);
    }
}
