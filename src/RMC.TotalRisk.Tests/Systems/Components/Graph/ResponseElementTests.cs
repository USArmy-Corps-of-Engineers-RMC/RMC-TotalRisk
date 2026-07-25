using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="ResponseElement"/> — ports, the sentinel-wrap guard, the reserved
/// bivariate secondary input, connection serialization, and cloning.
/// </summary>
[TestClass]
public class ResponseElementTests
{
    /// <summary>Builds a valid labeled tabular response on the default table.</summary>
    private static TabularResponse Fragility()
    {
        return new TabularResponse { Name = "Fragility", SpecifiedHazard = "Stage", HazardUnit = "ft" };
    }

    /// <summary>Builds a resolver over a fixed element set.</summary>
    private static RiskElementResolver ResolverOver(params IRiskElement[] elements)
    {
        var list = new List<IRiskElement>(elements);
        return new RiskElementResolver(
            id => list.Find(e => e.Id == id),
            name => list.Find(e => string.Equals(e.Name, name, StringComparison.Ordinal)));
    }

    /// <summary>Verifies the shape: one input, the two branch ports, reserved secondary.</summary>
    [TestMethod]
    public void Test_Defaults_UnivariateShape()
    {
        // Act
        var element = new ResponseElement("Breach");

        // Assert — port 0 = Fail, port 1 = Non-Fail (arch doc §7.9, Phase 6.7).
        Assert.AreEqual(1, element.InputCount);
        Assert.AreEqual(2, element.OutputCount);
        Assert.IsNull(element.Function);
        Assert.IsNull(element.Input);
        Assert.IsNull(element.SecondaryInput);
    }

    /// <summary>Verifies both populated inputs enumerate.</summary>
    [TestMethod]
    public void Test_GetInputConnections_EnumeratesBoth()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var element = new ResponseElement("Breach")
        {
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard, 1),
        };

        // Assert
        Assert.AreEqual(2, element.GetInputConnections().Count());
    }

    /// <summary>Verifies the validation matrix: sentinel wrap and reserved secondary input.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Valid: named with a valid univariate response.
        Assert.IsTrue(new ResponseElement("Breach") { Function = Fragility() }.Validate().IsValid);

        // Missing function.
        Assert.IsFalse(new ResponseElement("Breach").Validate().IsValid);

        // The non-failure sentinel is never wrapped in a response element.
        var sentinel = new ResponseElement("Breach") { Function = new NonFailResponse() };
        var (sentinelValid, sentinelMessages) = sentinel.Validate();
        Assert.IsFalse(sentinelValid);
        Assert.IsTrue(sentinelMessages.Any(m => m.Contains("non-failure response sentinel")));

        // The secondary input is reserved until bivariate responses land (Phase 11).
        var secondary = new ResponseElement("Breach")
        {
            Function = Fragility(),
            SecondaryInput = new RiskConnection(new HazardElement("Hazard"), 1),
        };
        var (secondaryValid, secondaryMessages) = secondary.Validate();
        Assert.IsFalse(secondaryValid);
        Assert.IsTrue(secondaryMessages.Any(m => m.Contains("reserved for bivariate")));
    }

    /// <summary>Verifies connection serialization including the reserved secondary triple.</summary>
    [TestMethod]
    public void Test_Serialization_ConnectionRoundTrip()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var original = new ResponseElement("Breach")
        {
            Function = Fragility(),
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard, 1),
        };

        // Act
        var xml = original.ToXElement();

        // Assert — both connection triples serialize under their kind names.
        Assert.AreEqual(hazard.Id.ToString("D"), xml.Attribute("SourceElementId")!.Value);
        Assert.AreEqual(hazard.Id.ToString("D"), xml.Attribute("SecondarySourceElementId")!.Value);
        Assert.AreEqual("1", xml.Attribute("SecondarySourcePort")!.Value);

        // Both resolve after load.
        var restored = new ResponseElement(xml);
        restored.ResolveDeserializedReferences(ResolverOver(hazard));
        Assert.AreSame(hazard, restored.Input!.Source);
        Assert.AreSame(hazard, restored.SecondaryInput!.Source);
        Assert.AreEqual(1, restored.SecondaryInput.SourcePort);
        CollectionAssert.AreEqual(original.Function!.CanonicalHash(), restored.Function!.CanonicalHash());

        // An unknown wrapped function throws.
        var badFunction = new XElement(nameof(ResponseElement),
            new XElement(nameof(ResponseElement.Function), new XElement("Bogus")));
        Assert.ThrowsException<InvalidOperationException>(() => new ResponseElement(badFunction));
    }

    /// <summary>Verifies cloning: shared Id, deep function copy, both connections re-linked.</summary>
    [TestMethod]
    public void Test_Clone_And_ResolveClonedConnections()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var original = new ResponseElement("Breach")
        {
            Function = Fragility(),
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard, 1),
        };

        // Act
        var hazardClone = (HazardElement)hazard.Clone();
        var clone = (ResponseElement)original.Clone();
        clone.ResolveClonedConnections(original,
            new Dictionary<IRiskElement, IRiskElement> { [hazard] = hazardClone });

        // Assert
        Assert.AreEqual(original.Id, clone.Id);
        Assert.AreNotSame(original.Function, clone.Function);
        Assert.AreSame(hazardClone, clone.Input!.Source);
        Assert.AreSame(hazardClone, clone.SecondaryInput!.Source);
        Assert.AreEqual(1, clone.SecondaryInput.SourcePort);
    }

    /// <summary>
    /// Verifies an <b>inline</b> composite response resolves its own <b>by-reference</b> children:
    /// the element must thread the resolver into the inline factory, not hand it a bare method
    /// group. Without the threading the nested references silently load as null children.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_InlineComposite_ResolvesByReferenceChildren()
    {
        // Arrange — a store of two stored child fragilities and a resolver over it.
        var overtopping = new TabularResponse { Name = "Overtopping", SpecifiedHazard = "Stage", HazardUnit = "ft" };
        var piping = new TabularResponse { Name = "Piping", SpecifiedHazard = "Stage", HazardUnit = "ft" };
        var store = new IRiskFunction[] { overtopping, piping }.ToDictionary(f => f.Id);
        var resolver = new RMC.TotalRisk.RiskFunctions.RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));

        var composite = new CompositeResponse(new[]
        {
            new WeightedResponseFunction(overtopping, 0.45d),
            new WeightedResponseFunction(piping, 0.55d),
        })
        {
            Name = "Overtopping/Piping",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
        };

        // The element writes the composite inline; the composite writes its children as markers.
        var element = new ResponseElement("Response") { Function = composite };
        var form = element.ToXElement();
        form.Element("Function")!.Elements().First()
            .ReplaceWith(composite.ToXElement(RMC.TotalRisk.Core.Enums.RiskSerializationMode.ByReference));

        // Act
        var restored = new ResponseElement(form, resolver);

        // Assert — the nested markers resolved back to the live stored instances.
        var restoredComposite = (CompositeResponse)restored.Function!;
        Assert.AreSame(overtopping, restoredComposite.ResponseFunctions[0].ResponseFunction);
        Assert.AreSame(piping, restoredComposite.ResponseFunctions[1].ResponseFunction);
        Assert.IsTrue(restoredComposite.Validate().IsValid);
    }
}
