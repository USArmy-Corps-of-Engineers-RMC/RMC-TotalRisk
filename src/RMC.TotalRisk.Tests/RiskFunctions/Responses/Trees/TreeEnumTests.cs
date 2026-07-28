using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Pins append-only common-tree enum values.</summary>
[TestClass]
public class TreeEnumTests
{
    /// <summary>Verifies link and deletion policy values.</summary>
    [TestMethod]
    public void Test_Values_ArePinned()
    {
        Assert.AreEqual(0, (int)TreeLinkMode.IndependentClone);
        Assert.AreEqual(1, (int)TreeLinkMode.SharedLogicalEvent);
        Assert.AreEqual(0, (int)TreeDeletePolicy.RejectIfReferenced);
        Assert.AreEqual(1, (int)TreeDeletePolicy.CascadeLinks);
        Assert.AreEqual(2, (int)TreeDeletePolicy.MaterializeLinks);
    }
}
