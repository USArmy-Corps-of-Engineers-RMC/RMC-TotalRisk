using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the objective-direction members: names, order, and values are serialized contract.
/// </summary>
[TestClass]
public class ObjectiveDirectionTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "Minimize", "Maximize" },
            Enum.GetNames<ObjectiveDirection>());
        Assert.AreEqual(0, (int)ObjectiveDirection.Minimize);
        Assert.AreEqual(1, (int)ObjectiveDirection.Maximize);
    }
}
