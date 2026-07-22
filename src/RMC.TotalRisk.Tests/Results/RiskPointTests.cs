using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="RiskPoint"/> — the recorded risk evaluation point with its parallel
/// entry lists.
/// </summary>
[TestClass]
public class RiskPointTests
{
    /// <summary>Verifies entry recording and the closed-form summary statistics.</summary>
    [TestMethod]
    public void Test_AddAndSummaryStatistics_ClosedForm()
    {
        // Arrange — mass 0.1 with two entries: (0.4, 100) and (0.1, 50).
        var point = new RiskPoint { HazardLevel = 12d, HazardProbability = 0.9d, HazardProbabilityMass = 0.1d };
        point.Add(0.4d, 100d);
        point.Add(0.1d, 50d);

        // Act
        point.SummaryStatistics(out double totalProbability, out double expectedConsequences);

        // Assert — Σ mass·p = 0.05; Σ mass·p·c = 4.5.
        Assert.AreEqual(2, point.ResponseProbabilities.Count);
        Assert.AreEqual(0.05d, totalProbability, 1e-15);
        Assert.AreEqual(4.5d, expectedConsequences, 1e-13);
    }

    /// <summary>Verifies the capacity constructor and its argument contract.</summary>
    [TestMethod]
    public void Test_CapacityConstructor()
    {
        // Act
        var point = new RiskPoint(8);

        // Assert
        Assert.AreEqual(0, point.ResponseProbabilities.Count);
        Assert.AreEqual(8, point.ResponseProbabilities.Capacity);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new RiskPoint(-1));
    }
}
