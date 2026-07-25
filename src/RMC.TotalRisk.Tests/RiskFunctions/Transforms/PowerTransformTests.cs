using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.RiskFunctions.Transforms;

/// <summary>
/// Unit tests for <see cref="PowerTransform"/> — v1.0 defaults, validation matrix (including the
/// Minimum-below-ξ advisory), closed-form sampling anchors (the Numerics <c>Test_Functions</c>
/// fixture constants) in both the forward and inverse forms, the ξ clamp, the conditional
/// sampling dimension, bounds, exact uncertainty summary, serialization round-trip, and hash
/// identity including the σ-conditional recipe.
/// </summary>
[TestClass]
public class PowerTransformTests
{
    /// <summary>Builds the Numerics closed-form anchor fixture: Y = 5·X² with log-space σ = 3.</summary>
    private static PowerTransform AnchorTransform()
    {
        return new PowerTransform
        {
            Name = "Rating",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            TransformedHazard = "Flow",
            TransformedHazardUnit = "cfs",
            Alpha = 5d,
            Beta = 2d,
            Xi = 0d,
            Sigma = 3d,
            IsUncertain = true,
        };
    }

    /// <summary>Verifies the v1.0 default construction state.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var t = new PowerTransform();

        // Assert — the exact v1.0 parameter defaults.
        Assert.AreEqual(1d, t.Alpha, 0d);
        Assert.AreEqual(1.5d, t.Beta, 0d);
        Assert.AreEqual(0d, t.Xi, 0d);
        Assert.AreEqual(0.1d, t.Sigma, 0d);
        Assert.IsFalse(t.IsInverse);
        Assert.IsTrue(t.IsUncertain);
        Assert.AreEqual(0d, t.Minimum, 0d);
        Assert.AreEqual(100d, t.Maximum, 0d);
        Assert.IsFalse(t.IsDeterministic);
        Assert.AreEqual(1, t.SamplingDimensions);

