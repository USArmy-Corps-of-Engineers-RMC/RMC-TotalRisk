using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="EnsembleResults"/> — the persisted summary ensemble with the v1.0
/// null-tolerant indexer and the JSON results contract.
/// </summary>
[TestClass]
public class EnsembleResultsTests
{
    /// <summary>Verifies the v1.0 null-tolerant indexer semantics.</summary>
    [TestMethod]
    public void Test_Indexer_NullTolerant()
    {
        // Arrange
        var ensemble = new EnsembleResults(2);
        var summary = new SystemRiskResults();

        // Act — out-of-range writes are ignored; out-of-range reads return null.
        ensemble[0] = summary;

        // Assert
        Assert.AreEqual(2, ensemble.Count);
        Assert.AreSame(summary, ensemble[0]);
        Assert.IsNull(ensemble[1]);
        Assert.IsNull(ensemble[5]);
        Assert.IsNull(ensemble[-1]);
        Assert.IsNull(new EnsembleResults()[0]);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ensemble[5] = summary);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ensemble[-1] = summary);
        Assert.IsTrue(ensemble.TryGetRealization(0, out var present));
        Assert.AreSame(summary, present);
        Assert.IsTrue(ensemble.TryGetRealization(1, out var unwritten));
        Assert.IsNull(unwritten);
        Assert.IsFalse(ensemble.TryGetRealization(5, out var missing));
        Assert.IsNull(missing);
    }

    /// <summary>Verifies the JSON and compressed round trips, including unwritten null slots.</summary>
    [TestMethod]
    public void Test_Json_RoundTrip_WithNullSlots()
    {
        // Arrange
        var ensemble = new EnsembleResults(3);
        ensemble[0] = new SystemRiskResults { FunctionEvaluations = 42d, StandardError = 0.5d };
        ensemble[2] = new SystemRiskResults { ChiSquared = 1.25d };

        // Act
        var fromJson = EnsembleResults.FromJson(ensemble.ToJson());
        var fromBytes = EnsembleResults.FromCompressedBytes(ensemble.ToCompressedBytes());

        // Assert
        Assert.AreEqual(3, fromJson.Count);
        Assert.AreEqual(42d, fromJson[0]!.FunctionEvaluations, 0d);
        Assert.IsNull(fromJson[1]);
        Assert.AreEqual(1.25d, fromJson[2]!.ChiSquared, 0d);
        Assert.AreEqual(ensemble.ToJson(), fromBytes.ToJson());
    }
}
