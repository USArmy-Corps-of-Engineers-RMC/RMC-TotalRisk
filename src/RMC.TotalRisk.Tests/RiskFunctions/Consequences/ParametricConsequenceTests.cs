using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.RiskFunctions.Consequences;

/// <summary>
/// Unit tests for <see cref="ParametricConsequence"/> — spec defaults, the validation matrix
/// (including the heavy-tail warning), nominal and percentile sampling algebra, two-dimension
/// realization sampling, conditional sigma serialization, hash identity, support bounds, and the
/// deterministic uncertainty summary.
/// </summary>
[TestClass]
public class ParametricConsequenceTests
{
    /// <summary>Builds the labeled reference configuration: α=10, β=1.5, h₀=2, U=500, deterministic.</summary>
    private static ParametricConsequence ReferenceConsequence()
    {
        return new ParametricConsequence
        {
            Name = "Life loss",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            Alpha = 10d,
            Beta = 1.5d,
            Threshold = 2d,
            UpperBound = 500d,
        };
    }

    /// <summary>Builds the uncertain reference configuration: σ_α = 0.3, σ_β = 0.2.</summary>
    private static ParametricConsequence UncertainConsequence()
    {
        var c = ReferenceConsequence();
        c.IsUncertain = true;
        c.SigmaAlpha = 0.3d;
        c.SigmaBeta = 0.2d;
        return c;
    }

    /// <summary>Verifies the spec (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §6.4) default construction state.</summary>
    [TestMethod]
    public void Test_Defaults_MatchSpec()
    {
        // Act
        var c = new ParametricConsequence();

        // Assert
        Assert.AreEqual(1d, c.Alpha, 0d);
        Assert.AreEqual(1.5d, c.Beta, 0d);
        Assert.AreEqual(0d, c.Threshold, 0d);
        Assert.IsTrue(double.IsPositiveInfinity(c.UpperBound));
        Assert.IsFalse(c.IsUncertain);
        Assert.AreEqual(0d, c.SigmaAlpha, 0d);
        Assert.AreEqual(0d, c.SigmaBeta, 0d);
        Assert.IsTrue(c.IsDeterministic);
        Assert.AreEqual(0, c.SamplingDimensions);
        Assert.AreEqual(ConsequenceFunctionType.Parametric, c.FunctionType);
    }

    /// <summary>Verifies the validation matrix: label errors, parameter errors, and both warnings.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // A labeled valid configuration passes with no messages.
        var valid = ReferenceConsequence();
        var (ok, okMessages) = valid.Validate();
        Assert.IsTrue(ok);
        Assert.AreEqual(0, okMessages.Count);

        // Missing labels are 4 errors.
        var unlabeled = new ParametricConsequence();
        var (isValid, messages) = unlabeled.Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(4, messages.Count(m => m.StartsWith("Error:", StringComparison.Ordinal)));

        // Parameter errors, one at a time.
        var badAlpha = ReferenceConsequence();
        badAlpha.Alpha = 0d;
        Assert.IsTrue(badAlpha.Validate().ValidationMessages.Any(m => m.Contains("Alpha must be a positive")));
        var badBeta = ReferenceConsequence();
        badBeta.Beta = -1d;
        Assert.IsTrue(badBeta.Validate().ValidationMessages.Any(m => m.Contains("Beta must be a positive")));
        var badThreshold = ReferenceConsequence();
        badThreshold.Threshold = double.NaN;
        Assert.IsTrue(badThreshold.Validate().ValidationMessages.Any(m => m.Contains("threshold must be a finite")));
        var badUpper = ReferenceConsequence();
        badUpper.UpperBound = 0d;
        Assert.IsTrue(badUpper.Validate().ValidationMessages.Any(m => m.Contains("upper bound must be greater than zero")));

        // A negative sigma while uncertain is an error.
        var badSigma = UncertainConsequence();
        badSigma.SigmaAlpha = -0.1d;
        var (sigmaValid, sigmaMessages) = badSigma.Validate();
        Assert.IsFalse(sigmaValid);
        Assert.IsTrue(sigmaMessages.Any(m => m.Contains("SigmaAlpha cannot be negative")));

