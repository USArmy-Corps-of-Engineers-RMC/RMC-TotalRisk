using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Distributions;
using Numerics.Mathematics.Optimization;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses;

/// <summary>
/// Unit tests for <see cref="ParametricResponse"/> — v1.0 defaults (LnNormal parent, seed 67891,
/// non-exceedance ordinates with NO inversion), the response-specific estimation-method matrix,
/// bootstrap and injection paths, sampling, bounds, serialization, and hash identity.
/// </summary>
[TestClass]
public class ParametricResponseTests
{
    /// <summary>Builds a labeled response with a Normal parent and a fast bootstrap configuration.</summary>
    private static ParametricResponse FastResponse()
    {
        return new ParametricResponse
        {
            Name = "Fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            ParentDistribution = new Normal(10d, 2d),
            EffectiveRecordLength = 30,
            Realizations = 200,
        };
    }

    /// <summary>Verifies the v1.0 default construction state (the deltas from the parametric hazard).</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var r = new ParametricResponse();

        // Assert
        Assert.IsInstanceOfType<LnNormal>(r.ParentDistribution);
        Assert.AreEqual(67891, r.PRNGSeed);
        Assert.AreEqual(23, r.ProbabilityOrdinates.Count);
        Assert.AreEqual(0.001d, r.ProbabilityOrdinates[0], 0d);
        Assert.AreEqual(0.999d, r.ProbabilityOrdinates[22], 0d);
        Assert.IsTrue(r.IsUncertain);
        Assert.AreEqual(0, r.SamplingDimensions);
        Assert.IsTrue(r.IsMonotonic(), "Parametric responses are monotonic by construction.");
    }

    /// <summary>Verifies the response-specific estimation-method rejection matrix.</summary>
    [TestMethod]
    public void Test_Validate_EstimationMethodMatrix()
    {
        // PMOM rejected for Weibull only.
        var r = FastResponse();
        r.ParentDistribution = new Weibull(10d, 2d);
        r.EstimationMethod = ParameterEstimationMethod.MethodOfMoments;
        Assert.IsTrue(r.Validate().ValidationMessages.Any(m => m.Contains("product moments")));

        // LMOM rejected for Logistic, Weibull, Triangular, and PERT.
        foreach (UnivariateDistributionBase dist in new UnivariateDistributionBase[]
                 { new Logistic(10d, 2d), new Weibull(10d, 2d), new Triangular(5d, 10d, 15d), new Pert(5d, 10d, 15d) })
        {
            var lmom = FastResponse();
            lmom.ParentDistribution = dist;
            lmom.EstimationMethod = ParameterEstimationMethod.MethodOfLinearMoments;
            Assert.IsTrue(lmom.Validate().ValidationMessages.Any(m => m.Contains("linear moments")),
                $"LMOM must be rejected for {dist.GetType().Name}.");
        }

        // MOM with a Normal parent is accepted (only the estimate gate remains).
        var ok = FastResponse();
        Assert.IsTrue(ok.Validate().ValidationMessages.All(m =>
            m.Contains("has not been estimated") || m.StartsWith("Warning:", StringComparison.Ordinal)));
    }

    /// <summary>Verifies the bootstrap estimate with NO exceedance inversion (the hazard-cluster delta).</summary>
    [TestMethod]
    public void Test_Estimate_NoInversion()
    {
        // Arrange
        var r = FastResponse();

        // Act
        r.Estimate();

        // Assert — ModeCurve[i] evaluates the parent at the ordinate DIRECTLY (non-exceedance).
        Assert.IsTrue(r.IsEstimated);
        for (int i = 0; i < r.ProbabilityOrdinates.Count; i++)
        {
            Assert.AreEqual(new Normal(10d, 2d).InverseCDF(r.ProbabilityOrdinates[i]), r.Results!.ModeCurve![i], 1e-9,
                $"Mode curve ordinate {i} must not be inverted.");
        }
        // The mean curve ascends with the ordinates (no reversal).
        var mean = r.SampleFunction();
        Assert.IsTrue(mean.InverseCDF(0.9d) > mean.InverseCDF(0.1d));
    }

    /// <summary>Verifies curve-form sampling throws (v1.0 behavior) and index semantics match the hazard.</summary>
    [TestMethod]
    public void Test_Sampling_Semantics()
    {
        // Arrange
        var r = FastResponse();
        r.Estimate();

        // Act / Assert
        Assert.ThrowsException<NotImplementedException>(() => r.SampleResponseFunction());
        Assert.ThrowsException<NotImplementedException>(() => r.SampleResponseFunction(0.5d));
        Assert.ThrowsException<NotImplementedException>(() => r.SampleResponseFunction(0));
        var byIndex = r.SampleFunction(100);
        var byPercentile = r.SampleFunction(0.5d);
        Assert.AreEqual(((Normal)byIndex).Mean, ((Normal)byPercentile).Mean, 1e-12);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => r.SampleFunction(200));
        Assert.ThrowsException<InvalidOperationException>(() => FastResponse().SampleFunction());
    }

    /// <summary>Verifies posterior injection for fragility posteriors.</summary>
    [TestMethod]
    public void Test_Estimate_PosteriorInjection()
    {
        // Arrange
        var r = FastResponse();
        var sets = new List<ParameterSet>();
        for (int i = 0; i < 40; i++)
            sets.Add(new ParameterSet(new[] { 10d + i * 0.1d, 2d }, 0d));

        // Act
        r.Estimate(sets);

        // Assert
        Assert.IsTrue(r.IsEstimated);
        Assert.IsTrue(r.PosteriorImported);
        Assert.AreEqual(40, r.Realizations);
        Assert.AreEqual(10d + 12 * 0.1d, ((Normal)r.SampleFunction(12)).Mean, 1e-12);
    }

    /// <summary>Verifies the probability bounds via the parent CDF at the hazard bounds (v1.0 shape).</summary>
    [TestMethod]
    public void Test_Bounds_ProbabilityViaParentCdf()
    {
        // Arrange — deterministic keeps the bounds analytic.
        var r = FastResponse();
        r.IsUncertain = false;
        r.Estimate();

        // Act / Assert — Min/MaxHazard are the parent quantiles at the ordinate extremes (no
        // inversion), and Min/MaxProbability close the loop through the parent CDF.
        Assert.AreEqual(new Normal(10d, 2d).InverseCDF(0.001d), r.MinHazard(), 1e-9);
        Assert.AreEqual(new Normal(10d, 2d).InverseCDF(0.999d), r.MaxHazard(), 1e-9);
        Assert.AreEqual(0.001d, r.MinProbability(), 1e-9);
        Assert.AreEqual(0.999d, r.MaxProbability(), 1e-9);
    }

    /// <summary>Verifies the XElement round-trip incl. the estimated posterior.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = FastResponse();
        original.Estimate();

        // Act
        var restored = new ParametricResponse(original.ToXElement());

        // Assert
        Assert.AreEqual(67891, restored.PRNGSeed);
        Assert.IsTrue(restored.IsEstimated);
        Assert.AreEqual(((Normal)original.SampleFunction(7)).Mean, ((Normal)restored.SampleFunction(7)).Mean, 0d);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>Verifies hash identity: metadata inert; configuration edits compute-relevant.</summary>
    [TestMethod]
    public void Test_CanonicalHash_Identity()
    {
        // Arrange
        var r = FastResponse();

        // Act / Assert
        HashInvariance.AssertMetadataInvariant(r);
        HashInvariance.AssertStrippedAttributesInert(r.ToXElement());
        HashInvariance.AssertComputeSensitive(r.CanonicalHash, () => r.EffectiveRecordLength = 45);
    }
}
