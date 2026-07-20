using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="TransformElement"/> — ports, function ownership, the stored input
/// connection with dual Id + Name serialization, pending-reference resolution, and cloning.
/// </summary>
[TestClass]
public class TransformElementTests
{
    /// <summary>Builds a valid labeled flow-to-stage rating transform.</summary>
    private static TabularTransform Rating()
    {
        return new TabularTransform
        {
            Name = "Rating",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(50d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a resolver over a fixed element set.</summary>
    private static RiskElementResolver ResolverOver(params IRiskElement[] elements)
    {
        var list = new List<IRiskElement>(elements);
        return new RiskElementResolver(
            id => list.Find(e => e.Id == id),
            name => list.Find(e => string.Equals(e.Name, name, StringComparison.Ordinal)));
    }

    /// <summary>Verifies the pass-through shape: one input, one output.</summary>
    [TestMethod]
    public void Test_Defaults_PassThroughShape()
    {
        // Act
        var element = new TransformElement("Rating");

        // Assert
        Assert.AreEqual(1, element.InputCount);
        Assert.AreEqual(1, element.OutputCount);
        Assert.IsNull(element.Function);
        Assert.IsNull(element.Input);
        Assert.AreEqual(0, element.GetInputConnections().Count());
    }

    /// <summary>Verifies input rewiring notifies and enumerates.</summary>
    [TestMethod]
    public void Test_Input_RewiringAndEnumeration()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var element = new TransformElement("Rating");
        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        element.Input = new RiskConnection(hazard);
        element.Input = new RiskConnection(hazard);   // equal connection — no raise

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(TransformElement.Input) }, raised);
        Assert.AreSame(hazard, element.GetInputConnections().Single().Source);
    }

    /// <summary>Verifies the validation matrix.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Valid: named with a valid function (connectivity is graph-level).
        Assert.IsTrue(new TransformElement("Rating") { Function = Rating() }.Validate().IsValid);

        // Missing function is an error; wrapped errors carry element context.
        Assert.IsFalse(new TransformElement("Rating").Validate().IsValid);
        var unlabeled = new TransformElement("Rating") { Function = new TabularTransform() };
        Assert.IsTrue(unlabeled.Validate().ValidationMessages.Any(
            m => m.StartsWith("Error: Element 'Rating':", StringComparison.Ordinal)));
    }

    /// <summary>Verifies connection serialization: dual Id + Name + port, resolved after load.</summary>
    [TestMethod]
    public void Test_Serialization_ConnectionRoundTrip()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var original = new TransformElement("Rating")
        {
            Function = Rating(),
            Input = new RiskConnection(hazard),
        };

        // Act
        var xml = original.ToXElement();

        // Assert — the serialized triple: authoritative Id, name fallback, port.
        Assert.AreEqual(hazard.Id.ToString("D"), xml.Attribute("SourceElementId")!.Value);
        Assert.AreEqual("Hazard", xml.Attribute("SourceElement")!.Value);
        Assert.AreEqual("0", xml.Attribute("SourcePort")!.Value);

        // The restored element holds the reference pending until the graph resolves it.
        var restored = new TransformElement(xml);
        Assert.IsNull(restored.Input);
        restored.ResolveDeserializedReferences(ResolverOver(hazard));
        Assert.AreSame(hazard, restored.Input!.Source);
        Assert.AreEqual(0, restored.Input.SourcePort);

        // The wrapped function round-trips bit-faithfully.
        CollectionAssert.AreEqual(original.Function!.CanonicalHash(), restored.Function!.CanonicalHash());
    }

    /// <summary>Verifies a stale serialized Id throws on resolution; a name-only link resolves leniently.</summary>
    [TestMethod]
    public void Test_Resolution_StaleIdThrows_NameFallbackWorks()
    {
        // Arrange — a stale Id (the referenced element is absent).
        var hazard = new HazardElement("Hazard");
        var withStale = new TransformElement("Rating") { Input = new RiskConnection(hazard) };
        var staleRestored = new TransformElement(withStale.ToXElement());

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => staleRestored.ResolveDeserializedReferences(ResolverOver()));

        // A hand-authored name-only link resolves by name.
        var nameOnly = new XElement(nameof(TransformElement));
        nameOnly.SetAttributeValue("SourceElement", "Hazard");
        var nameRestored = new TransformElement(nameOnly);
        nameRestored.ResolveDeserializedReferences(ResolverOver(hazard));
        Assert.AreSame(hazard, nameRestored.Input!.Source);

        // An unknown wrapped function throws.
        var badFunction = new XElement(nameof(TransformElement),
            new XElement(nameof(TransformElement.Function), new XElement("Bogus")));
        Assert.ThrowsException<InvalidOperationException>(() => new TransformElement(badFunction));
    }

    /// <summary>Verifies cloning: shared Id, deep function copy, connections re-linked via the map.</summary>
    [TestMethod]
    public void Test_Clone_And_ResolveClonedConnections()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var original = new TransformElement("Rating")
        {
            Function = Rating(),
            Input = new RiskConnection(hazard),
        };

        // Act — clone drops connections; the cloning graph re-links through the map.
        var hazardClone = (HazardElement)hazard.Clone();
        var clone = (TransformElement)original.Clone();
        Assert.IsNull(clone.Input);
        clone.ResolveClonedConnections(original,
            new Dictionary<IRiskElement, IRiskElement> { [hazard] = hazardClone });

        // Assert
        Assert.AreEqual(original.Id, clone.Id);
        Assert.AreNotSame(original.Function, clone.Function);
        CollectionAssert.AreEqual(original.Function!.CanonicalHash(), clone.Function!.CanonicalHash());
        Assert.AreSame(hazardClone, clone.Input!.Source);

        // A source absent from the map keeps the original reference (validation flags it).
        var orphanClone = (TransformElement)original.Clone();
        orphanClone.ResolveClonedConnections(original, new Dictionary<IRiskElement, IRiskElement>());
        Assert.AreSame(hazard, orphanClone.Input!.Source);
    }
}
