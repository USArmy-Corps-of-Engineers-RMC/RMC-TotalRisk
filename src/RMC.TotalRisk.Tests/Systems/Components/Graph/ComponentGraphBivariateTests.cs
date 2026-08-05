using System;
using System.Collections.Generic;
using System.Linq;
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
/// Unit tests for the two-port graph surface on <see cref="ComponentGraph"/> — the
/// secondary-chain resolution walk (<see cref="ComponentGraph.TryResolveSecondaryChain"/>), the
/// mode-dependent bivariate wiring matrix under univariate and bivariate roots, the generalized
/// hazard-source binding rules, the picker's port-1 and secondary-chain options, and the
/// unconsumed-secondary advisory.
/// </summary>
[TestClass]
public class ComponentGraphBivariateTests
{
    #region Fixtures

    /// <summary>Builds a valid labeled bivariate hazard over two tabular marginals.</summary>
    private static BivariateHazard JointHazard()
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

    /// <summary>Builds a valid labeled univariate hazard.</summary>
    private static TabularHazard UnivariateHazard()
    {
        return new TabularHazard { Name = "Surge Frequency", SpecifiedHazard = "Surge", HazardUnit = "ft" };
    }

    /// <summary>Builds a valid univariate transform for the secondary chain (pool → pool stage).</summary>
    private static TabularTransform PoolTransform(string name)
    {
        return new TabularTransform
        {
            Name = name,
            SpecifiedHazard = "Pool Elevation",
            HazardUnit = "ft",
            TransformedHazard = "Pool Stage",
            TransformedHazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(300d, new Deterministic(150d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a valid labeled bivariate transform (surge × pool → stage).</summary>
    private static BivariateTransform JointTransform()
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

    /// <summary>Builds a valid labeled bivariate response on the default 2×2 grid.</summary>
    private static BivariateResponse JointResponse()
    {
        return new BivariateResponse
        {
            Name = "Joint Fragility",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
        };
    }

    /// <summary>Builds a valid labeled univariate tabular response.</summary>
    private static TabularResponse Fragility(string name = "Fragility")
    {
        return new TabularResponse { Name = name, SpecifiedHazard = "Surge", HazardUnit = "ft" };
    }

    /// <summary>Builds a valid labeled univariate tabular consequence.</summary>
    private static TabularConsequence Damages(string name = "Damages")
    {
        return new TabularConsequence
        {
            Name = name,
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
        };
    }

    /// <summary>Builds a valid labeled bivariate consequence surface.</summary>
    private static BivariateConsequence JointDamages()
    {
        return new BivariateConsequence
        {
            Name = "Surge-Pool Damages",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
            X1Values = new[] { 0d, 10d, 20d },
            X2Values = new[] { 100d, 200d },
            ZValues = new[,] { { 1d, 2d }, { 3d, 5d }, { 4d, 8d } },
        };
    }

    /// <summary>Adds a univariate consequence terminal wired to an upstream element output.</summary>
    private static ConsequenceElement AddTerminal(ComponentGraph graph, string name,
        IRiskElement upstream, int port = 0)
    {
        var terminal = new ConsequenceElement(name) { Input = new RiskConnection(upstream, port) };
        terminal.Functions.Add(Damages($"{name} curve"));
        graph.AddElement(terminal);
        return terminal;
    }

    /// <summary>
    /// Builds the canonical joint graph: a bivariate root, a joint-mode bivariate response
    /// consuming both hazard outputs directly, one failure terminal, and the non-failure path.
    /// </summary>
    private static (ComponentGraph Graph, HazardElement Hazard, ResponseElement Response) JointGraph()
    {
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = JointHazard() };
        var response = new ResponseElement("Breach")
        {
            Function = JointResponse(),
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        graph.AddElement(hazard);
        graph.AddElement(response);
        AddTerminal(graph, "Failure Damages", response);
        AddTerminal(graph, "Baseline Damages", hazard);
        return (graph, hazard, response);
    }

    /// <summary>
    /// Builds the fully wired two-port cascade: the bivariate root, the secondary chain
    /// Ty1 → Ty2 off the hazard's port 1, a bivariate transform consuming both dimensions, and a
    /// joint response consuming the transform's two outputs.
    /// </summary>
    private static (ComponentGraph Graph, HazardElement Hazard, TransformElement Ty1,
        TransformElement Ty2, TransformElement Bivariate, ResponseElement Response) CascadeGraph()
    {
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = JointHazard() };
        var ty1 = new TransformElement("Pool Chain 1")
        {
            Function = PoolTransform("Pool Rating 1"),
            Input = new RiskConnection(hazard, 1),
        };
        var ty2 = new TransformElement("Pool Chain 2")
        {
            Function = PoolTransform("Pool Rating 2"),
            Input = new RiskConnection(ty1),
        };
        var bivariate = new TransformElement("Stage Surface")
        {
            Function = JointTransform(),
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(ty2),
        };
        var response = new ResponseElement("Breach")
        {
            Function = JointResponse(),
            Input = new RiskConnection(bivariate),
            SecondaryInput = new RiskConnection(bivariate, 1),
        };
        graph.AddElement(hazard);
        graph.AddElement(ty1);
        graph.AddElement(ty2);
        graph.AddElement(bivariate);
        graph.AddElement(response);
        AddTerminal(graph, "Failure Damages", response);
        AddTerminal(graph, "Baseline Damages", bivariate);
        return (graph, hazard, ty1, ty2, bivariate, response);
    }

    #endregion

    #region TryResolveSecondaryChain

    /// <summary>Verifies a direct hazard-port-1 connection resolves with an empty chain.</summary>
    [TestMethod]
    public void Test_TryResolveSecondaryChain_DirectPortOne_EmptyChain()
    {
        // Arrange
        var fixture = JointGraph();
        var chain = new List<TransformElement>();

        // Act
        bool resolved = fixture.Graph.TryResolveSecondaryChain(
            fixture.Response.SecondaryInput!, chain, out string error);

        // Assert
        Assert.IsTrue(resolved);
        Assert.AreEqual(string.Empty, error);
        Assert.AreEqual(0, chain.Count);
    }

    /// <summary>
    /// Verifies a multi-transform chain resolves upstream-first, and a pass-through hop through
    /// a bivariate transform resolves the identical chain the transform itself anchors.
    /// </summary>
    [TestMethod]
    public void Test_TryResolveSecondaryChain_TransformChainAndPassthroughHop()
    {
        // Arrange
        var fixture = CascadeGraph();
        var chain = new List<TransformElement>();

        // Act / Assert — the bivariate transform's own anchor: [Ty1, Ty2] upstream-first.
        Assert.IsTrue(fixture.Graph.TryResolveSecondaryChain(
            fixture.Bivariate.SecondaryInput!, chain, out _));
        CollectionAssert.AreEqual(new[] { fixture.Ty1, fixture.Ty2 }, chain);

        // The response hops through the bivariate transform's pass-through output and lands on
        // the same chain (the hop itself adds nothing).
        Assert.IsTrue(fixture.Graph.TryResolveSecondaryChain(
            fixture.Response.SecondaryInput!, chain, out _));
        CollectionAssert.AreEqual(new[] { fixture.Ty1, fixture.Ty2 }, chain);
    }

    /// <summary>
    /// Verifies the primary-kind sources fail loudly: the hazard's port 0, a bivariate
    /// transform's z output, and response/consequence sources are never secondary chains.
    /// </summary>
    [TestMethod]
    public void Test_TryResolveSecondaryChain_PrimaryKindSources_Fail()
    {
        // Arrange
        var fixture = CascadeGraph();
        var chain = new List<TransformElement>();

        // The hazard's primary output.
        Assert.IsFalse(fixture.Graph.TryResolveSecondaryChain(
            new RiskConnection(fixture.Hazard), chain, out string hazardError));
        StringAssert.Contains(hazardError, "secondary output (port 1)");

        // The bivariate transform's z output — a primary-kind signal.
        Assert.IsFalse(fixture.Graph.TryResolveSecondaryChain(
            new RiskConnection(fixture.Bivariate), chain, out string zError));
        StringAssert.Contains(zError, "axes never cross");

        // A response source.
        Assert.IsFalse(fixture.Graph.TryResolveSecondaryChain(
            new RiskConnection(fixture.Response), chain, out string responseError));
        StringAssert.Contains(responseError, "univariate transform elements only");
    }

    /// <summary>
    /// Verifies the between-bivariate directness guard: a univariate transform between two
    /// bivariate elements fails (the two elements would consume different secondary signals).
    /// </summary>
    [TestMethod]
    public void Test_TryResolveSecondaryChain_UnivariateBetweenBivariate_Fails()
    {
        // Arrange — a univariate transform re-shaping the pass-through output before the
        // response's secondary input.
        var fixture = CascadeGraph();
        var between = new TransformElement("Between")
        {
            Function = PoolTransform("Between Rating"),
            Input = new RiskConnection(fixture.Bivariate, 1),
        };
        fixture.Graph.AddElement(between);
        var chain = new List<TransformElement>();

        // Act
        bool resolved = fixture.Graph.TryResolveSecondaryChain(
            new RiskConnection(between), chain, out string error);

        // Assert
        Assert.IsFalse(resolved);
        StringAssert.Contains(error, "univariate transforms between bivariate elements are not supported");
    }

    /// <summary>
    /// Verifies the dangling and unclassifiable cases fail loudly: an out-of-graph source, an
    /// unconnected chain transform, a function-less chain transform, and a hop through a
    /// bivariate transform whose own secondary input is unwired.
    /// </summary>
    [TestMethod]
    public void Test_TryResolveSecondaryChain_DanglingCases_Fail()
    {
        // Arrange
        var fixture = JointGraph();
        var chain = new List<TransformElement>();

        // An out-of-graph source.
        var outside = new TransformElement("Outside") { Function = PoolTransform("Outside Rating") };
        Assert.IsFalse(fixture.Graph.TryResolveSecondaryChain(
            new RiskConnection(outside), chain, out string outsideError));
        StringAssert.Contains(outsideError, "not in the graph");

        // A chain transform with no input.
        var unconnected = new TransformElement("Unconnected") { Function = PoolTransform("Unconnected Rating") };
        fixture.Graph.AddElement(unconnected);
        Assert.IsFalse(fixture.Graph.TryResolveSecondaryChain(
            new RiskConnection(unconnected), chain, out string unconnectedError));
        StringAssert.Contains(unconnectedError, "has no input");

        // A function-less chain transform cannot be classified.
        var functionless = new TransformElement("Functionless")
        {
            Input = new RiskConnection(fixture.Hazard, 1),
        };
        fixture.Graph.AddElement(functionless);
        Assert.IsFalse(fixture.Graph.TryResolveSecondaryChain(
            new RiskConnection(functionless), chain, out string functionlessError));
        StringAssert.Contains(functionlessError, "no transform function");

        // A hop through a bivariate transform whose own secondary input is unwired.
        var unwired = new TransformElement("Unwired Surface")
        {
            Function = JointTransform(),
            Input = new RiskConnection(fixture.Hazard),
        };
        fixture.Graph.AddElement(unwired);
        Assert.IsFalse(fixture.Graph.TryResolveSecondaryChain(
            new RiskConnection(unwired, 1), chain, out string unwiredError));
        StringAssert.Contains(unwiredError, "own secondary input is not connected");
    }

    /// <summary>Verifies the visited-set cycle guard fails rather than looping.</summary>
    [TestMethod]
    public void Test_TryResolveSecondaryChain_Cycle_Fails()
    {
        // Arrange — two univariate transforms consuming each other.
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = JointHazard() };
        var first = new TransformElement("First") { Function = PoolTransform("First Rating") };
        var second = new TransformElement("Second") { Function = PoolTransform("Second Rating") };
        first.Input = new RiskConnection(second);
        second.Input = new RiskConnection(first);
        graph.AddElement(hazard);
        graph.AddElement(first);
        graph.AddElement(second);

        // Act
        bool resolved = graph.TryResolveSecondaryChain(
            new RiskConnection(second), new List<TransformElement>(), out string error);

        // Assert
        Assert.IsFalse(resolved);
        StringAssert.Contains(error, "circular reference");
    }

    #endregion

    #region Wiring matrix — bivariate root

    /// <summary>Verifies the canonical joint-response wiring validates.</summary>
    [TestMethod]
    public void Test_Wiring_JointResponse_DirectRawY_Valid()
    {
        // Act
        var (isValid, messages) = JointGraph().Graph.Validate();

        // Assert
        Assert.IsTrue(isValid, string.Join(" | ", messages));
    }

    /// <summary>Verifies the fully wired two-port cascade validates.</summary>
    [TestMethod]
    public void Test_Wiring_FullTwoPortCascade_Valid()
    {
        // Act
        var (isValid, messages) = CascadeGraph().Graph.Validate();

        // Assert
        Assert.IsTrue(isValid, string.Join(" | ", messages));
    }

    /// <summary>
    /// Verifies a Secondary-bound path — univariate elements consuming the hazard's port 1 as
    /// their primary signal — validates beside an ordinary primary failure path.
    /// </summary>
    [TestMethod]
    public void Test_Wiring_SecondaryBoundPath_Valid()
    {
        // Arrange
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = JointHazard() };
        var primaryResponse = new ResponseElement("Primary Breach")
        {
            Function = Fragility("Primary Fragility"),
            Input = new RiskConnection(hazard),
        };
        var poolTransform = new TransformElement("Pool Rating")
        {
            Function = PoolTransform("Pool Rating"),
            Input = new RiskConnection(hazard, 1),
        };
        var secondaryResponse = new ResponseElement("Pool Breach")
        {
            Function = new TabularResponse { Name = "Pool Fragility", SpecifiedHazard = "Pool Stage", HazardUnit = "ft" },
            Input = new RiskConnection(poolTransform),
        };
        graph.AddElement(hazard);
        graph.AddElement(primaryResponse);
        graph.AddElement(poolTransform);
        graph.AddElement(secondaryResponse);
        AddTerminal(graph, "Primary Damages", primaryResponse);
        AddTerminal(graph, "Pool Damages", secondaryResponse);
        AddTerminal(graph, "Baseline Damages", hazard);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert — valid, and the consumed port 1 raises no advisory.
        Assert.IsTrue(isValid, string.Join(" | ", messages));
        Assert.IsFalse(messages.Any(m => m.Contains("secondary output (port 1) of the bivariate hazard")));
    }

    /// <summary>
    /// Verifies the generalized binding targets: the hazard's raw secondary output and a
    /// resolved secondary-chain transform are both legal hazard-source bindings.
    /// </summary>
    [TestMethod]
    public void Test_Wiring_Binding_RawYAndChainTransform_Valid()
    {
        // Arrange — the cascade graph, whose failure terminal binds at the raw Y and whose
        // baseline terminal binds at the chain transform.
        var fixture = CascadeGraph();
        var terminals = fixture.Graph.GetElements<ConsequenceElement>().ToArray();
        terminals[0].HazardSource = new RiskConnection(fixture.Hazard, 1);
        terminals[1].HazardSource = new RiskConnection(fixture.Ty1);

        // Act
        var (isValid, messages) = fixture.Graph.Validate();

        // Assert
        Assert.IsTrue(isValid, string.Join(" | ", messages));
    }

    /// <summary>Verifies a joint-mode bivariate response with no secondary input errors.</summary>
    [TestMethod]
    public void Test_Wiring_JointResponse_MissingSecondary_Error()
    {
        // Arrange
        var fixture = JointGraph();
        fixture.Response.SecondaryInput = null;

        // Act
        var (isValid, messages) = fixture.Graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("joint mode") && m.Contains("secondary input")));
    }

    /// <summary>Verifies a joint-mode bivariate response on a multi-response path errors.</summary>
    [TestMethod]
    public void Test_Wiring_JointResponse_MultiStage_Error()
    {
        // Arrange — a univariate response upstream of the joint response.
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = JointHazard() };
        var initiation = new ResponseElement("Initiation")
        {
            Function = Fragility("Initiation Fragility"),
            Input = new RiskConnection(hazard),
        };
        var joint = new ResponseElement("Breach")
        {
            Function = JointResponse(),
            Input = new RiskConnection(initiation),
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        graph.AddElement(hazard);
        graph.AddElement(initiation);
        graph.AddElement(joint);
        AddTerminal(graph, "Failure Damages", joint);
        AddTerminal(graph, "Baseline Damages", hazard);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("single-response paths only")));
    }

    /// <summary>
    /// Verifies a bivariate element's primary input tracing to the secondary dimension errors —
    /// directly and through a univariate intermediate (kind propagates through univariate
    /// transforms).
    /// </summary>
    [TestMethod]
    public void Test_Wiring_BivariatePrimaryInput_TracesToSecondary_Error()
    {
        // Arrange — the bivariate transform's primary input rides the secondary dimension
        // through a univariate transform.
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = JointHazard() };
        var poolTransform = new TransformElement("Pool Rating")
        {
            Function = PoolTransform("Pool Rating"),
            Input = new RiskConnection(hazard, 1),
        };
        var bivariate = new TransformElement("Stage Surface")
        {
            Function = JointTransform(),
            Input = new RiskConnection(poolTransform),
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        graph.AddElement(hazard);
        graph.AddElement(poolTransform);
        graph.AddElement(bivariate);
        AddTerminal(graph, "Damages", bivariate);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("primary input consumes the hazard's secondary signal")));
    }

    /// <summary>
    /// Verifies the one-secondary-chain-per-path identity: a downstream bivariate element wired
    /// to a prefix of the chain resolves cleanly on its own yet errors at the path level —
    /// the engine routes exactly one chain per failure mode.
    /// </summary>
    [TestMethod]
    public void Test_Wiring_PathChainIdentity_Mismatch_Error()
    {
        // Arrange — the bivariate transform consumes the full chain [Ty1, Ty2]; the joint
        // response bypasses it and reads Ty1 directly (the prefix chain [Ty1]).
        var fixture = CascadeGraph();
        fixture.Response.SecondaryInput = new RiskConnection(fixture.Ty1);

        // Both anchors resolve individually — the defect is path-scoped.
        var chain = new List<TransformElement>();
        Assert.IsTrue(fixture.Graph.TryResolveSecondaryChain(fixture.Bivariate.SecondaryInput!, chain, out _));
        Assert.IsTrue(fixture.Graph.TryResolveSecondaryChain(fixture.Response.SecondaryInput!, chain, out _));

        // Act
        var (isValid, messages) = fixture.Graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("two different secondary chains")));
    }

    /// <summary>
    /// Verifies a hazard-source binding onto a bivariate transform's pass-through output errors
    /// with the redirect guidance (bind at the hazard or a secondary-chain transform).
    /// </summary>
    [TestMethod]
    public void Test_Wiring_BindingToBivariatePassthrough_Error()
    {
        // Arrange
        var fixture = CascadeGraph();
        var terminal = fixture.Graph.GetElements<ConsequenceElement>().First();
        terminal.HazardSource = new RiskConnection(fixture.Bivariate, 1);

        // Act
        var (isValid, messages) = fixture.Graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("pass-through output (port 1)")
            && m.Contains("bind at the hazard's secondary output or a secondary-chain transform")));
    }

    /// <summary>
    /// Verifies an off-path, off-chain binding target still errors, with the message naming both
    /// legal surfaces.
    /// </summary>
    [TestMethod]
    public void Test_Wiring_OffPathBinding_StillErrors()
    {
        // Arrange — the primary terminal binds onto the other path's transform.
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = JointHazard() };
        var response = new ResponseElement("Breach")
        {
            Function = Fragility(),
            Input = new RiskConnection(hazard),
        };
        var otherTransform = new TransformElement("Pool Rating")
        {
            Function = PoolTransform("Pool Rating"),
            Input = new RiskConnection(hazard, 1),
        };
        var otherResponse = new ResponseElement("Pool Breach")
        {
            Function = new TabularResponse { Name = "Pool Fragility", SpecifiedHazard = "Pool Stage", HazardUnit = "ft" },
            Input = new RiskConnection(otherTransform),
        };
        graph.AddElement(hazard);
        graph.AddElement(response);
        graph.AddElement(otherTransform);
        graph.AddElement(otherResponse);
        var boundTerminal = AddTerminal(graph, "Failure Damages", response);
        AddTerminal(graph, "Pool Damages", otherResponse);
        AddTerminal(graph, "Baseline Damages", hazard);
        boundTerminal.HazardSource = new RiskConnection(otherTransform);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert — the other path's transform is neither on this terminal's path nor on its
        // (absent) secondary chain.
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("on its own upstream path or its secondary chain")));
    }

    /// <summary>
    /// Verifies the unconsumed-secondary advisory: a bivariate root nothing reads port 1 from
    /// warns without invalidating.
    /// </summary>
    [TestMethod]
    public void Test_Wiring_UnusedPortOne_Warning()
    {
        // Arrange — an entirely univariate model under a bivariate root.
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = JointHazard() };
        var response = new ResponseElement("Breach")
        {
            Function = Fragility(),
            Input = new RiskConnection(hazard),
        };
        graph.AddElement(hazard);
        graph.AddElement(response);
        AddTerminal(graph, "Failure Damages", response);
        AddTerminal(graph, "Baseline Damages", hazard);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsTrue(isValid, string.Join(" | ", messages));
        Assert.IsTrue(messages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)
            && m.Contains("secondary output (port 1)")));
    }

    #endregion

    #region Wiring matrix — univariate root (collapse mode)

    /// <summary>
    /// Verifies a bivariate response under a univariate root is legal in collapse mode with a
    /// null secondary input — including as a stage in a multi-response cascade.
    /// </summary>
    [TestMethod]
    public void Test_Wiring_CollapseResponse_UnderUnivariateRoot_Valid()
    {
        // Arrange — a univariate initiation stage cascading into the collapse-mode bivariate
        // response.
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = UnivariateHazard() };
        var initiation = new ResponseElement("Initiation")
        {
            Function = Fragility("Initiation Fragility"),
            Input = new RiskConnection(hazard),
        };
        var collapse = new ResponseElement("Breach")
        {
            Function = JointResponse(),
            Input = new RiskConnection(initiation),
        };
        graph.AddElement(hazard);
        graph.AddElement(initiation);
        graph.AddElement(collapse);
        AddTerminal(graph, "Failure Damages", collapse);
        AddTerminal(graph, "Baseline Damages", hazard);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert — no errors; cascade advisories are the only messages allowed.
        Assert.IsTrue(isValid, string.Join(" | ", messages));
    }

    /// <summary>
    /// Verifies a bivariate response under a univariate root with a CONNECTED secondary input
    /// errors — collapse mode has no port-1 source to consume.
    /// </summary>
    [TestMethod]
    public void Test_Wiring_CollapseResponse_ConnectedSecondary_Error()
    {
        // Arrange
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = UnivariateHazard() };
        var collapse = new ResponseElement("Breach")
        {
            Function = JointResponse(),
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard),
        };
        graph.AddElement(hazard);
        graph.AddElement(collapse);
        AddTerminal(graph, "Failure Damages", collapse);
        AddTerminal(graph, "Baseline Damages", hazard);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("collapse mode")));
    }

    /// <summary>Verifies a bivariate transform under a univariate root errors.</summary>
    [TestMethod]
    public void Test_Wiring_BivariateTransform_UnderUnivariateRoot_Error()
    {
        // Arrange
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = UnivariateHazard() };
        var bivariate = new TransformElement("Stage Surface")
        {
            Function = JointTransform(),
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard),
        };
        graph.AddElement(hazard);
        graph.AddElement(bivariate);
        AddTerminal(graph, "Damages", bivariate);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("wraps a bivariate transform, but the component hazard is univariate")));
    }

    /// <summary>Verifies a bivariate consequence under a univariate root errors.</summary>
    [TestMethod]
    public void Test_Wiring_BivariateConsequence_UnderUnivariateRoot_Error()
    {
        // Arrange
        var graph = new ComponentGraph();
        var hazard = new HazardElement("Hazard") { Function = UnivariateHazard() };
        var terminal = new ConsequenceElement("Damages")
        {
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard),
        };
        terminal.Functions.Add(JointDamages());
        graph.AddElement(hazard);
        graph.AddElement(terminal);

        // Act
        var (isValid, messages) = graph.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("wraps a bivariate consequence, but the component hazard is univariate")));
    }

    #endregion

    #region Picker

    /// <summary>
    /// Verifies the picker's port-1 option: labeled from the bivariate hazard's declared
    /// secondary pair at chain position 0, beside the primary options.
    /// </summary>
    [TestMethod]
    public void Test_Picker_PortOneOption_LabeledFromSecondaryPair()
    {
        // Arrange
        var fixture = JointGraph();
        var terminal = fixture.Graph.GetElements<ConsequenceElement>().First();

        // Act
        var options = fixture.Graph.GetAvailableHazardSources(terminal);

        // Assert — port 0 with the primary pair, port 1 with the secondary pair, both at
        // position 0 of their dimensions.
        Assert.AreEqual(2, options.Count);
        Assert.AreEqual(new HazardSourceOption(fixture.Hazard, 0, 0, "Surge", "ft"), options[0]);
        Assert.AreEqual(new HazardSourceOption(fixture.Hazard, 1, 0, "Pool Elevation", "ft"), options[1]);
    }

    /// <summary>
    /// Verifies the picker's secondary-chain options: one per resolved chain transform at
    /// position k + 1, appended after the primary options.
    /// </summary>
    [TestMethod]
    public void Test_Picker_ChainOptions_PositionsFollowChainOrder()
    {
        // Arrange
        var fixture = CascadeGraph();
        var terminal = fixture.Graph.GetElements<ConsequenceElement>().First();

        // Act
        var options = fixture.Graph.GetAvailableHazardSources(terminal);

        // Assert — the primary options (hazard ports, then the on-path bivariate transform's z
        // at primary position 1), then the chain options at secondary positions 1 and 2.
        Assert.AreEqual(5, options.Count);
        Assert.AreEqual(new HazardSourceOption(fixture.Hazard, 0, 0, "Surge", "ft"), options[0]);
        Assert.AreEqual(new HazardSourceOption(fixture.Hazard, 1, 0, "Pool Elevation", "ft"), options[1]);
        Assert.AreEqual(new HazardSourceOption(fixture.Bivariate, 0, 1, "Stage", "ft"), options[2]);
        Assert.AreEqual(new HazardSourceOption(fixture.Ty1, 0, 1, "Pool Stage", "ft"), options[3]);
        Assert.AreEqual(new HazardSourceOption(fixture.Ty2, 0, 2, "Pool Stage", "ft"), options[4]);
    }

    #endregion

    #region Referenced functions

    /// <summary>
    /// Verifies the dependency set includes a bivariate hazard's linked marginals exactly once,
    /// yielded after their hazard.
    /// </summary>
    [TestMethod]
    public void Test_GetReferencedFunctions_IncludesMarginalsOnce()
    {
        // Arrange
        var fixture = JointGraph();
        var hazard = (BivariateHazard)fixture.Hazard.Function!;

        // Act
        var functions = fixture.Graph.GetReferencedFunctions().ToList();

        // Assert — the hazard leads, its marginals follow, and no entry repeats.
        Assert.AreEqual(0, functions.IndexOf(hazard));
        Assert.AreEqual(1, functions.IndexOf(hazard.MarginalX!));
        Assert.AreEqual(2, functions.IndexOf(hazard.MarginalY!));
        Assert.AreEqual(functions.Count, functions.Distinct().Count());
    }

    #endregion
}
