using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="DependencyType"/> — the member names and declared order are
/// serialized contract and must never change.
/// </summary>
[TestClass]
public class DependencyTypeTests
{
    /// <summary>Pins the v1.0 member names, order, and underlying values.</summary>
    [TestMethod]
    public void Test_Members_PinnedToV10Contract()
    {
        // Assert — names in declared order.
        CollectionAssert.AreEqual(
            new[] { "Independent", "PerfectlyPositive", "PerfectlyNegative", "CorrelationMatrix" },
            Enum.GetNames<DependencyType>());

        // Underlying values.
        Assert.AreEqual(0, (int)DependencyType.Independent);
        Assert.AreEqual(1, (int)DependencyType.PerfectlyPositive);
        Assert.AreEqual(2, (int)DependencyType.PerfectlyNegative);
        Assert.AreEqual(3, (int)DependencyType.CorrelationMatrix);
    }
}
