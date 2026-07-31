using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Tests the immutable node-importance entry container.</summary>
[TestClass]
public class TreeNodeImportanceEntryTests
{
    /// <summary>Verifies the constructor round-trips every statistic.</summary>
    [TestMethod]
    public void Test_Construction_RoundTripsValues()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        double[] summary = { 0.1d, 0.2d, 0.3d, 0.4d, 0.5d };

        // Act
        var entry = new TreeNodeImportanceEntry(id, "Gate A", "R/x:0", true, summary, 0.75d, 0.5d);

        // Assert
        Assert.AreEqual(id, entry.NodeId);
        Assert.AreEqual("Gate A", entry.Name);
        Assert.AreEqual("R/x:0", entry.CanonicalPath);
        Assert.IsTrue(entry.HasUncertainty);
        CollectionAssert.AreEqual(summary, entry.ProbabilitySummary.ToArray());
        Assert.AreEqual(0.75d, entry.AggregateCorrelation, 0d);
        Assert.AreEqual(0.5d, entry.FirstOrderIndex, 0d);
    }

    /// <summary>Verifies required reference arguments are guarded.</summary>
    [TestMethod]
    public void Test_Construction_NullArguments_Throw()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => new TreeNodeImportanceEntry(
            Guid.NewGuid(), null!, "R", false, new[] { 0d, 0d, 0d, 0d, 0d }, 0d, 0d));
        Assert.ThrowsException<ArgumentNullException>(() => new TreeNodeImportanceEntry(
            Guid.NewGuid(), "A", null!, false, new[] { 0d, 0d, 0d, 0d, 0d }, 0d, 0d));
        Assert.ThrowsException<ArgumentNullException>(() => new TreeNodeImportanceEntry(
            Guid.NewGuid(), "A", "R", false, null!, 0d, 0d));
    }
}
