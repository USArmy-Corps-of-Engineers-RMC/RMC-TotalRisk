using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Tests the immutable node-importance result container.</summary>
[TestClass]
public class TreeNodeImportanceResultTests
{
    /// <summary>Verifies the constructor round-trips the echoed inputs and statistics.</summary>
    [TestMethod]
    public void Test_Construction_RoundTripsValues()
    {
        // Arrange
        double[] summary = { 0.1d, 0.2d, 0.3d, 0.4d, 0.5d };
        var entry = new TreeNodeImportanceEntry(Guid.NewGuid(), "A", "R/a:0", true,
            new[] { 0d, 0d, 0d, 0d, 0d }, 1d, 1d);

        // Act
        var result = new TreeNodeImportanceResult(2d, 500, 42, summary, 0.01d, new[] { entry });

        // Assert
        Assert.AreEqual(2d, result.HazardLevel, 0d);
        Assert.AreEqual(500, result.Iterations);
        Assert.AreEqual(42, result.Seed);
        CollectionAssert.AreEqual(summary, result.AggregateSummary.ToArray());
        Assert.AreEqual(0.01d, result.AggregateVariance, 0d);
        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreSame(entry, result.Entries[0]);
    }

    /// <summary>Verifies required reference arguments are guarded.</summary>
    [TestMethod]
    public void Test_Construction_NullArguments_Throw()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => new TreeNodeImportanceResult(
            0d, 10, 1, null!, 0d, Array.Empty<TreeNodeImportanceEntry>()));
        Assert.ThrowsException<ArgumentNullException>(() => new TreeNodeImportanceResult(
            0d, 10, 1, new[] { 0d, 0d, 0d, 0d, 0d }, 0d, null!));
    }
}
