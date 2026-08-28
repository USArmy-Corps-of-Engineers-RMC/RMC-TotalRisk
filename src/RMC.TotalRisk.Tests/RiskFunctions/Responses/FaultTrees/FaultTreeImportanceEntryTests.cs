using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.FaultTrees;

/// <summary>
/// Unit tests for <see cref="FaultTreeImportanceEntry"/> — the per-variable measure container.
/// </summary>
[TestClass]
public class FaultTreeImportanceEntryTests
{
    /// <summary>Verifies construction stores every slot.</summary>
    [TestMethod]
    public void Test_Construction_StoresValues()
    {
        // Act
        var id = Guid.NewGuid();
        var entry = new FaultTreeImportanceEntry(id, "Gate seal", "Root/Joint/Gate seal",
            0.1d, 0.8d, 0.3d, 0.29d, 3.6d, 1.4d);

        // Assert
        Assert.AreEqual(id, entry.NodeId);
        Assert.AreEqual("Gate seal", entry.Name);
        Assert.AreEqual("Root/Joint/Gate seal", entry.CanonicalPath);
        Assert.AreEqual(0.1d, entry.BaselineProbability, 0d);
        Assert.AreEqual(0.8d, entry.Birnbaum, 0d);
        Assert.AreEqual(0.3d, entry.Criticality, 0d);
        Assert.AreEqual(0.29d, entry.FussellVesely, 0d);
        Assert.AreEqual(3.6d, entry.RiskAchievementWorth, 0d);
        Assert.AreEqual(1.4d, entry.RiskReductionWorth, 0d);
    }
}
