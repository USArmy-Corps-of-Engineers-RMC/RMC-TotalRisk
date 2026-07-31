using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Tests the node-importance options: defaults, validation, and echoed values.</summary>
[TestClass]
public class TreeNodeImportanceOptionsTests
{
    /// <summary>Verifies the constructor stores the hazard level and applies the v1.0 defaults.</summary>
    [TestMethod]
    public void Test_Construction_AppliesDefaults()
    {
        // Act
        var options = new TreeNodeImportanceOptions(1.5d);

        // Assert
        Assert.AreEqual(1.5d, options.HazardLevel, 0d);
        Assert.AreEqual(1000, options.Iterations);
        Assert.AreEqual(12345, options.Seed);
    }

    /// <summary>Verifies a non-finite hazard level is rejected.</summary>
    [TestMethod]
    public void Test_Construction_NonFiniteHazard_Throws()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => new TreeNodeImportanceOptions(double.NaN));
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => new TreeNodeImportanceOptions(double.PositiveInfinity));
    }

    /// <summary>Verifies the iteration count accepts valid values and rejects fewer than two.</summary>
    [TestMethod]
    public void Test_Iterations_ValidatesMinimum()
    {
        // Arrange
        var options = new TreeNodeImportanceOptions(0d);

        // Act
        options.Iterations = 2;

        // Assert
        Assert.AreEqual(2, options.Iterations);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.Iterations = 1);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.Iterations = 0);
    }

    /// <summary>Verifies the seed round-trips any integer, including negatives.</summary>
    [TestMethod]
    public void Test_Seed_RoundTrips()
    {
        // Arrange
        var options = new TreeNodeImportanceOptions(0d);

        // Act
        options.Seed = -777;

        // Assert
        Assert.AreEqual(-777, options.Seed);
    }
}