        // Flagged uncertain with both sigmas zero warns but stays valid.
        var inert = ReferenceConsequence();
        inert.IsUncertain = true;
        var (inertValid, inertMessages) = inert.Validate();
        Assert.IsTrue(inertValid);
        Assert.IsTrue(inertMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("sample deterministically")));

        // Exponent uncertainty with no cap raises the heavy-tail warning but stays valid.
        var heavyTail = UncertainConsequence();
        heavyTail.UpperBound = double.PositiveInfinity;
        var (heavyValid, heavyMessages) = heavyTail.Validate();
        Assert.IsTrue(heavyValid);
        Assert.IsTrue(heavyMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("unbounded")));
    }

    /// <summary>Verifies the nominal curve evaluates the exact closed form.</summary>
    [TestMethod]
    public void Test_SampleFunction_Nominal_KnownPoints()
    {
        // Arrange
        var c = ReferenceConsequence();

        // Act
        var f = c.SampleFunction();

        // Assert
        Assert.AreEqual(0d, f.Function(2d), 0d);
        Assert.AreEqual(10d, f.Function(3d), 1e-12);
        Assert.AreEqual(80d, f.Function(6d), 1e-12);
        Assert.AreEqual(500d, f.Function(100d), 0d, "The cap must clamp far above the crossing.");
    }

    /// <summary>Verifies invalid parameters throw at sample time (the cluster gate).</summary>
    [TestMethod]
    public void Test_SampleFunction_InvalidParameters_Throws()
    {
        // Arrange
        var c = ReferenceConsequence();
        c.Alpha = 0d;

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => c.SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(() => c.SampleFunction(0.5d));
    }

    /// <summary>
    /// Verifies co-monotonic percentile sampling: one z drives both coefficients, the median
    /// percentile reproduces the nominal curve, and the realized algebra is exact at the unit
    /// offset (where the exponent drops out) and on the power segment.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Percentile_CoMonotonicLognormal()
    {
        // Arrange
        var c = UncertainConsequence();
        c.UpperBound = double.PositiveInfinity;

        // Act / Assert — the median percentile is the nominal curve.
        var median = c.SampleFunction(0.5d);
        Assert.AreEqual(10d, median.Function(3d), 1e-9);
        Assert.AreEqual(80d, median.Function(6d), 1e-9);

        // At the 95th percentile: α_p = α·e^{σα·z}, β_p = β·e^{σβ·z} with the same z.
        double z = Normal.StandardZ(0.95d);
        var upper = c.SampleFunction(0.95d);
        Assert.AreEqual(10d * Math.Exp(0.3d * z), upper.Function(3d), 1e-9,
            "At unit offset the exponent drops out: C = α_p.");
        double expected = 10d * Math.Exp(0.3d * z) * Math.Pow(4d, 1.5d * Math.Exp(0.2d * z));
        Assert.AreEqual(expected, upper.Function(6d), 1e-9 * expected);
    }

    /// <summary>
    /// Verifies realization-index sampling draws the two coefficients from independent sampler
    /// dimensions, and that identical content with the same seed reproduces bit-identically.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_RealizationIndex_UsesTwoIndependentDimensions()
    {
        // Arrange — two identically configured instances, same sampler seed.
        var a = UncertainConsequence();
        var b = UncertainConsequence();
        a.UpperBound = double.PositiveInfinity;
        b.UpperBound = double.PositiveInfinity;
        a.SetupSampler(100, 12345, SamplingScheme.LatinHypercube);
        b.SetupSampler(100, 12345, SamplingScheme.LatinHypercube);

        // Act / Assert — same content + seed → bit-identical realizations.
        for (int i = 0; i < 100; i += 13)
        {
            Assert.AreEqual(a.SampleFunction(i).Function(6d), b.SampleFunction(i).Function(6d), 0d);
        }

        // The two dimensions are independent: back out z₁ (from the unit offset) and z₂ (from the
        // power segment) and require them to differ somewhere — a co-monotonic sampler would give
        // z₁ = z₂ at every realization.
        bool dimensionsDiffer = false;
        for (int i = 0; i < 100; i++)
        {
            var f = a.SampleFunction(i);
            double alphaRealized = f.Function(3d);
            double betaRealized = Math.Log(f.Function(6d) / alphaRealized) / Math.Log(4d);
            double z1 = Math.Log(alphaRealized / 10d) / 0.3d;
            double z2 = Math.Log(betaRealized / 1.5d) / 0.2d;
            if (Math.Abs(z1 - z2) > 1e-6)
            {
                dimensionsDiffer = true;
                break;
            }
        }
        Assert.IsTrue(dimensionsDiffer, "Realization sampling must draw the coefficients from independent dimensions.");
    }

    /// <summary>Verifies uncertain realization sampling requires SetupSampler first.</summary>
    [TestMethod]
    public void Test_SampleFunction_BeforeSetup_Throws()
    {
        // Arrange
        var c = UncertainConsequence();

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => c.SampleFunction(3));

        // Deterministic instances need no sampler (D = 0).
        var d = ReferenceConsequence();
        Assert.AreEqual(80d, d.SampleFunction(3).Function(6d), 1e-12);
    }

    /// <summary>Verifies the XElement round-trip preserves state and hash, including an infinite cap.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip_IncludingInfiniteUpperBound()
    {
        // Arrange
        var original = UncertainConsequence();
        original.UpperBound = double.PositiveInfinity;

        // Act
        var restored = new ParametricConsequence(original.ToXElement());

        // Assert
        Assert.AreEqual(original.Alpha, restored.Alpha, 0d);
        Assert.AreEqual(original.Beta, restored.Beta, 0d);
        Assert.AreEqual(original.Threshold, restored.Threshold, 0d);
        Assert.IsTrue(double.IsPositiveInfinity(restored.UpperBound));
        Assert.AreEqual(original.IsUncertain, restored.IsUncertain);
        Assert.AreEqual(original.SigmaAlpha, restored.SigmaAlpha, 0d);
        Assert.AreEqual(original.SigmaBeta, restored.SigmaBeta, 0d);
        Assert.AreEqual(original.SpecifiedConsequence, restored.SpecifiedConsequence);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>Verifies the sigma attributes are written only while uncertain (recipe-literal).</summary>
    [TestMethod]
    public void Test_Serialization_SigmasConditional_OnIsUncertain()
    {
        // Arrange — sigmas set but the function is not flagged uncertain.
        var c = ReferenceConsequence();
        c.SigmaAlpha = 0.5d;
        c.SigmaBeta = 0.4d;

        // Act / Assert — no sigma attributes while deterministic.
        var element = c.ToXElement();
        Assert.IsNull(element.Attribute(nameof(ParametricConsequence.SigmaAlpha)));
        Assert.IsNull(element.Attribute(nameof(ParametricConsequence.SigmaBeta)));

        // Flagging uncertain writes them.
        c.IsUncertain = true;
        element = c.ToXElement();
        Assert.IsNotNull(element.Attribute(nameof(ParametricConsequence.SigmaAlpha)));
        Assert.IsNotNull(element.Attribute(nameof(ParametricConsequence.SigmaBeta)));
    }

    /// <summary>Verifies sigma edits are hash-inert while deterministic and hash-moving while uncertain.</summary>
    [TestMethod]
    public void Test_CanonicalHash_SigmaEdits_InertWhenDeterministic_MoveWhenUncertain()
    {
        // Arrange
        var c = ReferenceConsequence();
        byte[] baseline = c.CanonicalHash();

        // Act / Assert — sigma edits cannot affect results while deterministic, so they are inert.
        c.SigmaAlpha = 0.9d;
        CollectionAssert.AreEqual(baseline, c.CanonicalHash());

        // The uncertainty toggle is compute-relevant, and once uncertain, sigma edits move the hash.
        c.IsUncertain = true;
        byte[] uncertainBaseline = c.CanonicalHash();
        CollectionAssert.AreNotEqual(baseline, uncertainBaseline);
        c.SigmaAlpha = 0.1d;
        CollectionAssert.AreNotEqual(uncertainBaseline, c.CanonicalHash());
    }

    /// <summary>Verifies hash identity: metadata inert, parameter edits compute-relevant.</summary>
    [TestMethod]
    public void Test_CanonicalHash_Identity()
    {
        // Arrange
        var c = UncertainConsequence();
        byte[] baseline = c.CanonicalHash();

        // Act / Assert — consequence labels are metadata (stripped).
        c.ConsequenceUnit = "people";
        CollectionAssert.AreEqual(baseline, c.CanonicalHash());
        HashInvariance.AssertMetadataInvariant(c);
        HashInvariance.AssertStrippedAttributesInert(c.ToXElement());
        HashInvariance.AssertComputeSensitive(c.CanonicalHash, () => c.Alpha = 2.5d);
    }

    /// <summary>Verifies the support bounds: threshold, saturation crossing, and the edge cases.</summary>
    [TestMethod]
    public void Test_MinMaxHazard_CrossingAndInfinity_EdgeCases()
    {
        // Arrange
        var c = ReferenceConsequence();

        // Assert — the crossing for (α=10, β=1.5, U=500) is 2 + 50^(2/3).
        Assert.AreEqual(2d, c.MinHazard(), 0d);
        Assert.AreEqual(2d + Math.Pow(50d, 2d / 3d), c.MaxHazard(), 1e-12);

        // No cap → unbounded support.
        c.UpperBound = double.PositiveInfinity;
        Assert.IsTrue(double.IsPositiveInfinity(c.MaxHazard()));

        // Invalid parameters → defensive positive infinity, never NaN.
        c.Alpha = 0d;
        Assert.IsTrue(double.IsPositiveInfinity(c.MaxHazard()));
    }

    /// <summary>Verifies the summary grid caps at the saturation crossing when the cap is finite.</summary>
    [TestMethod]
    public void Test_UncertaintySummaryHazards_CapsAtCrossing()
    {
        // Arrange
        var capped = ReferenceConsequence();

        // Act
        double[] grid = capped.UncertaintySummaryHazards();

        // Assert — strictly increasing, first point at the threshold, last at the crossing.
        Assert.AreEqual(capped.Threshold, grid[0], 0d);
        Assert.AreEqual(capped.MaxHazard(), grid[^1], 0d);
        for (int i = 1; i < grid.Length; i++)
        {
            Assert.IsTrue(grid[i] > grid[i - 1], "The summary grid must be strictly increasing.");
        }

        // Without a cap the grid spans the full four decades above the threshold.
        var uncapped = ReferenceConsequence();
        uncapped.UpperBound = double.PositiveInfinity;
        double[] openGrid = uncapped.UncertaintySummaryHazards();
        Assert.AreEqual(101, openGrid.Length);
        Assert.AreEqual(uncapped.Threshold + 100d, openGrid[^1], 1e-9);
    }

    /// <summary>Verifies property-change notification on every settable property.</summary>
    [TestMethod]
    public void Test_PropertyChange_Notification()
    {
        // Arrange
        var c = new ParametricConsequence();
        var raised = new List<string>();
        c.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        c.Alpha = 2d;
        c.Beta = 2.5d;
        c.Threshold = 1d;
        c.UpperBound = 100d;
        c.IsUncertain = true;
        c.SigmaAlpha = 0.1d;
        c.SigmaBeta = 0.2d;

        // Assert
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(ParametricConsequence.Alpha), nameof(ParametricConsequence.Beta),
                nameof(ParametricConsequence.Threshold), nameof(ParametricConsequence.UpperBound),
                nameof(ParametricConsequence.IsUncertain), nameof(ParametricConsequence.SigmaAlpha),
                nameof(ParametricConsequence.SigmaBeta),
            },
            raised);

        // Setting the same value again must not raise.
        raised.Clear();
        c.Alpha = 2d;
        Assert.AreEqual(0, raised.Count);
    }
}
