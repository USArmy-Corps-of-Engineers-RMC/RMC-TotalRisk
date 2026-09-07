using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the constraint-scope members: names, order, and values are serialized contract.
/// </summary>
[TestClass]
public class ConstraintScopeTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "EveryEpoch", "FirstEpoch", "Horizon" },
            Enum.GetNames<ConstraintScope>());
        Assert.AreEqual(0, (int)ConstraintScope.EveryEpoch);
        Assert.AreEqual(1, (int)ConstraintScope.FirstEpoch);
        Assert.AreEqual(2, (int)ConstraintScope.Horizon);
    }
}
