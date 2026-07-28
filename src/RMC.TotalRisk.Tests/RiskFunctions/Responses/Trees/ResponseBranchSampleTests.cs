using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Tests immutable exhaustive branch samples.</summary>
[TestClass]
public class ResponseBranchSampleTests
{
    /// <summary>Verifies a valid partition is retained as an immutable deep copy.</summary>
    [TestMethod]
    public void Test_Construction_ValidPartition_DeepCopies()
    {
        double[] hazards = { 0d, 1d };
        double[] failure = { 0.2d, 0.7d };
        var branches = new[]
        {
            new ResponseBranchDescriptor(Guid.NewGuid(), "Fail", true, 2),
            new ResponseBranchDescriptor(Guid.NewGuid(), "No Fail", false, 3),
        };

        var sample = new ResponseBranchSample(hazards, branches, new[] { failure, new[] { 0.8d, 0.3d } });
        hazards[0] = 99d;
        failure[0] = 99d;

        Assert.AreEqual(0d, sample.Hazards[0]);
        Assert.AreEqual(0.2d, sample.Probabilities[0][0]);
    }

    /// <summary>Verifies shape, bounds, ordering, and exhaustive-sum checks.</summary>
    [TestMethod]
    public void Test_Construction_InvalidSamples_Throw()
    {
        var branch = new[] { new ResponseBranchDescriptor(Guid.NewGuid(), "Only", true, 2) };

        Assert.ThrowsException<ArgumentException>(() =>
            new ResponseBranchSample(new[] { 1d, 0d }, branch, new[] { new[] { 1d, 1d } }));
        Assert.ThrowsException<ArgumentException>(() =>
            new ResponseBranchSample(new[] { 0d }, branch, new[] { new[] { 0.9d } }));
        Assert.ThrowsException<ArgumentException>(() =>
            new ResponseBranchSample(new[] { 0d }, branch, new[] { new[] { 1.1d } }));
    }
}
