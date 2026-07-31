using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.FaultTrees;

/// <summary>Tests the minimal cut-set member addressing record.</summary>
[TestClass]
public class FaultTreeCutSetEventTests
{
    /// <summary>Verifies content retention and null guards.</summary>
    [TestMethod]
    public void Test_Members_RetainedAndGuarded()
    {
        // Arrange
        Guid nodeId = Guid.NewGuid();

        // Act
        var member = new FaultTreeCutSetEvent(nodeId, "Pump", "R/abc:0");

        // Assert
        Assert.AreEqual(nodeId, member.NodeId);
        Assert.AreEqual("Pump", member.Name);
        Assert.AreEqual("R/abc:0", member.CanonicalPath);
        Assert.ThrowsException<ArgumentNullException>(
            () => new FaultTreeCutSetEvent(nodeId, null!, "R"));
        Assert.ThrowsException<ArgumentNullException>(
            () => new FaultTreeCutSetEvent(nodeId, "Pump", null!));
    }
}
