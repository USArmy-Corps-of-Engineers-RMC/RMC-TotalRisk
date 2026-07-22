using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="ComponentRealization"/> — construction, extent defaults, and the
/// failure-mode fan-out.
/// </summary>
[TestClass]
public class ComponentRealizationTests
{
    /// <summary>Verifies construction shapes and the extent tracking defaults.</summary>
    [TestMethod]
    public void Test_Construction_ShapesAndDefaults()
    {
        // Act
        var realization = new ComponentRealization(failureModes: 3);

        // Assert
        Assert.AreEqual(3, realization.FailureModes.Count);
        Assert.AreEqual(double.MaxValue, realization.MinN);
        Assert.AreEqual(double.MinValue, realization.MaxN);
        Assert.AreEqual(double.MaxValue, realization.MinH);
        Assert.AreEqual(double.MinValue, realization.MaxH);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ComponentRealization(-1));
    }

    /// <summary>Verifies the fan-out reaches the failure modes and the memory dump clears them.</summary>
    [TestMethod]
    public void Test_FanOut_ReachesFailureModes()
    {
        // Arrange
        var realization = new ComponentRealization(failureModes: 1);
        realization.Curves.Fail.AddRiskPoint(1d, 0.4d, 0.1d, 10d);
        realization.Curves.Fail.AddRiskPoint(2d, 0.8d, 0.2d, 20d);
        realization.FailureModes[0].Curves.Fail.AddRiskPoint(1d, 0.4d, 0.1d, 10d);
        realization.FailureModes[0].Curves.Fail.AddRiskPoint(2d, 0.8d, 0.2d, 20d);

        // Act
        realization.ProcessHazardProbabilities();
        realization.DumpMemory();

        // Assert
        Assert.AreEqual(0, realization.Curves.Fail.RiskPoints.Count);
        Assert.AreEqual(0, realization.FailureModes[0].Curves.Fail.RiskPoints.Count);
    }
}
