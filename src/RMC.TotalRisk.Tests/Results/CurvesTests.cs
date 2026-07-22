using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="Curves"/> — the five risk-type streams with their v1.0
/// exhaustiveness assignments and the fan-out pipeline.
/// </summary>
[TestClass]
public class CurvesTests
{
    /// <summary>Verifies the v1.0 exhaustiveness assignments per stream.</summary>
    [TestMethod]
    public void Test_Defaults_ExhaustivenessPerStream()
    {
        // Act
        var curves = new Curves();

        // Assert — Fail/Excess/NonFail defective; Background/Total exhaustive.
        Assert.IsFalse(curves.Excess.IsExhaustive);
        Assert.IsFalse(curves.Fail.IsExhaustive);
        Assert.IsFalse(curves.NonFail.IsExhaustive);
        Assert.IsTrue(curves.Background.IsExhaustive);
        Assert.IsTrue(curves.Total.IsExhaustive);
    }

    /// <summary>Verifies the fan-out pipeline builds every stream and the clone is deep.</summary>
    [TestMethod]
    public void Test_FanOut_AndDeepClone()
    {
        // Arrange — one pair set per stream through the shared construction.
        var curves = new Curves();
        var pairs = new List<(double Mass, double Consequence)> { (0.1d, 10d), (0.2d, 5d) };
        curves.Fail.CreateCurve(pairs, 200);
        curves.Total.CreateCurve(new List<(double Mass, double Consequence)> { (0.5d, 10d), (0.5d, 5d) }, 200);

        // Act
        var clone = curves.Clone();
        clone.Fail.LECConsequences[0] = -1d;
        curves.DumpMemory();

        // Assert
        Assert.AreEqual(curves.Fail.Mean, clone.Fail.Mean, 0d);
        Assert.AreNotEqual(-1d, curves.Fail.LECConsequences[0], "Clone must be deep.");
        Assert.AreEqual(7.5d, curves.Total.Mean, 1e-13);
    }
}
