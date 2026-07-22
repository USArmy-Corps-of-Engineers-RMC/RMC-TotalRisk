using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="SampledFailureMode"/> — known-point response and consequence math,
/// the Q-N shared-percentile pairing, the Q-V branch-enumerated recording shape, the clamping
/// flags, and the multi-stage placeholder throw.
/// </summary>
[TestClass]
public class SampledFailureModeTests
{
    /// <summary>Builds a deterministic fragility: P[F|h] linear from (10 → 0) to (20 → 1).</summary>
    private static TabularResponse Fragility()
    {
        return new TabularResponse
        {
            Name = "Fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(10d, new Deterministic(0d)), new UncertainOrdinate(20d, new Deterministic(1d)) },
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

    /// <summary>Builds a normally uncertain consequence: (0 → N(0,0)) to (30 → N(mean, sd)).</summary>
    private static TabularConsequence UncertainConsequence(string name, double mean, double sd)
    {
        var consequence = Consequence(name, 0d);
        consequence.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Normal(0d, 0d)), new UncertainOrdinate(30d, new Normal(mean, sd)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal);
        return consequence;
    }

    /// <summary>Builds a non-failure mode carrying the given consequence.</summary>
    private static FailureMode NonFailureMode(TabularConsequence consequence)
    {
        return new FailureMode(null, null, null, consequence);
    }

    /// <summary>Verifies the mean-sample response and consequence math at known points.</summary>
    [TestMethod]
    public void Test_MeanSample_KnownPointMath()
    {
        // Arrange — no transforms: SRP(15) = 0.5 on the linear fragility; consequence(15) = 150.
        var mode = new FailureMode(null, null, Fragility(), Consequence("Damages", 300d));
        var sampled = mode.Sample(nonFailureMode: null);
        var realization = new FailureModeRealization();
        var flags = new RiskComputeFlags();

        // Act
        var output = sampled.ComputeRisk(probability: 0.4d, hazardLevel: 15d, null, flags, realization, recordOutput: true);

        // Assert
        Assert.AreEqual(0.5d, sampled.SRP(15d), 1e-12);
        Assert.AreEqual(15d, sampled.InverseSRP(0.5d), 1e-12);
        Assert.AreEqual(0.5d, output.ProbabilityOfFailure, 1e-12);
        Assert.AreEqual(0.5d, output.ProbabilityOfNonFailure, 1e-12);
        Assert.AreEqual(150d, output.MeanFailureConsequences, 1e-12);
        Assert.AreEqual(150d, output.MeanExcessConsequences, 1e-12, "With no non-failure mode the excess equals the failure consequence.");
        Assert.AreEqual(0d, output.NonFailureConsequences, 0d);
        Assert.AreEqual(1, output.ResponseProbabilities.Count);
        Assert.AreEqual(0.5d, output.ResponseProbabilities[0], 1e-12);
        Assert.AreEqual(1, realization.Curves.Fail.RiskPoints.Count);
        Assert.AreEqual(15d, realization.Curves.Fail.RiskPoints[0].HazardLevel, 0d);
        Assert.AreEqual(0.4d, realization.Curves.Fail.RiskPoints[0].HazardProbability, 0d);
        Assert.IsFalse(flags.Any);
    }

    /// <summary>
    /// Verifies the Q-N shared draw: identical-content failure and non-failure consequences,
    /// paired at the same coupling percentile, sample the identical curve — the excess is exactly
    /// zero in every realization. Independent draws would differ almost surely. The pin runs a
    /// single-failure-mode component under the competing method, whose component excess IS the
    /// mode's paired excess (the joint method's component-level excess deliberately uses the
    /// non-failure mode's own draw — v1.0 semantics — so it would not cancel).
    /// </summary>
    [TestMethod]
    public void Test_QN_SharedPercentile_PairedConsequencesCoherent()
    {
        // Arrange — a component provides the sampler walk (seeds + coupling matrices); the
        // engine path (SetupSamplers → Sample) carries the frozen projection snapshot.
        var component = new SystemComponent { Name = "Q-N Pin" };
        component.HazardFunction = SampledComponentTests.StageFrequency();
        component.AddFailureMode(new FailureMode(null, null, Fragility(), UncertainConsequence("Failure Loss", 100d, 25d)));
        component.AddFailureMode(NonFailureMode(UncertainConsequence("Non-Failure Loss", 100d, 25d)));
        component.FailureModeMethod = FailureModeMethod.CompetingFailures;
        component.SetupSamplers(64, componentSeed: 12345, SamplingScheme.LatinHypercube);

        // Act / Assert — across realizations the paired excess is exactly zero.
        for (int k = 0; k < 64; k++)
        {
            var sampled = component.Sample(k);
            var flags = new RiskComputeFlags();
            var realization = new ComponentRealization(failureModes: 1);
            var output = sampled.ComputeRisk(0.5d, 15d, flags, realization);
            Assert.AreEqual(0d, output.MeanExcessConsequences, 0d,
                $"Realization {k}: identical-content paired consequences at one shared percentile must cancel exactly.");
            Assert.IsFalse(flags.HasNegativeExcessConsequence);
        }
    }

    /// <summary>
    /// Verifies the Q-V recording shape: a day/night mixture consequence records one Fail entry
    /// per exposure branch with weight-scaled probabilities and branch-specific consequences.
    /// </summary>
    [TestMethod]
    public void Test_MixtureConsequence_BranchEnumeratedRecording()
    {
        // Arrange — day 55% at 100, night 45% at 300 (values at hazard 30; halved at 15).
        var mixture = new CompositeConsequence(new[]
        {
            new WeightedConsequenceFunction(Consequence("Day", 100d), 0.55d),
            new WeightedConsequenceFunction(Consequence("Night", 300d), 0.45d),
        })
        {
            Name = "Day/Night",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        var mode = new FailureMode(null, null, Fragility(), mixture);
        var sampled = mode.Sample(null);
        var realization = new FailureModeRealization();
        var flags = new RiskComputeFlags();

        // Act — SRP(15) = 0.5; branch values at 15 are 50 and 150.
        var output = sampled.ComputeRisk(0.4d, 15d, null, flags, realization, recordOutput: true);

        // Assert — entries per branch on the output and the recorded point.
        Assert.AreEqual(2, sampled.FailureBranchCount);
        Assert.AreEqual(2, output.ResponseProbabilities.Count);
        Assert.AreEqual(0.5d * 0.55d, output.ResponseProbabilities[0], 1e-12);
        Assert.AreEqual(50d, output.FailureConsequences[0], 1e-12);
        Assert.AreEqual(0.5d * 0.45d, output.ResponseProbabilities[1], 1e-12);
        Assert.AreEqual(150d, output.FailureConsequences[1], 1e-12);
        Assert.AreEqual(0.55d * 50d + 0.45d * 150d, output.MeanFailureConsequences, 1e-12);

        var point = realization.Curves.Fail.RiskPoints[0];
        Assert.AreEqual(2, point.ResponseProbabilities.Count);
        Assert.AreEqual(0.5d * 0.55d, point.ResponseProbabilities[0], 1e-12);
        Assert.AreEqual(150d, point.Consequences[1], 1e-12);
    }

    /// <summary>
    /// Verifies the clamping flags: a non-failure consequence exceeding the failure consequence
    /// clamps the excess to zero and raises the excess flag (only while failure is possible).
    /// </summary>
    [TestMethod]
    public void Test_ComputeRisk_NegativeExcess_ClampsAndFlags()
    {
        // Arrange — fail 30 at full scale, non-fail 300: excess is negative everywhere.
        var mode = new FailureMode(null, null, Fragility(), Consequence("Small Fail", 30d));
        var nonFailure = NonFailureMode(Consequence("Large NonFail", 300d));
        var sampledNonFail = nonFailure.Sample(null);
        var sampled = mode.Sample(nonFailure);
        var flags = new RiskComputeFlags();
        var realization = new FailureModeRealization();

        // Act
        var output = sampled.ComputeRisk(0.4d, 15d, sampledNonFail, flags, realization);

        // Assert
        Assert.AreEqual(0d, output.MeanExcessConsequences, 0d);
        Assert.IsTrue(flags.HasNegativeExcessConsequence);
        Assert.AreEqual(15d, output.MeanFailureConsequences, 1e-12);
        Assert.AreEqual(150d, output.NonFailureConsequences, 1e-12);
    }

    /// <summary>
    /// Verifies the multi-stage placeholder: sampling a mode with two response stages throws the
    /// documented deferral (event-tree phase), while authoring validation stays untouched.
    /// </summary>
    [TestMethod]
    public void Test_MultiStage_SamplingThrows_AuthoringUnaffected()
    {
        // Arrange
        var mode = new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<RMC.TotalRisk.Core.Interfaces.ITransformFunction>(), Fragility()),
                new ResponseStage(new List<RMC.TotalRisk.Core.Interfaces.ITransformFunction>(), Fragility()),
            },
            null,
            new List<RMC.TotalRisk.Core.Interfaces.IConsequenceFunction> { Consequence("Damages", 300d) });

        // Act / Assert — the sampling seam throws; the mode itself still validates for authoring.
        var exception = Assert.ThrowsException<NotSupportedException>(() => mode.Sample(null));
        StringAssert.Contains(exception.Message, "event-tree");
        Assert.IsTrue(mode.Validate().IsValid, "Authoring validation must not gate multi-stage modes; only the engine does.");
    }

    /// <summary>Verifies the constructor argument contract.</summary>
    [TestMethod]
    public void Test_Constructor_NullMode_Throws()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => new SampledFailureMode(null!, null));
    }
}
