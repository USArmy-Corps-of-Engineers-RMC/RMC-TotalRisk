using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the life-cycle accounting-convention members: names, order, and values are serialized
/// contract.
/// </summary>
[TestClass]
public class LifeCycleAccountingTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "NonAbsorbing", "Absorbing" },
            Enum.GetNames<LifeCycleAccounting>());
        Assert.AreEqual(0, (int)LifeCycleAccounting.NonAbsorbing);
        Assert.AreEqual(1, (int)LifeCycleAccounting.Absorbing);
    }
}
