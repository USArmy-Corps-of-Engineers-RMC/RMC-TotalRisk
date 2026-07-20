using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Systems.Components;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components;

/// <summary>
/// Cross-cutting tests for the two serialization modes — the seam that lets a consuming layer
/// store input functions once, in their own right, and have risk graphs point at them instead of
/// embedding stale copies.
/// </summary>
/// <remarks>
/// The headline guarantee is that the mode is a <b>persistence</b> concern only: a component's
/// canonical hash, and therefore every Monte Carlo seed derived from it, is identical whichever
/// mode its graph was written in. The self-contained path is asserted to be byte-for-byte
/// unchanged, because headless callers, verification oracles, and the API all depend on it.
/// </remarks>
[TestClass]
public class SerializationModeTests
{
    /// <summary>A store of risk functions, standing in for a consuming layer's project.</summary>
    private sealed class FunctionStore
    {
        /// <summary>The stored functions, in insertion order.</summary>
        private readonly List<IRiskFunction> _functions = new List<IRiskFunction>();

        /// <summary>Adds a function to the store and returns it.</summary>
        /// <typeparam name="T">The concrete function type.</typeparam>
        /// <param name="function">The function to store.</param>
        /// <returns>The stored function.</returns>
        public T Add<T>(T function) where T : IRiskFunction
        {
            _functions.Add(function);
            return function;
        }

        /// <summary>A resolver over this store, as the consuming layer would supply.</summary>
        /// <returns>The resolver.</returns>
        public RiskFunctionResolver Resolver()
        {
            return new RiskFunctionResolver(
                id => _functions.FirstOrDefault(f => f.Id == id),
                name => _functions.FirstOrDefault(f => f.Name == name));
        }
    }

