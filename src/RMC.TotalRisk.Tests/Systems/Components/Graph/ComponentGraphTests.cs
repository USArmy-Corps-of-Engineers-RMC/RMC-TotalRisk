using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="ComponentGraph"/> — membership and name authority, derived topology
/// (Kahn sort, fan-out, upstream paths, available hazard sources), the structural validation
/// matrix, construct-then-resolve serialization, and deep cloning with re-linking.
/// </summary>
[TestClass]
public class ComponentGraphTests
{
    /// <summary>Builds a valid labeled tabular hazard.</summary>
    private static TabularHazard FlowFrequency()
    {
        return new TabularHazard { Name = "Flow Frequency", SpecifiedHazard = "Flow", HazardUnit = "cfs" };
    }

    /// <summary>Builds a labeled deterministic transform between hazard types.</summary>
    private static TabularTransform Rating(string fromHazard, string fromUnit, string toHazard, string toUnit)
    {
        return new TabularTransform
        {
            Name = $"{fromHazard} to {toHazard}",
            SpecifiedHazard = fromHazard,
            HazardUnit = fromUnit,
            TransformedHazard = toHazard,
            TransformedHazardUnit = toUnit,
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(50d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a valid labeled tabular response.</summary>
    private static TabularResponse Fragility(string hazard, string unit)
    {
        return new TabularResponse { Name = "Fragility", SpecifiedHazard = hazard, HazardUnit = unit };
    }

    /// <summary>Builds a valid labeled tabular consequence.</summary>
    private static TabularConsequence Damages(string hazard, string unit, string type = "Damages", string typeUnit = "$")
    {
        return new TabularConsequence
        {
            Name = $"{type} curve",
            SpecifiedHazard = hazard,
            HazardUnit = unit,
            SpecifiedConsequence = type,
            ConsequenceUnit = typeUnit,
        };
    }

    /// <summary>
    /// Builds the levee-style graph: Flow hazard → rating (Flow→Stage) → breach response →
    /// failure damages, plus a response-free non-failure damages path off the rating.
    /// </summary>
    private static (ComponentGraph Graph, HazardElement Hazard, TransformElement Rating,
        ResponseElement Response, ConsequenceElement Fail, ConsequenceElement NonFail) LeveeGraph()
    {
        var hazard = new HazardElement("Hazard") { Function = FlowFrequency() };
        var rating = new TransformElement("Rating")
        {
            Function = Rating("Flow", "cfs", "Stage", "ft"),
            Input = new RiskConnection(hazard),
        };
        var response = new ResponseElement("Breach")
        {
            Function = Fragility("Stage", "ft"),
            Input = new RiskConnection(rating),
        };
        var fail = new ConsequenceElement("Failure Damages") { Input = new RiskConnection(response) };
        fail.Functions.Add(Damages("Stage", "ft"));
        var nonFail = new ConsequenceElement("Non-Failure Damages") { Input = new RiskConnection(rating) };
        nonFail.Functions.Add(Damages("Stage", "ft"));

        var graph = new ComponentGraph();
        graph.AddElement(hazard);
        graph.AddElement(rating);
        graph.AddElement(response);
        graph.AddElement(fail);
        graph.AddElement(nonFail);
        return (graph, hazard, rating, response, fail, nonFail);
    }

    /// <summary>Verifies membership uniqueness: instance, Id, and non-empty name.</summary>
    [TestMethod]
    public void Test_AddElement_EnforcesUniqueness()
    {
        // Arrange
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard");
        graph.AddElement(hazard);

        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => graph.AddElement(null!));
        Assert.ThrowsException<InvalidOperationException>(() => graph.AddElement(hazard));
        Assert.ThrowsException<InvalidOperationException>(() => graph.AddElement((HazardElement)hazard.Clone()));
        Assert.ThrowsException<InvalidOperationException>(() => graph.AddElement(new TransformElement("Hazard")));

        // Unnamed elements never collide (validation reports missing names instead).
        graph.AddElement(new TransformElement());
        graph.AddElement(new ConsequenceElement());
        Assert.AreEqual(3, graph.Elements.Count);
    }

    /// <summary>Verifies the attached name authority and the non-throwing rename paths.</summary>
    [TestMethod]
    public void Test_NameAuthority_RenameFlows()
    {
        // Arrange
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard");
        var rating = new TransformElement("Rating");
        graph.AddElement(hazard);
        graph.AddElement(rating);

        // A colliding rename throws (the authority is attached).
        Assert.ThrowsException<InvalidOperationException>(() => rating.Name = "Hazard");
        Assert.AreEqual("Rating", rating.Name);

        // TryRenameElement declines collisions and foreign elements, applies free names.
        Assert.IsFalse(graph.TryRenameElement(rating, "Hazard"));
        Assert.IsFalse(graph.TryRenameElement(new TransformElement("Foreign"), "Other"));
        Assert.IsTrue(graph.TryRenameElement(rating, "Stage Rating"));
        Assert.AreEqual("Stage Rating", rating.Name);

        // GetUniqueName counts up from the base name.
        Assert.AreEqual("Hazard (2)", graph.GetUniqueName("Hazard"));
        Assert.AreEqual("Fresh", graph.GetUniqueName("Fresh"));

        // Removal detaches the authority: the removed element renames freely.
        graph.RemoveElement(rating);
        rating.Name = "Hazard";
        Assert.AreEqual("Hazard", rating.Name);
    }

    /// <summary>Verifies the lookups by name, Id, and type.</summary>
    [TestMethod]
    public void Test_Lookups_ByNameIdAndType()
    {
        // Arrange
        var (graph, hazard, rating, response, fail, nonFail) = LeveeGraph();

        // Assert
        Assert.AreSame(hazard, graph.GetElement("Hazard"));
        Assert.IsNull(graph.GetElement("Absent"));
        Assert.IsNull(graph.GetElement(string.Empty));
        Assert.AreSame(rating, graph.GetElementById(rating.Id));
        Assert.IsNull(graph.GetElementById(Guid.NewGuid()));
        CollectionAssert.AreEqual(new[] { fail, nonFail }, graph.GetElements<ConsequenceElement>().ToArray());
        Assert.AreSame(response, graph.GetElements<ResponseElement>().Single());
    }

    /// <summary>Verifies the Kahn order runs upstream → downstream regardless of declared order.</summary>
    [TestMethod]
    public void Test_TopologicalSort_UpstreamFirst()
    {
        // Arrange — declare in scrambled order.
        var hazard = new HazardElement("Hazard") { Function = FlowFrequency() };
        var rating = new TransformElement("Rating") { Input = new RiskConnection(hazard) };
        var fail = new ConsequenceElement("Damages") { Input = new RiskConnection(rating) };
        var graph = new ComponentGraph();
        graph.AddElement(fail);
        graph.AddElement(rating);
        graph.AddElement(hazard);

        // Act
        bool acyclic = graph.TopologicalSort();
        var sorted = graph.SortedElements!.ToList();

        // Assert
        Assert.IsTrue(acyclic);
        Assert.IsTrue(sorted.IndexOf(hazard) < sorted.IndexOf(rating));
        Assert.IsTrue(sorted.IndexOf(rating) < sorted.IndexOf(fail));
    }

    /// <summary>Verifies cycle detection and cache invalidation on rewiring.</summary>
    [TestMethod]
    public void Test_TopologicalSort_CycleDetectionAndInvalidation()
    {
        // Arrange — a two-transform cycle.
        var a = new TransformElement("A");
        var b = new TransformElement("B");
        a.Input = new RiskConnection(b);
        b.Input = new RiskConnection(a);
        var graph = new ComponentGraph();
        graph.AddElement(a);
        graph.AddElement(b);

        // Act / Assert
        Assert.IsFalse(graph.TopologicalSort());
        Assert.IsNull(graph.SortedElements);

        // Breaking the cycle through the element property invalidates the cached sort.
        b.Input = null;
        Assert.IsTrue(graph.TopologicalSort());
        Assert.IsNotNull(graph.SortedElements);
    }

    /// <summary>Verifies the derived fan-out map.</summary>
    [TestMethod]
    public void Test_GetDownstreamElements_DerivedFanOut()
    {
        // Arrange
        var (graph, hazard, rating, response, fail, nonFail) = LeveeGraph();

        // Assert — consumers in declared order; terminals have none.
        CollectionAssert.AreEqual(new IRiskElement[] { rating }, graph.GetDownstreamElements(hazard).ToArray());
        CollectionAssert.AreEqual(new IRiskElement[] { response, nonFail }, graph.GetDownstreamElements(rating).ToArray());
        Assert.AreEqual(0, graph.GetDownstreamElements(fail).Count());
        Assert.ThrowsException<ArgumentNullException>(() => graph.GetDownstreamElements(null!).ToArray());
    }

    /// <summary>Verifies the root-first upstream path walk.</summary>
    [TestMethod]
    public void Test_GetUpstreamPath_RootFirst()
    {
        // Arrange
        var (graph, hazard, rating, response, fail, _) = LeveeGraph();

        // Assert
        CollectionAssert.AreEqual(new IRiskElement[] { hazard, rating, response, fail },
            graph.GetUpstreamPath(fail).ToArray());
        CollectionAssert.AreEqual(new IRiskElement[] { hazard }, graph.GetUpstreamPath(hazard).ToArray());
    }

    /// <summary>Verifies the available-hazard-sources enumeration with positions and labels.</summary>
    [TestMethod]
    public void Test_GetAvailableHazardSources_PositionsAndLabels()
    {
        // Arrange
        var (graph, hazard, rating, _, fail, _) = LeveeGraph();

        // Act
        var options = graph.GetAvailableHazardSources(fail);

        // Assert — the raw hazard at position 0, the rating output at position 1.
        Assert.AreEqual(2, options.Count);
        Assert.AreSame(hazard, options[0].Element);
        Assert.AreEqual(0, options[0].OutputPort);
        Assert.AreEqual(0, options[0].ChainPosition);
        Assert.AreEqual("Flow", options[0].HazardLabel);
        Assert.AreEqual("cfs", options[0].HazardUnit);
        Assert.AreSame(rating, options[1].Element);
        Assert.AreEqual(1, options[1].ChainPosition);
        Assert.AreEqual("Stage", options[1].HazardLabel);

        // The hazard root has no upstream sources; a floater path has none either.
        Assert.AreEqual(0, graph.GetAvailableHazardSources(hazard).Count);
        var floater = new TransformElement("Floater");
        graph.AddElement(floater);
        Assert.AreEqual(0, graph.GetAvailableHazardSources(floater).Count);
    }

    /// <summary>Verifies the fully wired levee graph validates clean.</summary>
    [TestMethod]
    public void Test_Validate_LeveeGraph_Valid()
    {
        // Act
        var (graph, _, _, _, _, _) = LeveeGraph();
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsTrue(isValid, string.Join(" | ", messages));
    }

    /// <summary>Verifies the single-hazard-root rule.</summary>
    [TestMethod]
    public void Test_Validate_HazardCount_Errors()
    {
        // Zero hazards.
        var empty = new ComponentGraph();
        Assert.IsTrue(empty.Validate().ValidationMessages.Any(m => m.Contains("exactly one hazard element")));

        // Two hazards.
        var (graph, _, _, _, _, _) = LeveeGraph();
        graph.AddElement(new HazardElement("Second Hazard") { Function = FlowFrequency() });
        var (isValid, messages) = graph.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("exactly one hazard element (found 2)")));
    }

