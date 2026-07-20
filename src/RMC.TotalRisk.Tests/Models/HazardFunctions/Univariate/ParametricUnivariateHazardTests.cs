using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Distributions;
using Numerics.Mathematics.Optimization;
using RMC.TotalRisk.Models.HazardFunctions.Univariate;
using RMC.TotalRisk.Models.Support;
using RMC.TotalRisk.Tests.Models.Support;

namespace RMC.TotalRisk.Tests.Models.HazardFunctions.Univariate;

/// <summary>
/// Unit tests for <see cref="ParametricUnivariateHazard"/> — v1.0 defaults and validation matrix,
/// the bootstrap and posterior-injection estimation paths, exceedance-ordinate inversion,
/// index/percentile sampling semantics, bounds fixes, serialization, and hash identity.
/// </summary>
[TestClass]
public class ParametricUnivariateHazardTests
{
    /// <summary>Builds a labeled hazard with a Normal parent and a fast bootstrap configuration.</summary>
    private static ParametricUnivariateHazard FastHazard()
    {
        var h = new ParametricUnivariateHazard
        {
            Name = "Flow Frequency",
            SpecifiedHazard = "Peak Flow",
            HazardUnit = "cfs",
            ParentDistribution = new Normal(100d, 20d),
            EffectiveRecordLength = 50,
            Realizations = 200,
        };
        return h;
    }

    /// <summary>Verifies the v1.0 default construction state.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var h = new ParametricUnivariateHazard();

