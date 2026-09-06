using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.RiskFunctions.Hazards;

namespace RMC.TotalRisk.Tests.Analyses;

/// <summary>
/// Tests the hazard-replacement input record: constructor guards and the referenced-not-owned
/// property contract.
/// </summary>
[TestClass]
public class HazardReplacementTests
{
    /// <summary>Verifies an empty target id is refused.</summary>
    [TestMethod]
    public void Test_Ctor_EmptyTargetId_Throws()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentException>(
            () => new HazardReplacement(Guid.Empty, new TabularHazard()));
    }

    /// <summary>Verifies a null replacement is refused.</summary>
    [TestMethod]
    public void Test_Ctor_NullReplacement_Throws()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(
            () => new HazardReplacement(Guid.NewGuid(), null!));
    }

    /// <summary>Verifies the properties echo the arguments and hold the live reference.</summary>
    [TestMethod]
    public void Test_Ctor_ValidArgs_PropertiesHeld()
    {
        // Arrange
        var target = Guid.NewGuid();
        var replacement = new TabularHazard { Name = "Future stage frequency" };

        // Act
        var action = new HazardReplacement(target, replacement);

        // Assert — referenced, not owned.
        Assert.AreEqual(target, action.TargetFunctionId);
        Assert.IsTrue(ReferenceEquals(replacement, action.Replacement));
    }
}
