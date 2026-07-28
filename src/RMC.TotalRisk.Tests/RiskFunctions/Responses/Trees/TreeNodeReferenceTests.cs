using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Tests persistent internal and external tree-node addressing.</summary>
[TestClass]
public class TreeNodeReferenceTests
{
    /// <summary>Verifies all address fields round-trip through construction.</summary>
    [TestMethod]
    public void Test_Construction_PreservesAddress()
    {
        Guid functionId = Guid.NewGuid();
        Guid nodeId = Guid.NewGuid();
        var reference = new TreeNodeReference(functionId, nodeId, "Tree", "Branch");

        Assert.AreEqual(functionId, reference.FunctionId);
        Assert.AreEqual(nodeId, reference.NodeId);
        Assert.AreEqual("Tree", reference.FunctionName);
        Assert.AreEqual("Branch", reference.NodeName);
        Assert.ThrowsException<ArgumentException>(() => new TreeNodeReference(null, Guid.Empty));
    }
}
