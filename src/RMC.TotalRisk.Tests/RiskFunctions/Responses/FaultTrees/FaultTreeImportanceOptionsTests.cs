using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.FaultTrees;

/// <summary>
/// Unit tests for <see cref="FaultTreeImportanceOptions"/> — the hazard-level echo and the
/// percentile guard.
/// </summary>
[TestClass]
public class FaultTreeImportanceOptionsTests
{
    /// <summary>Verifies construction, the mean default, and the percentile guard.</summary>
    [TestMethod]
    public void Test_Construction_AndPercentileGuard()
    {
        // Act
        var options = new FaultTreeImportanceOptions(12.5d);

        // Assert
        Assert.AreEqual(12.5d, options.HazardLevel, 0d);
        Assert.AreEqual(-1d, options.Percentile, 0d);

        options.Percentile = 0.95d;
        Assert.AreEqual(0.95d, options.Percentile, 0d);
        options.Percentile = -1d;
        Assert.AreEqual(-1d, options.Percentile, 0d);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.Percentile = 0d);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.Percentile = 1d);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.Percentile = double.NaN);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.Percentile = -0.5d);
    }
}
