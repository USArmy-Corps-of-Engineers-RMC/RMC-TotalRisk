using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses;

/// <summary>
/// Unit tests for <see cref="TabularResponse"/> — v1.0 defaults, probability-bounds validation,
/// the legacy monotonicity algorithm, co-monotonic sampling, serialization, and hash identity.
/// </summary>
[TestClass]
public class TabularResponseTests
{
    /// <summary>Builds a labeled deterministic fragility: (10→0), (20→1).</summary>
    private static TabularResponse DeterministicResponse()
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

    /// <summary>Builds a triangular-uncertain fragility over three ordinates.</summary>
    private static TabularResponse UncertainResponse()
    {
        var r = DeterministicResponse();
        r.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[]
            {
                new UncertainOrdinate(10d, new Triangular(0d, 0.05d, 0.1d)),
                new UncertainOrdinate(15d, new Triangular(0.2d, 0.4d, 0.6d)),
                new UncertainOrdinate(20d, new Triangular(0.7d, 0.9d, 1d)),
            },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Triangular);
        return r;
    }

    /// <summary>Verifies the v1.0 default construction state, incl. the None probability transform.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var r = new TabularResponse();

        // Assert — ProbabilityTransform defaults None (deliberately unlike TabularHazard).
        Assert.AreEqual(Transform.None, r.HazardTransform);
        Assert.AreEqual(Transform.None, r.ProbabilityTransform);
        Assert.IsTrue(r.IsDeterministic);
        Assert.AreEqual(1, r.SamplingDimensions);
    }

    /// <summary>Verifies probability bounds are validated across each ordinate's full range.</summary>
    [TestMethod]
    public void Test_Validate_ProbabilityBounds()
    {
        // Arrange — valid fragility passes.
        Assert.IsTrue(DeterministicResponse().Validate().IsValid);

        // An ordinate whose distribution can exceed 1 is an error (bounded distributions so the
        // upper-bound check is what fires).
        var overOne = DeterministicResponse();
        overOne.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(10d, new Triangular(0d, 0.05d, 0.1d)), new UncertainOrdinate(20d, new Triangular(0.8d, 0.95d, 1.2d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Triangular);
        var (isValid, messages) = overOne.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("less than or equal to 1")));
    }

    /// <summary>Verifies the non-zero-first-ordinate warning and the non-monotonic warning.</summary>
    [TestMethod]
    public void Test_Validate_Warnings()
    {
        // Arrange — non-zero first ordinate.
        var nonZero = DeterministicResponse();
        nonZero.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(10d, new Deterministic(0.1d)), new UncertainOrdinate(20d, new Deterministic(1d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
        var (stillValid, messages) = nonZero.Validate();
        Assert.IsTrue(stillValid);
        Assert.IsTrue(messages.Any(m => m.Contains("probability of failure greater than zero")));

        // A decreasing fragility is allowed but warned (multivariate scenarios).
        var decreasing = DeterministicResponse();
        decreasing.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(10d, new Deterministic(0.5d)), new UncertainOrdinate(20d, new Deterministic(0.2d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
        Assert.IsFalse(decreasing.IsMonotonic());
        var (decValid, decMessages) = decreasing.Validate();
        Assert.IsTrue(decValid);
        Assert.IsTrue(decMessages.Any(m => m.Contains("not monotonically increasing")));
    }

    /// <summary>Verifies the legacy IsMonotonic algorithm passes an increasing uncertain fragility.</summary>
    [TestMethod]
    public void Test_IsMonotonic_UncertainIncreasing_True()
    {
        // Act / Assert
        Assert.IsTrue(UncertainResponse().IsMonotonic());
    }

    /// <summary>Verifies curve and distribution sampling at known points.</summary>
    [TestMethod]
    public void Test_Sampling_KnownPoints()
    {
        // Arrange
        var r = DeterministicResponse();

        // Act
        var curve = r.SampleResponseFunction();
        var srp = r.SampleFunction();

        // Assert — mean curve is the table; the SRP CDF interpolates it.
        Assert.AreEqual(2, curve.Count);
        Assert.AreEqual(0d, curve[0].Y, 0d);
        Assert.AreEqual(1d, curve[1].Y, 0d);
        Assert.AreEqual(0.5d, srp.CDF(15d), 1e-12);
        Assert.AreEqual(0d, srp.CDF(5d), 0d);
        Assert.AreEqual(1d, srp.CDF(25d), 0d);

        // Co-monotonic percentile sample: every ordinate at its 5th percentile.
        var uncertain = UncertainResponse();
        var p05 = uncertain.SampleResponseFunction(0.05d);
        Assert.AreEqual(new Triangular(0d, 0.05d, 0.1d).InverseCDF(0.05d), p05[0].Y, 1e-12);
        Assert.AreEqual(new Triangular(0.2d, 0.4d, 0.6d).InverseCDF(0.05d), p05[1].Y, 1e-12);
    }

    /// <summary>Verifies invalid tables throw on distribution sampling.</summary>
    [TestMethod]
    public void Test_SampleFunction_InvalidTable_Throws()
    {
        // Arrange — probabilities above 1 make the table unusable.
        var r = DeterministicResponse();
        r.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(10d, new Deterministic(0d)), new UncertainOrdinate(20d, new Deterministic(2d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => r.SampleFunction());
    }

    /// <summary>Verifies the v1.0 bounds surface.</summary>
    [TestMethod]
    public void Test_Bounds_MatchV10()
    {
        // Arrange
        var r = UncertainResponse();

        // Assert
        Assert.AreEqual(10d, r.MinHazard(), 0d);
        Assert.AreEqual(20d, r.MaxHazard(), 0d);
        Assert.AreEqual(new Triangular(0d, 0.05d, 0.1d).Mean, r.MinProbability(), 1e-12);
        Assert.AreEqual(new Triangular(0.7d, 0.9d, 1d).Mean, r.MaxProbability(), 1e-12);
    }

    /// <summary>Verifies the exact uncertainty summary over the fragility table.</summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_ExactPercentiles()
    {
        // Arrange
        var r = UncertainResponse();

        // Act
        var summary = r.ComputeUncertaintyResults(0.80d)!;

        // Assert
        Assert.AreEqual(new Triangular(0.2d, 0.4d, 0.6d).Mean, summary.MeanCurve![1], 1e-12);
        Assert.AreEqual(new Triangular(0.2d, 0.4d, 0.6d).InverseCDF(0.10d), summary.ConfidenceIntervals![1, 0], 0d);
        Assert.AreEqual(new Triangular(0.2d, 0.4d, 0.6d).InverseCDF(0.90d), summary.ConfidenceIntervals[1, 1], 0d);
    }

    /// <summary>Verifies the XElement round-trip preserves state and hash.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = UncertainResponse();
        original.ProbabilityTransform = Transform.NormalZ;

        // Act
        var restored = new TabularResponse(original.ToXElement());

        // Assert
        Assert.AreEqual(original.ProbabilityTransform, restored.ProbabilityTransform);
        Assert.AreEqual(original.UncertainOrderedPairedData.Count, restored.UncertainOrderedPairedData.Count);
        Assert.AreEqual(original.UncertainOrderedPairedData[2].Y!.Mean, restored.UncertainOrderedPairedData[2].Y!.Mean, 0d);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>Verifies hash identity: metadata inert, table edits compute-relevant.</summary>
    [TestMethod]
    public void Test_CanonicalHash_Identity()
    {
        // Arrange
        var r = UncertainResponse();

        // Act / Assert
        HashInvariance.AssertMetadataInvariant(r);
        HashInvariance.AssertStrippedAttributesInert(r.ToXElement());
        HashInvariance.AssertComputeSensitive(r.CanonicalHash, () => r.ProbabilityTransform = Transform.NormalZ);
    }
}
