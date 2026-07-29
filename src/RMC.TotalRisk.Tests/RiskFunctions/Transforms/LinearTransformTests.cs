using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.RiskFunctions.Transforms;

/// <summary>
/// Unit tests for <see cref="LinearTransform"/> — v1.0 defaults, validation matrix, closed-form
/// sampling anchors (the Numerics <c>Test_Functions</c> fixture constants), the conditional
/// sampling dimension, bounds, exact uncertainty summary, serialization round-trip, and hash
/// identity including the σ-conditional recipe.
/// </summary>
[TestClass]
public class LinearTransformTests
{
    /// <summary>Builds the Numerics closed-form anchor fixture: Y = −2 + 5X + ε, ε ~ N(0, 3).</summary>
    private static LinearTransform AnchorTransform()
    {
        return new LinearTransform
        {
            Name = "Stage shift",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            Alpha = -2d,
            Beta = 5d,
            Sigma = 3d,
            IsUncertain = true,
        };
    }

    /// <summary>Verifies the v1.0 default construction state.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var t = new LinearTransform();

        // Assert — the exact v1.0 parameter defaults.
        Assert.AreEqual(0d, t.Alpha, 0d);
        Assert.AreEqual(1d, t.Beta, 0d);
        Assert.AreEqual(10d, t.Sigma, 0d);
        Assert.IsTrue(t.IsUncertain);
        Assert.AreEqual(0d, t.Minimum, 0d);
        Assert.AreEqual(100d, t.Maximum, 0d);
        Assert.IsFalse(t.IsDeterministic);
        Assert.AreEqual(1, t.SamplingDimensions);

