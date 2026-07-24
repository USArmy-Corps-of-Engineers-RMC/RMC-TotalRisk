using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Systems.Components;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="SampledComponent"/> — the four failure-mode combination rules
/// against direct probability-kernel calls at known points, the Q-V branch entries, the
/// non-failure recording, and extent tracking.
/// </summary>
[TestClass]
public class SampledComponentTests
{
    /// <summary>Builds the shared stage-frequency hazard: exceedance 0.999 → stage 0 up to 0.001 → stage 30.</summary>
    public static TabularHazard StageFrequency()
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

    /// <summary>Builds a deterministic fragility rising linearly from (start → 0) to (end → 1).</summary>
    private static TabularResponse Fragility(string name, double start, double end)
    {
        return new TabularResponse
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(start, new Deterministic(0d)), new UncertainOrdinate(end, new Deterministic(1d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a labeled deterministic consequence: linear from (0 → 0) to (30 → valueAtThirty).</summary>
    private static TabularConsequence Consequence(string name, double valueAtThirty)
    {
        return new TabularConsequence
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(30d, new Deterministic(valueAtThirty)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>
    /// Builds a two-failure-mode component with a non-failure path: fragilities (10→20) and
    /// (10→30), consequences 300 and 600 at full scale, non-failure 60.
    /// </summary>
    private static SystemComponent TwoModeComponent(FailureModeMethod method, JointConsequenceType jointConsequences = JointConsequenceType.Maximum)
    {
        var component = new SystemComponent { Name = "Two Modes" };
        component.HazardFunction = StageFrequency();
        component.AddFailureMode(new FailureMode(null, null, Fragility("Mode A", 10d, 20d), Consequence("A Loss", 300d)));
        component.AddFailureMode(new FailureMode(null, null, Fragility("Mode B", 10d, 30d), Consequence("B Loss", 600d)));
        component.AddFailureMode(new FailureMode(null, null, null, Consequence("Non-Failure Loss", 60d)));
        component.FailureModeMethod = method;
        component.JointConsequences = jointConsequences;
        return component;
    }

    /// <summary>Sets up and samples the component's mean realization.</summary>
    private static SampledComponent MeanSample(SystemComponent component)
    {
        component.SetupSamplers(8, componentSeed: 12345, SamplingScheme.LatinHypercube);
        return component.Sample();
    }

    /// <summary>
    /// Verifies the joint-failures pathways at a known point against hand-computed
    /// inclusion–exclusion: SRPs (0.5, 0.25) give pathways 0.375/0.125/0.125 and union 0.625,
    /// with the Maximum joint-consequence rule.
    /// </summary>
    [TestMethod]
    public void Test_JointFailures_KnownPoint_VsHandComputation()
    {
        // Arrange — at h = 15: SRP_A = 0.5, SRP_B = 0.25; cF_A = 150, cF_B = 300; nf = 30.
        var component = TwoModeComponent(FailureModeMethod.JointFailures);
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 2);
        var flags = new RiskComputeFlags();

        // Act
        var output = sampled.ComputeRisk(0.6d, 15d, flags, realization, recordOutput: true);

        // Assert — union, conditional means, and the recorded pathway entries.
        Assert.AreEqual(0.625d, output.ProbabilityOfFailure, 1e-12);
        Assert.AreEqual(0.375d, output.ProbabilityOfNonFailure, 1e-12);
        Assert.AreEqual(30d, output.NonFailureConsequences, 1e-12);
        // eCF = 0.375·150 + 0.125·300 + 0.125·max(150,300) = 131.25 → conditional mean 210.
        Assert.AreEqual(210d, output.MeanFailureConsequences, 1e-12);
        // Excess vs nf 30: (120, 270, 270) → eCI = 112.5 → conditional mean 180.
        Assert.AreEqual(180d, output.MeanExcessConsequences, 1e-12);
        Assert.AreEqual(3, output.ResponseProbabilities.Count, "One entry per failure pathway.");

        double sum = 0d;
        for (int i = 0; i < output.ResponseProbabilities.Count; i++) sum += output.ResponseProbabilities[i];
        Assert.AreEqual(0.625d, sum, 1e-12, "Pathway probabilities must sum to the union.");

        // The Total point carries the pathway entries plus the non-failure entry.
        var total = realization.Curves.Total.RiskPoints[0];
        Assert.AreEqual(4, total.ResponseProbabilities.Count);
        Assert.AreEqual(0.375d, total.ResponseProbabilities[3], 1e-12);
        Assert.AreEqual(30d, total.Consequences[3], 1e-12);
        var background = realization.Curves.Background.RiskPoints[0];
        Assert.AreEqual(1d, background.ResponseProbabilities[0], 0d);
        Assert.AreEqual(30d, background.Consequences[0], 1e-12);
    }

    /// <summary>
    /// Verifies the common-cause adjustment against the direct probability kernel: the total
    /// probability of failure equals the union, allocated proportionally.
    /// </summary>
    [TestMethod]
    public void Test_CommonCause_VsDirectKernel()
    {
        // Arrange
        var component = TwoModeComponent(FailureModeMethod.CommonCauseFailures);
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 2);
        var flags = new RiskComputeFlags();

        // Act — SRPs (0.5, 0.25): cca = union/Σ = 0.625/0.75.
        var output = sampled.ComputeRisk(0.6d, 15d, flags, realization);

        // Assert
        double cca = Probability.CommonCauseAdjustment(new System.Collections.Generic.List<double> { 0.5d, 0.25d });
        Assert.AreEqual(0.625d / 0.75d, cca, 1e-12);
        Assert.AreEqual(0.625d, output.ProbabilityOfFailure, 1e-12, "The common-cause allocation preserves the union.");
        // eCF = cca·(0.5·150 + 0.25·300) = cca·150 = 125 → conditional mean 200.
        Assert.AreEqual(200d, output.MeanFailureConsequences, 1e-12);
    }

    /// <summary>
    /// Verifies the mutually-exclusive normalization and its probability-above-one warning flag.
    /// </summary>
    [TestMethod]
    public void Test_MutuallyExclusive_NormalizationAndFlag()
    {
        // Arrange — both fragilities saturate at h = 30: SRPs (1, 1) sum to 2.
        var component = TwoModeComponent(FailureModeMethod.MutuallyExclusive);
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 2);
        var flags = new RiskComputeFlags();

        // Act
        var output = sampled.ComputeRisk(0.99d, 30d, flags, realization);

        // Assert — normalized to 0.5 each; the flag records the overshoot.
        Assert.AreEqual(1d, output.ProbabilityOfFailure, 1e-12);
        Assert.IsTrue(flags.HasProbabilityGreaterThanOne);
        // eCF = 0.5·300 + 0.5·600 = 450 → conditional mean 450.
        Assert.AreEqual(450d, output.MeanFailureConsequences, 1e-12);
    }

    /// <summary>
    /// Verifies competing failures short-circuit to the raw response probability with a single
    /// mode (no cumulative-incidence pre-processing).
    /// </summary>
    [TestMethod]
    public void Test_Competing_SingleMode_UsesRawResponse()
    {
        // Arrange
        var component = new SystemComponent { Name = "One Mode" };
        component.HazardFunction = StageFrequency();
        component.AddFailureMode(new FailureMode(null, null, Fragility("Mode A", 10d, 20d), Consequence("A Loss", 300d)));
        component.FailureModeMethod = FailureModeMethod.CompetingFailures;
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 1);
        var flags = new RiskComputeFlags();

        // Act
        var output = sampled.ComputeRisk(0.6d, 15d, flags, realization);

        // Assert
        Assert.IsNull(sampled.HazardBins, "A single competing mode needs no pre-processing.");
        Assert.AreEqual(0.5d, output.ProbabilityOfFailure, 1e-12);
        Assert.AreEqual(150d, output.MeanFailureConsequences, 1e-12);
    }

    /// <summary>
    /// Verifies the competing-risks pre-processing builds the 200-bin cumulative incidence
    /// functions for two or more modes and the adjusted probabilities sum below the union.
    /// </summary>
    [TestMethod]
    public void Test_Competing_TwoModes_CifPreProcessing()
    {
        // Arrange
        var component = TwoModeComponent(FailureModeMethod.CompetingFailures);
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 2);
        var flags = new RiskComputeFlags();

        // Act
        var output = sampled.ComputeRisk(0.6d, 15d, flags, realization);

        // Assert — the CIF machinery ran (200 bins) and produced a defensible total.
        Assert.IsNotNull(sampled.HazardBins);
        Assert.AreEqual(200, sampled.HazardBins!.Count);
        Assert.IsTrue(output.ProbabilityOfFailure > 0d && output.ProbabilityOfFailure <= 1d);
        Assert.AreEqual(2, output.ResponseProbabilities.Count, "One entry per mode's single branch.");
    }

