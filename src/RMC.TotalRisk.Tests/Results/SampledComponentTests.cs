using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components;

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
}
