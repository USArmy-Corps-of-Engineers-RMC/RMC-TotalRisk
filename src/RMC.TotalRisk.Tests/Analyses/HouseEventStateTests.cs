using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Analyses;

namespace RMC.TotalRisk.Tests.Analyses;

/// <summary>Tests the house-event override input record.</summary>
[TestClass]
public class HouseEventStateTests
{
    /// <summary>Verifies construction stores the address and state.</summary>
    [TestMethod]
    public void Test_Constructor_StoresAddressAndState()
    {
        var functionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var overrideState = new HouseEventState(functionId, nodeId, true);

        Assert.AreEqual(functionId, overrideState.FunctionId);
        Assert.AreEqual(nodeId, overrideState.NodeId);
        Assert.IsTrue(overrideState.State);
    }

    /// <summary>Verifies empty ids are rejected.</summary>
    [TestMethod]
    public void Test_Constructor_EmptyIds_Throw()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            new HouseEventState(Guid.Empty, Guid.NewGuid(), false));
        Assert.ThrowsException<ArgumentException>(() =>
            new HouseEventState(Guid.NewGuid(), Guid.Empty, false));
    }
}
