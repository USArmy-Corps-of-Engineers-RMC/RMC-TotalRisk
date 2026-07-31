using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.FaultTrees;

/// <summary>Pins the serialized fault-tree gate discriminator.</summary>
[TestClass]
public class FaultTreeGateTypeTests
{
    /// <summary>Pins members, order, and underlying values of the serialized gate enum.</summary>
    [TestMethod]
    public void Test_Members_ValuesAndOrderPinned()
    {
        // Assert
        CollectionAssert.AreEqual(new[] { "And", "Or", "Xor", "KOfN" },
            Enum.GetNames<FaultTreeGateType>());
        Assert.AreEqual(0, (int)FaultTreeGateType.And);
        Assert.AreEqual(1, (int)FaultTreeGateType.Or);
        Assert.AreEqual(2, (int)FaultTreeGateType.Xor);
        Assert.AreEqual(3, (int)FaultTreeGateType.KOfN);
    }
}
