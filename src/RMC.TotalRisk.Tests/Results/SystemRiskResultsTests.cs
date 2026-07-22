using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="SystemRiskResults"/> — the full-realization summary capture with
/// its component tree and integration diagnostics.
/// </summary>
[TestClass]
public class SystemRiskResultsTests
{
    /// <summary>Verifies the capture summarizes the system streams, components, and diagnostics.</summary>
    [TestMethod]
    public void Test_Capture_TreeAndDiagnostics()
    {
        // Arrange
        var realization = new SystemRealization(new List<ComponentRealization> { new ComponentRealization(failureModes: 1) })
        {
            FunctionEvaluations = 777d,
            StandardError = 0.001d,
            ChiSquared = 1.1d,
        };
        realization.Curves.Fail.CreateCurve(new List<(double Mass, double Consequence)> { (0.02d, 25d), (0.03d, 5d) }, 200);

        // Act
        var results = new SystemRiskResults(realization);

        // Assert
        Assert.AreEqual(realization.Curves.Fail.TotalProbability, results.Fail.TotalProbability, 0d);
        Assert.AreEqual(1, results.ComponentResults.Count);
        Assert.AreEqual(1, results.ComponentResults[0].FailureModeResults.Count);
        Assert.AreEqual(777d, results.FunctionEvaluations, 0d);
        Assert.AreEqual(0.001d, results.StandardError, 0d);
        Assert.AreEqual(1.1d, results.ChiSquared, 0d);
        Assert.ThrowsException<ArgumentNullException>(() => new SystemRiskResults(null!));
    }
}