        // The sampling dimension follows the uncertainty flag
        // (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §5.8.4).
        t.IsUncertain = false;
        Assert.IsTrue(t.IsDeterministic);
        Assert.AreEqual(0, t.SamplingDimensions);
    }

    /// <summary>Verifies the validation matrix: labels, range, parameters, and the σ gate.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Arrange — the configured anchor passes.
        Assert.IsTrue(AnchorTransform().Validate().IsValid);

        // Missing labels are errors.
        var unlabeled = new LinearTransform();
        var (isValid, messages) = unlabeled.Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(4, messages.Count(m => m.StartsWith("Error:", StringComparison.Ordinal)));

        // Minimum at or above the maximum is an error.
        var range = AnchorTransform();
        range.Minimum = 100d;
        Assert.IsFalse(range.Validate().IsValid);

        // A non-positive σ is an error only while uncertain (the v1.0 gate).
        var sigma = AnchorTransform();
        sigma.Sigma = 0d;
        Assert.IsFalse(sigma.Validate().IsValid);
        sigma.IsUncertain = false;
        Assert.IsTrue(sigma.Validate().IsValid);

        // Non-finite parameters are errors.
        var alpha = AnchorTransform();
        alpha.Alpha = double.NaN;
        Assert.IsFalse(alpha.Validate().IsValid);
    }

    /// <summary>Verifies mean sampling against the Numerics closed-form anchor, with clamps.</summary>
    [TestMethod]
    public void Test_SampleFunction_Mean_KnownPoints()
    {
        // Arrange
        var t = AnchorTransform();

        // Act
        var f = t.SampleFunction();

        // Assert — the Test_Functions anchor: −2 + 5·6 = 28; the mean function is deterministic.
        Assert.AreEqual(28d, f.Function(6d), 0d);
        // Evaluation clamps to [Minimum, Maximum].
        Assert.AreEqual(-2d, f.Function(-10d), 0d);
        Assert.AreEqual(498d, f.Function(200d), 0d);
    }

    /// <summary>
    /// Verifies percentile sampling against the Numerics closed-form anchor
    /// (CL 0.75 → 30.0234692505882) and the co-monotonic constant-offset property.
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
        Assert.AreEqual(30.0234692505882d, p75.Function(6d), 1e-9);

        // Co-monotonic: the whole curve shifts by the same z·σ offset at every hazard.
        double offsetAt6 = p75.Function(6d) - mean.Function(6d);
        double offsetAt20 = p75.Function(20d) - mean.Function(20d);
        Assert.AreEqual(offsetAt6, offsetAt20, 1e-12);
        Assert.AreEqual(new Normal(0d, 3d).InverseCDF(0.75d), offsetAt6, 1e-12);

        // A deterministic transform ignores the percentile (the v1.0 behavior).
        var deterministic = AnchorTransform();
        deterministic.IsUncertain = false;
        Assert.AreEqual(28d, deterministic.SampleFunction(0.95d).Function(6d), 0d);
    }

    /// <summary>Verifies unusable configurations throw on sampling (v1.1 upgrade of the v1.0 silent path).</summary>
    [TestMethod]
    public void Test_SampleFunction_InvalidConfiguration_Throws()
    {
        // Arrange — a collapsed range is unusable.
        var t = AnchorTransform();
        t.Minimum = 100d;

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => t.SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(() => t.SampleFunction(0.5d));

        // A non-positive σ is unusable only while uncertain.
        var sigma = AnchorTransform();
        sigma.Sigma = -1d;
        Assert.ThrowsException<InvalidOperationException>(() => sigma.SampleFunction(0.5d));
        sigma.IsUncertain = false;
        Assert.AreEqual(28d, sigma.SampleFunction(0.5d).Function(6d), 0d);
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
        Assert.AreEqual(28d, deterministic.SampleFunction(3).Function(6d), 0d);
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

        // Mean-only transformed bounds evaluate the deterministic function at the range ends.
        Assert.AreEqual(-2d, t.MinTransformedHazard(meanOnly: true), 1e-12);
        Assert.AreEqual(498d, t.MaxTransformedHazard(meanOnly: true), 1e-12);

        // Full-uncertainty bounds probe the 1e-5 / 1−1e-5 percentiles (the v1.0 probes).
        var z = new Normal(0d, 3d);
        Assert.AreEqual(-2d + z.InverseCDF(0.00001d), t.MinTransformedHazard(false), 1e-9);
        Assert.AreEqual(498d + z.InverseCDF(1d - 0.00001d), t.MaxTransformedHazard(false), 1e-9);
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

        // Symmetric Gaussian residual: mean = median = α + βx; CI bounds are ±z·σ offsets.
        var z = new Normal(0d, 3d);
        for (int i = 0; i < hazards.Length; i += 33)
        {
            double expected = -2d + 5d * hazards[i];
            Assert.AreEqual(expected, summary.MeanCurve![i], 1e-12);
            Assert.AreEqual(expected, summary.ModeCurve![i], 1e-12);
            Assert.AreEqual(expected + z.InverseCDF(0.05d), summary.ConfidenceIntervals![i, 0], 1e-9);
            Assert.AreEqual(expected + z.InverseCDF(0.95d), summary.ConfidenceIntervals[i, 1], 1e-9);
        }

        // A deterministic transform yields degenerate (equal) curves.
        var deterministic = AnchorTransform();
        deterministic.IsUncertain = false;
        var flat = deterministic.ComputeUncertaintyResults(0.90d)!;
        Assert.AreEqual(flat.MeanCurve![10], flat.ConfidenceIntervals![10, 0], 0d);
        Assert.AreEqual(flat.MeanCurve[10], flat.ConfidenceIntervals[10, 1], 0d);

        // Guards: bad width throws; an unusable configuration returns null.
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => t.ComputeUncertaintyResults(1.5d));
        var invalid = AnchorTransform();
        invalid.Minimum = 100d;
        Assert.IsNull(invalid.ComputeUncertaintyResults(0.90d));
    }

    /// <summary>Verifies the XElement round-trip restores every serialized member, and the σ-conditional attribute.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = AnchorTransform();
        original.Description = "Round trip";
        original.Minimum = -5d;
        original.Maximum = 250d;

        // Act
        var restored = new LinearTransform(original.ToXElement());

        // Assert
        Assert.AreEqual(original.Name, restored.Name);
        Assert.AreEqual(original.Description, restored.Description);
        Assert.AreEqual(original.SpecifiedHazard, restored.SpecifiedHazard);
        Assert.AreEqual(original.TransformedHazard, restored.TransformedHazard);
        Assert.AreEqual(original.Alpha, restored.Alpha, 0d);
        Assert.AreEqual(original.Beta, restored.Beta, 0d);
        Assert.AreEqual(original.Sigma, restored.Sigma, 0d);
        Assert.AreEqual(original.IsUncertain, restored.IsUncertain);
        Assert.AreEqual(original.Minimum, restored.Minimum, 0d);
        Assert.AreEqual(original.Maximum, restored.Maximum, 0d);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash(), "Round-trip must preserve the canonical hash.");

        // The σ attribute is written only while uncertain (the §5.5.3 recipe-literal conditional).
        Assert.IsNotNull(original.ToXElement().Attribute(nameof(LinearTransform.Sigma)));
        original.IsUncertain = false;
        Assert.IsNull(original.ToXElement().Attribute(nameof(LinearTransform.Sigma)));
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
        HashInvariance.AssertComputeSensitive(t.CanonicalHash, () => t.Beta = 2d);
        HashInvariance.AssertComputeSensitive(t.CanonicalHash, () => t.Sigma = 1.5d);
        HashInvariance.AssertComputeSensitive(t.CanonicalHash, () => t.IsUncertain = false);

        // While deterministic, σ is compute-inert and must not move the hash.
        var deterministic = AnchorTransform();
        deterministic.IsUncertain = false;
        var before = deterministic.CanonicalHash();
        deterministic.Sigma = 999d;
        CollectionAssert.AreEqual(before, deterministic.CanonicalHash(),
            "A deterministic transform's σ is compute-inert and must not reach the hash surface.");
    }
}
