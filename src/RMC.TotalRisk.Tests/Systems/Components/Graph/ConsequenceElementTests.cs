using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="ConsequenceElement"/> — the input-only terminal: the ordered
/// multi-type function list, the hazard-source binding override, serialization, and cloning.
/// </summary>
[TestClass]
public class ConsequenceElementTests
{
    /// <summary>Builds a valid labeled tabular consequence on the default table.</summary>
    private static TabularConsequence Damages(string type = "Damages", string unit = "$")
    {
        return new TabularConsequence
        {
            Name = $"{type} curve",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = type,
            ConsequenceUnit = unit,
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

    /// <summary>Verifies the sink-analog shape: one input, no outputs, empty function list.</summary>
    [TestMethod]
    public void Test_Defaults_SinkShape()
    {
        // Act
        var element = new ConsequenceElement("Damages");

        // Assert
        Assert.AreEqual(1, element.InputCount);
        Assert.AreEqual(0, element.OutputCount);
        Assert.AreEqual(0, element.Functions.Count);
        Assert.IsNull(element.Input);
        Assert.IsNull(element.HazardSource);
    }

    /// <summary>Verifies list assignment coercion, notification, and function enumeration.</summary>
    [TestMethod]
    public void Test_Functions_AssignmentAndEnumeration()
    {
        // Arrange
        var element = new ConsequenceElement("Damages");
        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        element.Functions = new List<IConsequenceFunction> { Damages(), Damages("Life Loss", "lives") };
        element.Functions = null!;   // coerces to empty and notifies

        // Assert
        CollectionAssert.AreEqual(
            new[] { nameof(ConsequenceElement.Functions), nameof(ConsequenceElement.Functions) }, raised);
        Assert.AreEqual(0, element.Functions.Count);

        // Enumeration skips null entries.
        element.Functions.Add(Damages());
        element.Functions.Add(null!);
        Assert.AreEqual(1, element.GetFunctions().Count());
    }

    /// <summary>Verifies the structural enumeration excludes the binding (not a path edge).</summary>
    [TestMethod]
    public void Test_GetInputConnections_StructuralOnly()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var response = new ResponseElement("Breach");
        var element = new ConsequenceElement("Damages")
        {
            Input = new RiskConnection(response),
            HazardSource = new RiskConnection(hazard),
        };

        // Assert — only the structural input is a path edge; the binding stays a typed property.
        Assert.AreSame(response, element.GetInputConnections().Single().Source);
    }

    /// <summary>Verifies the validation matrix: empty list, null entries, duplicate-type warning.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Valid: named with one valid function.
        var valid = new ConsequenceElement("Damages");
        valid.Functions.Add(Damages());
        Assert.IsTrue(valid.Validate().IsValid);

        // No functions is an error.
        Assert.IsFalse(new ConsequenceElement("Damages").Validate().IsValid);

        // A null entry is an error.
        var withNull = new ConsequenceElement("Damages");
        withNull.Functions.Add(Damages());
        withNull.Functions.Add(null!);
        Assert.IsFalse(withNull.Validate().IsValid);

        // Duplicate consequence-type labels warn (types are positional).
        var duplicate = new ConsequenceElement("Damages");
        duplicate.Functions.Add(Damages());
        duplicate.Functions.Add(Damages());
        var (duplicateValid, duplicateMessages) = duplicate.Validate();
        Assert.IsTrue(duplicateValid);
        Assert.IsTrue(duplicateMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("multiple consequence functions labeled")));
    }

    /// <summary>Verifies serialization: ordered functions, both connection triples, resolution.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange — a two-type terminal bound to the raw hazard.
        var hazard = new HazardElement("Hazard");
        var response = new ResponseElement("Breach");
        var original = new ConsequenceElement("Damages")
        {
            Input = new RiskConnection(response),
            HazardSource = new RiskConnection(hazard),
        };
        original.Functions.Add(Damages());
        original.Functions.Add(Damages("Life Loss", "lives"));

        // Act
        var xml = original.ToXElement();

        // Assert — the binding serializes under its own kind names.
        Assert.AreEqual(response.Id.ToString("D"), xml.Attribute("SourceElementId")!.Value);
        Assert.AreEqual(hazard.Id.ToString("D"), xml.Attribute("HazardSourceElementId")!.Value);
        Assert.AreEqual("Hazard", xml.Attribute("HazardSourceElement")!.Value);
        Assert.AreEqual("0", xml.Attribute("HazardSourcePort")!.Value);

        // The restored element resolves both references and preserves function order.
        var restored = new ConsequenceElement(xml);
        restored.ResolveDeserializedReferences(ResolverOver(hazard, response));
        Assert.AreSame(response, restored.Input!.Source);
        Assert.AreSame(hazard, restored.HazardSource!.Source);
        Assert.AreEqual(2, restored.Functions.Count);
        Assert.AreEqual("Life Loss", restored.Functions[1].SpecifiedConsequence);
        CollectionAssert.AreEqual(original.Functions[0].CanonicalHash(), restored.Functions[0].CanonicalHash());

        // An unknown function child throws (no silent content loss).
        var badFunction = new XElement(nameof(ConsequenceElement),
            new XElement(nameof(ConsequenceElement.Functions), new XElement("Bogus")));
        Assert.ThrowsException<InvalidOperationException>(() => new ConsequenceElement(badFunction));
    }

    /// <summary>Verifies cloning: shared Id, deep function copies, both connections re-linked.</summary>
    [TestMethod]
    public void Test_Clone_And_ResolveClonedConnections()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var original = new ConsequenceElement("Damages") { HazardSource = new RiskConnection(hazard) };
        original.Functions.Add(Damages());

        // Act
        var hazardClone = (HazardElement)hazard.Clone();
        var clone = (ConsequenceElement)original.Clone();
        clone.ResolveClonedConnections(original,
            new Dictionary<IRiskElement, IRiskElement> { [hazard] = hazardClone });

        // Assert
        Assert.AreEqual(original.Id, clone.Id);
        Assert.AreEqual(1, clone.Functions.Count);
        Assert.AreNotSame(original.Functions[0], clone.Functions[0]);
        CollectionAssert.AreEqual(original.Functions[0].CanonicalHash(), clone.Functions[0].CanonicalHash());
        Assert.AreSame(hazardClone, clone.HazardSource!.Source);
        Assert.IsNull(clone.Input);
    }
}