    /// <summary>Verifies constructor and sampling argument contracts.</summary>
    [TestMethod]
    public void Test_ArgumentContracts()
    {
        // Arrange
        var component = TwoModeComponent(FailureModeMethod.JointFailures);

        // Act / Assert — sampling before setup throws; null arguments throw.
        Assert.ThrowsException<InvalidOperationException>(() => component.Sample());
        Assert.ThrowsException<ArgumentNullException>(() => new SampledComponent(null!, Array.Empty<FailureMode>(), null));
        var sampled = MeanSample(component);
        Assert.ThrowsException<ArgumentNullException>(
            () => sampled.ComputeRisk(0.5d, 15d, null!, new ComponentRealization(2)));
    }

    /// <summary>Builds the flow-frequency hazard for the profile-remap fixtures (0.999 → 0 cfs up to 0.001 → 100 cfs).</summary>
    private static TabularHazard FlowFrequency()
    {
        return new TabularHazard
        {
            Name = "Flow Frequency",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            NoUncertaintyFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Deterministic(0d)),
                    new UncertainOrdinate(0.5d, new Deterministic(50d)),
                    new UncertainOrdinate(0.001d, new Deterministic(100d)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds the deterministic Flow→Stage rating T(h) = h / 2 for the profile-remap fixtures.</summary>
    private static TabularTransform FlowToStage()
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

    /// <summary>
    /// Builds the profile-remap component: flow hazard → rating (T(h) = h/2) → stage fragility
    /// and stage consequences, plus a transform-free non-failure path.
    /// </summary>
    private static SystemComponent RemapComponent()
    {
        var component = new SystemComponent { Name = "Remap" };
        component.HazardFunction = FlowFrequency();
        component.AddFailureMode(new FailureMode(new List<ITransformFunction> { FlowToStage() }, null,
            Fragility("Mode A", 10d, 20d), Consequence("A Loss", 300d)));
        component.AddFailureMode(new FailureMode(null, null, null, Consequence("Non-Failure Loss", 60d)));
        return component;
    }

    /// <summary>
    /// Verifies the profile-axis remap (Q-T): with the rating element selected, every recorded
    /// risk point — component and mode scope — and the hazard extents carry the composed profile
    /// signal T(h) = h/2, while the evaluation itself (the SRP through the mode chain) is
    /// untouched.
    /// </summary>
    [TestMethod]
    public void Test_ComputeRisk_ProfileRemap_RecordsProfileAxis()
    {
        // Arrange — profile = the rating element; flow 30 maps to stage 15 on the profile axis.
        var component = RemapComponent();
        component.SetProfileHazardElement(component.Graph.GetElements<TransformElement>().Single());
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 1);
        var flags = new RiskComputeFlags();

        // Act
        var output = sampled.ComputeRisk(0.6d, 30d, flags, realization, recordOutput: true);

        // Assert — recorded coordinates carry the profile signal at component and mode scope.
        Assert.AreEqual(15d, realization.Curves.Total.RiskPoints[0].HazardLevel, 1e-12);
        Assert.AreEqual(15d, realization.Curves.Fail.RiskPoints[0].HazardLevel, 1e-12);
        Assert.AreEqual(15d, realization.FailureModes[0].Curves.Fail.RiskPoints[0].HazardLevel, 1e-12);
        Assert.AreEqual(15d, realization.MinH, 1e-12);
        Assert.AreEqual(15d, realization.MaxH, 1e-12);

        // The evaluation is unchanged: the fragility still sees the mode chain's stage signal,
        // SRP(stage 15) = 0.5.
        Assert.AreEqual(0.5d, output.ProbabilityOfFailure, 1e-12);
    }

    /// <summary>
    /// Verifies the joint-failures % contribution attribution at two hand-computed evaluation
    /// points under the Maximum rule: within each exclusive pathway the probability splits
    /// equally (the Shapley value of the union game) and the combined consequence splits
    /// proportionally to the participants' marginal consequences, then the trapezoid finalize
    /// integrates with the same masses the recorded curves see.
    /// </summary>
    /// <remarks>
    /// Hand computation. At h = 15 (p = 0.6): SRP (0.5, 0.25); c = (150, 300); nf = 30;
    /// pathways A-only 0.375, B-only 0.125, AB 0.125 with Max consequence 300 split 1:2 —
    /// P = (0.4375, 0.1875); fail = (68.75, 62.5); excess totals (45, 33.75, 33.75) split the
    /// same way — (56.25, 56.25). At h = 12 (p = 0.4): SRP (0.2, 0.1); c = (120, 240);
    /// nf = 24 — P = (0.19, 0.09); fail = (23.2, 22.4); excess = (18.72, 20.16). Trapezoid
    /// masses over probabilities (0.4, 0.6) are (0.5, 0.5).
    /// </remarks>
    [TestMethod]
    public void Test_Contribution_JointMaximum_HandComputedSplit()
    {
        // Arrange
        var component = TwoModeComponent(FailureModeMethod.JointFailures, JointConsequenceType.Maximum);
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 2);
        var flags = new RiskComputeFlags();

        // Act — two recording evaluations, then the trapezoid finalize.
        sampled.ComputeRisk(0.4d, 12d, flags, realization, recordOutput: true);
        sampled.ComputeRisk(0.6d, 15d, flags, realization, recordOutput: true);
        realization.FinalizeContributions(trapezoidMasses: true);

        // Assert — mode A.
        var a = realization.FailureModes[0].Contribution!;
        Assert.AreEqual(0.5d * 0.19d + 0.5d * 0.4375d, a.FailureProbability, 1e-12);
        Assert.AreEqual(0.5d * 23.2d + 0.5d * 68.75d, a.FailureMean, 1e-9);
        Assert.AreEqual(0.5d * 18.72d + 0.5d * 56.25d, a.ExcessMean, 1e-9);

        // Mode B.
        var b = realization.FailureModes[1].Contribution!;
        Assert.AreEqual(0.5d * 0.09d + 0.5d * 0.1875d, b.FailureProbability, 1e-12);
        Assert.AreEqual(0.5d * 22.4d + 0.5d * 62.5d, b.FailureMean, 1e-9);
        Assert.AreEqual(0.5d * 20.16d + 0.5d * 56.25d, b.ExcessMean, 1e-9);
    }

