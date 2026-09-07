using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the ALARP-proximity members: names, order, and values are serialized contract.
/// </summary>
[TestClass]
public class AlarpProximityTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "JustBelowTolerableLimit", "JustAboveBroadlyAcceptable" },
            Enum.GetNames<AlarpProximity>());
        Assert.AreEqual(0, (int)AlarpProximity.JustBelowTolerableLimit);
        Assert.AreEqual(1, (int)AlarpProximity.JustAboveBroadlyAcceptable);
    }
}