    /// <summary>Verifies dangling connections and port bounds are errors (bindings included).</summary>
    [TestMethod]
    public void Test_Validate_DanglingAndPortBounds_Errors()
    {
        // A connection to an element outside the graph.
        var (graph, _, rating, _, _, _) = LeveeGraph();
        rating.Input = new RiskConnection(new HazardElement("Foreign"));
        Assert.IsTrue(graph.Validate().ValidationMessages.Any(m => m.Contains("not in the graph")));

        // An out-of-range port on a structural input.
        var (graph2, hazard2, rating2, _, _, _) = LeveeGraph();
        rating2.Input = new RiskConnection(hazard2, 1);
        Assert.IsTrue(graph2.Validate().ValidationMessages.Any(m => m.Contains("output port 1")));

        // An out-of-range port on a binding.
        var (graph3, hazard3, _, _, fail3, _) = LeveeGraph();
        fail3.HazardSource = new RiskConnection(hazard3, 1);
        Assert.IsTrue(graph3.Validate().ValidationMessages.Any(
            m => m.Contains("hazard-source binding") && m.Contains("output port 1")));

        // A response's Non-Fail port (1) is legal since Phase 6.7; port 2 is out of range.
        var (graph4, _, _, response4, fail4, _) = LeveeGraph();
        fail4.Input = new RiskConnection(response4, 1);
        Assert.IsFalse(graph4.Validate().ValidationMessages.Any(m => m.Contains("output port")),
            "The Non-Fail port must pass the port-bounds check.");
        fail4.Input = new RiskConnection(response4, 2);
        Assert.IsTrue(graph4.Validate().ValidationMessages.Any(m => m.Contains("output port 2")));
    }