    /// <summary>Builds a labeled deterministic transform between two hazard types.</summary>
    /// <param name="fromHazard">The input hazard label.</param>
    /// <param name="toHazard">The output hazard label.</param>
    /// <returns>The transform.</returns>
    private static TabularTransform Rating(string fromHazard, string toHazard)
    {
        return new TabularTransform
        {
            Name = $"{fromHazard} to {toHazard}",
            SpecifiedHazard = fromHazard,
            HazardUnit = "cfs",
            TransformedHazard = toHazard,
            TransformedHazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(50d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>
    /// Builds the levee scenario — flow hazard, rating, breach response, and both a failure and a
    /// non-failure consequence path — with every function registered in a store, as a consuming
    /// layer would hold them.
    /// </summary>
    /// <param name="store">Receives the scenario's functions.</param>
    /// <returns>The component.</returns>
    private static SystemComponent LeveeComponent(FunctionStore store)
    {
        var component = new SystemComponent { Name = "Levee" };

        var hazard = new HazardElement("Hazard")
        {
            Function = store.Add(new TabularHazard { Name = "Flow Frequency", SpecifiedHazard = "Flow", HazardUnit = "cfs" }),
        };
        var rating = new TransformElement("Rating")
        {
            Function = store.Add(Rating("Flow", "Stage")),
            Input = new RiskConnection(hazard),
        };
        var response = new ResponseElement("Breach")
        {
            Function = store.Add(new TabularResponse { Name = "Fragility", SpecifiedHazard = "Stage", HazardUnit = "ft" }),
            Input = new RiskConnection(rating),
        };
        var fail = new ConsequenceElement("Failure Damages") { Input = new RiskConnection(response) };
        fail.AddFunction(store.Add(Damages("Failure Damages")));
        fail.AddFunction(store.Add(Damages("Failure Life Loss", "Life Loss", "lives")));
        var nonFail = new ConsequenceElement("Non-Failure Damages") { Input = new RiskConnection(rating) };
        nonFail.AddFunction(store.Add(Damages("Non-Failure Damages")));
        nonFail.AddFunction(store.Add(Damages("Non-Failure Life Loss", "Life Loss", "lives")));

        component.Graph.AddElement(hazard);
        component.Graph.AddElement(rating);
        component.Graph.AddElement(response);
        component.Graph.AddElement(fail);
        component.Graph.AddElement(nonFail);
        return component;
    }

    /// <summary>Builds a labeled tabular consequence.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="type">The consequence type label.</param>
    /// <param name="typeUnit">The consequence unit.</param>
    /// <returns>The consequence function.</returns>
    private static TabularConsequence Damages(string name, string type = "Damages", string typeUnit = "$")
    {
        return new TabularConsequence
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = type,
            ConsequenceUnit = typeUnit,
        };
    }

    /// <summary>
    /// THE headline invariant: the persistence mode cannot move a canonical hash. The component
    /// hashes its projected failure modes, which always carry their functions inline, so a graph
    /// stored by reference seeds identically to one stored self-contained. If this ever fails,
    /// a project's Monte Carlo results depend on how the project was saved.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_IsIdenticalAcrossSerializationModes()
    {
        // Arrange
        var store = new FunctionStore();
        var component = LeveeComponent(store);
        byte[] baseline = component.CanonicalHash();

        // Act — round-trip through each mode.
        var selfContained = new SystemComponent(component.ToXElement(RiskSerializationMode.SelfContained));
        var byReference = new SystemComponent(
            component.ToXElement(RiskSerializationMode.ByReference), store.Resolver());

        // Assert
        CollectionAssert.AreEqual(baseline, selfContained.CanonicalHash());
        CollectionAssert.AreEqual(baseline, byReference.CanonicalHash(),
            "The serialization mode is a persistence concern and must never move a seed.");
    }

    /// <summary>
    /// Verifies the by-reference form carries no function content — only ids and names — so a
    /// stored function has exactly one copy of its content, in the store.
    /// </summary>
    [TestMethod]
    public void Test_ByReference_WritesReferencesInsteadOfContent()
    {
        // Arrange
        var store = new FunctionStore();
        var component = LeveeComponent(store);

        // Act
        var xml = component.ToXElement(RiskSerializationMode.ByReference);

        // Assert — no concrete function element survives anywhere in the tree.
        var names = xml.Descendants().Select(e => e.Name.LocalName).ToList();
        Assert.IsFalse(names.Contains(nameof(TabularHazard)));
        Assert.IsFalse(names.Contains(nameof(TabularTransform)));
        Assert.IsFalse(names.Contains(nameof(TabularResponse)));
        Assert.IsFalse(names.Contains(nameof(TabularConsequence)));

        // One reference per wrapped function: four single-function elements' worth plus four
        // ordered consequences across the two terminals.
        var references = xml.Descendants("FunctionReference").ToList();
        Assert.AreEqual(7, references.Count);
        foreach (var reference in references)
        {
            Assert.IsTrue(Guid.TryParse(reference.Attribute("Id")?.Value, out _));
            Assert.IsFalse(string.IsNullOrEmpty(reference.Attribute("Name")?.Value));
        }

        // The self-contained form still carries the content, unchanged.
        Assert.IsTrue(component.ToXElement().Descendants().Any(e => e.Name.LocalName == nameof(TabularHazard)));
    }

    /// <summary>
    /// The property that makes the whole seam worthwhile: a by-reference round-trip re-attaches
    /// the SAME function instances, so an edit made where a function is stored is seen by every
    /// graph using it. An embedded copy would silently shadow that edit.
    /// </summary>
    [TestMethod]
    public void Test_ByReference_RoundTripReattachesTheLiveInstances()
    {
        // Arrange
        var store = new FunctionStore();
        var component = LeveeComponent(store);
        var originals = component.GetReferencedFunctions().ToList();

        // Act
        var restored = new SystemComponent(
            component.ToXElement(RiskSerializationMode.ByReference), store.Resolver());
        var reattached = restored.GetReferencedFunctions().ToList();

        // Assert
        Assert.AreEqual(7, originals.Count);
        CollectionAssert.AreEqual(originals, reattached);
        for (int i = 0; i < originals.Count; i++)
        {
            Assert.AreSame(originals[i], reattached[i],
                "A by-reference round-trip must re-attach the live stored function, not a copy.");
        }

        // A self-contained round-trip is the opposite by design: isolated copies.
        var isolated = new SystemComponent(component.ToXElement()).GetReferencedFunctions().ToList();
        Assert.AreNotSame(originals[0], isolated[0]);
    }

    /// <summary>
    /// Verifies the resolver policy matches the element resolver's: a serialized id is
    /// authoritative and throws when stale, because a dangling persistent reference means the
    /// stored form is inconsistent and silently dropping content would lose it on the next save.
    /// </summary>
    [TestMethod]
    public void Test_StaleFunctionId_Throws()
    {
        // Arrange — persist by reference, then resolve against an empty store.
        var store = new FunctionStore();
        var component = LeveeComponent(store);
        var xml = component.ToXElement(RiskSerializationMode.ByReference);
        var emptyStore = new FunctionStore();

        // Act / Assert
        var exception = Assert.ThrowsException<InvalidOperationException>(
            () => new SystemComponent(xml, emptyStore.Resolver()));
        StringAssert.Contains(exception.Message, "is not available");
    }

    /// <summary>
    /// Verifies a name-only reference that finds nothing is lenient — it resolves to null and is
    /// reported by validation as an unresolved reference, naming it, rather than as the misleading
    /// "no function assigned".
    /// </summary>
    [TestMethod]
    public void Test_UnresolvedNameReference_IsReportedByValidation()
    {
        // Arrange — a hand-authored reference carrying only a name, resolved against an empty store.
        var element = new HazardElement("Hazard");
        var xml = new XElement(nameof(HazardElement),
            new XAttribute("Name", "Hazard"),
            new XElement("Function", new XElement("FunctionReference", new XAttribute("Name", "Missing Curve"))));

        // Act
        var restored = new HazardElement(xml, new FunctionStore().Resolver());
        var (isValid, messages) = restored.Validate();

        // Assert
        Assert.IsNull(restored.Function);
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("Missing Curve")),
            "The unresolved reference must be named so a user can act on it.");
        Assert.IsFalse(messages.Any(m => m.Contains("has no hazard function assigned")),
            "An unresolved reference is not the same as an unassigned function.");
    }