    /// <summary>
    /// Verifies the zero-consequence attribution convention: with every failure consequence
    /// zero, the tuple value sums vanish and the consequence split falls back to the equal
    /// split — the probability attribution is untouched (it never reads consequences), which
    /// is what makes % of the annualized failure probability available in reliability mode.
    /// </summary>
    [TestMethod]
    public void Test_Contribution_ZeroConsequences_EqualSplitFallback()
    {
        // Arrange — the same fragilities with zero-valued consequences.
        var component = new SystemComponent { Name = "Zero" };
        component.HazardFunction = StageFrequency();
        component.AddFailureMode(new FailureMode(null, null, Fragility("Mode A", 10d, 20d), Consequence("A Loss", 0d)));
        component.AddFailureMode(new FailureMode(null, null, Fragility("Mode B", 10d, 30d), Consequence("B Loss", 0d)));
        component.AddFailureMode(new FailureMode(null, null, null, Consequence("Non-Failure Loss", 0d)));
        component.FailureModeMethod = FailureModeMethod.JointFailures;
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 2);
        var flags = new RiskComputeFlags();

        // Act
        sampled.ComputeRisk(0.4d, 12d, flags, realization, recordOutput: true);
        sampled.ComputeRisk(0.6d, 15d, flags, realization, recordOutput: true);
        realization.FinalizeContributions(trapezoidMasses: true);

