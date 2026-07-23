using System;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Analyses;

namespace RMC.TotalRisk.Tests.Analyses;

/// <summary>
/// Unit tests for <see cref="ConsequenceTypeDescriptor"/> — the declared consequence-type axis
/// entry: construction coercion, serialization round-trip, and forward-loading of older forms.
/// </summary>
[TestClass]
public class ConsequenceTypeDescriptorTests
{
    /// <summary>
    /// Verifies construction: labels are stored verbatim and nulls coerce to empty (a blank
    /// label declares a wildcard position).
    /// </summary>
    [TestMethod]
    public void Test_Construction_StoresLabels_CoercesNull()
    {
        // Arrange / Act
        var declared = new ConsequenceTypeDescriptor("Damages", "$");
        var blank = new ConsequenceTypeDescriptor(null, null);

        // Assert
        Assert.AreEqual("Damages", declared.SpecifiedConsequence);
        Assert.AreEqual("$", declared.ConsequenceUnit);
        Assert.AreEqual(string.Empty, blank.SpecifiedConsequence);
        Assert.AreEqual(string.Empty, blank.ConsequenceUnit);
    }

    /// <summary>
    /// Verifies the serialization round-trip: the element name, both attributes, and the
    /// restored labels.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var descriptor = new ConsequenceTypeDescriptor("Life Loss", "lives");

        // Act
        var element = descriptor.ToXElement();
        var restored = new ConsequenceTypeDescriptor(element);

        // Assert
        Assert.AreEqual(nameof(ConsequenceTypeDescriptor), element.Name.LocalName);
        Assert.AreEqual("Life Loss", restored.SpecifiedConsequence);
        Assert.AreEqual("lives", restored.ConsequenceUnit);
    }

    /// <summary>
    /// Verifies forward loading: missing attributes fall back to empty labels, and a null
    /// element throws.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_MissingAttributes_LoadForward()
    {
        // Arrange — a bare element with no attributes.
        var bare = new XElement(nameof(ConsequenceTypeDescriptor));

        // Act
        var restored = new ConsequenceTypeDescriptor(bare);

        // Assert
        Assert.AreEqual(string.Empty, restored.SpecifiedConsequence);
        Assert.AreEqual(string.Empty, restored.ConsequenceUnit);
        Assert.ThrowsException<ArgumentNullException>(() => new ConsequenceTypeDescriptor((XElement)null!));
    }
}
