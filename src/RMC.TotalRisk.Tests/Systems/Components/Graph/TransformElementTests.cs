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
/// Unit tests for <see cref="TransformElement"/> — ports (including the bivariate arity gates),
/// function ownership, the stored input and secondary-input connections with dual Id + Name
/// serialization, pending-reference resolution, and cloning.
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

    /// <summary>Builds a valid labeled bivariate surge-pool stage transform.</summary>
    private static BivariateTransform Bivariate()
    {
        return new BivariateTransform
        {
            Name = "Surge-Pool Stage",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            X1Values = new[] { 0d, 10d, 20d },
            X2Values = new[] { 100d, 200d },
            ZValues = new[,] { { 1d, 2d }, { 3d, 5d }, { 4d, 8d } },
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

    /// <summary>
    /// Verifies the bivariate arity gates: both port counts flip to two with a bivariate
    /// function, and the function setter reports the arity changes alongside the function.
    /// </summary>
    [TestMethod]
    public void Test_Arity_BivariateGate()
    {
        // Arrange
        var element = new TransformElement("Transform") { Function = Rating() };
        Assert.AreEqual(1, element.InputCount);
        Assert.AreEqual(1, element.OutputCount);
        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        element.Function = Bivariate();

        // Assert — two inputs (primary x, secondary y) and two outputs (port 0 = z,
        // port 1 = the secondary pass-through).
        Assert.AreEqual(2, element.InputCount);
        Assert.AreEqual(2, element.OutputCount);
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(TransformElement.Function), nameof(TransformElement.InputCount),
                nameof(TransformElement.OutputCount),
            }, raised);
    }

    /// <summary>
    /// Verifies the element-local bivariate validation pair: a secondary input on a univariate
    /// transform errors, and a bivariate transform without one errors.
    /// </summary>
    [TestMethod]
    public void Test_Validate_SecondaryInputPairing()
    {
        // A secondary input while the wrapped transform is univariate.
        var hazard = new HazardElement("Hazard");
        var univariate = new TransformElement("Transform")
        {
            Function = Rating(),
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        var (univariateValid, univariateMessages) = univariate.Validate();
        Assert.IsFalse(univariateValid);
        Assert.IsTrue(univariateMessages.Any(m => m.Contains("secondary input, but its transform function is univariate")));

        // A bivariate transform without a secondary input.
        var unwired = new TransformElement("Transform") { Function = Bivariate() };
        var (unwiredValid, unwiredMessages) = unwired.Validate();
        Assert.IsFalse(unwiredValid);
        Assert.IsTrue(unwiredMessages.Any(m => m.Contains("wraps a bivariate transform but has no secondary input")));

        // Wired bivariate passes element-local validation (chain rules are graph-level).
        var wired = new TransformElement("Transform")
        {
            Function = Bivariate(),
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        Assert.IsTrue(wired.Validate().IsValid);
    }

    /// <summary>
    /// Verifies the secondary connection triple serializes under its kind names, resolves
    /// pending after load, and re-links through the clone map — the same contract the primary
    /// input carries.
    /// </summary>
    [TestMethod]
    public void Test_SecondaryInput_SerializationAndCloneRemap()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var original = new TransformElement("Transform")
        {
            Function = Bivariate(),
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        var raised = new List<string>();
        original.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        original.SecondaryInput = new RiskConnection(hazard, 1);   // equal — no raise

        // Act
        var xml = original.ToXElement();

        // Assert — the secondary triple beside the primary triple.
        CollectionAssert.AreEqual(Array.Empty<string>(), raised);
        Assert.AreEqual(hazard.Id.ToString("D"), xml.Attribute("SecondarySourceElementId")!.Value);
        Assert.AreEqual("Hazard", xml.Attribute("SecondarySourceElement")!.Value);
        Assert.AreEqual("1", xml.Attribute("SecondarySourcePort")!.Value);

        // Pending until the graph resolves; then live.
        var restored = new TransformElement(xml);
        Assert.IsNull(restored.SecondaryInput);
        restored.ResolveDeserializedReferences(ResolverOver(hazard));
        Assert.AreSame(hazard, restored.SecondaryInput!.Source);
        Assert.AreEqual(1, restored.SecondaryInput.SourcePort);

        // The clone remaps the secondary connection through the original→clone map.
        var hazardClone = (HazardElement)hazard.Clone();
        var clone = (TransformElement)original.Clone();
        Assert.IsNull(clone.SecondaryInput);
        clone.ResolveClonedConnections(original,
            new Dictionary<IRiskElement, IRiskElement> { [hazard] = hazardClone });
        Assert.AreSame(hazardClone, clone.Input!.Source);
        Assert.AreSame(hazardClone, clone.SecondaryInput!.Source);
        Assert.AreEqual(1, clone.SecondaryInput.SourcePort);
    }

    /// <summary>
    /// Verifies a univariate element's serialized form carries no secondary attributes — the
    /// byte-compatibility guarantee for every existing model.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_NoSecondary_NoNewAttributes()
    {
        // Arrange
        var element = new TransformElement("Transform")
        {
            Function = Rating(),
            Input = new RiskConnection(new HazardElement("Hazard")),
        };

        // Act
        var xml = element.ToXElement();

        // Assert
        Assert.IsNull(xml.Attribute("SecondarySourceElementId"));
        Assert.IsNull(xml.Attribute("SecondarySourceElement"));
        Assert.IsNull(xml.Attribute("SecondarySourcePort"));
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

    /// <summary>
    /// Verifies an <b>inline</b> composite transform resolves its own <b>by-reference</b> children:
    /// the element must thread the resolver into the inline factory, not hand it a bare method
    /// group. Without the threading the nested references silently load as null children.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_InlineComposite_ResolvesByReferenceChildren()
    {
        // Arrange — a store of two stored candidate rating curves and a resolver over it.
        var ratingA = new LinearTransform
        {
            Name = "Rating A",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            IsUncertain = false,
        };
        var ratingB = new LinearTransform
        {
            Name = "Rating B",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            Alpha = 2d,
            IsUncertain = false,
        };
        var store = new IRiskFunction[] { ratingA, ratingB }.ToDictionary(f => f.Id);
        var resolver = new RMC.TotalRisk.RiskFunctions.RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));

        var composite = new CompositeTransform(new[]
        {
            new WeightedTransformFunction(ratingA, 0.4d),
            new WeightedTransformFunction(ratingB, 0.6d),
        })
        {
            Name = "Blended rating",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
        };

        // The element writes the composite inline; the composite writes its children as markers.
        var element = new TransformElement("Transform") { Function = composite };
        var form = element.ToXElement();
        form.Element("Function")!.Elements().First()
            .ReplaceWith(composite.ToXElement(RMC.TotalRisk.Core.Enums.RiskSerializationMode.ByReference));

        // Act
        var restored = new TransformElement(form, resolver);

        // Assert — the nested markers resolved back to the live stored instances.
        var restoredComposite = (CompositeTransform)restored.Function!;
        Assert.AreSame(ratingA, restoredComposite.TransformFunctions[0].TransformFunction);
        Assert.AreSame(ratingB, restoredComposite.TransformFunctions[1].TransformFunction);
        Assert.IsTrue(restoredComposite.Validate().IsValid);
    }
}
