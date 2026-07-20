using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Models.RiskAnalysis.Components;

namespace RMC.TotalRisk.Tests.Models.RiskAnalysis.Components;

/// <summary>
/// Unit tests for <see cref="RiskType"/> — the member names and declared order are serialized
/// contract and must never change.
/// </summary>
[TestClass]
public class RiskTypeTests
{
    /// <summary>Pins the v1.0 member names, order, and underlying values.</summary>
    [TestMethod]
    public void Test_Members_PinnedToV10Contract()
    {
        // Assert — names in declared order.
        CollectionAssert.AreEqual(
            new[] { "Excess", "Background", "Total", "Fail", "NonFail" },
            Enum.GetNames<RiskType>());

        // Underlying values.
        Assert.AreEqual(0, (int)RiskType.Excess);
        Assert.AreEqual(1, (int)RiskType.Background);
        Assert.AreEqual(2, (int)RiskType.Total);
        Assert.AreEqual(3, (int)RiskType.Fail);
        Assert.AreEqual(4, (int)RiskType.NonFail);
    }
}
