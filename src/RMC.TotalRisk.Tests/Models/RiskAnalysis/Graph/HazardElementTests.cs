using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Models.HazardFunctions.Univariate;
using RMC.TotalRisk.Models.RiskAnalysis.Graph;

namespace RMC.TotalRisk.Tests.Models.RiskAnalysis.Graph;

/// <summary>
/// Unit tests for <see cref="HazardElement"/> — the output-only graph root: ports, function
/// ownership, validation with element context, inline serialization, and deep cloning.
/// </summary>
[TestClass]
public class HazardElementTests
{
    /// <summary>Builds a valid labeled tabular hazard (labeled defaults are valid).</summary>
    private static TabularHazard ValidHazard()
    {
        return new TabularHazard { Name = "Flow Frequency", SpecifiedHazard = "Flow", HazardUnit = "cfs" };
    }

    /// <summary>Verifies the source-analog shape: no inputs, one output, nothing wrapped.</summary>
    [TestMethod]
    public void Test_Defaults_SourceShape()
    {
        // Act
        var element = new HazardElement("Hazard");

        // Assert
        Assert.AreEqual(0, element.InputCount);
        Assert.AreEqual(1, element.OutputCount);
        Assert.IsNull(element.Function);
        Assert.AreEqual(0, element.GetInputConnections().Count());
        Assert.AreEqual(0, element.GetFunctions().Count());
    }

    /// <summary>Verifies function assignment notifies and surfaces through GetFunctions.</summary>
    [TestMethod]
    public void Test_Function_AssignmentAndEnumeration()
    {
        // Arrange
        var element = new HazardElement("Hazard");
        string? raised = null;
        element.PropertyChanged += (_, e) => raised = e.PropertyName;
        var hazard = ValidHazard();

        // Act
        element.Function = hazard;

        // Assert
        Assert.AreEqual(nameof(HazardElement.Function), raised);
        Assert.AreSame(hazard, element.GetFunctions().Single());
    }

    /// <summary>Verifies the validation matrix: name, missing function, and context aggregation.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // A named element with a valid function passes.
        var valid = new HazardElement("Hazard") { Function = ValidHazard() };
        Assert.IsTrue(valid.Validate().IsValid);

        // A missing function is an error.
        var missing = new HazardElement("Hazard");
        var (missingValid, missingMessages) = missing.Validate();
        Assert.IsFalse(missingValid);
        Assert.IsTrue(missingMessages.Any(m => m.Contains("no hazard function")));

        // Wrapped-function errors aggregate with the element name as context.
        var unlabeled = new HazardElement("Hazard") { Function = new TabularHazard() };
        var (unlabeledValid, unlabeledMessages) = unlabeled.Validate();
        Assert.IsFalse(unlabeledValid);
        Assert.IsTrue(unlabeledMessages.Any(m => m.StartsWith("Error: Element 'Hazard':", StringComparison.Ordinal)));
    }

    /// <summary>Verifies the serialization round trip: base attributes and the inline function.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = new HazardElement("Hazard")
        {
            Description = "The root.",
            LeftPosition = 10d,
            TopPosition = 20d,
            Function = ValidHazard(),
        };

        // Act
        var xml = original.ToXElement();
        var restored = new HazardElement(xml);

        // Assert — identity, metadata, and the wrapped function all round-trip.
        Assert.AreEqual(nameof(HazardElement), xml.Name.LocalName);
        Assert.AreEqual(original.Id, restored.Id);
        Assert.AreEqual(original.Name, restored.Name);
        Assert.AreEqual(original.Description, restored.Description);
        Assert.AreEqual(original.LeftPosition, restored.LeftPosition, 0d);
        Assert.AreEqual(original.TopPosition, restored.TopPosition, 0d);
        Assert.IsNotNull(restored.Function);
        CollectionAssert.AreEqual(original.Function!.CanonicalHash(), restored.Function!.CanonicalHash(),
            "The wrapped function must round-trip to the same canonical hash.");
    }

    /// <summary>Verifies a missing Id attribute yields a fresh identity (permissive read).</summary>
    [TestMethod]
    public void Test_Serialization_MissingId_GetsFreshGuid()
    {
        // Act
        var restored = new HazardElement(new XElement(nameof(HazardElement)));

        // Assert
        Assert.AreNotEqual(Guid.Empty, restored.Id);
    }

    /// <summary>Verifies an unreconstructable wrapped function throws (no silent content loss).</summary>
    [TestMethod]
    public void Test_Serialization_UnknownFunction_Throws()
    {
        // Arrange
        var xml = new XElement(nameof(HazardElement),
            new XElement(nameof(HazardElement.Function), new XElement("BogusHazard")));

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => new HazardElement(xml));
        Assert.ThrowsException<ArgumentNullException>(() => new HazardElement((XElement)null!));
    }

    /// <summary>Verifies the clone deep-copies the wrapped function.</summary>
    [TestMethod]
    public void Test_Clone_DeepCopiesFunction()
    {
        // Arrange
        var original = new HazardElement("Hazard") { Function = ValidHazard() };

        // Act
        var clone = (HazardElement)original.Clone();

        // Assert — same content, distinct instances.
        Assert.IsNotNull(clone.Function);
        Assert.AreNotSame(original.Function, clone.Function);
        CollectionAssert.AreEqual(original.Function!.CanonicalHash(), clone.Function!.CanonicalHash());
    }
}