    /// <summary>Verifies cycle, unreachable-element, and non-consequence-leaf errors.</summary>
    [TestMethod]
    public void Test_Validate_StructureErrors()
    {
        // A cycle is an error.
        var (graph, hazard, rating, _, _, _) = LeveeGraph();
        var loop = new TransformElement("Loop") { Function = Rating("Stage", "ft", "Stage2", "ft") };
        graph.AddElement(loop);
        loop.Input = new RiskConnection(rating);
        rating.Input = new RiskConnection(loop);
        Assert.IsTrue(graph.Validate().ValidationMessages.Any(m => m.Contains("Circular reference")));

        // A floater is not connected to the hazard element.
        var (graph2, _, _, _, _, _) = LeveeGraph();
        graph2.AddElement(new TransformElement("Floater") { Function = Rating("Flow", "cfs", "Stage", "ft") });
        Assert.IsTrue(graph2.Validate().ValidationMessages.Any(m => m.Contains("'Floater' is not connected")));

        // A non-consequence leaf ends a path.
        var hazard3 = new HazardElement("Hazard") { Function = FlowFrequency() };
        var rating3 = new TransformElement("Rating")
        {
            Function = Rating("Flow", "cfs", "Stage", "ft"),
            Input = new RiskConnection(hazard3),
        };
        var graph3 = new ComponentGraph();
        graph3.AddElement(hazard3);
        graph3.AddElement(rating3);
        var (_, messages3) = graph3.Validate();
        Assert.IsTrue(messages3.Any(m => m.Contains("A path ends at 'Rating'")));
        Assert.IsTrue(messages3.Any(m => m.Contains("at least one consequence element")));
    }

