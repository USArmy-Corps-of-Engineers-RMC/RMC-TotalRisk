using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

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

    /// <summary>Builds a valid labeled bivariate hazard over two tabular marginals.</summary>
    private static BivariateHazard ValidBivariateHazard()
    {
        return new BivariateHazard(
            new TabularHazard { Name = "Surge Frequency", SpecifiedHazard = "Surge", HazardUnit = "ft" },
            new TabularHazard { Name = "Pool Frequency", SpecifiedHazard = "Pool Elevation", HazardUnit = "ft" })
        {
            Name = "Joint Hazard",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
        };
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

    /// <summary>
    /// Verifies function assignment notifies (the arity-bearing OutputCount alongside Function)
    /// and surfaces through GetFunctions.
    /// </summary>
    [TestMethod]
    public void Test_Function_AssignmentAndEnumeration()
    {
        // Arrange
        var element = new HazardElement("Hazard");
        var raised = new List<string?>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var hazard = ValidHazard();

        // Act
        element.Function = hazard;

        // Assert
        CollectionAssert.AreEqual(
            new[] { nameof(HazardElement.Function), nameof(HazardElement.OutputCount) }, raised);
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

    /// <summary>
    /// Verifies an <b>inline</b> composite hazard resolves its own <b>by-reference</b> children:
    /// the element must thread the resolver into the inline factory, not hand it a bare method
    /// group. Without the threading the nested references silently fail to resolve and the
    /// composite loads with null children.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_InlineComposite_ResolvesByReferenceChildren()
    {
        // Arrange — a store of two stored child hazards and a resolver over it.
        var rain = new TabularHazard { Name = "Rain", SpecifiedHazard = "Flow", HazardUnit = "cfs" };
        var snow = new TabularHazard { Name = "Snow", SpecifiedHazard = "Flow", HazardUnit = "cfs" };
        var store = new IRiskFunction[] { rain, snow }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));

        var composite = new CompositeHazard(new[]
        {
            new WeightedHazardFunction(rain, 0.45d),
            new WeightedHazardFunction(snow, 0.55d),
        })
        {
            Name = "Rain/Snow",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
        };

        // The element writes the composite inline; the composite writes its children as markers.
        var element = new HazardElement("Hazard") { Function = composite };
        var form = element.ToXElement();
        form.Element("Function")!.Elements().First()
            .ReplaceWith(composite.ToXElement(RiskSerializationMode.ByReference));

        // Act
        var restored = new HazardElement(form, resolver);

        // Assert — the nested markers resolved back to the live stored instances.
        var restoredComposite = (CompositeHazard)restored.Function!;
        Assert.AreSame(rain, restoredComposite.HazardFunctions[0].HazardFunction);
        Assert.AreSame(snow, restoredComposite.HazardFunctions[1].HazardFunction);
        Assert.IsTrue(restoredComposite.Validate().IsValid);
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

    /// <summary>
    /// Verifies the bivariate arity gate: the output count flips to two with a bivariate wrapped
    /// function (port 0 = primary X, port 1 = secondary Y) and back, with the arity raise.
    /// </summary>
    [TestMethod]
    public void Test_OutputCount_BivariateGate()
    {
        // Arrange
        var element = new HazardElement("Hazard") { Function = ValidHazard() };
        Assert.AreEqual(1, element.OutputCount);
        var raised = new List<string?>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act / Assert — bivariate opens port 1; univariate closes it.
        element.Function = ValidBivariateHazard();
        Assert.AreEqual(2, element.OutputCount);
        element.Function = ValidHazard();
        Assert.AreEqual(1, element.OutputCount);
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(HazardElement.Function), nameof(HazardElement.OutputCount),
                nameof(HazardElement.Function), nameof(HazardElement.OutputCount),
            }, raised);
    }

    /// <summary>
    /// Verifies the graph's port-bounds check across the gate: a connection to port 1 of a
    /// univariate hazard is a dangling-port error, and the identical wiring under a bivariate
    /// hazard passes the bounds check.
    /// </summary>
    [TestMethod]
    public void Test_Graph_PortOneBounds_FollowTheGate()
    {
        // Arrange — a terminal consuming hazard port 1 (a Secondary-bound non-failure path).
        static ComponentGraph BuildGraph(IHazardFunction hazardFunction)
        {
            var graph = new ComponentGraph();
            var hazard = new HazardElement("Hazard") { Function = hazardFunction };
            var terminal = new ConsequenceElement("Damages")
            {
                Input = new RiskConnection(hazard, 1),
            };
            terminal.Functions.Add(new RMC.TotalRisk.RiskFunctions.Consequences.TabularConsequence
            {
                Name = "Damages",
                SpecifiedHazard = "Pool Elevation",
                HazardUnit = "ft",
                SpecifiedConsequence = "Damages",
                ConsequenceUnit = "$",
            });
            graph.AddElement(hazard);
            graph.AddElement(terminal);
            return graph;
        }

        // Act / Assert — univariate: port 1 exceeds the single output.
        var univariateMessages = BuildGraph(ValidHazard()).Validate().ValidationMessages;
        Assert.IsTrue(univariateMessages.Any(m => m.Contains("references output port 1") && m.Contains("exposes 1 output(s)")));

        // Bivariate: the same wiring is within bounds (no port-bounds message).
        var bivariateMessages = BuildGraph(ValidBivariateHazard()).Validate().ValidationMessages;
        Assert.IsFalse(bivariateMessages.Any(m => m.Contains("exposes")));
    }
}
