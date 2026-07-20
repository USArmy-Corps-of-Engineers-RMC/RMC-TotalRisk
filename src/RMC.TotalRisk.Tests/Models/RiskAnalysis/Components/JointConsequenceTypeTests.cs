using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Models.RiskAnalysis.Components;

namespace RMC.TotalRisk.Tests.Models.RiskAnalysis.Components;

/// <summary>
/// Unit tests for <see cref="JointConsequenceType"/> — the member names and declared order are
/// serialized contract and must never change.
/// </summary>
[TestClass]
public class JointConsequenceTypeTests
{
    /// <summary>Pins the v1.0 member names, order, and underlying values.</summary>
    [TestMethod]
    public void Test_Members_PinnedToV10Contract()
    {
        // Assert — names in declared order.
        CollectionAssert.AreEqual(
            new[] { "Additive", "Average", "Maximum", "Minimum" },
            Enum.GetNames<JointConsequenceType>());

        // Underlying values.
        Assert.AreEqual(0, (int)JointConsequenceType.Additive);
        Assert.AreEqual(1, (int)JointConsequenceType.Average);
        Assert.AreEqual(2, (int)JointConsequenceType.Maximum);
        Assert.AreEqual(3, (int)JointConsequenceType.Minimum);
    }
}
