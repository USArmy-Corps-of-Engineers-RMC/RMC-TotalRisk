using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Tests.Systems.Components;

/// <summary>
/// Unit tests for <see cref="LatentFactor"/> — construction, property change notification, the
/// defensive loadings copy, and the pipe-joined G17 serialization round trip with permissive
/// malformed reads.
/// </summary>
[TestClass]
public class LatentFactorTests
{
    /// <summary>Verifies the constructors, defaults, and argument guards.</summary>
    [TestMethod]
    public void Test_Construction_DefaultsAndGuards()
    {
        // Arrange & Act
        var empty = new LatentFactor();
        var configured = new LatentFactor("Soil Unit", new[] { 0.8d, 0.6d });

        // Assert
        Assert.AreEqual(string.Empty, empty.Name);
        Assert.AreEqual(string.Empty, empty.Description);
        Assert.AreEqual(0, empty.Loadings.Count);
        Assert.AreEqual("Soil Unit", configured.Name);
        Assert.AreEqual(2, configured.Loadings.Count);
        Assert.AreEqual(0.8d, configured.Loadings[0], 0d);
        Assert.AreEqual(0.6d, configured.Loadings[1], 0d);

        Assert.ThrowsException<ArgumentNullException>(() => new LatentFactor(null!, new[] { 0.5d }));
        Assert.ThrowsException<ArgumentNullException>(() => new LatentFactor("F", null!));
        Assert.ThrowsException<ArgumentNullException>(() => new LatentFactor((XElement)null!));
    }

    /// <summary>Verifies property round trips, change notification, and null handling.</summary>
    [TestMethod]
    public void Test_Properties_RoundTripAndNotification()
    {
        // Arrange
        var factor = new LatentFactor();
        var changed = new List<string>();
        factor.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        // Act
        factor.Name = "Construction Era";
        factor.Description = "Shared 1950s design basis";
        factor.Loadings = new[] { 0.5d, -0.25d, 1d / 3d };
        factor.Name = "Construction Era"; // unchanged — no event
        factor.Name = null!;

        // Assert
        CollectionAssert.AreEqual(
            new[] { nameof(LatentFactor.Name), nameof(LatentFactor.Description), nameof(LatentFactor.Loadings), nameof(LatentFactor.Name) },
            changed);
        Assert.AreEqual(string.Empty, factor.Name, "A null assignment normalizes to the empty string.");
        Assert.ThrowsException<ArgumentNullException>(() => factor.Loadings = null!);
    }

    /// <summary>Verifies the loadings setter copies its input defensively.</summary>
    [TestMethod]
    public void Test_Loadings_DefensiveCopy()
    {
        // Arrange
        var source = new List<double> { 0.1d, 0.2d };
        var factor = new LatentFactor { Loadings = source };

        // Act — mutating the source after assignment must not reach the factor.
        source[0] = 99d;
        source.Add(0.3d);

        // Assert
        Assert.AreEqual(2, factor.Loadings.Count);
        Assert.AreEqual(0.1d, factor.Loadings[0], 0d);
    }

    /// <summary>Verifies the serialization round trip, bit-exact G17 loadings included.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange — a non-representable-in-short-form loading exercises G17 exactness.
        var factor = new LatentFactor("Soil Unit", new[] { 1d / 3d, -0.75d, 0d })
        {
            Description = "Shared foundation stratum",
        };

        // Act
        var restored = new LatentFactor(factor.ToXElement());

        // Assert
        Assert.AreEqual(factor.Name, restored.Name);
        Assert.AreEqual(factor.Description, restored.Description);
        Assert.AreEqual(factor.Loadings.Count, restored.Loadings.Count);
        for (int i = 0; i < factor.Loadings.Count; i++)
        {
            Assert.AreEqual(factor.Loadings[i], restored.Loadings[i], 0d, $"Loading {i} must round-trip bit-exactly.");
        }
        Assert.AreEqual(factor.ToXElement().ToString(), restored.ToXElement().ToString());
    }

    /// <summary>Verifies permissive reads: empty and malformed loadings reconstruct for validation to report.</summary>
    [TestMethod]
    public void Test_Serialization_PermissiveReads()
    {
        // Arrange & Act — an empty factor round-trips empty; a malformed token reads as NaN.
        var empty = new LatentFactor(new LatentFactor().ToXElement());
        var malformed = new LatentFactor(new XElement(nameof(LatentFactor),
            new XAttribute(nameof(LatentFactor.Loadings), "0.5|garbage|0.25")));

        // Assert — the shape is preserved so validation can name the defect, never truncated.
        Assert.AreEqual(0, empty.Loadings.Count);
        Assert.AreEqual(3, malformed.Loadings.Count);
        Assert.AreEqual(0.5d, malformed.Loadings[0], 0d);
        Assert.IsTrue(double.IsNaN(malformed.Loadings[1]));
        Assert.AreEqual(0.25d, malformed.Loadings[2], 0d);
    }
}
