using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.FaultTrees;

/// <summary>
/// Unit tests for <see cref="FaultTreeImportanceResult"/> — the query-result container.
/// </summary>
[TestClass]
public class FaultTreeImportanceResultTests
{
    /// <summary>Verifies construction stores the header slots and the entry list.</summary>
    [TestMethod]
    public void Test_Construction_StoresValues()
    {
        // Arrange
        var entry = new FaultTreeImportanceEntry(Guid.NewGuid(), "A", "Root/A", 0.1d, 1d, 1d, 1d, 10d, double.PositiveInfinity);

        // Act
        var result = new FaultTreeImportanceResult(15d, -1d, 0.02d, new[] { entry });

        // Assert
        Assert.AreEqual(15d, result.HazardLevel, 0d);
        Assert.AreEqual(-1d, result.Percentile, 0d);
        Assert.AreEqual(0.02d, result.TopEventProbability, 0d);
        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreSame(entry, result.Entries[0]);
    }
}
