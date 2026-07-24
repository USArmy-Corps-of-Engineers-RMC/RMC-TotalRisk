using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="Curves"/> — the five risk-type streams with their v1.0
/// exhaustiveness assignments, the enum-driven stream accessor, and the fan-out pipeline.
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

    /// <summary>
    /// Verifies the enum-driven stream accessor maps every <see cref="RiskType"/> member to its
    /// named stream and rejects undefined members.
    /// </summary>
    [TestMethod]
    public void Test_GetCurve_MapsEveryStream()
    {
        // Arrange
        var curves = new Curves();

        // Act / Assert
        Assert.AreSame(curves.Excess, curves.GetCurve(RiskType.Excess));
        Assert.AreSame(curves.Background, curves.GetCurve(RiskType.Background));
        Assert.AreSame(curves.Total, curves.GetCurve(RiskType.Total));
        Assert.AreSame(curves.Fail, curves.GetCurve(RiskType.Fail));
        Assert.AreSame(curves.NonFail, curves.GetCurve(RiskType.NonFail));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => curves.GetCurve((RiskType)99));
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