        // The sampling dimension follows the uncertainty flag (architecture doc §5.8.4).
        t.IsUncertain = false;
        Assert.IsTrue(t.IsDeterministic);
        Assert.AreEqual(0, t.SamplingDimensions);
    }

    /// <summary>Verifies the validation matrix: labels, range, parameter domains, the σ gate, and the ξ advisory.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Arrange — the configured anchor passes.
        Assert.IsTrue(AnchorTransform().Validate().IsValid);

        // Missing labels are errors.
        var unlabeled = new PowerTransform();
        var (isValid, messages) = unlabeled.Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(4, messages.Count(m => m.StartsWith("Error:", StringComparison.Ordinal)));

        // A non-positive coefficient is an error (the relation is evaluated in log space).
        var alpha = AnchorTransform();
        alpha.Alpha = 0d;
        Assert.IsFalse(alpha.Validate().IsValid);

        // The exponent range is [−10, 10] (the v1.0 rule).
        var beta = AnchorTransform();
        beta.Beta = 12d;
        Assert.IsFalse(beta.Validate().IsValid);

        // A non-positive σ is an error only while uncertain (the v1.0 gate).
        var sigma = AnchorTransform();
        sigma.Sigma = 0d;
        Assert.IsFalse(sigma.Validate().IsValid);
        sigma.IsUncertain = false;
        Assert.IsTrue(sigma.Validate().IsValid);

        // A minimum below ξ is legal but advisory: evaluation clamps at ξ (see the class remarks).
        var clamp = AnchorTransform();
        clamp.Xi = 10d;
        var (clampValid, clampMessages) = clamp.Validate();
        Assert.IsTrue(clampValid);
        Assert.IsTrue(clampMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("location parameter")));
    }

    /// <summary>Verifies mean sampling against the Numerics closed-form anchors, with the ξ and Maximum clamps.</summary>
    [TestMethod]
    public void Test_SampleFunction_Mean_KnownPoints()
    {
        // Arrange
        var t = AnchorTransform();

        // Act
        var f = t.SampleFunction();

        // Assert — the Test_Functions anchor: 5·6² = 180; the default form at 6 is 6^1.5.
        Assert.AreEqual(180d, f.Function(6d), 1e-12);
        var defaults = new PowerTransform { IsUncertain = false };
        Assert.AreEqual(Math.Pow(6d, 1.5d), defaults.SampleFunction().Function(6d), 1e-12);

        // Evaluation clamps above Maximum and at ξ below (α·ε^β ≈ 0 for β > 0).
        Assert.AreEqual(5d * 100d * 100d, f.Function(200d), 1e-9);
        Assert.IsTrue(f.Function(-5d) < 1e-12);
    }

    /// <summary>
    /// Verifies percentile sampling against the Numerics closed-form anchor
    /// (CL 0.75 → 1361.61408399941) and the co-monotonic constant-ratio property of the
    /// multiplicative lognormal residual.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Percentile_CoMonotonic()
    {
        // Arrange
        var t = AnchorTransform();

        // Act
        var p75 = t.SampleFunction(0.75d);
        var mean = t.SampleFunction();

        // Assert — the Test_Functions anchor constant.
        Assert.AreEqual(1361.61408399941d, p75.Function(6d), 1e-9);

        // Co-monotonic multiplicative residual: the ratio to the median curve is constant.
        double ratioAt6 = p75.Function(6d) / mean.Function(6d);
        double ratioAt20 = p75.Function(20d) / mean.Function(20d);
        Assert.AreEqual(ratioAt6, ratioAt20, 1e-9);
        Assert.AreEqual(Math.Exp(new Normal(0d, 3d).InverseCDF(0.75d)), ratioAt6, 1e-9);

        // A deterministic transform ignores the percentile (the v1.0 behavior).
        var deterministic = AnchorTransform();
        deterministic.IsUncertain = false;
        Assert.AreEqual(180d, deterministic.SampleFunction(0.95d).Function(6d), 1e-12);
    }

    /// <summary>Verifies the inverse form against the Numerics closed-form anchors.</summary>
    [TestMethod]
    public void Test_SampleFunction_InverseForm_KnownPoints()
    {
        // Arrange
        var t = AnchorTransform();
        t.IsInverse = true;

        // Act / Assert — the Test_Functions anchors: sqrt(6/5) deterministic; 0.398290417772997 at CL 0.75.
        Assert.AreEqual(Math.Sqrt(6d / 5d), t.SampleFunction().Function(6d), 1e-12);
        Assert.AreEqual(0.398290417772997d, t.SampleFunction(0.75d).Function(6d), 1e-9);
    }

    /// <summary>Verifies unusable configurations throw on sampling (v1.1 upgrade of the v1.0 silent path).</summary>
    [TestMethod]
    public void Test_SampleFunction_InvalidConfiguration_Throws()
    {
        // Arrange — a non-positive coefficient is unusable.
        var t = AnchorTransform();
        t.Alpha = 0d;

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => t.SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(() => t.SampleFunction(0.5d));

        // A collapsed range is unusable.
        var range = AnchorTransform();
        range.Minimum = 100d;
        Assert.ThrowsException<InvalidOperationException>(() => range.SampleFunction(0.5d));
    }

    /// <summary>
    /// Verifies the realization-index overload: uncertain instances read the pre-allocated
    /// percentile row; deterministic instances (D = 0) return the mean function without any
    /// sampler setup — the engine samples transforms by index on the full-MC path.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_RealizationIndex_UsesSamplerRow()
    {
        // Arrange
        var t = AnchorTransform();
        t.SetupSampler(20, 12345, SamplingScheme.LatinHypercube);

        // Act / Assert — rows are deterministic per seed.
        var first = t.SampleFunction(7).Function(6d);
        t.SetupSampler(20, 12345, SamplingScheme.LatinHypercube);
        Assert.AreEqual(first, t.SampleFunction(7).Function(6d), 0d);

        // Before setup, index sampling throws for an uncertain transform.
        var fresh = AnchorTransform();
        Assert.ThrowsException<InvalidOperationException>(() => fresh.SampleFunction(0));

        // A deterministic transform has no percentile matrix and returns the mean directly.
        var deterministic = AnchorTransform();
        deterministic.IsUncertain = false;
        Assert.AreEqual(180d, deterministic.SampleFunction(3).Function(6d), 1e-12);
    }

    /// <summary>Verifies the v1.0 bounds surface.</summary>
    [TestMethod]
    public void Test_Bounds_MatchV10()
    {
        // Arrange
        var t = AnchorTransform();

        // Assert — hazard bounds are the wrapper range.
        Assert.AreEqual(0d, t.MinHazard(), 0d);
        Assert.AreEqual(100d, t.MaxHazard(), 0d);

        // Mean-only transformed bounds evaluate the deterministic function at the range ends
        // (the ξ clamp sends the minimum to α·ε^β ≈ 0).
        Assert.IsTrue(t.MinTransformedHazard(meanOnly: true) < 1e-12);
        Assert.AreEqual(5d * 100d * 100d, t.MaxTransformedHazard(meanOnly: true), 1e-9);

        // Full-uncertainty bounds probe the 1e-5 / 1−1e-5 percentiles (the v1.0 probes).
        double zHi = new Normal(0d, 3d).InverseCDF(1d - 0.00001d);
        Assert.AreEqual(5d * 100d * 100d * Math.Exp(zHi), t.MaxTransformedHazard(false), 1e-6 * 5d * 100d * 100d * Math.Exp(zHi));
    }

    /// <summary>Verifies the uncertainty summary is exact closed-form evaluation over the grid.</summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_ExactClosedForms()
    {
        // Arrange
        var t = AnchorTransform();

        // Act
        double[] hazards = t.UncertaintySummaryHazards();
        var summary = t.ComputeUncertaintyResults(0.90d)!;

        // Assert — the grid spans the range with 100 points.
        Assert.AreEqual(100, hazards.Length);
        Assert.AreEqual(0d, hazards[0], 0d);
        Assert.AreEqual(100d, hazards[^1], 0d);

        // Median = deterministic curve; mean = median·exp(σ²/2); CI bounds = median·exp(z·σ).
        var z = new Normal(0d, 1d);
        for (int i = 33; i < hazards.Length; i += 33)
        {
            double median = 5d * Math.Pow(hazards[i], 2d);
            Assert.AreEqual(median, summary.ModeCurve![i], Math.Max(1e-9, 1e-12 * median));
            Assert.AreEqual(median * Math.Exp(3d * 3d / 2d), summary.MeanCurve![i], 1e-9 * median * Math.Exp(4.5d));
            Assert.AreEqual(median * Math.Exp(3d * z.InverseCDF(0.05d)), summary.ConfidenceIntervals![i, 0], 1e-9 * median);
            Assert.AreEqual(median * Math.Exp(3d * z.InverseCDF(0.95d)), summary.ConfidenceIntervals[i, 1], 1e-6 * median * Math.Exp(3d * z.InverseCDF(0.95d)));
        }

        // Inverse form: the CI is the envelope (the percentile direction reverses through the
        // inversion for β > 0), and the mean applies the σ/|β| lognormal factor to (Y − ξ).
        var inverse = AnchorTransform();
        inverse.IsInverse = true;
        var invSummary = inverse.ComputeUncertaintyResults(0.90d)!;
        double[] invHazards = inverse.UncertaintySummaryHazards();
        int probe = 50;
        double invMedian = Math.Sqrt(invHazards[probe] / 5d);
        Assert.AreEqual(invMedian, invSummary.ModeCurve![probe], 1e-9);
        Assert.AreEqual(invMedian * Math.Exp(3d * 3d / (2d * 2d * 2d)), invSummary.MeanCurve![probe], 1e-9 * invSummary.MeanCurve[probe]);
        Assert.IsTrue(invSummary.ConfidenceIntervals![probe, 0] < invMedian);
        Assert.IsTrue(invSummary.ConfidenceIntervals[probe, 1] > invMedian);

        // Guards: bad width throws; an unusable configuration returns null.
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => t.ComputeUncertaintyResults(0d));
        var invalid = AnchorTransform();
        invalid.Alpha = -1d;
        Assert.IsNull(invalid.ComputeUncertaintyResults(0.90d));
    }

    /// <summary>Verifies the XElement round-trip restores every serialized member, and the σ-conditional attribute.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = AnchorTransform();
        original.Description = "Round trip";
        original.Xi = 1.5d;
        original.IsInverse = true;
        original.Minimum = 2d;
        original.Maximum = 250d;

        // Act
        var restored = new PowerTransform(original.ToXElement());

        // Assert
        Assert.AreEqual(original.Name, restored.Name);
        Assert.AreEqual(original.Description, restored.Description);
        Assert.AreEqual(original.SpecifiedHazard, restored.SpecifiedHazard);
        Assert.AreEqual(original.TransformedHazard, restored.TransformedHazard);
        Assert.AreEqual(original.Alpha, restored.Alpha, 0d);
        Assert.AreEqual(original.Beta, restored.Beta, 0d);
        Assert.AreEqual(original.Xi, restored.Xi, 0d);
        Assert.AreEqual(original.Sigma, restored.Sigma, 0d);
        Assert.AreEqual(original.IsInverse, restored.IsInverse);
        Assert.AreEqual(original.IsUncertain, restored.IsUncertain);
        Assert.AreEqual(original.Minimum, restored.Minimum, 0d);
        Assert.AreEqual(original.Maximum, restored.Maximum, 0d);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash(), "Round-trip must preserve the canonical hash.");

        // The σ attribute is written only while uncertain (the §5.5.3 recipe-literal conditional).
        Assert.IsNotNull(original.ToXElement().Attribute(nameof(PowerTransform.Sigma)));
        original.IsUncertain = false;
        Assert.IsNull(original.ToXElement().Attribute(nameof(PowerTransform.Sigma)));
    }

    /// <summary>Verifies hash identity: metadata inert, compute edits move it, deterministic σ inert.</summary>
    [TestMethod]
    public void Test_CanonicalHash_Identity()
    {
        // Arrange
        var t = AnchorTransform();

        // Act / Assert
        HashInvariance.AssertMetadataInvariant(t);
        HashInvariance.AssertStrippedAttributesInert(t.ToXElement());
        HashInvariance.AssertComputeSensitive(t.CanonicalHash, () => t.Xi = 0.5d);
        HashInvariance.AssertComputeSensitive(t.CanonicalHash, () => t.IsInverse = true);
        HashInvariance.AssertComputeSensitive(t.CanonicalHash, () => t.Sigma = 0.25d);

        // While deterministic, σ is compute-inert and must not move the hash.
        var deterministic = AnchorTransform();
        deterministic.IsUncertain = false;
        var before = deterministic.CanonicalHash();
        deterministic.Sigma = 999d;
        CollectionAssert.AreEqual(before, deterministic.CanonicalHash(),
            "A deterministic transform's σ is compute-inert and must not reach the hash surface.");
    }
}