    /// <summary>Verifies the at-most-one non-failure path rule.</summary>
    [TestMethod]
    public void Test_Validate_MultipleNonFailPaths_Error()
    {
        // Arrange — a second response-free terminal off the rating.
        var (graph, _, rating, _, _, _) = LeveeGraph();
        var second = new ConsequenceElement("Second Non-Fail") { Input = new RiskConnection(rating) };
        second.Functions.Add(Damages("Stage", "ft"));
        graph.AddElement(second);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("At most one non-failure path")));
    }

    /// <summary>
    /// Verifies the polarity-aware branch-claim advisories (arch doc §7.9, Q2 ruling): duplicate
    /// leaf signatures and prefix-nested claims warn inside cascade-active graphs but stay legal,
    /// legacy same-port fan-out stays silent, and an unwired Fail port warns.
    /// </summary>
    [TestMethod]
    public void Test_Validate_BranchClaims_Advisories()
    {
        // Legacy same-port fan-out with no cascade machinery: silent (backward compatible).
        var (legacy, _, _, legacyResponse, _, _) = LeveeGraph();
        var legacySecond = new ConsequenceElement("Life Loss") { Input = new RiskConnection(legacyResponse) };
        legacySecond.Functions.Add(Damages("Stage", "ft"));
        legacy.AddElement(legacySecond);
        var (legacyValid, legacyMessages) = legacy.Validate();
        Assert.IsTrue(legacyValid);
        Assert.IsFalse(legacyMessages.Any(m => m.Contains("same response branch") || m.Contains("Fail port")),
            "A pre-6.7 shape must produce no branch-claim advisories.");

        // Cascade-active (a Non-Fail terminal exists) + duplicate Fail-port terminals: warns, legal.
        var (dup, _, _, dupResponse, _, _) = LeveeGraph();
        var dupPartial = new ConsequenceElement("Partial Damages") { Input = new RiskConnection(dupResponse, 1) };
        dupPartial.Functions.Add(Damages("Stage", "ft"));
        var dupSecond = new ConsequenceElement("Life Loss") { Input = new RiskConnection(dupResponse) };
        dupSecond.Functions.Add(Damages("Stage", "ft"));
        dup.AddElement(dupPartial);
        dup.AddElement(dupSecond);
        var (dupValid, dupMessages) = dup.Validate();
        Assert.IsTrue(dupValid, string.Join(" | ", dupMessages));
        Assert.IsTrue(dupMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)
            && m.Contains("same response branch") && m.Contains("'Failure Damages'") && m.Contains("'Life Loss'")));

        // A terminal on the branch a continuation also claims: prefix-nested warning, legal.
        var (nested, _, _, nestedResponse, _, _) = LeveeGraph();
        var progression = new ResponseElement("Progression")
        {
            Function = Fragility("Stage", "ft"),
            Input = new RiskConnection(nestedResponse),
        };
        var breachTerminal = new ConsequenceElement("Breach Damages") { Input = new RiskConnection(progression) };
        breachTerminal.Functions.Add(Damages("Stage", "ft"));
        nested.AddElement(progression);
        nested.AddElement(breachTerminal);
        var (nestedValid, nestedMessages) = nested.Validate();
        Assert.IsTrue(nestedValid, string.Join(" | ", nestedMessages));
        Assert.IsTrue(nestedMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)
            && m.Contains("also continues") && m.Contains("'Failure Damages'") && m.Contains("'Breach Damages'")));

        // A response consumed only through its Non-Fail port (an else-chain head whose Fail
        // branch is unwired): the Fail branch mass silently flows to background — warned.
        var (unwired, _, unwiredRating, unwiredResponse, _, _) = LeveeGraph();
        var elseHead = new ResponseElement("Else Head")
        {
            Function = Fragility("Stage", "ft"),
            Input = new RiskConnection(unwiredRating),
        };
        unwiredResponse.Input = new RiskConnection(elseHead, 1);
        unwired.AddElement(elseHead);
        var (unwiredValid, unwiredMessages) = unwired.Validate();
        Assert.IsTrue(unwiredValid, string.Join(" | ", unwiredMessages));
        Assert.IsTrue(unwiredMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)
            && m.Contains("Fail port") && m.Contains("'Else Head'")));
    }

    /// <summary>Verifies binding placement: on-path targets only, at or before the last response's input.</summary>
    [TestMethod]
    public void Test_Validate_BindingPlacement()
    {
        // A binding to the raw hazard is legal (the levee scenario).
        var (graph, hazard, _, _, fail, _) = LeveeGraph();
        fail.HazardSource = new RiskConnection(hazard);
        Assert.IsTrue(graph.Validate().IsValid);

        // A binding to an element on another branch is an error.
        var (graph2, _, rating2, response2, fail2, _) = LeveeGraph();
        var spur = new TransformElement("Spur")
        {
            Function = Rating("Stage", "ft", "Depth", "ft"),
            Input = new RiskConnection(rating2),
        };
        var spurTerminal = new ConsequenceElement("Spur Damages") { Input = new RiskConnection(spur) };
        spurTerminal.Functions.Add(Damages("Depth", "ft"));
        graph2.AddElement(spur);
        graph2.AddElement(spurTerminal);
        fail2.HazardSource = new RiskConnection(spur);
        Assert.IsTrue(graph2.Validate().ValidationMessages.Any(m => m.Contains("on its own upstream path")));

        // A binding to a response element is likewise rejected (responses produce no signal).
        var (graph3, _, _, response3, fail3, _) = LeveeGraph();
        fail3.HazardSource = new RiskConnection(response3);
        Assert.IsTrue(graph3.Validate().ValidationMessages.Any(m => m.Contains("on its own upstream path")));

        // A binding after the last response's input is an error; at the input it is legal.
        var hazard4 = new HazardElement("Hazard") { Function = FlowFrequency() };
        var rating4 = new TransformElement("Rating")
        {
            Function = Rating("Flow", "cfs", "Stage", "ft"),
            Input = new RiskConnection(hazard4),
        };
        var response4 = new ResponseElement("Breach")
        {
            Function = Fragility("Stage", "ft"),
            Input = new RiskConnection(rating4),
        };
        var trailing4 = new TransformElement("Damage Reach")
        {
            Function = Rating("Stage", "ft", "Damage Stage", "ft"),
            Input = new RiskConnection(response4),
        };
        var fail4 = new ConsequenceElement("Damages") { Input = new RiskConnection(trailing4) };
        fail4.Functions.Add(Damages("Damage Stage", "ft"));
        var graph4 = new ComponentGraph();
        graph4.AddElement(hazard4);
        graph4.AddElement(rating4);
        graph4.AddElement(response4);
        graph4.AddElement(trailing4);
        graph4.AddElement(fail4);

        fail4.HazardSource = new RiskConnection(trailing4);
        Assert.IsTrue(graph4.Validate().ValidationMessages.Any(m => m.Contains("at or before the last response's input")));
        fail4.HazardSource = new RiskConnection(rating4);
        Assert.IsTrue(graph4.Validate().IsValid);
    }

    /// <summary>Verifies positional consequence alignment against the non-failure path.</summary>
    [TestMethod]
    public void Test_Validate_ConsequenceAlignment()
    {
        // A count mismatch against the non-failure path is an error.
        var (graph, _, _, _, fail, _) = LeveeGraph();
        fail.Functions.Add(Damages("Stage", "ft", "Life Loss", "lives"));
        var (isValid, messages) = graph.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("pairing is positional")));

        // Matching counts with mismatched paired labels warn only.
        var (graph2, _, _, _, fail2, nonFail2) = LeveeGraph();
        fail2.Functions.Add(Damages("Stage", "ft", "Life Loss", "lives"));
        nonFail2.Functions.Add(Damages("Stage", "ft", "Damages", "$"));
        var (isValid2, messages2) = graph2.Validate();
        Assert.IsTrue(isValid2);
        Assert.IsTrue(messages2.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("pairs with")));
    }

    /// <summary>Verifies the shared-function-instance warning.</summary>
    [TestMethod]
    public void Test_Validate_SharedInstance_Warning()
    {
        // Arrange — the same consequence instance in both terminals.
        var (graph, _, _, _, fail, nonFail) = LeveeGraph();
        var shared = Damages("Stage", "ft");
        fail.Functions[0] = shared;
        nonFail.Functions[0] = shared;

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsTrue(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("same function instance")));
    }

    /// <summary>Verifies the serialization round trip: order, identity, and resolved links.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var (graph, hazard, rating, response, fail, _) = LeveeGraph();
        fail.HazardSource = new RiskConnection(hazard);

        // Act
        var xml = graph.ToXElement();
        var restored = new ComponentGraph(xml);

        // Assert — declared order and identities round-trip.
        Assert.AreEqual(nameof(ComponentGraph), xml.Name.LocalName);
        Assert.AreEqual(graph.Elements.Count, restored.Elements.Count);
        for (int i = 0; i < graph.Elements.Count; i++)
        {
            Assert.AreEqual(graph.Elements[i].Id, restored.Elements[i].Id);
            Assert.AreEqual(graph.Elements[i].Name, restored.Elements[i].Name);
            Assert.AreEqual(graph.Elements[i].GetType(), restored.Elements[i].GetType());
        }

        // Connections resolve to the restored instances (never the originals).
        var restoredRating = (TransformElement)restored.GetElement("Rating")!;
        var restoredFail = (ConsequenceElement)restored.GetElement("Failure Damages")!;
        Assert.AreSame(restored.GetElement("Hazard"), restoredRating.Input!.Source);
        Assert.AreSame(restored.GetElement("Breach"), restoredFail.Input!.Source);
        Assert.AreSame(restored.GetElement("Hazard"), restoredFail.HazardSource!.Source);

        // The restored graph is valid and re-serializes identically.
        Assert.IsTrue(restored.Validate().IsValid);
        Assert.AreEqual(xml.ToString(), restored.ToXElement().ToString());
    }

    /// <summary>Verifies unknown element types are skipped on load (forward compatibility).</summary>
    [TestMethod]
    public void Test_Serialization_UnknownElementSkipped()
    {
        // Arrange
        var (graph, _, _, _, _, _) = LeveeGraph();
        var xml = graph.ToXElement();
        xml.Element("Elements")!.Add(new XElement("FutureElementType"));

        // Act
        var restored = new ComponentGraph(xml);

        // Assert
        Assert.AreEqual(graph.Elements.Count, restored.Elements.Count);
    }

    /// <summary>Verifies a stale serialized connection Id throws on load.</summary>
    [TestMethod]
    public void Test_Serialization_StaleConnectionId_Throws()
    {
        // Arrange — a transform referencing an element that is not serialized alongside it.
        var foreign = new HazardElement("Foreign");
        var rating = new TransformElement("Rating") { Input = new RiskConnection(foreign) };
        var graph = new ComponentGraph();
        graph.AddElement(rating);

        // Act / Assert
        var xml = graph.ToXElement();
        Assert.ThrowsException<InvalidOperationException>(() => new ComponentGraph(xml));
        Assert.ThrowsException<ArgumentNullException>(() => new ComponentGraph((XElement)null!));
    }

    /// <summary>Verifies deep cloning: isolated instances, shared Ids, re-linked connections.</summary>
    [TestMethod]
    public void Test_Clone_DeepAndRelinked()
    {
        // Arrange
        var (graph, hazard, rating, _, fail, _) = LeveeGraph();
        fail.HazardSource = new RiskConnection(hazard);

        // Act
        var clone = graph.Clone();

        // Assert — same shape, distinct instances, shared Ids.
        Assert.AreEqual(graph.Elements.Count, clone.Elements.Count);
        for (int i = 0; i < graph.Elements.Count; i++)
        {
            Assert.AreNotSame(graph.Elements[i], clone.Elements[i]);
            Assert.AreEqual(graph.Elements[i].Id, clone.Elements[i].Id);
        }

        // Connections point into the clone, not the original.
        var clonedRating = (TransformElement)clone.GetElement("Rating")!;
        var clonedFail = (ConsequenceElement)clone.GetElement("Failure Damages")!;
        Assert.AreSame(clone.GetElement("Hazard"), clonedRating.Input!.Source);
        Assert.AreSame(clone.GetElement("Hazard"), clonedFail.HazardSource!.Source);
        Assert.IsTrue(clone.Validate().IsValid);

        // The clone is isolated: rewiring it leaves the original valid.
        clonedRating.Input = null;
        Assert.IsTrue(graph.Validate().IsValid);
        Assert.IsFalse(clone.Validate().IsValid);
    }

    /// <summary>Verifies membership changes raise change notification.</summary>
    [TestMethod]
    public void Test_PropertyChanged_OnMembership()
    {
        // Arrange
        var graph = new ComponentGraph();
        var raised = new List<string>();
        graph.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        var hazard = new HazardElement("Hazard");

        // Act
        graph.AddElement(hazard);
        graph.RemoveElement(hazard);
        graph.RemoveElement(hazard);   // absent — no raise

        // Assert
        CollectionAssert.AreEqual(
            new[] { nameof(ComponentGraph.Elements), nameof(ComponentGraph.Elements) }, raised);
    }
}