        // Assert — the probability split is the Shapley attribution regardless of consequences.
        var a = realization.FailureModes[0].Contribution!;
        var b = realization.FailureModes[1].Contribution!;
        Assert.AreEqual(0.5d * 0.19d + 0.5d * 0.4375d, a.FailureProbability, 1e-12);
        Assert.AreEqual(0.5d * 0.09d + 0.5d * 0.1875d, b.FailureProbability, 1e-12);
        Assert.AreEqual(0d, a.FailureMean, 0d);
        Assert.AreEqual(0d, b.FailureMean, 0d);
        Assert.AreEqual(0d, a.ExcessMean, 0d);
    }

    /// <summary>
    /// Verifies the common-cause % contribution at hand-computed points: the adjusted marginal
    /// is the exclusive event, so each mode's contribution is its adjusted probability and the
    /// adjusted-scaled means — the decomposition other tools report.
    /// </summary>
    /// <remarks>
    /// Hand computation. At h = 15: union = 0.625, Σp = 0.75, factor 5/6 — adjusted
    /// (0.41666…, 0.208333…); c = (150, 300); paired excess vs nf 30 = (120, 270). At h = 12:
    /// union = 0.28, Σp = 0.3, factor 14/15 — adjusted (0.186666…, 0.093333…); c = (120, 240);
    /// excess = (96, 216). Trapezoid masses (0.5, 0.5).
    /// </remarks>
    [TestMethod]
    public void Test_Contribution_CommonCause_AdjustedMarginals()
    {
        // Arrange
        var component = TwoModeComponent(FailureModeMethod.CommonCauseFailures);
        var sampled = MeanSample(component);
        var realization = new ComponentRealization(failureModes: 2);
        var flags = new RiskComputeFlags();

        // Act
        sampled.ComputeRisk(0.4d, 12d, flags, realization, recordOutput: true);
        sampled.ComputeRisk(0.6d, 15d, flags, realization, recordOutput: true);
        realization.FinalizeContributions(trapezoidMasses: true);

        // Assert
        double adjustedA12 = 0.2d * (0.28d / 0.3d);
        double adjustedA15 = 0.5d * (0.625d / 0.75d);
        double adjustedB12 = 0.1d * (0.28d / 0.3d);
        double adjustedB15 = 0.25d * (0.625d / 0.75d);
        var a = realization.FailureModes[0].Contribution!;
        var b = realization.FailureModes[1].Contribution!;
        Assert.AreEqual(0.5d * (adjustedA12 + adjustedA15), a.FailureProbability, 1e-12);
        Assert.AreEqual(0.5d * (adjustedA12 * 120d + adjustedA15 * 150d), a.FailureMean, 1e-9);
        Assert.AreEqual(0.5d * (adjustedA12 * 96d + adjustedA15 * 120d), a.ExcessMean, 1e-9);
        Assert.AreEqual(0.5d * (adjustedB12 + adjustedB15), b.FailureProbability, 1e-12);
        Assert.AreEqual(0.5d * (adjustedB12 * 240d + adjustedB15 * 300d), b.FailureMean, 1e-9);
        Assert.AreEqual(0.5d * (adjustedB12 * 216d + adjustedB15 * 270d), b.ExcessMean, 1e-9);
    }

    /// <summary>
    /// Verifies the unset default records the raw driving hazard (bit-identical pre-6.6
    /// behavior) and that selecting the profile changes recorded coordinates only — every
    /// computed output is identical between the two runs.
    /// </summary>
    [TestMethod]
    public void Test_ComputeRisk_NoProfile_RawAxis_OutputsMatchProfileRun()
    {
        // Arrange — the same model twice; only B selects the profile element.
        var componentA = RemapComponent();
        var componentB = RemapComponent();
        componentB.SetProfileHazardElement(componentB.Graph.GetElements<TransformElement>().Single());
        var sampledA = MeanSample(componentA);
        var sampledB = MeanSample(componentB);
        var realizationA = new ComponentRealization(failureModes: 1);
        var realizationB = new ComponentRealization(failureModes: 1);
        var flags = new RiskComputeFlags();

        // Act
        var outputA = sampledA.ComputeRisk(0.6d, 30d, flags, realizationA, recordOutput: true);
        var outputB = sampledB.ComputeRisk(0.6d, 30d, flags, realizationB, recordOutput: true);

        // Assert — A records the raw flow coordinate; B the profile stage coordinate.
        Assert.AreEqual(30d, realizationA.Curves.Total.RiskPoints[0].HazardLevel, 0d);
        Assert.AreEqual(30d, realizationA.FailureModes[0].Curves.Fail.RiskPoints[0].HazardLevel, 0d);
        Assert.AreEqual(15d, realizationB.Curves.Total.RiskPoints[0].HazardLevel, 1e-12);

        // Every computed output is bit-identical — the remap labels recorded points only.
        Assert.AreEqual(outputA.ProbabilityOfFailure, outputB.ProbabilityOfFailure, 0d);
        Assert.AreEqual(outputA.ProbabilityOfNonFailure, outputB.ProbabilityOfNonFailure, 0d);
        Assert.AreEqual(outputA.MeanFailureConsequences, outputB.MeanFailureConsequences, 0d);
        Assert.AreEqual(outputA.MeanExcessConsequences, outputB.MeanExcessConsequences, 0d);
        Assert.AreEqual(outputA.NonFailureConsequences, outputB.NonFailureConsequences, 0d);
    }
}
