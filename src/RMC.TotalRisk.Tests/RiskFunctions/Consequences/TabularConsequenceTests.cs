using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.RiskFunctions.Consequences;

/// <summary>
/// Unit tests for <see cref="TabularConsequence"/> — v1.0 defaults, validation matrix incl. the
/// legacy warnings, the negative-consequence clamp, sampling, serialization, and hash identity.
/// </summary>
[TestClass]
public class TabularConsequenceTests
{
    /// <summary>Builds a labeled deterministic damage table: (0→0), (100→1000).</summary>
    private static TabularConsequence DeterministicConsequence()
    {
        return new TabularConsequence
        {
            Name = "Damages",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(1000d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Verifies the v1.0 default construction state.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var c = new TabularConsequence();

        // Assert
        Assert.AreEqual(Transform.None, c.HazardTransform);
        Assert.AreEqual(Transform.None, c.ConsequenceTransform);
        Assert.AreEqual(2, c.UncertainOrderedPairedData.Count);
        Assert.IsTrue(c.IsDeterministic);
        Assert.AreEqual(1, c.SamplingDimensions);
    }

    /// <summary>Verifies the validation matrix incl. both v1.0 warnings.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Arrange — valid table passes with no warnings (first ordinate is zero).
        var valid = DeterministicConsequence();
        var (ok, okMessages) = valid.Validate();
        Assert.IsTrue(ok);
        Assert.AreEqual(0, okMessages.Count);

        // Non-zero first ordinate warns about flat extrapolation below the table.
        var nonZero = DeterministicConsequence();
        nonZero.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Deterministic(5d)), new UncertainOrdinate(100d, new Deterministic(1000d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
        var (stillValid, warnMessages) = nonZero.Validate();
        Assert.IsTrue(stillValid, "Warnings must not invalidate.");
        Assert.IsTrue(warnMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("greater than zero")));

        // A negative sampled range warns about the zero clamp.
        var negative = DeterministicConsequence();
        negative.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Normal(0d, 1d)), new UncertainOrdinate(100d, new Normal(1000d, 10d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal);
        var (negValid, negMessages) = negative.Validate();
        Assert.IsTrue(negValid);
        Assert.IsTrue(negMessages.Any(m => m.Contains("Negative consequence values will be set to zero")));

        // Missing labels are errors (4 label errors).
        var unlabeled = new TabularConsequence();
        var (isValid, messages) = unlabeled.Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(4, messages.Count(m => m.StartsWith("Error:", StringComparison.Ordinal)));
    }

    /// <summary>Verifies mean sampling, interpolation, extrapolation clamps, and the negative clamp.</summary>
    [TestMethod]
    public void Test_SampleFunction_KnownPoints_AndNegativeClamp()
    {
        // Arrange
        var c = DeterministicConsequence();

        // Act
        var f = c.SampleFunction();

        // Assert — exact interpolation and clamps.
        Assert.AreEqual(500d, f.Function(50d), 0d);
        Assert.AreEqual(0d, f.Function(-5d), 0d);
        Assert.AreEqual(1000d, f.Function(500d), 0d);

        // The sampled function clamps negative consequences to zero (AllowNegativeYValues=false):
        // a low percentile of a Normal(0, 1) first ordinate is negative → evaluates to 0.
        var uncertain = DeterministicConsequence();
        uncertain.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Normal(0d, 1d)), new UncertainOrdinate(100d, new Normal(1000d, 10d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal);
        var low = uncertain.SampleFunction(0.01d);
        Assert.AreEqual(0d, low.Function(0d), 0d, "Negative sampled consequences must clamp to zero.");
    }

    /// <summary>Verifies invalid tables throw on sampling.</summary>
    [TestMethod]
    public void Test_SampleFunction_InvalidTable_Throws()
    {
        // Arrange
        var c = DeterministicConsequence();
        c.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Deterministic(0d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => c.SampleFunction());
    }

    /// <summary>Verifies bounds and the exact uncertainty summary.</summary>
    [TestMethod]
    public void Test_Bounds_And_UncertaintySummary()
    {
        // Arrange
        var c = DeterministicConsequence();
        c.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Normal(100d, 10d)), new UncertainOrdinate(100d, new Normal(1000d, 10d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal);

        // Act
        var summary = c.ComputeUncertaintyResults(0.90d)!;

        // Assert
        Assert.AreEqual(0d, c.MinHazard(), 0d);
        Assert.AreEqual(100d, c.MaxHazard(), 0d);
        Assert.AreEqual(100d, summary.MeanCurve![0], 0d);
        Assert.AreEqual(new Normal(100d, 10d).InverseCDF(0.05d), summary.ConfidenceIntervals![0, 0], 0d);
        Assert.AreEqual(new Normal(1000d, 10d).InverseCDF(0.95d), summary.ConfidenceIntervals[1, 1], 0d);
    }

    /// <summary>Verifies the XElement round-trip preserves state and hash.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = DeterministicConsequence();
        original.ConsequenceTransform = Transform.Logarithmic;

        // Act
        var restored = new TabularConsequence(original.ToXElement());

        // Assert
        Assert.AreEqual(original.SpecifiedConsequence, restored.SpecifiedConsequence);
        Assert.AreEqual(original.ConsequenceUnit, restored.ConsequenceUnit);
        Assert.AreEqual(original.ConsequenceTransform, restored.ConsequenceTransform);
        Assert.AreEqual(original.UncertainOrderedPairedData[1].Y!.Mean, restored.UncertainOrderedPairedData[1].Y!.Mean, 0d);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>Verifies hash identity: metadata inert, table edits compute-relevant.</summary>
    [TestMethod]
    public void Test_CanonicalHash_Identity()
    {
        // Arrange
        var c = DeterministicConsequence();
        c.SpecifiedConsequence = "Life Loss";
        byte[] baseline = c.CanonicalHash();

        // Act / Assert — consequence labels are metadata (stripped).
        c.ConsequenceUnit = "lives";
        CollectionAssert.AreEqual(baseline, c.CanonicalHash());
        HashInvariance.AssertMetadataInvariant(c);
        HashInvariance.AssertStrippedAttributesInert(c.ToXElement());
        HashInvariance.AssertComputeSensitive(c.CanonicalHash, () => c.HazardTransform = Transform.Logarithmic);
    }
}