        // Assert
        Assert.IsInstanceOfType<LogPearsonTypeIII>(h.ParentDistribution);
        Assert.IsTrue(h.IsUncertain);
        Assert.AreEqual(100, h.EffectiveRecordLength);
        Assert.AreEqual(0.9d, h.ConfidenceIntervalWidth, 0d);
        Assert.AreEqual(10000, h.Realizations);
        Assert.AreEqual(12345, h.PRNGSeed);
        Assert.AreEqual(ParameterEstimationMethod.MethodOfMoments, h.EstimationMethod);
        Assert.AreEqual(25, h.ProbabilityOrdinates.Count);
        Assert.AreEqual(0.000001d, h.ProbabilityOrdinates[0], 0d);
        Assert.AreEqual(0.99d, h.ProbabilityOrdinates[24], 0d);
        Assert.IsFalse(h.IsEstimated);
        Assert.AreEqual(0, h.SamplingDimensions);
    }

    /// <summary>Verifies the v1.0 validation matrix: ranges, ordinates, estimation-method rejections, estimate gate.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Un-estimated function is invalid (estimate gate).
        var h = FastHazard();
        var (isValid, messages) = h.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("has not been estimated")));

        // Out-of-range configuration values are errors.
        h.EffectiveRecordLength = 5;
        Assert.IsTrue(h.Validate().ValidationMessages.Any(m => m.Contains("effective record length")));
        h.EffectiveRecordLength = 50;
        h.PRNGSeed = 0;
        Assert.IsTrue(h.Validate().ValidationMessages.Any(m => m.Contains("PRNG seed")));
        h.PRNGSeed = 12345;
        h.Realizations = 50;
        Assert.IsTrue(h.Validate().ValidationMessages.Any(m => m.Contains("between 100 and 100,000")));
        h.Realizations = 500;
        Assert.IsTrue(h.Validate().ValidationMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("less than 1,000")));

        // Estimation-method rejections (v1.0 hazard matrix).
        var pmom = FastHazard();
        pmom.ParentDistribution = new Weibull(10d, 2d);
        pmom.EstimationMethod = ParameterEstimationMethod.MethodOfMoments;
        Assert.IsTrue(pmom.Validate().ValidationMessages.Any(m => m.Contains("product moments")));
        pmom.EstimationMethod = ParameterEstimationMethod.MethodOfLinearMoments;
        Assert.IsTrue(pmom.Validate().ValidationMessages.Any(m => m.Contains("linear moments")));

        // Unordered ordinates are errors.
        var unordered = FastHazard();
        unordered.ProbabilityOrdinates.Clear();
        unordered.ProbabilityOrdinates.Add(0.5d);
        unordered.ProbabilityOrdinates.Add(0.1d);
        Assert.IsTrue(unordered.Validate().ValidationMessages.Any(m => m.Contains("ascending order")));
    }

    /// <summary>Verifies the bootstrap estimate: posterior size, exceedance inversion, and mean sampling.</summary>
    [TestMethod]
    public void Test_Estimate_Bootstrap()
    {
        // Arrange
        var h = FastHazard();

        // Act
        h.Estimate();

        // Assert
        Assert.IsTrue(h.IsEstimated);
        Assert.IsFalse(h.PosteriorImported);
        Assert.AreEqual(200, h.Results!.ParameterSets!.Length);
        // ModeCurve inverts exceedance → non-exceedance: ordinate 0 (AEP 1e-6) is the LARGEST quantile.
        double top = new Normal(100d, 20d).InverseCDF(1d - 0.000001d);
        Assert.AreEqual(top, h.Results.ModeCurve![0], 1e-9);
        // Mean sampling produces an increasing empirical curve across the ordinate span.
        var mean = h.SampleFunction();
        Assert.IsTrue(mean.InverseCDF(0.99d) > mean.InverseCDF(0.01d));
        // Validation passes once estimated (with the low-realizations warning only).
        var (isValid, messages) = h.Validate();
        Assert.IsTrue(isValid);
        Assert.IsTrue(messages.All(m => m.StartsWith("Warning:", StringComparison.Ordinal)));
    }

    /// <summary>Verifies the estimate lifecycle: a compute edit clears the estimate (v1.0 behavior).</summary>
    [TestMethod]
    public void Test_Estimate_InvalidatedByComputeEdits()
    {
        // Arrange
        var h = FastHazard();
        h.Estimate();
        Assert.IsTrue(h.IsEstimated);

        // Act
        h.EffectiveRecordLength = 60;

        // Assert
        Assert.IsFalse(h.IsEstimated);
        Assert.IsNull(h.Results);
        Assert.ThrowsException<InvalidOperationException>(() => h.SampleFunction());
    }

    /// <summary>Verifies posterior injection: ensemble alignment, summary curves, and sampling.</summary>
    [TestMethod]
    public void Test_Estimate_PosteriorInjection()
    {
        // Arrange — an externally fitted Normal posterior of 50 draws (below the bootstrap minimum).
        var h = FastHazard();
        var sets = new List<ParameterSet>();
        for (int i = 0; i < 50; i++)
            sets.Add(new ParameterSet(new[] { 100d + i * 0.5d, 20d }, 0d));

        // Act
        h.Estimate(sets);

        // Assert — realizations align to the ensemble; imported posteriors bypass the range check.
        Assert.IsTrue(h.IsEstimated);
        Assert.IsTrue(h.PosteriorImported);
        Assert.AreEqual(50, h.Realizations);
        Assert.IsTrue(h.Validate().IsValid || h.Validate().ValidationMessages.All(m => m.StartsWith("Warning:", StringComparison.Ordinal)));
        // Index sampling configures the exact injected parameter set.
        var draw7 = h.SampleFunction(7);
        Assert.AreEqual(100d + 7 * 0.5d, ((Normal)draw7).Mean, 1e-12);
        // The mean curve is the expected quantile: mean of the injected means at the median ordinate.
        double expectedMedian = sets.Average(s => s.Values[0]);
        int medianIndex = h.ProbabilityOrdinates.IndexOf(0.5d);
        Assert.AreEqual(expectedMedian, h.Results!.MeanCurve![medianIndex], 1e-9);
        // Injection also round-trips through serialization.
        var restored = new ParametricUnivariateHazard(h.ToXElement());
        Assert.IsTrue(restored.IsEstimated);
        Assert.IsTrue(restored.PosteriorImported);
        Assert.AreEqual(50, restored.Realizations);
        Assert.AreEqual(((Normal)draw7).Mean, ((Normal)restored.SampleFunction(7)).Mean, 1e-12);
    }

    /// <summary>Verifies index/percentile sampling semantics incl. the v1.1 clamp and range throw.</summary>
    [TestMethod]
    public void Test_Sampling_IndexAndPercentileSemantics()
    {
        // Arrange
        var h = FastHazard();
        h.Estimate();

        // Act / Assert — floor-index lookup; percentile 1.0 clamps to the last draw (v1.1 fix).
        var byPercentile = h.SampleFunction(0.5d);
        var byIndex = h.SampleFunction(100);
        Assert.AreEqual(((Normal)byIndex).Mean, ((Normal)byPercentile).Mean, 1e-12);
        var last = h.SampleFunction(199);
        Assert.AreEqual(((Normal)last).Mean, ((Normal)h.SampleFunction(1.0d)).Mean, 1e-12);
        // Out-of-range indices throw (v1.0 returned null).
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => h.SampleFunction(200));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => h.SampleFunction(-1));
    }

    /// <summary>Verifies the per-flag bounds caches (the v1.0 shared-cache defect is fixed).</summary>
    [TestMethod]
    public void Test_Bounds_PerFlagCache()
    {
        // Arrange
        var h = FastHazard();
        h.Estimate();

        // Act — query meanOnly FIRST (the v1.0 bug returned this cached pair for both flags).
        double meanOnlyMin = h.MinHazard(true);
        double fullMin = h.MinHazard(false);
        double fullMax = h.MaxHazard(false);

        // Assert — full-uncertainty bounds must bracket the mean-only bounds.
        Assert.IsTrue(fullMin <= meanOnlyMin, "Full-uncertainty minimum must not exceed the mean-only minimum.");
        Assert.IsTrue(fullMax >= h.MaxHazard(true), "Full-uncertainty maximum must not be below the mean-only maximum.");
    }

    /// <summary>Verifies the uncertainty summary surfaces stored results and re-slices other widths.</summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_StoredAndResliced()
    {
        // Arrange
        var h = FastHazard();
        h.Estimate();

        // Act
        var stored = h.ComputeUncertaintyResults(0.9d)!;
        var resliced = h.ComputeUncertaintyResults(0.5d)!;

        // Assert — same width returns the stored results; a narrower width tightens the intervals.
        Assert.AreSame(h.Results, stored);
        for (int i = 0; i < h.ProbabilityOrdinates.Count; i++)
        {
            Assert.IsTrue(resliced.ConfidenceIntervals![i, 0] >= stored.ConfidenceIntervals![i, 0] - 1e-9);
            Assert.IsTrue(resliced.ConfidenceIntervals[i, 1] <= stored.ConfidenceIntervals[i, 1] + 1e-9);
        }
        Assert.IsNull(new ParametricUnivariateHazard().ComputeUncertaintyResults());
    }

    /// <summary>Verifies the XElement round-trip incl. the estimated posterior.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = FastHazard();
        original.Estimate();

        // Act
        var restored = new ParametricUnivariateHazard(original.ToXElement());

        // Assert
        Assert.AreEqual(original.EffectiveRecordLength, restored.EffectiveRecordLength);
        Assert.AreEqual(original.Realizations, restored.Realizations);
        Assert.AreEqual(original.PRNGSeed, restored.PRNGSeed);
        Assert.AreEqual(original.ProbabilityOrdinates.Count, restored.ProbabilityOrdinates.Count);
        Assert.IsTrue(restored.IsEstimated);
        Assert.AreEqual(((Normal)original.SampleFunction(42)).Mean, ((Normal)restored.SampleFunction(42)).Mean, 0d);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>Verifies hash identity: metadata inert; seed and configuration edits compute-relevant.</summary>
    [TestMethod]
    public void Test_CanonicalHash_Identity()
    {
        // Arrange
        var h = FastHazard();

        // Act / Assert
        HashInvariance.AssertMetadataInvariant(h);
        HashInvariance.AssertStrippedAttributesInert(h.ToXElement());
        HashInvariance.AssertComputeSensitive(h.CanonicalHash, () => h.PRNGSeed = 54321);
    }
}
