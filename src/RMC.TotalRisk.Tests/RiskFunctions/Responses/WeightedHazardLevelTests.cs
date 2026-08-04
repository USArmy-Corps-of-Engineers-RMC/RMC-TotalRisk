using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses;

/// <summary>
/// Unit tests for <see cref="WeightedHazardLevel"/> — the v1.0 defaults, change notification, the
/// exact legacy attribute round trip, permissive reads, and clone independence.
/// </summary>
[TestClass]
public class WeightedHazardLevelTests
{
    /// <summary>Verifies the v1.0 default construction state (level 0, weight 0).</summary>
    [TestMethod]
    public void Test_Defaults_AreZero()
    {
        // Act
        var level = new WeightedHazardLevel();

        // Assert
        Assert.AreEqual(0d, level.Level);
        Assert.AreEqual(0d, level.Weight);
    }

    /// <summary>Verifies both properties raise change notification, and same-value sets stay silent.</summary>
    [TestMethod]
    public void Test_PropertyChanges_RaiseNotifications()
    {
        // Arrange
        var level = new WeightedHazardLevel();
        var raised = new List<string>();
        level.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        // Act
        level.Level = 1090d;
        level.Weight = 0.25d;
        level.Level = 1090d;
        level.Weight = 0.25d;

        // Assert — one raise per real change, none for the same-value sets.
        CollectionAssert.AreEqual(new[] { nameof(WeightedHazardLevel.Level), nameof(WeightedHazardLevel.Weight) }, raised);
    }

    /// <summary>
    /// Pins the exact legacy serialized shape: element <c>WeightedHazardLevel</c> with the
    /// <c>Level</c> attribute first and <c>Weight</c> second, both "G17" invariant.
    /// </summary>
    [TestMethod]
    public void Test_ToXElement_ExactLegacyAttributeShape()
    {
        // Arrange
        var level = new WeightedHazardLevel { Level = 1090.5d, Weight = 1d / 3d };

        // Act
        var element = level.ToXElement();

        // Assert
        Assert.AreEqual(nameof(WeightedHazardLevel), element.Name.LocalName);
        var attributes = element.Attributes().ToArray();
        Assert.AreEqual(2, attributes.Length);
        Assert.AreEqual(nameof(WeightedHazardLevel.Level), attributes[0].Name.LocalName);
        Assert.AreEqual(nameof(WeightedHazardLevel.Weight), attributes[1].Name.LocalName);
        Assert.AreEqual("1090.5", attributes[0].Value);
        Assert.AreEqual((1d / 3d).ToString("G17", System.Globalization.CultureInfo.InvariantCulture), attributes[1].Value);
    }

    /// <summary>Verifies a round trip reproduces both values bit-exactly.</summary>
    [TestMethod]
    public void Test_XmlRoundTrip_Exact()
    {
        // Arrange
        var original = new WeightedHazardLevel { Level = 0.1d, Weight = 1d / 3d };

        // Act
        var restored = new WeightedHazardLevel(original.ToXElement());

        // Assert
        Assert.AreEqual(original.Level, restored.Level, 0d);
        Assert.AreEqual(original.Weight, restored.Weight, 0d);
        Assert.AreEqual(original.ToXElement().ToString(), restored.ToXElement().ToString());
    }

    /// <summary>
    /// Verifies permissive reads: missing attributes keep the zero defaults, an unparseable token
    /// becomes NaN (so validation rejects it instead of a fabricated value entering the surface),
    /// and a null element throws.
    /// </summary>
    [TestMethod]
    public void Test_Ctor_PermissiveReads()
    {
        // Act
        var missing = new WeightedHazardLevel(new XElement(nameof(WeightedHazardLevel)));
        var unparseable = new WeightedHazardLevel(new XElement(nameof(WeightedHazardLevel),
            new XAttribute(nameof(WeightedHazardLevel.Level), "not-a-number"),
            new XAttribute(nameof(WeightedHazardLevel.Weight), "")));

        // Assert
        Assert.AreEqual(0d, missing.Level);
        Assert.AreEqual(0d, missing.Weight);
        Assert.IsTrue(double.IsNaN(unparseable.Level));
        Assert.IsTrue(double.IsNaN(unparseable.Weight));
        Assert.ThrowsException<ArgumentNullException>(() => new WeightedHazardLevel(null!));
    }

    /// <summary>Verifies a clone carries the values and is independent of its source.</summary>
    [TestMethod]
    public void Test_Clone_IsIndependent()
    {
        // Arrange
        var original = new WeightedHazardLevel { Level = 1100d, Weight = 0.4d };

        // Act
        var clone = original.Clone();
        clone.Level = 1200d;
        clone.Weight = 0.6d;

        // Assert
        Assert.AreNotSame(original, clone);
        Assert.AreEqual(1100d, original.Level);
        Assert.AreEqual(0.4d, original.Weight);
        Assert.AreEqual(1200d, clone.Level);
        Assert.AreEqual(0.6d, clone.Weight);
    }
}
