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
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Systems.Components;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components;

/// <summary>
/// Unit tests for <see cref="SystemComponent"/> — v1.0 option surface, the graph-owned topology
/// with failure-mode projection, the chain-expansion authoring path, the identity-form canonical
/// hash (element identity can never perturb seeds), occurrence indexing, the multivariate-normal
/// constants, and serialization.
/// </summary>
[TestClass]
public class SystemComponentTests
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
    private static TabularResponse Fragility(string hazard, string unit, string name = "Fragility")
    {
        return new TabularResponse { Name = name, SpecifiedHazard = hazard, HazardUnit = unit };
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
    /// Builds the levee component by wiring the graph directly: Flow hazard → rating (Flow→Stage)
    /// → breach response → failure damages, plus a response-free non-failure damages path.
    /// </summary>
    private static SystemComponent LeveeComponent()
    {
        var component = new SystemComponent { Name = "Levee" };
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

        component.Graph.AddElement(hazard);
        component.Graph.AddElement(rating);
        component.Graph.AddElement(response);
        component.Graph.AddElement(fail);
        component.Graph.AddElement(nonFail);
        return component;
    }

    /// <summary>
    /// Builds the partial-damage cascade by wiring both response ports (arch doc §7.9): the flow
    /// hazard feeds an initiation response; its Fail port continues to a progression response
    /// whose Fail port carries full-breach damages and whose Non-Fail port carries partial
    /// damages; a response-free background path completes the component.
    /// </summary>
    private static SystemComponent CascadeComponent()
    {
        var component = new SystemComponent { Name = "Dam" };
        var hazard = new HazardElement("Hazard") { Function = FlowFrequency() };
        var initiation = new ResponseElement("Initiation")
        {
            Function = Fragility("Flow", "cfs", "Initiation Fragility"),
            Input = new RiskConnection(hazard),
        };
        var progression = new ResponseElement("Progression")
        {
            Function = Fragility("Flow", "cfs", "Progression Fragility"),
            Input = new RiskConnection(initiation),
        };
        var full = new ConsequenceElement("Full Breach Damages") { Input = new RiskConnection(progression) };
        full.Functions.Add(Damages("Flow", "cfs"));
        var partial = new ConsequenceElement("Partial Damages") { Input = new RiskConnection(progression, 1) };
        partial.Functions.Add(Damages("Flow", "cfs"));
        var background = new ConsequenceElement("Background Damages") { Input = new RiskConnection(hazard) };
        background.Functions.Add(Damages("Flow", "cfs"));

        component.Graph.AddElement(hazard);
        component.Graph.AddElement(initiation);
        component.Graph.AddElement(progression);
        component.Graph.AddElement(full);
        component.Graph.AddElement(partial);
        component.Graph.AddElement(background);
        return component;
    }

    /// <summary>Verifies the v1.0 default construction state.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var component = new SystemComponent();

        // Assert
        Assert.AreEqual("System Component", component.Name);
        Assert.AreEqual(FailureModeMethod.JointFailures, component.FailureModeMethod);
        Assert.AreEqual(JointConsequenceType.Maximum, component.JointConsequences);
        Assert.AreEqual(DependencyType.Independent, component.FailureModeDependency);
        Assert.AreEqual(0d, component.HazardThreshold, 0d);
        Assert.IsNull(component.HazardFunction);
        Assert.AreEqual(0, component.Graph.Elements.Count);
        Assert.AreEqual(0, component.FailureModes.Count);
        Assert.IsTrue(component.IsDeterministic);
        Assert.AreEqual(0, component.OccurrenceIndex);
        Assert.IsNull(component.FailureModeIndicators);
        Assert.IsNull(component.FailureModeMultivariateNormal);
    }

    /// <summary>Verifies the v1.0 hazard constructor creates the graph root and auto-name.</summary>
    [TestMethod]
    public void Test_V10Constructor_CreatesHazardRoot()
    {
        // Arrange
        var hazard = FlowFrequency();

        // Act
        var component = new SystemComponent(hazard, FailureModeMethod.CompetingFailures, JointConsequenceType.Additive, 12.5d);

        // Assert
        Assert.AreEqual("System Component - Flow Frequency", component.Name);
        Assert.AreSame(hazard, component.HazardFunction);
        Assert.AreEqual(1, component.Graph.Elements.Count);
        Assert.AreEqual(FailureModeMethod.CompetingFailures, component.FailureModeMethod);
        Assert.AreEqual(JointConsequenceType.Additive, component.JointConsequences);
        Assert.AreEqual(12.5d, component.HazardThreshold, 0d);
    }

    /// <summary>Verifies the hazard-function view creates the root on demand and auto-names.</summary>
    [TestMethod]
    public void Test_HazardFunction_View_CreatesRoot()
    {
        // Arrange
        var component = new SystemComponent();
        var hazard = FlowFrequency();

        // Act
        component.HazardFunction = hazard;

        // Assert — a root element wraps the function; the default name upgraded.
        Assert.AreSame(hazard, component.HazardFunction);
        Assert.AreEqual(1, component.Graph.GetElements<HazardElement>().Count());
        Assert.AreEqual("System Component - Flow Frequency", component.Name);

        // Re-assignment reuses the root.
        var replacement = FlowFrequency();
        component.HazardFunction = replacement;
        Assert.AreSame(replacement, component.HazardFunction);
        Assert.AreEqual(1, component.Graph.Elements.Count);
    }

    /// <summary>Verifies the levee projection: order, shapes, parent wiring, and the non-fail form.</summary>
    [TestMethod]
    public void Test_Projection_LeveeScenario()
    {
        // Act
        var component = LeveeComponent();
        var modes = component.FailureModes;

        // Assert — one mode per terminal in declared order.
        Assert.AreEqual(2, modes.Count);

        // The failure mode: rating in stage 0, breach response, one consequence.
        var fail = modes[0];
        Assert.IsFalse(fail.IsNonFailureMode);
        Assert.AreEqual(1, fail.ResponseStages.Count);
        Assert.AreEqual(1, fail.ResponseStages[0].Transforms.Count);
        Assert.IsInstanceOfType(fail.ResponseStages[0].Response, typeof(TabularResponse));
        Assert.AreEqual(0, fail.ResponseToConsequence.Count);
        Assert.AreEqual(1, fail.ConsequenceFunctions.Count);
        Assert.IsNull(fail.ConsequenceHazardPosition);
        Assert.AreEqual(1, fail.ResolvedConsequenceHazardPosition);
        Assert.IsFalse(fail.MultipleConsequences);
        Assert.AreSame(component, fail.Parent);

        // The non-failure mode: canonical stage form (transforms stay bindable).
        var nonFail = modes[1];
        Assert.IsTrue(nonFail.IsNonFailureMode);
        Assert.AreEqual(1, nonFail.ResponseStages.Count);
        Assert.AreEqual(1, nonFail.ResponseStages[0].Transforms.Count);
        Assert.AreEqual(0, nonFail.ResponseToConsequence.Count);

        // The component validates clean.
        var (isValid, messages) = component.Validate();
        Assert.IsTrue(isValid, string.Join(" | ", messages));
    }

    /// <summary>Verifies the levee binding: consequences bound to the raw flow project position 0.</summary>
    [TestMethod]
    public void Test_Projection_LeveeBinding_RawHazard()
    {
        // Arrange — the acceptance scenario: consequences given peak flow, computed upstream.
        var component = LeveeComponent();
        var hazardElement = component.Graph.GetElements<HazardElement>().Single();
        var failTerminal = (ConsequenceElement)component.Graph.GetElement("Failure Damages")!;

        // Act
        failTerminal.HazardSource = new RiskConnection(hazardElement);
        var mode = component.FailureModes[0];

        // Assert — the binding projects to chain position 0 (the raw hazard).
        Assert.AreEqual(0, mode.ConsequenceHazardPosition);
        Assert.AreEqual(HazardDimension.Primary, mode.ConsequenceHazardDimension);
        Assert.AreEqual(0, mode.ResolvedConsequenceHazardPosition);
    }

    /// <summary>Verifies a response fanning out to two terminals sets the multiple-consequences flag.</summary>
    [TestMethod]
    public void Test_Projection_ResponseFanOut_SetsMultipleConsequences()
    {
        // Arrange — a second terminal off the breach response.
        var component = LeveeComponent();
        var response = component.Graph.GetElements<ResponseElement>().Single();
        var second = new ConsequenceElement("Life Loss") { Input = new RiskConnection(response) };
        second.Functions.Add(Damages("Stage", "ft", "Life Loss", "lives"));
        component.Graph.AddElement(second);

        // Act
        var modes = component.FailureModes;

        // Assert — both response-fed modes carry the flag; the non-fail mode does not.
        Assert.AreEqual(3, modes.Count);
        Assert.IsTrue(modes[0].MultipleConsequences);
        Assert.IsTrue(modes[2].MultipleConsequences);
        Assert.IsFalse(modes[1].MultipleConsequences);
    }

    /// <summary>Verifies a response chain projects multiple stages in order.</summary>
    [TestMethod]
    public void Test_Projection_ResponseChain_TwoStages()
    {
        // Arrange — H → R1 → T → R2 → C.
        var component = new SystemComponent();
        var hazard = new HazardElement("Hazard") { Function = FlowFrequency() };
        var first = new ResponseElement("Gate Failure")
        {
            Function = Fragility("Flow", "cfs", "Gate Fragility"),
            Input = new RiskConnection(hazard),
        };
        var rating = new TransformElement("Rating")
        {
            Function = Rating("Flow", "cfs", "Stage", "ft"),
            Input = new RiskConnection(first),
        };
        var second = new ResponseElement("Breach")
        {
            Function = Fragility("Stage", "ft", "Breach Fragility"),
            Input = new RiskConnection(rating),
        };
        var terminal = new ConsequenceElement("Damages") { Input = new RiskConnection(second) };
        terminal.Functions.Add(Damages("Stage", "ft"));
        component.Graph.AddElement(hazard);
        component.Graph.AddElement(first);
        component.Graph.AddElement(rating);
        component.Graph.AddElement(second);
        component.Graph.AddElement(terminal);

        // Act
        var mode = component.FailureModes.Single();

        // Assert — stage 0: no transforms + gate response; stage 1: rating + breach response.
        Assert.AreEqual(2, mode.ResponseStages.Count);
        Assert.AreEqual(0, mode.ResponseStages[0].Transforms.Count);
        Assert.AreEqual("Gate Fragility", mode.ResponseStages[0].Response.Name);
        Assert.AreEqual(1, mode.ResponseStages[1].Transforms.Count);
        Assert.AreEqual("Breach Fragility", mode.ResponseStages[1].Response.Name);
        Assert.AreEqual(1, mode.TotalStageTransformCount);
        Assert.IsFalse(mode.IsNonFailureMode);
    }

    /// <summary>
    /// Verifies the cascade projection (arch doc §7.9): exit ports become stage polarities,
    /// sibling end states share response occurrence ordinals, and terminal names are stamped.
    /// </summary>
    [TestMethod]
    public void Test_Projection_PortPolarity_Cascade()
    {
        // Act
        var modes = CascadeComponent().FailureModes;

        // Assert — declared terminal order: full breach, partial damages, background.
        Assert.AreEqual(3, modes.Count);

        var full = modes[0];
        Assert.AreEqual(2, full.ResponseStages.Count);
        Assert.AreEqual(BranchPolarity.Fail, full.ResponseStages[0].BranchPolarity);
        Assert.AreEqual(BranchPolarity.Fail, full.ResponseStages[1].BranchPolarity);
        CollectionAssert.AreEqual(new[] { 0, 1 }, full.ProjectedResponseOrdinals);
        Assert.AreEqual("Full Breach Damages", full.ProjectedTerminalName);
        Assert.IsFalse(full.MultipleConsequences);

        var partial = modes[1];
        Assert.AreEqual(2, partial.ResponseStages.Count);
        Assert.AreEqual(BranchPolarity.Fail, partial.ResponseStages[0].BranchPolarity);
        Assert.AreEqual(BranchPolarity.NonFail, partial.ResponseStages[1].BranchPolarity);
        CollectionAssert.AreEqual(new[] { 0, 1 }, partial.ProjectedResponseOrdinals,
            "Sibling end states share their response elements' occurrence ordinals.");
        Assert.AreEqual("Partial Damages", partial.ProjectedTerminalName);
        Assert.IsFalse(partial.MultipleConsequences);

        var background = modes[2];
        Assert.IsTrue(background.IsNonFailureMode);
        Assert.IsNull(background.ProjectedResponseOrdinals);
        Assert.AreEqual("Background Damages", background.ProjectedTerminalName);

        // Sibling stages reuse the same shared response function instances (draw coherence).
        Assert.AreSame(full.ResponseStages[0].Response, partial.ResponseStages[0].Response);
        Assert.AreSame(full.ResponseStages[1].Response, partial.ResponseStages[1].Response);
    }

    /// <summary>
    /// Verifies the multiple-consequences flag is port-aware (arch doc §7.9): a Fail terminal
    /// and a Non-Fail terminal are distinct end states, not fan-out of one branch; two terminals
    /// on the same port still flag.
    /// </summary>
    [TestMethod]
    public void Test_Projection_BothPortFanOut_FlagPortAware()
    {
        // Arrange — the levee response gains a Non-Fail-port terminal.
        var component = LeveeComponent();
        var response = component.Graph.GetElements<ResponseElement>().Single();
        var partial = new ConsequenceElement("Partial Damages") { Input = new RiskConnection(response, 1) };
        partial.Functions.Add(Damages("Stage", "ft"));
        component.Graph.AddElement(partial);

        // Assert — one consumer per port: no mode is flagged.
        var modes = component.FailureModes;
        Assert.AreEqual(3, modes.Count);
        Assert.IsFalse(modes[0].MultipleConsequences, "A lone Fail-port terminal must not flag.");
        Assert.IsFalse(modes[2].MultipleConsequences, "A lone Non-Fail-port terminal must not flag.");
        Assert.AreEqual(BranchPolarity.NonFail, modes[2].ResponseStages[0].BranchPolarity);

        // Act — a second Fail-port terminal restores the same-port fan-out flag there only.
        var second = new ConsequenceElement("Life Loss") { Input = new RiskConnection(response) };
        second.Functions.Add(Damages("Stage", "ft", "Life Loss", "lives"));
        component.Graph.AddElement(second);
        modes = component.FailureModes;

        // Assert
        Assert.IsTrue(modes[0].MultipleConsequences);
        Assert.IsTrue(modes[3].MultipleConsequences);
        Assert.IsFalse(modes[2].MultipleConsequences, "The Non-Fail port still has a single consumer.");
    }

    /// <summary>
    /// Verifies chain expansion wires each post-response connection at the stage's polarity port,
    /// so a Non-Fail-polarity chain round-trips through the graph bit-identically.
    /// </summary>
    [TestMethod]
    public void Test_AddFailureMode_PolarityRoundTrip()
    {
        // Arrange — initiation (Fail) then progression (Non-Fail): a partial-damage chain.
        FailureMode BuildMode() => new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction>(), Fragility("Flow", "cfs", "Initiation Fragility")),
                new ResponseStage(new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") },
                    Fragility("Stage", "ft", "Progression Fragility"), BranchPolarity.NonFail),
            },
            null,
            new List<IConsequenceFunction> { Damages("Stage", "ft") });

        var component = new SystemComponent(FlowFrequency());

        // Act
        component.AddFailureMode(BuildMode());
        var projected = component.FailureModes.Single();

        // Assert — polarities survive the expansion and the hash is bit-identical.
        Assert.AreEqual(BranchPolarity.Fail, projected.ResponseStages[0].BranchPolarity);
        Assert.AreEqual(BranchPolarity.NonFail, projected.ResponseStages[1].BranchPolarity);
        CollectionAssert.AreEqual(BuildMode().CanonicalHash(), projected.CanonicalHash(),
            "Expanding a polarity-carrying chain and projecting it back must preserve the canonical hash.");

        // The terminal consumes the progression response's Non-Fail port.
        var terminal = component.Graph.GetElements<ConsequenceElement>().Single();
        Assert.AreEqual(1, terminal.Input!.SourcePort);
    }

    /// <summary>Verifies each FailureModes access returns a fresh, equal-content snapshot.</summary>
    [TestMethod]
    public void Test_FailureModes_FreshSnapshot()
    {
        // Arrange
        var component = LeveeComponent();

        // Act
        var first = component.FailureModes;
        var second = component.FailureModes;

        // Assert — distinct instances, identical content.
        Assert.AreNotSame(first[0], second[0]);
        CollectionAssert.AreEqual(first[0].CanonicalHash(), second[0].CanonicalHash());
    }

    /// <summary>Verifies the parent wiring feeds hazard labels into continuity validation.</summary>
    [TestMethod]
    public void Test_Projection_ParentFeedsHazardLabels()
    {
        // Arrange — the rating declares 'Stage' input but the hazard produces 'Flow'.
        var component = LeveeComponent();
        var rating = component.Graph.GetElements<TransformElement>().Single();
        rating.Function!.SpecifiedHazard = "Stage";

        // Act
        var (isValid, messages) = component.Validate();

        // Assert — advisory only, naming the mismatch against the hazard label.
        Assert.IsTrue(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("'Flow'")));
    }

    /// <summary>Verifies chain expansion is lossless: the projected mode hashes bit-identically.</summary>
    [TestMethod]
    public void Test_AddFailureMode_ExpansionRoundTrip()
    {
        // Arrange — a hand-built chain mode.
        FailureMode BuildMode() => new FailureMode(
            new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") },
            new List<ITransformFunction> { Rating("Stage", "ft", "Damage Stage", "ft") },
            Fragility("Stage", "ft"),
            Damages("Damage Stage", "ft"));

        var component = new SystemComponent(FlowFrequency());

        // Act
        component.AddFailureMode(BuildMode());
        var projected = component.FailureModes.Single();

        // Assert — the expansion created the elements and projects the same content.
        Assert.AreEqual(5, component.Graph.Elements.Count);
        CollectionAssert.AreEqual(BuildMode().CanonicalHash(), projected.CanonicalHash(),
            "Expanding a chain mode and projecting it back must preserve the canonical hash.");

        // An explicit non-default binding position becomes a hazard-source binding on the terminal.
        var boundMode = BuildMode();
        boundMode.ConsequenceHazardPosition = 0;
        component.AddFailureMode(boundMode);
        var terminal = component.Graph.GetElements<ConsequenceElement>().Last();
        Assert.IsNotNull(terminal.HazardSource);
        Assert.IsInstanceOfType(terminal.HazardSource!.Source, typeof(HazardElement));
        Assert.AreEqual(0, component.FailureModes[1].ConsequenceHazardPosition);
    }

    /// <summary>Verifies non-failure modes expand without a response element.</summary>
    [TestMethod]
    public void Test_AddFailureMode_NonFail_NoResponseElement()
    {
        // Arrange — a stage-form non-failure mode.
        var component = new SystemComponent(FlowFrequency());
        var nonFail = new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") }, new NonFailResponse()),
            },
            null,
            new List<IConsequenceFunction> { Damages("Stage", "ft") });

        // Act
        component.AddFailureMode(nonFail);

        // Assert — hazard + transform + consequence; no response element; projects non-fail.
        Assert.AreEqual(0, component.Graph.GetElements<ResponseElement>().Count());
        var projected = component.FailureModes.Single();
        Assert.IsTrue(projected.IsNonFailureMode);
        CollectionAssert.AreEqual(nonFail.CanonicalHash(), projected.CanonicalHash());
    }

    /// <summary>Verifies determinism aggregates over the hazard and projected modes.</summary>
    [TestMethod]
    public void Test_IsDeterministic_Aggregates()
    {
        // The deterministic levee component.
        var component = LeveeComponent();
        Assert.IsTrue(component.IsDeterministic);

        // An uncertain response makes the component uncertain.
        component.Graph.GetElements<ResponseElement>().Single().Function = new ParametricResponse();
        Assert.IsFalse(component.IsDeterministic);
    }

    /// <summary>Verifies the combination caches re-key on the failure-path count.</summary>
    [TestMethod]
    public void Test_FailureModeCombinations_CountKeyed()
    {
        // Arrange — one failure path.
        var component = LeveeComponent();

        // Assert — 2^1 combinations of 1 mode; one subset count.
        Assert.AreEqual(1, component.FailureModeIndicators!.GetLength(1));
        CollectionAssert.AreEqual(new[] { 1 }, component.FailureModeBinomialCombinations);

        // Adding a second failure path re-keys the caches automatically.
        var response = component.Graph.GetElements<ResponseElement>().Single();
        var second = new ConsequenceElement("Second Damages") { Input = new RiskConnection(response) };
        second.Functions.Add(Damages("Stage", "ft"));
        component.Graph.AddElement(second);
        Assert.AreEqual(2, component.FailureModeIndicators!.GetLength(1));
        CollectionAssert.AreEqual(new[] { 2, 1 }, component.FailureModeBinomialCombinations);
    }

    /// <summary>Verifies the multivariate-normal off-diagonal constants and matrix validation.</summary>
    [TestMethod]
    public void Test_MultivariateNormal_V10Constants()
    {
        // Arrange — two failure paths off one response fan-out.
        var component = LeveeComponent();
        var response = component.Graph.GetElements<ResponseElement>().Single();
        var second = new ConsequenceElement("Second Damages") { Input = new RiskConnection(response) };
        second.Functions.Add(Damages("Stage", "ft"));
        component.Graph.AddElement(second);

        // Independent: identity correlation.
        component.FailureModeDependency = DependencyType.Independent;
        Assert.IsNotNull(component.FailureModeMultivariateNormal);
        Assert.AreEqual(0d, component.CorrelationMatrix![0, 1], 0d);

        // Perfectly positive: off-diagonals 1 − √εmach.
        component.FailureModeDependency = DependencyType.PerfectlyPositive;
        Assert.IsNotNull(component.FailureModeMultivariateNormal);
        Assert.AreEqual(1d - Math.Sqrt(Tools.DoubleMachineEpsilon), component.CorrelationMatrix![0, 1], 0d);

        // Perfectly negative at D = 2: −1/(D−1) + √εmach.
        component.FailureModeDependency = DependencyType.PerfectlyNegative;
        Assert.IsNotNull(component.FailureModeMultivariateNormal);
        Assert.AreEqual(-1d + Math.Sqrt(Tools.DoubleMachineEpsilon), component.CorrelationMatrix![0, 1], 0d);

        // A valid user matrix builds; an indefinite one is rejected.
        component.FailureModeDependency = DependencyType.CorrelationMatrix;
        component.CorrelationMatrix = new double[,] { { 1d, 0.5d }, { 0.5d, 1d } };
        Assert.IsTrue(component.IsCorrelationMatrixValid());
        Assert.IsNotNull(component.FailureModeMultivariateNormal);
        component.CorrelationMatrix = new double[,] { { 1d, 2d }, { 2d, 1d } };
        Assert.IsFalse(component.IsCorrelationMatrixValid());
        Assert.IsNull(component.FailureModeMultivariateNormal);

        // A wrong-dimension matrix is rejected too.
        component.CorrelationMatrix = new double[,] { { 1d } };
        Assert.IsFalse(component.IsCorrelationMatrixValid());
    }

    /// <summary>Verifies the v1.0 dependency coercion for the single-mode methods.</summary>
    [TestMethod]
    public void Test_FailureModeMethod_CoercesDependency()
    {
        // Arrange
        var component = new SystemComponent { FailureModeDependency = DependencyType.PerfectlyPositive };

        // Act
        component.FailureModeMethod = FailureModeMethod.CommonCauseFailures;

        // Assert — common-cause has no dependence model (v1.0 behavior).
        Assert.AreEqual(DependencyType.Independent, component.FailureModeDependency);

        component.FailureModeDependency = DependencyType.PerfectlyNegative;
        component.FailureModeMethod = FailureModeMethod.MutuallyExclusive;
        Assert.AreEqual(DependencyType.Independent, component.FailureModeDependency);
    }

    /// <summary>
    /// Verifies that <see cref="SystemComponent.SetupSamplers(int, int, SamplingScheme)"/> materializes the automatic
    /// dependency matrix before any sampling — the compute path captures the raw
    /// <see cref="SystemComponent.CorrelationMatrix"/> reference, and v1.0 kept it fresh by
    /// rebuilding eagerly on every edit. The regression this pins: under perfectly negative
    /// dependence the derived matrix previously never materialized on the run path (only the
    /// <see cref="SystemComponent.FailureModeMultivariateNormal"/> getter built it), which
    /// silently zeroed the dependent combination kernels.
    /// </summary>
    [TestMethod]
    public void Test_SetupSamplers_MaterializesDependencyMatrix()
    {
        // Arrange — two failure paths, perfectly negative dependence; the MVN getter is never read.
        var component = LeveeComponent();
        var response = component.Graph.GetElements<ResponseElement>().Single();
        var second = new ConsequenceElement("Second Damages") { Input = new RiskConnection(response) };
        second.Functions.Add(Damages("Stage", "ft"));
        component.Graph.AddElement(second);
        component.FailureModeDependency = DependencyType.PerfectlyNegative;
        Assert.IsNull(component.CorrelationMatrix, "Precondition: nothing has materialized the derived matrix yet.");

        // Act
        component.SetupSamplers(8, 12345, SamplingScheme.LatinHypercube);

        // Assert — the derived matrix is current for the sampled-component capture.
        Assert.IsNotNull(component.CorrelationMatrix, "SetupSamplers must materialize the automatic dependency matrix.");
        Assert.AreEqual(-1d + Math.Sqrt(Tools.DoubleMachineEpsilon), component.CorrelationMatrix![0, 1], 0d,
            "The perfectly negative off-diagonal must be −1/(D−1) + √εmach at D = 2.");
    }

    /// <summary>Verifies the serialization round trip, including the G17 correlation matrix.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange — the levee with a second failure path and a user correlation matrix.
        var component = LeveeComponent();
        var response = component.Graph.GetElements<ResponseElement>().Single();
        var second = new ConsequenceElement("Second Damages") { Input = new RiskConnection(response) };
        second.Functions.Add(Damages("Stage", "ft"));
        component.Graph.AddElement(second);
        component.FailureModeDependency = DependencyType.CorrelationMatrix;
        component.CorrelationMatrix = new double[,] { { 1d, 1d / 3d }, { 1d / 3d, 1d } };
        component.HazardThreshold = 987.125d;

        // Act
        var restored = new SystemComponent(component.ToXElement());

        // Assert — options, matrix (bit-exact G17), graph, projection, and hash all round-trip.
        Assert.AreEqual(component.Name, restored.Name);
        Assert.AreEqual(component.FailureModeMethod, restored.FailureModeMethod);
        Assert.AreEqual(component.FailureModeDependency, restored.FailureModeDependency);
        Assert.AreEqual(component.JointConsequences, restored.JointConsequences);
        Assert.AreEqual(component.HazardThreshold, restored.HazardThreshold, 0d);
        Assert.AreEqual(1d / 3d, restored.CorrelationMatrix![0, 1], 0d);
        Assert.AreEqual(component.Graph.Elements.Count, restored.Graph.Elements.Count);
        Assert.AreEqual(component.FailureModes.Count, restored.FailureModes.Count);
        CollectionAssert.AreEqual(component.CanonicalHash(), restored.CanonicalHash(),
            "Round-trip must preserve the canonical hash.");
        Assert.AreEqual(component.ToXElement().ToString(), restored.ToXElement().ToString());
    }

    /// <summary>
    /// Verifies the seed-identity guarantee: element renames, fresh ids, canvas moves, and
    /// description edits never move the canonical hash — the v1.0 canvas-position seed bug is
    /// structurally unreachable.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_ElementIdentityInert()
    {
        static void AssertIdentityInert(SystemComponent component)
        {
            byte[] baseline = component.CanonicalHash();

            // Every identity/display edit the UI layer can make.
            foreach (var element in component.Graph.Elements.ToList())
            {
                component.Graph.TryRenameElement(element, element.Name + " (renamed)");
                element.Description = "Edited description.";
                element.LeftPosition += 250d;
                element.TopPosition -= 125d;
                element.AssignNewId();
            }
            component.Name = "Renamed component";
            foreach (var element in component.Graph.Elements)
            {
                foreach (var function in element.GetFunctions())
                {
                    function.Name += " (renamed)";
                    function.Description = "Edited.";
                }
            }

            CollectionAssert.AreEqual(baseline, component.CanonicalHash(),
                "Element identity, canvas position, and metadata edits must never change the canonical hash.");
        }

        // The single-stage levee and the both-port cascade (the §7.9 topology annotation derives
        // from structure, so identity edits stay inert on cascades too).
        AssertIdentityInert(LeveeComponent());
        AssertIdentityInert(CascadeComponent());
    }

    /// <summary>Verifies two independently built equal-content components hash identically.</summary>
    [TestMethod]
    public void Test_CanonicalHash_EqualContent_EqualHash()
    {
        // Act — independent builds mean different Guids, same content.
        var a = LeveeComponent();
        var b = LeveeComponent();

        // Assert — including the cascade topology (ordinals derive from structure, not identity).
        CollectionAssert.AreEqual(a.CanonicalHash(), b.CanonicalHash(),
            "Identical-content components must hash identically regardless of element identity.");
        CollectionAssert.AreEqual(CascadeComponent().CanonicalHash(), CascadeComponent().CanonicalHash(),
            "Identical-content cascades must hash identically regardless of element identity.");
    }

    /// <summary>
    /// Verifies the identity form distinguishes response-sharing topology (arch doc §7.9): two
    /// terminals fed by ONE response element (shared draws, one chance node) and two terminals
    /// fed by equal-content DUPLICATE elements (independent draws, separate events) carry
    /// identical mode XML but must hash differently — the topology changes the exclusivity
    /// algebra and the sampled streams, so it is compute content.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_SharedVsDuplicatedResponse_Distinct()
    {
        // Arrange — shared: ONE response element carries both branch terminals (a genuine
        // exclusive pair: Fail port → failure damages, Non-Fail port → partial damages).
        var shared = LeveeComponent();
        var sharedResponse = shared.Graph.GetElements<ResponseElement>().Single();
        var sharedPartial = new ConsequenceElement("Partial Damages") { Input = new RiskConnection(sharedResponse, 1) };
        sharedPartial.Functions.Add(Damages("Stage", "ft"));
        shared.Graph.AddElement(sharedPartial);

        // Duplicated: an equal-content parallel response element carries the Non-Fail terminal —
        // two independent chance nodes, not one.
        var duplicated = LeveeComponent();
        var rating = duplicated.Graph.GetElements<TransformElement>().Single();
        var duplicateResponse = new ResponseElement("Breach Copy")
        {
            Function = Fragility("Stage", "ft"),
            Input = new RiskConnection(rating),
        };
        var duplicatedPartial = new ConsequenceElement("Partial Damages") { Input = new RiskConnection(duplicateResponse, 1) };
        duplicatedPartial.Functions.Add(Damages("Stage", "ft"));
        duplicated.Graph.AddElement(duplicateResponse);
        duplicated.Graph.AddElement(duplicatedPartial);

        // Assert — mode-for-mode the projected XMLs are equal content (same stage content and
        // polarities, no fan-out flags), but the identity hashes differ: one chance node with an
        // exclusive branch pair versus two independent chance nodes is a compute distinction.
        for (int i = 0; i < 3; i++)
        {
            CollectionAssert.AreEqual(
                shared.FailureModes[i].CanonicalHash(),
                duplicated.FailureModes[i].CanonicalHash(),
                $"Projected mode {i} must be equal-content by construction.");
        }
        CollectionAssert.AreNotEqual(shared.CanonicalHash(), duplicated.CanonicalHash(),
            "Shared and duplicated response topologies compute differently and must hash differently.");
    }

    /// <summary>
    /// Verifies the topology annotation lives in the identity form only: neither the component's
    /// persisted graph nor a projected mode's XML carries the ResponseNodes attribute.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_ResponseNodesIdentityOnly()
    {
        // Arrange
        var component = CascadeComponent();

        // Assert
        Assert.IsFalse(component.ToXElement().ToString().Contains("ResponseNodes", StringComparison.Ordinal),
            "The persisted graph must not carry the identity-form topology annotation.");
        Assert.IsFalse(component.FailureModes[0].ToXElement().ToString().Contains("ResponseNodes", StringComparison.Ordinal),
            "A projected mode's persistence XML must not carry the identity-form topology annotation.");
    }

    /// <summary>Verifies every compute-relevant edit moves the hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_ComputeSensitive()
    {
        void AssertMoves(Action<SystemComponent> mutation, string description)
        {
            var component = LeveeComponent();
            byte[] before = component.CanonicalHash();
            mutation(component);
            CollectionAssert.AreNotEqual(before, component.CanonicalHash(), $"{description} must change the canonical hash.");
        }

        AssertMoves(c => c.FailureModeMethod = FailureModeMethod.CompetingFailures, "The failure-mode method");
        AssertMoves(c => c.JointConsequences = JointConsequenceType.Additive, "The joint-consequence option");
        AssertMoves(c => c.FailureModeDependency = DependencyType.PerfectlyPositive, "The dependency option");
        AssertMoves(c => c.HazardThreshold = 42d, "The hazard threshold");
        AssertMoves(c =>
        {
            var terminal = (ConsequenceElement)c.Graph.GetElement("Failure Damages")!;
            terminal.HazardSource = new RiskConnection(c.Graph.GetElements<HazardElement>().Single());
        }, "A consequence hazard binding");
        AssertMoves(c =>
        {
            var rating = c.Graph.GetElements<TransformElement>().Single();
            ((TabularTransform)rating.Function!).UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(75d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
        }, "A transform table edit");
        AssertMoves(c =>
        {
            var response = c.Graph.GetElements<ResponseElement>().Single();
            var second = new ConsequenceElement("Life Loss") { Input = new RiskConnection(response) };
            second.Functions.Add(Damages("Stage", "ft", "Life Loss", "lives"));
            c.Graph.AddElement(second);
        }, "Adding a failure path");
    }

    /// <summary>Verifies declared element order is semantic: reordering terminals moves the hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_TerminalOrderIsContent()
    {
        // Arrange — the same two-terminal topology declared in two orders, with distinct
        // consequence content so the modes are distinguishable.
        SystemComponent Build(bool lifeLossFirst)
        {
            var component = LeveeComponent();
            var response = component.Graph.GetElements<ResponseElement>().Single();
            var lifeLoss = new ConsequenceElement("Life Loss") { Input = new RiskConnection(response) };
            var curve = Damages("Stage", "ft", "Life Loss", "lives");
            curve.UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(10d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
            lifeLoss.Functions.Add(curve);
            if (lifeLossFirst)
            {
                // Rebuild with the life-loss terminal declared before the damages terminal.
                var reordered = new SystemComponent { Name = component.Name };
                var elements = component.Graph.Elements.ToList();
                component.Graph.AddElement(lifeLoss);
                foreach (var element in elements.Take(3)) { component.Graph.RemoveElement(element); reordered.Graph.AddElement(element); }
                component.Graph.RemoveElement(lifeLoss);
                reordered.Graph.AddElement(lifeLoss);
                foreach (var element in elements.Skip(3)) { component.Graph.RemoveElement(element); reordered.Graph.AddElement(element); }
                return reordered;
            }
            component.Graph.AddElement(lifeLoss);
            return component;
        }

        // Act / Assert — projected failure-mode order follows declared terminal order.
        var damagesFirst = Build(lifeLossFirst: false);
        var lifeFirst = Build(lifeLossFirst: true);
        CollectionAssert.AreNotEqual(damagesFirst.CanonicalHash(), lifeFirst.CanonicalHash(),
            "Reordering consequence elements reorders the projected failure modes, which is a compute edit.");
    }

    /// <summary>Verifies the occurrence-index scheme (architecture doc §5.5.4).</summary>
    [TestMethod]
    public void Test_AssignOccurrenceIndices_Scheme()
    {
        // Identical-content components number 0..n−1 in declared order (the tiebreak).
        var a = LeveeComponent();
        var b = LeveeComponent();
        var c = LeveeComponent();
        SystemComponent.AssignOccurrenceIndices(new[] { a, b, c });
        Assert.AreEqual(0, a.OccurrenceIndex);
        Assert.AreEqual(1, b.OccurrenceIndex);
        Assert.AreEqual(2, c.OccurrenceIndex);

        // Distinct-content components are unaffected by each other.
        var distinct = LeveeComponent();
        distinct.HazardThreshold = 99d;
        SystemComponent.AssignOccurrenceIndices(new[] { a, distinct, b });
        Assert.AreEqual(0, a.OccurrenceIndex);
        Assert.AreEqual(0, distinct.OccurrenceIndex);
        Assert.AreEqual(1, b.OccurrenceIndex);

        // Cross-analysis stability: the (hash, occurrence) multiset survives declared reorder.
        SystemComponent.AssignOccurrenceIndices(new[] { b, distinct, a });
        Assert.AreEqual(0, b.OccurrenceIndex);
        Assert.AreEqual(1, a.OccurrenceIndex);
        Assert.AreEqual(0, distinct.OccurrenceIndex);

        // Metadata edits never perturb the assignment.
        b.Name = "Renamed";
        SystemComponent.AssignOccurrenceIndices(new[] { b, a });
        Assert.AreEqual(0, b.OccurrenceIndex);
        Assert.AreEqual(1, a.OccurrenceIndex);

        // Guards.
        Assert.ThrowsException<ArgumentNullException>(() => SystemComponent.AssignOccurrenceIndices(null!));
        Assert.ThrowsException<ArgumentException>(() => SystemComponent.AssignOccurrenceIndices(new SystemComponent[] { null! }));
    }

    /// <summary>Verifies the clone is deep and isolated.</summary>
    [TestMethod]
    public void Test_Clone_DeepAndIsolated()
    {
        // Arrange
        var component = LeveeComponent();

        // Act
        var clone = component.Clone();

        // Assert — equal content, distinct graph and function instances.
        CollectionAssert.AreEqual(component.CanonicalHash(), clone.CanonicalHash());
        Assert.AreNotSame(component.Graph, clone.Graph);
        Assert.AreNotSame(component.HazardFunction, clone.HazardFunction);

        // Mutating the clone leaves the original untouched (v1.1 improvement over v1.0's
        // shared-reference clone).
        byte[] baseline = component.CanonicalHash();
        clone.HazardThreshold = 555d;
        ((TabularTransform)clone.Graph.GetElements<TransformElement>().Single().Function!).UncertainOrderedPairedData =
            new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(1d)), new UncertainOrdinate(100d, new Deterministic(99d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
        CollectionAssert.AreEqual(baseline, component.CanonicalHash());
        CollectionAssert.AreNotEqual(baseline, clone.CanonicalHash());
    }

    /// <summary>Verifies the validation matrix: graph pass-through, matrix errors, warning relay.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // The levee validates clean.
        Assert.IsTrue(LeveeComponent().Validate().IsValid);

        // Graph structural errors pass through.
        var broken = LeveeComponent();
        broken.Graph.AddElement(new TransformElement("Floater") { Function = Rating("Flow", "cfs", "Stage", "ft") });
        Assert.IsFalse(broken.Validate().IsValid);

        // A missing user correlation matrix errors in the correlation-matrix mode.
        var matrixless = LeveeComponent();
        matrixless.FailureModeDependency = DependencyType.CorrelationMatrix;
        var (matrixValid, matrixMessages) = matrixless.Validate();
        Assert.IsFalse(matrixValid);
        Assert.IsTrue(matrixMessages.Any(m => m.Contains("correlation matrix")));

        // An empty component reports the missing hazard root.
        Assert.IsFalse(new SystemComponent().Validate().IsValid);
    }

    /// <summary>Verifies change notification for options and the graph relay.</summary>
    [TestMethod]
    public void Test_PropertyChanged_Raised()
    {
        // Arrange
        var component = new SystemComponent();
        var raised = new List<string>();
        component.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        component.Name = "Levee";
        component.FailureModeMethod = FailureModeMethod.CompetingFailures;
        component.JointConsequences = JointConsequenceType.Minimum;
        component.FailureModeDependency = DependencyType.PerfectlyPositive;
        component.HazardThreshold = 1d;
        component.Graph.AddElement(new HazardElement("Hazard"));

        // Assert — the graph membership change relays as a failure-mode change.
        CollectionAssert.AreEqual(new[]
        {
            nameof(SystemComponent.Name),
            nameof(SystemComponent.FailureModeMethod),
            nameof(SystemComponent.JointConsequences),
            nameof(SystemComponent.FailureModeDependency),
            nameof(SystemComponent.HazardThreshold),
            nameof(SystemComponent.FailureModes),
        }, raised);
    }

    /// <summary>Builds a data-bearing stage-frequency hazard for the sampler tests.</summary>
    private static TabularHazard SamplerHazard()
    {
        return new TabularHazard
        {
            Name = "Stage Frequency",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            NoUncertaintyFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Deterministic(0d)),
                    new UncertainOrdinate(0.5d, new Deterministic(10d)),
                    new UncertainOrdinate(0.001d, new Deterministic(30d)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds an uncertain data-bearing fragility for the sampler tests.</summary>
    private static TabularResponse SamplerFragility(string name)
    {
        return new TabularResponse
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(10d, new Triangular(0d, 0.05d, 0.1d)), new UncertainOrdinate(20d, new Triangular(0.7d, 0.9d, 1d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Triangular),
        };
    }

    /// <summary>Builds a labeled deterministic data-bearing consequence for the sampler tests.</summary>
    private static TabularConsequence SamplerConsequence(string name)
    {
        return new TabularConsequence
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(30d, new Deterministic(300d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a labeled n-branch mixture of deterministic children with equal weights.</summary>
    private static CompositeConsequence SamplerMixture(string name, int branches)
    {
        var mixture = new CompositeConsequence
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        for (int i = 0; i < branches; i++)
        {
            mixture.ConsequenceFunctions.Add(new WeightedConsequenceFunction(SamplerConsequence($"{name} Branch {i}"), 1d / branches));
        }
        return mixture;
    }

    /// <summary>Builds a component of two fail modes over the given response functions.</summary>
    private static SystemComponent SamplerComponent(IResponseFunction responseA, IResponseFunction responseB)
    {
        var component = new SystemComponent { Name = "Sampler" };
        component.HazardFunction = SamplerHazard();
        component.AddFailureMode(new FailureMode(null, null, responseA, SamplerConsequence("A Loss")));
        component.AddFailureMode(new FailureMode(null, null, responseB, SamplerConsequence("B Loss")));
        return component;
    }

    /// <summary>
    /// Verifies the single-owner seeding rule: one response instance shared by two modes draws
    /// identical realizations everywhere it appears (one instance = one knowledge quantity),
    /// while two distinct equal-content instances get different ordinals and draw independently.
    /// </summary>
    [TestMethod]
    public void Test_SetupSamplers_SingleOwnerSeeding_SharedVersusDistinct()
    {
        // Arrange / Act — shared instance.
        var shared = SamplerFragility("Shared Fragility");
        var componentShared = SamplerComponent(shared, shared);
        componentShared.SetupSamplers(32, componentSeed: 12345, SamplingScheme.LatinHypercube);

        // Assert — both modes carry the identical sampled response in every realization.
        for (int k = 0; k < 32; k++)
        {
            var sampled = componentShared.Sample(k);
            Assert.AreEqual(sampled.FailureModes[0].SRP(15d), sampled.FailureModes[1].SRP(15d), 0d,
                $"Realization {k}: a shared instance must draw identically at both positions.");
        }

        // Arrange / Act — distinct equal-content instances.
        var componentDistinct = SamplerComponent(SamplerFragility("Fragility"), SamplerFragility("Fragility"));
        componentDistinct.SetupSamplers(32, componentSeed: 12345, SamplingScheme.LatinHypercube);

        // Assert — the ordinals decouple the streams.
        bool anyDiffer = false;
        for (int k = 0; k < 32; k++)
        {
            var sampled = componentDistinct.Sample(k);
            anyDiffer |= sampled.FailureModes[0].SRP(15d) != sampled.FailureModes[1].SRP(15d);
        }
        Assert.IsTrue(anyDiffer, "Equal-content distinct instances must draw independently (ordinal in the seed).");
    }

    /// <summary>
    /// Verifies metadata edits never move the Monte Carlo stream: renaming the component and its
    /// functions and re-running the sampler walk reproduces bit-identical draws.
    /// </summary>
    [TestMethod]
    public void Test_SetupSamplers_MetadataEdits_StreamUnchanged()
    {
        // Arrange
        var fragility = SamplerFragility("Breach");
        var component = SamplerComponent(fragility, SamplerFragility("Second"));
        component.SetupSamplers(16, componentSeed: 12345, SamplingScheme.LatinHypercube);
        var baseline = new double[16];
        for (int k = 0; k < 16; k++)
        {
            baseline[k] = component.Sample(k).FailureModes[0].SRP(15d);
        }

        // Act — rename everything and re-run the walk with the same component seed.
        component.Name = "Renamed Component";
        fragility.Name = "Renamed Breach";
        fragility.AssignNewId();
        component.SetupSamplers(16, componentSeed: 12345, SamplingScheme.LatinHypercube);

        // Assert
        for (int k = 0; k < 16; k++)
        {
            Assert.AreEqual(baseline[k], component.Sample(k).FailureModes[0].SRP(15d), 0d,
                "Metadata edits must never move Monte Carlo draws.");
        }
    }

    /// <summary>
    /// Verifies the run snapshot: Sample() uses the projection captured by SetupSamplers even if
    /// the graph changes afterward (a fresh projection would carry unseeded coupling matrices).
    /// </summary>
    [TestMethod]
    public void Test_Sample_UsesFrozenSnapshot()
    {
        // Arrange
        var component = new SystemComponent { Name = "Snapshot" };
        component.HazardFunction = SamplerHazard();
        component.AddFailureMode(new FailureMode(null, null, SamplerFragility("Only Mode"), SamplerConsequence("Loss")));
        component.SetupSamplers(8, componentSeed: 12345, SamplingScheme.LatinHypercube);

        // Act — mutate the graph after the walk.
        component.AddFailureMode(new FailureMode(null, null, SamplerFragility("Added Later"), SamplerConsequence("Late Loss")));
        var sampled = component.Sample(0);

        // Assert — the snapshot still carries one mode; sampling before any walk throws.
        Assert.AreEqual(1, sampled.FailureModeCount);
        Assert.ThrowsException<InvalidOperationException>(() => new SystemComponent().Sample());
    }

    /// <summary>
    /// Verifies the component-level Q-W guardrails on the joint method: the cross product of the
    /// failure modes' primary exposure branches warns above 64 and errors above 1024.
    /// </summary>
    [TestMethod]
    public void Test_Validate_JointBranchProduct_Guardrails()
    {
        // Arrange / Act / Assert — 9 × 9 = 81 branches: warning only.
        var warningComponent = SamplerComponent(SamplerFragility("A"), SamplerFragility("B"));
        var warningTerminals = warningComponent.Graph.GetElements<ConsequenceElement>().ToList();
        warningTerminals[0].Functions[0] = SamplerMixture("Mix A", 9);
        warningTerminals[1].Functions[0] = SamplerMixture("Mix B", 9);
        var (warnValid, warnMessages) = warningComponent.Validate();
        Assert.IsTrue(warnValid, string.Join("; ", warnMessages));
        Assert.IsTrue(warnMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("exposure branches (81)")),
            string.Join("; ", warnMessages));

        // 33 × 33 = 1089 branches: error.
        var errorComponent = SamplerComponent(SamplerFragility("A"), SamplerFragility("B"));
        var errorTerminals = errorComponent.Graph.GetElements<ConsequenceElement>().ToList();
        errorTerminals[0].Functions[0] = SamplerMixture("Mix A", 33);
        errorTerminals[1].Functions[0] = SamplerMixture("Mix B", 33);
        var (errorValid, errorMessages) = errorComponent.Validate();
        Assert.IsFalse(errorValid);
        Assert.IsTrue(errorMessages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("1024")),
            string.Join("; ", errorMessages));
    }

    /// <summary>
    /// Verifies the profile hazard selection (Q-T) round-trips through both serialization modes
    /// and that a pre-6.6 payload without the attribute loads forward as the primary-hazard
    /// default.
    /// </summary>
    [TestMethod]
    public void Test_ProfileHazardElementId_RoundTrip_And_ForwardLoad()
    {
        // Arrange
        var component = LeveeComponent();
        var rating = component.Graph.GetElements<TransformElement>().Single();
        component.SetProfileHazardElement(rating);

        // Act — self-contained round-trip.
        var restored = new SystemComponent(component.ToXElement());

        // Assert — the id survives and resolves to the restored graph's rating element.
        Assert.AreEqual(rating.Id, restored.ProfileHazardElementId);
        Assert.IsInstanceOfType(restored.Graph.GetElementById(restored.ProfileHazardElementId!.Value), typeof(TransformElement));

        // Act / Assert — by-reference round-trip preserves the id too.
        var functions = component.GetReferencedFunctions().ToList();
        var resolver = new RMC.TotalRisk.RiskFunctions.RiskFunctionResolver(
            id => functions.FirstOrDefault(f => f.Id == id),
            name => functions.FirstOrDefault(f => f.Name == name));
        var byReference = new SystemComponent(component.ToXElement(RiskSerializationMode.ByReference), resolver);
        Assert.AreEqual(rating.Id, byReference.ProfileHazardElementId);

        // Act / Assert — a payload with the attribute removed (the pre-6.6 shape) loads null.
        var legacy = component.ToXElement();
        legacy.Attribute(nameof(SystemComponent.ProfileHazardElementId))!.Remove();
        var forward = new SystemComponent(legacy);
        Assert.IsNull(forward.ProfileHazardElementId);
    }

    /// <summary>
    /// Verifies the profile hazard selection is seed-inert (the ratified Q-T classification):
    /// selecting, changing, or clearing the profile element never moves the canonical hash.
    /// </summary>
    [TestMethod]
    public void Test_ProfileHazardElementId_HashInert()
    {
        // Arrange
        var component = LeveeComponent();
        byte[] baseline = component.CanonicalHash();
        var rating = component.Graph.GetElements<TransformElement>().Single();

        // Act / Assert — select, re-identify, and clear: the hash never moves.
        component.SetProfileHazardElement(rating);
        CollectionAssert.AreEqual(baseline, component.CanonicalHash(),
            "Selecting a profile hazard element must never change the canonical hash (seed-inert).");
        component.ProfileHazardElementId = Guid.NewGuid();
        CollectionAssert.AreEqual(baseline, component.CanonicalHash(),
            "An arbitrary (even unresolvable) profile id must never change the canonical hash.");
        component.ProfileHazardElementId = null;
        CollectionAssert.AreEqual(baseline, component.CanonicalHash(),
            "Clearing the profile selection must never change the canonical hash.");
    }

    /// <summary>
    /// Verifies the typed selector contract: assignment by element, clearing with null, and the
    /// argument guards for non-transform and foreign elements.
    /// </summary>
    [TestMethod]
    public void Test_SetProfileHazardElement_Contract()
    {
        // Arrange
        var component = LeveeComponent();
        var rating = component.Graph.GetElements<TransformElement>().Single();
        var response = component.Graph.GetElements<ResponseElement>().Single();

        // Act / Assert — assign and clear.
        component.SetProfileHazardElement(rating);
        Assert.AreEqual(rating.Id, component.ProfileHazardElementId);
        component.SetProfileHazardElement(null);
        Assert.IsNull(component.ProfileHazardElementId);

        // A response element is not a valid profile axis.
        Assert.ThrowsException<ArgumentException>(() => component.SetProfileHazardElement(response));

        // A transform element of another component's graph is foreign.
        var other = LeveeComponent();
        var foreignRating = other.Graph.GetElements<TransformElement>().Single();
        Assert.ThrowsException<ArgumentException>(() => component.SetProfileHazardElement(foreignRating));
    }

    /// <summary>
    /// Verifies the profile-selection validation matrix: unresolvable ids, non-transform
    /// targets, unassigned functions, and hazard-disconnected transforms are errors; a valid
    /// selection with a non-zero hazard threshold raises the profile-axis advisory warning.
    /// </summary>
    [TestMethod]
    public void Test_Validate_ProfileHazard_Matrix()
    {
        // Unresolvable id.
        var component = LeveeComponent();
        component.ProfileHazardElementId = Guid.NewGuid();
        var (unresolvedValid, unresolvedMessages) = component.Validate();
        Assert.IsFalse(unresolvedValid);
        Assert.IsTrue(unresolvedMessages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("does not resolve")),
            string.Join("; ", unresolvedMessages));

        // Non-transform target.
        component = LeveeComponent();
        component.ProfileHazardElementId = component.Graph.GetElements<ResponseElement>().Single().Id;
        var (nonTransformValid, nonTransformMessages) = component.Validate();
        Assert.IsFalse(nonTransformValid);
        Assert.IsTrue(nonTransformMessages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("not a transform element")),
            string.Join("; ", nonTransformMessages));

        // A transform with no assigned function.
        component = LeveeComponent();
        var bare = new TransformElement("Bare")
        {
            Input = new RiskConnection(component.Graph.GetElements<HazardElement>().Single()),
        };
        component.Graph.AddElement(bare);
        component.ProfileHazardElementId = bare.Id;
        var (bareValid, bareMessages) = component.Validate();
        Assert.IsFalse(bareValid);
        Assert.IsTrue(bareMessages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("no transform function")),
            string.Join("; ", bareMessages));

        // A transform disconnected from the hazard root.
        component = LeveeComponent();
        var dangling = new TransformElement("Dangling") { Function = Rating("Stage", "ft", "Elevation", "ft") };
        component.Graph.AddElement(dangling);
        component.ProfileHazardElementId = dangling.Id;
        var (danglingValid, danglingMessages) = component.Validate();
        Assert.IsFalse(danglingValid);
        Assert.IsTrue(danglingMessages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("not connected upstream")),
            string.Join("; ", danglingMessages));

        // A valid selection with a non-zero threshold: valid, with the axis advisory warning.
        component = LeveeComponent();
        component.SetProfileHazardElement(component.Graph.GetElements<TransformElement>().Single());
        component.HazardThreshold = 25d;
        var (advisoryValid, advisoryMessages) = component.Validate();
        Assert.IsTrue(advisoryValid, string.Join("; ", advisoryMessages));
        Assert.IsTrue(advisoryMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("profile hazard axis")),
            string.Join("; ", advisoryMessages));

        // The same selection with the default zero threshold: no advisory.
        component.HazardThreshold = 0d;
        var (quietValid, quietMessages) = component.Validate();
        Assert.IsTrue(quietValid, string.Join("; ", quietMessages));
        Assert.IsFalse(quietMessages.Any(m => m.Contains("profile hazard axis")), string.Join("; ", quietMessages));
    }

    /// <summary>
    /// Verifies <see cref="SystemComponent.Clone"/> carries the profile hazard selection: the
    /// clone's id resolves to the clone's own rating element (element ids persist through the
    /// serialization round-trip).
    /// </summary>
    [TestMethod]
    public void Test_Clone_CarriesProfileHazardSelection()
    {
        // Arrange
        var component = LeveeComponent();
        var rating = component.Graph.GetElements<TransformElement>().Single();
        component.SetProfileHazardElement(rating);

        // Act
        var clone = component.Clone();

        // Assert
        Assert.AreEqual(rating.Id, clone.ProfileHazardElementId);
        var resolved = clone.Graph.GetElementById(clone.ProfileHazardElementId!.Value);
        Assert.IsInstanceOfType(resolved, typeof(TransformElement));
        Assert.AreSame(clone.Graph.GetElements<TransformElement>().Single(), resolved);
    }
}
