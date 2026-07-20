using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="RiskConnection"/> — construction guards, immutability, and equality
/// semantics (same source instance, same port).
/// </summary>
[TestClass]
public class RiskConnectionTests
{
    /// <summary>Verifies construction guards and property capture.</summary>
    [TestMethod]
    public void Test_Construction_GuardsAndProperties()
    {
        // Arrange
        var source = new HazardElement("Hazard");

        // Act
        var connection = new RiskConnection(source, 0);

        // Assert
        Assert.AreSame(source, connection.Source);
        Assert.AreEqual(0, connection.SourcePort);
        Assert.ThrowsException<ArgumentNullException>(() => new RiskConnection(null!));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new RiskConnection(source, -1));
    }

    /// <summary>Verifies the default port is the primary output.</summary>
    [TestMethod]
    public void Test_DefaultPort_IsPrimary()
    {
        // Act
        var connection = new RiskConnection(new HazardElement("Hazard"));

        // Assert
        Assert.AreEqual(0, connection.SourcePort);
    }

    /// <summary>Verifies equality: same source instance and port.</summary>
    [TestMethod]
    public void Test_Equality_BySourceInstanceAndPort()
    {
        // Arrange
        var a = new HazardElement("A");
        var b = new HazardElement("B");

        // Assert — equal for the same instance + port, across connection instances.
        Assert.AreEqual(new RiskConnection(a, 0), new RiskConnection(a, 0));
        Assert.AreEqual(new RiskConnection(a, 0).GetHashCode(), new RiskConnection(a, 0).GetHashCode());

        // Different port or different source instance are unequal.
        Assert.AreNotEqual(new RiskConnection(a, 0), new RiskConnection(a, 1));
        Assert.AreNotEqual(new RiskConnection(a, 0), new RiskConnection(b, 0));
        Assert.IsFalse(new RiskConnection(a, 0).Equals(null));
    }
}
