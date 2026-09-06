using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Tests the life-cycle epoch scope row: guards, echoes, and the defensive snapshot.
/// </summary>
[TestClass]
public class LifeCycleEpochEntryTests
{
    /// <summary>Verifies null arguments are refused.</summary>
    [TestMethod]
    public void Test_Ctor_NullArguments_Throw()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(
            () => new LifeCycleEpochEntry(null!, 0.1d, new[] { 1d }));
        Assert.ThrowsException<ArgumentNullException>(
            () => new LifeCycleEpochEntry("System", 0.1d, null!));
    }

    /// <summary>Verifies the properties echo the arguments.</summary>
    [TestMethod]
    public void Test_Ctor_PropertiesEchoed()
    {
        // Act
        var entry = new LifeCycleEpochEntry("Dam", 0.05d, new[] { 500d, 2d });

        // Assert
        Assert.AreEqual("Dam", entry.Name);
        Assert.AreEqual(0.05d, entry.FailureProbability);
        Assert.AreEqual(2, entry.ExpectedConsequences.Count);
        Assert.AreEqual(500d, entry.ExpectedConsequences[0]);
        Assert.AreEqual(2d, entry.ExpectedConsequences[1]);
    }

    /// <summary>Verifies the consequence list is a defensive snapshot.</summary>
    [TestMethod]
    public void Test_Ctor_Snapshot_InputMutationInert()
    {
        // Arrange
        var means = new List<double> { 500d };
        var entry = new LifeCycleEpochEntry("System", 0.05d, means);

        // Act
        means[0] = 999d;
        means.Add(1d);

        // Assert
        Assert.AreEqual(1, entry.ExpectedConsequences.Count);
        Assert.AreEqual(500d, entry.ExpectedConsequences[0]);
    }
}