    /// <summary>
    /// Verifies that reading a by-reference form with no resolver degrades safely: the references
    /// are recorded as unresolved and reported, rather than silently producing an empty graph that
    /// looks authored.
    /// </summary>
    [TestMethod]
    public void Test_ByReferenceWithoutResolver_ReportsUnresolvedReferences()
    {
        // Arrange
        var store = new FunctionStore();
        var component = LeveeComponent(store);

        // Act
        var restored = new SystemComponent(component.ToXElement(RiskSerializationMode.ByReference));

        // Assert
        Assert.AreEqual(0, restored.GetReferencedFunctions().Count());
        var (isValid, messages) = restored.Graph.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("Flow Frequency")));
    }

    /// <summary>
    /// Verifies a function's own edits propagate out through the element and the graph, so a
    /// consuming layer can invalidate dependent results when a shared input changes. Without this,
    /// editing a stored hazard curve would leave every analysis using it silently stale.
    /// </summary>
    [TestMethod]
    public void Test_EditingAWrappedFunction_NotifiesElementAndGraph()
    {
        // Arrange
        var hazard = new TabularHazard { Name = "Flow Frequency", SpecifiedHazard = "Flow", HazardUnit = "cfs" };
        var element = new HazardElement("Hazard") { Function = hazard };
        var graph = new ComponentGraph();
        graph.AddElement(element);

        var elementRaised = new List<string>();
        var graphRaised = new List<string>();
        element.PropertyChanged += (_, e) => elementRaised.Add(e.PropertyName ?? "");
        graph.PropertyChanged += (_, e) => graphRaised.Add(e.PropertyName ?? "");

        // Act
        hazard.Description = "Updated after the 2026 study.";

        // Assert
        CollectionAssert.Contains(elementRaised, "Function");
        Assert.AreNotEqual(0, graphRaised.Count, "The graph must forward a wrapped function's change.");
    }

    /// <summary>
    /// Verifies replacing a wrapped function detaches the old subscription, so a function no
    /// longer used by an element stops notifying it.
    /// </summary>
    [TestMethod]
    public void Test_ReplacingAWrappedFunction_DetachesTheOldSubscription()
    {
        // Arrange
        var first = new TabularHazard { Name = "First" };
        var second = new TabularHazard { Name = "Second" };
        var element = new HazardElement("Hazard") { Function = first };
        element.Function = second;

        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        // Act
        first.Description = "No longer wired to the element.";

        // Assert
        Assert.AreEqual(0, raised.Count);

        // Act — the current function still notifies.
        second.Description = "Still wired.";

        // Assert
        CollectionAssert.Contains(raised, "Function");
    }

    /// <summary>
    /// A model-only end-to-end smoke test: build a complete, valid system from the model library
    /// alone — no store, no resolver, no consuming layer anywhere in the call path — using the
    /// authoring conveniences a graph editor would call. This is the "usable headless" guarantee.
    /// </summary>
    [TestMethod]
    public void Test_ModelOnly_BuildsValidatesAndHashesWithNoConsumingLayer()
    {
        // Arrange / Act — author the graph purely through the factory and object model.
        var component = new SystemComponent { Name = "Dam" };
        var hazard = RiskElementFactory.CreateForFunction(
            new TabularHazard { Name = "Pool Frequency", SpecifiedHazard = "Pool", HazardUnit = "ft" });
        var response = RiskElementFactory.Create(RiskElementType.Response, "Overtopping");
        Assert.IsTrue(response.TryAssignFunction(
            new TabularResponse { Name = "Fragility", SpecifiedHazard = "Pool", HazardUnit = "ft" }, out _));
        var consequence = RiskElementFactory.Create(RiskElementType.Consequence, "Damages");
        Assert.IsTrue(consequence.TryAssignFunction(Damages("Damage curve"), out _));

        ((ResponseElement)response).Input = new RiskConnection(hazard);
        ((ConsequenceElement)consequence).Input = new RiskConnection(response);

        component.Graph.AddElement(hazard);
        component.Graph.AddElement(response);
        component.Graph.AddElement(consequence);

        // Assert — valid, projects one failure mode, hashes stably, and round-trips standalone.
        var (isValid, messages) = component.Validate();
        Assert.IsTrue(isValid, string.Join(" | ", messages));
        Assert.AreEqual(1, component.FailureModes.Count);
        CollectionAssert.AreEqual(component.CanonicalHash(), component.CanonicalHash());
        CollectionAssert.AreEqual(
            component.CanonicalHash(),
            new SystemComponent(component.ToXElement()).CanonicalHash());
    }

    /// <summary>
    /// Verifies the authoring helpers report a cluster mismatch instead of throwing — dropping the
    /// wrong function onto a node is ordinary user error in a graph editor, not a defect.
    /// </summary>
    [TestMethod]
    public void Test_TryAssignFunction_ReportsClusterMismatch()
    {
        // Arrange
        var element = RiskElementFactory.Create(RiskElementType.Response, "Breach");

        // Act
        bool assigned = element.TryAssignFunction(new TabularHazard { Name = "Flow Frequency" }, out string error);

        // Assert
        Assert.IsFalse(assigned);
        StringAssert.Contains(error, "TabularHazard");
        StringAssert.Contains(error, "a response function");
        Assert.AreEqual(0, element.GetFunctions().Count());
    }

    /// <summary>
    /// Verifies <see cref="RiskElementFactory.CreateForFunction"/> maps every cluster onto its
    /// element type — the mapping a graph editor would otherwise re-implement and drift on.
    /// </summary>
    [TestMethod]
    public void Test_CreateForFunction_MapsEveryCluster()
    {
        // Act / Assert
        Assert.AreEqual(RiskElementType.Hazard,
            RiskElementFactory.CreateForFunction(new TabularHazard { Name = "H" }).ElementType);
        Assert.AreEqual(RiskElementType.Transform,
            RiskElementFactory.CreateForFunction(Rating("Flow", "Stage")).ElementType);
        Assert.AreEqual(RiskElementType.Response,
            RiskElementFactory.CreateForFunction(new TabularResponse { Name = "R" }).ElementType);

        var consequence = RiskElementFactory.CreateForFunction(Damages("C"));
        Assert.AreEqual(RiskElementType.Consequence, consequence.ElementType);
        Assert.AreEqual(1, consequence.GetFunctions().Count());

        // The element takes the function's name by default, and an explicit name overrides it.
        Assert.AreEqual("H", RiskElementFactory.CreateForFunction(new TabularHazard { Name = "H" }).Name);
        Assert.AreEqual("Root", RiskElementFactory.CreateForFunction(new TabularHazard { Name = "H" }, "Root").Name);
    }

    /// <summary>
    /// Verifies the dependency set a consuming layer persists alongside a by-reference component:
    /// distinct functions, in graph declared order.
    /// </summary>
    [TestMethod]
    public void Test_GetReferencedFunctions_IsDistinctAndInGraphOrder()
    {
        // Arrange — the same consequence function used by two terminals must appear once.
        var shared = Damages("Shared Damages");
        var component = new SystemComponent { Name = "Shared" };
        var hazard = new HazardElement("Hazard") { Function = new TabularHazard { Name = "Flow Frequency" } };
        var response = new ResponseElement("Breach")
        {
            Function = new TabularResponse { Name = "Fragility" },
            Input = new RiskConnection(hazard),
        };
        var fail = new ConsequenceElement("Failure") { Input = new RiskConnection(response) };
        fail.AddFunction(shared);
        var nonFail = new ConsequenceElement("Non-Failure") { Input = new RiskConnection(hazard) };
        nonFail.AddFunction(shared);

        component.Graph.AddElement(hazard);
        component.Graph.AddElement(response);
        component.Graph.AddElement(fail);
        component.Graph.AddElement(nonFail);

        // Act
        var referenced = component.GetReferencedFunctions().ToList();

        // Assert
        Assert.AreEqual(3, referenced.Count);
        Assert.AreEqual("Flow Frequency", referenced[0].Name);
        Assert.AreEqual("Fragility", referenced[1].Name);
        Assert.AreSame(shared, referenced[2]);
    }
}
