using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Tests stable response-branch descriptors.</summary>
[TestClass]
public class ResponseBranchDescriptorTests
{
    /// <summary>Verifies descriptor values and constructor guards.</summary>
    [TestMethod]
    public void Test_Construction_ValidatesAndPreservesValues()
    {
        Guid id = Guid.NewGuid();
        var descriptor = new ResponseBranchDescriptor(id, "Failure", true, 2);

        Assert.AreEqual(id, descriptor.Id);
        Assert.AreEqual("Failure", descriptor.Name);
        Assert.IsTrue(descriptor.IsFailure);
        Assert.AreEqual(2, descriptor.OutputPort);
        Assert.ThrowsException<ArgumentException>(() => new ResponseBranchDescriptor(Guid.Empty, "x", true, 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ResponseBranchDescriptor(id, "x", true, -1));
    }
}
