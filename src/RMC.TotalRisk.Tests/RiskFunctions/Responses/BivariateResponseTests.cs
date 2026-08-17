using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses;

/// <summary>
/// Unit tests for <see cref="BivariateResponse"/> — the preserved v1.0 collapse mode (weighted
/// collapse values, curve flags, the distribution wrapper, bounds semantics, grid auto-resize),
/// the explicit <c>EstimateWeights</c> surface with the fixed load-order behavior, the stored
/// lenient secondary-hazard link, the joint surface augmentation with the pinned extrapolation
/// and clamp policy, the full validation matrix, the legacy serialization shape, and the hash
/// classification.
/// </summary>
[TestClass]
public class BivariateResponseTests
{
    #region Fixtures

    /// <summary>Applies the standard four axis labels.</summary>
    private static void Label(BivariateResponse response)
    {
        response.SpecifiedHazard = "Peak Ground Acceleration";
        response.HazardUnit = "g";
        response.SecondarySpecifiedHazard = "Pool Elevation";
        response.SecondaryHazardUnit = "ft";
    }

    /// <summary>Replaces both level collections (adds resize the grid as they go).</summary>
    private static void SetLevels(BivariateResponse response, double[] primary,
        (double Level, double Weight)[] secondary)
    {
        response.PrimaryHazardLevels.Clear();
        foreach (double level in primary) response.PrimaryHazardLevels.Add(level);
        response.SecondaryHazardLevels.Clear();
        foreach (var (level, weight) in secondary)
            response.SecondaryHazardLevels.Add(new WeightedHazardLevel { Level = level, Weight = weight });
    }

    /// <summary>
    /// The Isabella-style 4×6 fixture: PGA rows against weighted pool-elevation columns, weights
    /// summing to one within an ulp.
    /// </summary>
    private static BivariateResponse Isabella()
    {
        var response = new BivariateResponse { Name = "Isabella Fragility" };
        Label(response);
        SetLevels(response,
            new[] { 0.2d, 0.4d, 0.6d, 0.8d },
            new[] { (1090d, 0.05d), (1100d, 0.1d), (1110d, 0.2d), (1120d, 0.3d), (1130d, 0.25d), (1140d, 0.1d) });
        response.ProbabilityValues = new[,]
        {
            { 0.00d, 0.00d, 0.01d, 0.02d, 0.05d, 0.10d },
            { 0.01d, 0.02d, 0.05d, 0.10d, 0.20d, 0.35d },
            { 0.05d, 0.10d, 0.20d, 0.35d, 0.55d, 0.75d },
            { 0.15d, 0.30d, 0.50d, 0.70d, 0.85d, 0.95d },
        };
        return response;
    }

    /// <summary>
    /// A single-secondary-column fixture whose collapse DECREASES with hazard — legal content
    /// (monotonicity is advisory), used for curve-flag and first/last-bounds pins.
    /// </summary>
    private static BivariateResponse DecreasingCollapse()
    {
        var response = new BivariateResponse { Name = "Decreasing" };
        Label(response);
        SetLevels(response, new[] { 0d, 1d }, new[] { (0d, 1d) });
        response.ProbabilityValues = new[,] { { 0.9d }, { 0.5d } };
        return response;
    }

    /// <summary>
    /// Builds a labeled, estimated, deterministic parametric hazard whose mean marginal CDF is
    /// the exact Normal CDF — the hand-computable weight-derivation source.
    /// </summary>
    private static ParametricUnivariateHazard PoolHazard(double mean, double standardDeviation)
    {
        var hazard = new ParametricUnivariateHazard
        {
            Name = "Pool Duration",
            SpecifiedHazard = "Pool Elevation",
            HazardUnit = "ft",
            ParentDistribution = new Normal(mean, standardDeviation),
            IsUncertain = false,
        };
        hazard.Estimate();
        return hazard;
    }

    /// <summary>A labeled tabular hazard — the house minimal valid univariate hazard.</summary>
    private static TabularHazard PoolTable(string name = "Pool Frequency")
    {
        return new TabularHazard { Name = name, SpecifiedHazard = "Pool Elevation", HazardUnit = "ft" };
    }

    /// <summary>Builds a resolver over an explicit store of live functions.</summary>
    private static RiskFunctionResolver ResolverOver(params IRiskFunction[] functions)
    {
        return new RiskFunctionResolver(
            id =>
            {
                foreach (var function in functions)
                {
                    if (function.Id == id) return function;
                }
                return null;
            },
            name =>
            {
                foreach (var function in functions)
                {
                    if (function.Name == name) return function;
                }
                return null;
            });
    }

    /// <summary>The independent in-test reimplementation of the v1.0 Voronoi weight derivation.</summary>
    private static double[] ExpectedVoronoiWeights(IUnivariateDistribution mean, double[] levels, out double rawSum)
    {
        var expected = new double[levels.Length];
        rawSum = 1d;
        if (levels.Length == 1)
        {
            expected[0] = 1d;
            return expected;
        }

        double previous = 0d;
        for (int i = 1; i < levels.Length; i++)
        {
            double midpoint = (levels[i] + levels[i - 1]) / 2d;
            double cdf = mean.CDF(midpoint);
            expected[i - 1] = i == 1 ? cdf : cdf - previous;
            previous = cdf;
        }
        expected[levels.Length - 1] = 1d - previous;

        double sum = 0d;
        for (int i = 0; i < expected.Length; i++) sum += expected[i];
        rawSum = sum;
        if (sum > 1d) expected[levels.Length - 1] -= sum - 1d;
        else if (sum < 1d) expected[levels.Length - 1] += 1d - sum;
        return expected;
    }

    /// <summary>The canonical hash as hex for equality asserts.</summary>
    private static string HashOf(IRiskFunction function)
    {
        return Convert.ToHexString(function.CanonicalHash());
    }

    #endregion

    #region Defaults and Change Notification

    /// <summary>
    /// Verifies the default construction state: the unit-square zero surface (primary {0, 1},
    /// secondary {0, 1} at weight 0.5 each, 2×2 zero grid), manual weights, no link, all
    /// transforms None, deterministic D = 0, the Bivariate discriminator — and that a labeled
    /// default validates cleanly with no warnings.
    /// </summary>
    [TestMethod]
    public void Test_Defaults_UnitSquareZeroSurface()
    {
        // Act
        var response = new BivariateResponse();

        // Assert — state.
        CollectionAssert.AreEqual(new[] { 0d, 1d }, response.PrimaryHazardLevels);
        Assert.AreEqual(2, response.SecondaryLevelCount);
        Assert.AreEqual(0d, response.SecondaryHazardLevels[0].Level);
        Assert.AreEqual(0.5d, response.SecondaryHazardLevels[0].Weight);
        Assert.AreEqual(1d, response.SecondaryHazardLevels[1].Level);
        Assert.AreEqual(0.5d, response.SecondaryHazardLevels[1].Weight);
        Assert.AreEqual(2, response.ProbabilityValues.GetLength(0));
        Assert.AreEqual(2, response.ProbabilityValues.GetLength(1));
        Assert.AreEqual(0d, response.ProbabilityValues[0, 0]);
        Assert.AreEqual(Transform.None, response.HazardTransform);
        Assert.AreEqual(Transform.None, response.SecondaryHazardTransform);
        Assert.AreEqual(Transform.None, response.ProbabilityTransform);
        Assert.IsTrue(response.UseManualWeights);
        Assert.IsNull(response.SecondaryHazardFunction);
        Assert.IsTrue(response.IsDeterministic);
        Assert.AreEqual(0, response.SamplingDimensions);
        Assert.AreEqual(ResponseFunctionType.Bivariate, response.FunctionType);
        Assert.IsTrue(response.SupportsOrderedCurveSampling);

        // Assert — an unlabeled default reports exactly the four label errors.
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(4, messages.Count);
        Assert.IsTrue(messages.All(m => m.StartsWith("Error:") && m.Contains("hazard")));

        // Assert — the labeled default validates with no messages at all.
        Label(response);
        var (labeledValid, labeledMessages) = response.Validate();
        Assert.IsTrue(labeledValid);
        Assert.AreEqual(0, labeledMessages.Count);
    }

    /// <summary>Verifies every settable property raises change notification exactly once per real change.</summary>
    [TestMethod]
    public void Test_PropertyChanges_RaiseNotifications()
    {
        // Arrange
        var response = new BivariateResponse();
        var raised = new List<string>();
        response.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        // Act — one real change per property, then repeat each with the same value.
        response.HazardTransform = Transform.Logarithmic;
        response.SecondaryHazardTransform = Transform.NormalZ;
        response.ProbabilityTransform = Transform.NormalZ;
        response.SecondarySpecifiedHazard = "Pool Elevation";
        response.SecondaryHazardUnit = "ft";
        response.UseManualWeights = false;
        response.ProbabilityValues = new double[2, 2];
        response.SecondaryHazardFunction = PoolTable();
        response.HazardTransform = Transform.Logarithmic;
        response.SecondaryHazardTransform = Transform.NormalZ;
        response.ProbabilityTransform = Transform.NormalZ;
        response.SecondarySpecifiedHazard = "Pool Elevation";
        response.SecondaryHazardUnit = "ft";
        response.UseManualWeights = false;

        // Assert
        CollectionAssert.AreEqual(new[]
        {
            nameof(BivariateResponse.HazardTransform),
            nameof(BivariateResponse.SecondaryHazardTransform),
            nameof(BivariateResponse.ProbabilityTransform),
            nameof(BivariateResponse.SecondarySpecifiedHazard),
            nameof(BivariateResponse.SecondaryHazardUnit),
            nameof(BivariateResponse.UseManualWeights),
            nameof(BivariateResponse.ProbabilityValues),
            nameof(BivariateResponse.SecondaryHazardFunction),
        }, raised);
    }

    /// <summary>Verifies a null surface assignment is ignored (reference kept, nothing raised).</summary>
    [TestMethod]
    public void Test_ProbabilityValues_NullAssignment_Ignored()
    {
        // Arrange
        var response = new BivariateResponse();
        var before = response.ProbabilityValues;
        var raised = new List<string>();
        response.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        // Act
        response.ProbabilityValues = null!;

        // Assert
        Assert.AreSame(before, response.ProbabilityValues);
        Assert.AreEqual(0, raised.Count);
    }

    #endregion

    #region Legacy Collapse Parity

    /// <summary>
    /// Verifies the collapse against hand-computed Σ P[i, j]·wⱼ values on the 4×6 fixture — one
    /// literal row plus a full independent re-summation.
    /// </summary>
    [TestMethod]
    public void Test_Collapse_MatchesHandComputedIsabellaValues()
    {
        // Arrange
        var response = Isabella();

        // Act
        var collapse = response.SampleResponseFunction();

        // Assert — the hand-computed first row: 0·0.05 + 0·0.1 + 0.01·0.2 + 0.02·0.3 + 0.05·0.25 + 0.10·0.1.
        Assert.AreEqual(4, collapse.Count);
        Assert.AreEqual(0.0305d, collapse[0].Y, 1e-15);

        // Assert — every ordinate matches an independent same-order re-summation bit-exactly.
        for (int i = 0; i < 4; i++)
        {
            double expected = 0d;
            for (int j = 0; j < 6; j++)
            {
                expected += response.ProbabilityValues[i, j] * response.SecondaryHazardLevels[j].Weight;
            }
            Assert.AreEqual(response.PrimaryHazardLevels[i], collapse[i].X, 0d);
            Assert.AreEqual(expected, collapse[i].Y, 0d);
        }
    }

    /// <summary>
    /// Pins the collapse curve flags — strictly ascending X, unconstrained Y — so a decreasing
    /// collapse is still a VALID curve (monotonicity is advisory, exact v1.0 construction).
    /// </summary>
    [TestMethod]
    public void Test_Collapse_CurveFlags_StrictAscendingXUnconstrainedY()
    {
        // Act
        var collapse = DecreasingCollapse().SampleResponseFunction();

        // Assert
        Assert.IsTrue(collapse.StrictX);
        Assert.AreEqual(SortOrder.Ascending, collapse.OrderX);
        Assert.IsFalse(collapse.StrictY);
        Assert.AreEqual(SortOrder.None, collapse.OrderY);
        Assert.IsTrue(collapse.IsValid);
        Assert.AreEqual(0.9d, collapse[0].Y, 0d);
        Assert.AreEqual(0.5d, collapse[1].Y, 0d);
    }

    /// <summary>
    /// Verifies <c>SampleFunction()</c> wraps the collapse in an empirical distribution carrying
    /// both interpolation transforms: node CDFs recover the collapse ordinates, and a
    /// log-geometric midpoint interpolates in normal-Z space.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_WrapsCollapseWithTransforms()
    {
        // Arrange
        var response = Isabella();
        response.HazardTransform = Transform.Logarithmic;
        response.ProbabilityTransform = Transform.NormalZ;
        var collapse = response.SampleResponseFunction();

        // Act
        var distribution = response.SampleFunction();

        // Assert — the wrapper carries the transforms and recovers the nodes.
        var empirical = (EmpiricalDistribution)distribution;
        Assert.AreEqual(Transform.Logarithmic, empirical.XTransform);
        Assert.AreEqual(Transform.NormalZ, empirical.ProbabilityTransform);
        for (int i = 0; i < collapse.Count; i++)
        {
            Assert.AreEqual(collapse[i].Y, distribution.CDF(collapse[i].X), 1e-12);
        }

        // Assert — at the log-geometric midpoint of the first interval the CDF interpolates
        // linearly in z-space: Φ((z(y₀) + z(y₁)) / 2).
        double expected = Normal.StandardCDF(
            0.5d * (Normal.StandardZ(collapse[0].Y) + Normal.StandardZ(collapse[1].Y)));
        Assert.AreEqual(expected, distribution.CDF(Math.Sqrt(0.2d * 0.4d)), 1e-10);
    }

    /// <summary>
    /// Verifies the percentile and realization-index overloads return the mean collapse — the
    /// surface is deterministic, exact v1.0 behavior — with no sampler state required.
    /// </summary>
    [TestMethod]
    public void Test_PercentileAndIndexOverloads_ReturnMeanCollapse()
    {
        // Arrange
        var response = Isabella();
        var mean = response.SampleResponseFunction();

        // Act
        var atPercentile = response.SampleResponseFunction(0.05d);
        var atIndex = response.SampleResponseFunction(3);

        // Assert
        for (int i = 0; i < mean.Count; i++)
        {
            Assert.AreEqual(mean[i].Y, atPercentile[i].Y, 0d);
            Assert.AreEqual(mean[i].Y, atIndex[i].Y, 0d);
        }
        Assert.AreEqual(response.SampleFunction().CDF(0.5d), response.SampleFunction(0.95d).CDF(0.5d), 0d);
        Assert.AreEqual(response.SampleFunction().CDF(0.5d), response.SampleFunction(7).CDF(0.5d), 0d);
    }

    /// <summary>Verifies monotonicity is evaluated on the collapsed curve (exact v1.0 loop).</summary>
    [TestMethod]
    public void Test_IsMonotonic_EvaluatesCollapsedCurve()
    {
        // Act / Assert
        Assert.IsTrue(Isabella().IsMonotonic());
        Assert.IsFalse(DecreasingCollapse().IsMonotonic());
    }

    /// <summary>
    /// Verifies the bounds semantics: hazard bounds are the first/last levels, and the
    /// probability bounds are the collapsed curve's FIRST and LAST ordinates (v1.0 semantics —
    /// on the decreasing fixture MinProbability exceeds MaxProbability).
    /// </summary>
    [TestMethod]
    public void Test_Bounds_MatchV10FirstLastSemantics()
    {
        // Arrange
        var isabella = Isabella();
        var decreasing = DecreasingCollapse();

        // Act / Assert
        Assert.AreEqual(0.2d, isabella.MinHazard(), 0d);
        Assert.AreEqual(0.8d, isabella.MaxHazard(), 0d);
        Assert.AreEqual(1090d, isabella.MinSecondaryHazard(), 0d);
        Assert.AreEqual(1140d, isabella.MaxSecondaryHazard(), 0d);
        Assert.AreEqual(0.0305d, isabella.MinProbability(), 1e-15);
        Assert.AreEqual(0.655d, isabella.MaxProbability(), 1e-15);
        Assert.AreEqual(0.9d, decreasing.MinProbability(), 0d);
        Assert.AreEqual(0.5d, decreasing.MaxProbability(), 0d);
    }

    /// <summary>Verifies the bounds throw loudly on emptied level collections.</summary>
    [TestMethod]
    public void Test_Bounds_EmptyCollections_Throw()
    {
        // Arrange — Clear is a Reset action, which deliberately does not resize the grid.
        var response = new BivariateResponse();
        response.PrimaryHazardLevels.Clear();
        response.SecondaryHazardLevels.Clear();

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => response.MinHazard());
        Assert.ThrowsException<InvalidOperationException>(() => response.MaxHazard());
        Assert.ThrowsException<InvalidOperationException>(() => response.MinSecondaryHazard());
        Assert.ThrowsException<InvalidOperationException>(() => response.MaxSecondaryHazard());
    }

    /// <summary>
    /// Verifies the sampling surfaces throw on an unusable table while <c>Validate</c> reports
    /// instead of throwing.
    /// </summary>
    [TestMethod]
    public void Test_Sampling_UnusableTable_ThrowsAndValidateReportsInstead()
    {
        // Arrange — a grid that disagrees with the level counts.
        var response = new BivariateResponse();
        Label(response);
        response.ProbabilityValues = new double[3, 5];

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => response.SampleResponseFunction());
        Assert.ThrowsException<InvalidOperationException>(() => response.SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(() => response.IsMonotonic());
        Assert.ThrowsException<InvalidOperationException>(() => response.MinProbability());
        Assert.ThrowsException<InvalidOperationException>(() => response.MaxProbability());
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("one row of probabilities")));
    }

    /// <summary>Verifies the deterministic surface has no uncertainty representation.</summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_ReturnsNull()
    {
        Assert.IsNull(Isabella().ComputeUncertaintyResults());
    }

    /// <summary>
    /// Verifies the D = 0 sampler contract: setup records the sample size only, and indexed
    /// sampling works with or without it.
    /// </summary>
    [TestMethod]
    public void Test_SetupSampler_DeterministicRecordsSampleSizeOnly()
    {
        // Arrange
        var response = Isabella();
        var beforeSetup = response.SampleResponseFunction(5);

        // Act
        response.SetupSampler(64, 12345, SamplingScheme.LatinHypercube);

        // Assert
        Assert.AreEqual(64, response.SampleSize);
        var afterSetup = response.SampleResponseFunction(63);
        for (int i = 0; i < beforeSetup.Count; i++)
        {
            Assert.AreEqual(beforeSetup[i].Y, afterSetup[i].Y, 0d);
        }
    }

    #endregion

    #region EstimateWeights

    /// <summary>Verifies the single-level degenerate case takes weight one (exact v1.0 rule).</summary>
    [TestMethod]
    public void Test_EstimateWeights_SingleLevel_TakesWeightOne()
    {
        // Arrange
        var response = new BivariateResponse();
        Label(response);
        SetLevels(response, new[] { 0d, 1d }, new[] { (1110d, 0.3d) });

        // Act
        response.EstimateWeights(PoolHazard(1110d, 20d));

        // Assert
        Assert.AreEqual(1d, response.SecondaryHazardLevels[0].Weight, 0d);
        Assert.IsFalse(response.UseManualWeights);
    }

    /// <summary>
    /// Verifies the Voronoi-midpoint derivation against hand-computed Normal CDF values:
    /// w₀ = F(1100), w₁ = F(1120) − F(1100), w₂ = 1 − F(1120) with midpoints at ±σ/2.
    /// </summary>
    [TestMethod]
    public void Test_EstimateWeights_VoronoiMidpoints_MatchHandComputedValues()
    {
        // Arrange
        var response = new BivariateResponse();
        Label(response);
        SetLevels(response, new[] { 0d, 1d }, new[] { (1090d, 0d), (1110d, 0d), (1130d, 0d) });
        var hazard = PoolHazard(1110d, 20d);

        // Act
        response.EstimateWeights(hazard);

        // Assert — the exact algorithm over the same mean function, bit-for-bit.
        var expected = ExpectedVoronoiWeights(hazard.SampleFunction(),
            new[] { 1090d, 1110d, 1130d }, out _);
        for (int j = 0; j < 3; j++)
        {
            Assert.AreEqual(expected[j], response.SecondaryHazardLevels[j].Weight, 0d);
        }

        // Assert — the semantic cross-check: the mean marginal is the exact Normal CDF, so the
        // first bin is Φ(−0.5) at the midpoint 1100 of a Normal(1110, 20).
        Assert.AreEqual(new Normal(1110d, 20d).CDF(1100d), response.SecondaryHazardLevels[0].Weight, 1e-12);

        // Assert — the weights sum to one exactly.
        double sum = 0d;
        for (int j = 0; j < 3; j++) sum += response.SecondaryHazardLevels[j].Weight;
        Assert.AreEqual(1d, sum, 0d);
    }

    /// <summary>
    /// Verifies the last-bin residual absorption: across a Normal-CDF parameter battery every
    /// derived weight matches the independent reimplementation bit-exactly and every sum is
    /// exactly one, and a constructed Uniform(0, 1) fixture — whose mean CDF returns its
    /// argument exactly — deterministically drives the raw sum BELOW one, pinning the absorbing
    /// branch. The greater-than branch is ported v1.0 defense: the telescoped raw sum ends in a
    /// complement addition that round-to-nearest cannot carry above one without an exact
    /// rounding tie, so no reachable CDF fixture exists; the reimplementation parity covers the
    /// branch symmetrically.
    /// </summary>
    [TestMethod]
    public void Test_EstimateWeights_ResidualAbsorption_SumsToOneExactly()
    {
        // Arrange / Act / Assert — the Normal battery: bit-exact parity and exact unit sums.
        for (int k = 0; k < 12; k++)
        {
            var levels = new[] { 1085d + 0.7d * k, 1101.3d, 1117d + 0.3d * k, 1133d };
            var response = new BivariateResponse();
            Label(response);
            SetLevels(response, new[] { 0d, 1d },
                new[] { (levels[0], 0d), (levels[1], 0d), (levels[2], 0d), (levels[3], 0d) });
            var hazard = PoolHazard(1100d + 3d * k, 12d + k);
            response.EstimateWeights(hazard);

            var expected = ExpectedVoronoiWeights(hazard.SampleFunction(), levels, out _);
            double sum = 0d;
            for (int j = 0; j < 4; j++)
            {
                Assert.AreEqual(expected[j], response.SecondaryHazardLevels[j].Weight, 0d, $"weight {j} at k={k}");
                sum += response.SecondaryHazardLevels[j].Weight;
            }
            Assert.AreEqual(1d, sum, 0d, $"sum at k={k}");
        }

        // Arrange — the deterministic below-one fixture over the exact-argument Uniform CDF.
        var uniformLevels = new[]
        {
            0.023796666415313569d, 0.31587167634436475d, 0.53429313996540995d,
            0.57983387511215823d, 0.80822254590141707d,
        };
        var uniformResponse = new BivariateResponse();
        Label(uniformResponse);
        SetLevels(uniformResponse, new[] { 0d, 1d }, uniformLevels.Select(l => (l, 0d)).ToArray());
        var uniformHazard = new ParametricUnivariateHazard
        {
            Name = "Uniform Pool",
            SpecifiedHazard = "Pool Elevation",
            HazardUnit = "ft",
            ParentDistribution = new Uniform(0d, 1d),
            IsUncertain = false,
        };
        uniformHazard.Estimate();

        // Act
        uniformResponse.EstimateWeights(uniformHazard);

        // Assert — the raw sum fell below one and the absorbing branch restored exactly one.
        var expectedUniform = ExpectedVoronoiWeights(uniformHazard.SampleFunction(), uniformLevels, out double rawSum);
        Assert.IsTrue(rawSum < 1d, $"raw sum {rawSum:G17} did not fall below one");
        double uniformSum = 0d;
        for (int j = 0; j < uniformLevels.Length; j++)
        {
            Assert.AreEqual(expectedUniform[j], uniformResponse.SecondaryHazardLevels[j].Weight, 0d, $"uniform weight {j}");
            uniformSum += uniformResponse.SecondaryHazardLevels[j].Weight;
        }
        Assert.AreEqual(1d, uniformSum, 0d);
    }

    /// <summary>Verifies estimation flips the provenance flag and raises both notifications.</summary>
    [TestMethod]
    public void Test_EstimateWeights_SetsUseManualWeightsFalse_AndRaises()
    {
        // Arrange
        var response = Isabella();
        var raised = new List<string>();
        response.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        // Act
        response.EstimateWeights(PoolHazard(1115d, 15d));

        // Assert
        Assert.IsFalse(response.UseManualWeights);
        Assert.IsTrue(raised.Contains(nameof(BivariateResponse.UseManualWeights)));
        Assert.IsTrue(raised.Contains(nameof(BivariateResponse.SecondaryHazardLevels)));
    }

    /// <summary>Verifies the no-arg overload derives from the stored link.</summary>
    [TestMethod]
    public void Test_EstimateWeights_NoArg_UsesStoredLink()
    {
        // Arrange — two identical fixtures, one estimated explicitly, one through the link.
        var hazard = PoolHazard(1112d, 18d);
        var explicitly = Isabella();
        explicitly.EstimateWeights(hazard);
        var throughLink = Isabella();
        throughLink.SecondaryHazardFunction = hazard;

        // Act
        throughLink.EstimateWeights();

        // Assert
        for (int j = 0; j < 6; j++)
        {
            Assert.AreEqual(explicitly.SecondaryHazardLevels[j].Weight, throughLink.SecondaryHazardLevels[j].Weight, 0d);
        }
    }

    /// <summary>
    /// Verifies the no-arg overload throws for a missing, invalid, or bivariate link, and for an
    /// empty level collection.
    /// </summary>
    [TestMethod]
    public void Test_EstimateWeights_NoArg_Throws_WhenLinkMissingInvalidOrBivariate()
    {
        // Arrange
        var noLink = Isabella();
        var invalidLink = Isabella();
        invalidLink.SecondaryHazardFunction = new ParametricUnivariateHazard { ParentDistribution = new Normal(1110d, 20d) };
        var bivariateLink = Isabella();
        bivariateLink.SecondaryHazardFunction = new BivariateHazard { Name = "Joint" };
        var noLevels = Isabella();
        noLevels.SecondaryHazardFunction = PoolHazard(1110d, 20d);
        noLevels.SecondaryHazardLevels.Clear();

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => noLink.EstimateWeights());
        Assert.ThrowsException<InvalidOperationException>(() => invalidLink.EstimateWeights());
        Assert.ThrowsException<InvalidOperationException>(() => bivariateLink.EstimateWeights());
        Assert.ThrowsException<InvalidOperationException>(() => noLevels.EstimateWeights());
    }

    /// <summary>Verifies the explicit overload stores its argument as the link.</summary>
    [TestMethod]
    public void Test_EstimateWeights_Explicit_StoresArgumentAsLink()
    {
        // Arrange
        var response = Isabella();
        var hazard = PoolHazard(1110d, 20d);

        // Act
        response.EstimateWeights(hazard);

        // Assert
        Assert.AreSame(hazard, response.SecondaryHazardFunction);
    }

    /// <summary>
    /// Verifies the explicit overload's guards run before any state change: a null argument, a
    /// bivariate hazard, and an invalid hazard all throw with the link and weights untouched.
    /// </summary>
    [TestMethod]
    public void Test_EstimateWeights_Explicit_GuardsRunBeforeStateChanges()
    {
        // Arrange
        var response = Isabella();
        var weightsBefore = new double[6];
        for (int j = 0; j < 6; j++) weightsBefore[j] = response.SecondaryHazardLevels[j].Weight;

        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => response.EstimateWeights(null!));
        Assert.ThrowsException<InvalidOperationException>(() => response.EstimateWeights(new BivariateHazard { Name = "Joint" }));
        Assert.ThrowsException<InvalidOperationException>(
            () => response.EstimateWeights(new ParametricUnivariateHazard { ParentDistribution = new Normal(0d, 1d) }));
        Assert.IsNull(response.SecondaryHazardFunction);
        Assert.IsTrue(response.UseManualWeights);
        for (int j = 0; j < 6; j++)
        {
            Assert.AreEqual(weightsBefore[j], response.SecondaryHazardLevels[j].Weight, 0d);
        }
    }

    /// <summary>
    /// THE fixed-bug pin: derivation never runs as a load or property-set side effect — setting
    /// the link, toggling the provenance flag, and a resolver-backed round trip all leave the
    /// stored weights bit-identical.
    /// </summary>
    [TestMethod]
    public void Test_NoDerivationOnLoadOrLinkSet_StoredWeightsSurvive()
    {
        // Arrange — stored weights deliberately different from what derivation would produce.
        var response = Isabella();
        var hazard = PoolHazard(1110d, 20d);
        var storedWeights = new double[6];
        for (int j = 0; j < 6; j++) storedWeights[j] = response.SecondaryHazardLevels[j].Weight;

        // Act — the three v1.0 overwrite sites: link set, flag set, and load.
        response.SecondaryHazardFunction = hazard;
        response.UseManualWeights = false;
        var restored = new BivariateResponse(response.ToXElement(), ResolverOver(hazard));

        // Assert — every site leaves the weights bit-identical.
        Assert.AreSame(hazard, restored.SecondaryHazardFunction);
        Assert.IsFalse(restored.UseManualWeights);
        for (int j = 0; j < 6; j++)
        {
            Assert.AreEqual(storedWeights[j], response.SecondaryHazardLevels[j].Weight, 0d);
            Assert.AreEqual(storedWeights[j], restored.SecondaryHazardLevels[j].Weight, 0d);
        }
    }

    #endregion

    #region Stored Link

    /// <summary>
    /// Verifies the subscription swap: edits to the linked hazard re-raise as
    /// <c>SecondaryHazardFunction</c>, and edits to a swapped-out hazard raise nothing.
    /// </summary>
    [TestMethod]
    public void Test_SecondaryHazardFunction_SubscriptionSwap()
    {
        // Arrange
        var response = new BivariateResponse();
        var first = PoolTable("First");
        var second = PoolTable("Second");
        int raised = 0;
        response.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BivariateResponse.SecondaryHazardFunction)) raised++;
        };

        // Act / Assert — assignment raises once, a linked edit re-raises.
        response.SecondaryHazardFunction = first;
        Assert.AreEqual(1, raised);
        first.Description = "edited";
        Assert.AreEqual(2, raised);

        // Act / Assert — after the swap the old link is silent and the new one re-raises.
        response.SecondaryHazardFunction = second;
        Assert.AreEqual(3, raised);
        first.Description = "edited again";
        Assert.AreEqual(3, raised);
        second.Description = "edited";
        Assert.AreEqual(4, raised);
    }

    /// <summary>Verifies a live link serializes its current id and name as the metadata pair.</summary>
    [TestMethod]
    public void Test_LinkAttributes_WriteLiveIdAndName()
    {
        // Arrange
        var hazard = PoolTable();
        var response = Isabella();
        response.SecondaryHazardFunction = hazard;

        // Act
        var element = response.ToXElement();

        // Assert
        Assert.AreEqual(hazard.Id.ToString("D"), element.Attribute("SecondaryHazardFunctionId")?.Value);
        Assert.AreEqual(hazard.Name, element.Attribute("SecondaryHazardFunctionName")?.Value);
    }

    /// <summary>
    /// Verifies the id-first repair: the link re-attaches to the LIVE stored instance by id even
    /// after the function was renamed (the name attribute is stale).
    /// </summary>
    [TestMethod]
    public void Test_LinkRepair_ById_SurvivesRename()
    {
        // Arrange
        var hazard = PoolTable("Original Name");
        var response = Isabella();
        response.SecondaryHazardFunction = hazard;
        var element = response.ToXElement();
        hazard.Name = "Renamed After Serialization";

        // Act
        var restored = new BivariateResponse(element, ResolverOver(hazard));

        // Assert
        Assert.AreSame(hazard, restored.SecondaryHazardFunction);
    }

    /// <summary>
    /// Verifies the lenient repair ladder: a stale id falls back to the name; a miss on both
    /// leaves the link null with no throw and no validation Error; a resolution of the wrong
    /// cluster is a miss; and a resolver-less read keeps the link null.
    /// </summary>
    [TestMethod]
    public void Test_LinkRepair_LenientNameFallback_AndMissNull()
    {
        // Arrange — a serialized link whose id matches nothing in the store.
        var original = PoolTable("Pool Frequency");
        var response = Isabella();
        response.SecondaryHazardFunction = original;
        var element = response.ToXElement();

        // Act / Assert — name fallback finds the equally named replacement instance.
        var replacement = PoolTable("Pool Frequency");
        var byName = new BivariateResponse(element, ResolverOver(replacement));
        Assert.AreSame(replacement, byName.SecondaryHazardFunction);

        // Act / Assert — a miss on both is null, never a throw, and never a validation Error
        // (manual mode also carries no link warning).
        var missed = new BivariateResponse(element, ResolverOver(PoolTable("Unrelated")));
        Assert.IsNull(missed.SecondaryHazardFunction);
        var (isValid, messages) = missed.Validate();
        Assert.IsTrue(isValid);
        Assert.IsFalse(messages.Any(m => m.Contains("secondary hazard function")));

        // Act / Assert — the wrong cluster at the same id is a miss.
        var transform = new TabularTransform { Name = "Pool Frequency" };
        var wrongClusterElement = new XElement(element);
        wrongClusterElement.SetAttributeValue("SecondaryHazardFunctionId", transform.Id.ToString("D"));
        var wrongCluster = new BivariateResponse(wrongClusterElement, ResolverOver(transform));
        Assert.IsNull(wrongCluster.SecondaryHazardFunction);

        // Act / Assert — resolver-less reads keep the link null.
        Assert.IsNull(new BivariateResponse(element).SecondaryHazardFunction);
    }

    /// <summary>
    /// Verifies pending-link retention: an unresolved link's attributes are re-written verbatim
    /// (a resolver-less round trip is bit-equal), a later live assignment replaces them, and
    /// clearing the link removes them.
    /// </summary>
    [TestMethod]
    public void Test_UnresolvedLink_PendingAttributesRewrittenVerbatim()
    {
        // Arrange — serialize with a live link, then reload with no resolver.
        var hazard = PoolTable();
        var original = Isabella();
        original.SecondaryHazardFunction = hazard;
        string serialized = original.ToXElement().ToString();

        // Act
        var restored = new BivariateResponse(XElement.Parse(serialized));

        // Assert — the unresolved pending pair is carried verbatim.
        Assert.IsNull(restored.SecondaryHazardFunction);
        Assert.AreEqual(serialized, restored.ToXElement().ToString());

        // Assert — a live assignment replaces the pending pair with the new function's identity.
        var replacement = PoolTable("Replacement");
        restored.SecondaryHazardFunction = replacement;
        Assert.AreEqual(replacement.Id.ToString("D"), restored.ToXElement().Attribute("SecondaryHazardFunctionId")?.Value);

        // Assert — clearing the link removes the attributes entirely.
        restored.SecondaryHazardFunction = null;
        Assert.IsNull(restored.ToXElement().Attribute("SecondaryHazardFunctionId"));
        Assert.IsNull(restored.ToXElement().Attribute("SecondaryHazardFunctionName"));
    }

    /// <summary>Verifies a bivariate link is a validation error.</summary>
    [TestMethod]
    public void Test_BivariateLink_ValidateError()
    {
        // Arrange
        var response = Isabella();
        response.SecondaryHazardFunction = new BivariateHazard { Name = "Joint Hazard" };

        // Act
        var (isValid, messages) = response.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m =>
            m.StartsWith("Error:") && m.Contains("'Joint Hazard' is bivariate")));
    }

    /// <summary>
    /// Verifies the two automatic-weight staleness warnings: freshly estimated weights are
    /// clean; a sum-preserving perturbation beyond the tolerance warns; an unresolved link with
    /// automatic weights warns that the check cannot run; and manual mode never warns.
    /// </summary>
    [TestMethod]
    public void Test_StalenessWarnings_AutomaticWeights()
    {
        // Arrange
        var hazard = PoolHazard(1112d, 16d);
        var response = Isabella();
        response.EstimateWeights(hazard);

        // Act / Assert — freshly estimated: no staleness messages (the fixture's own zero-row
        // advisory is unrelated).
        var freshMessages = response.Validate().ValidationMessages;
        Assert.IsFalse(freshMessages.Any(m => m.Contains("differ") || m.Contains("unresolved or invalid")));

        // Act / Assert — a sum-preserving perturbation beyond 1e-8 warns "differ".
        response.SecondaryHazardLevels[0].Weight += 1e-3d;
        response.SecondaryHazardLevels[1].Weight -= 1e-3d;
        var (staleValid, staleMessages) = response.Validate();
        Assert.IsTrue(staleValid);
        Assert.IsTrue(staleMessages.Any(m => m.StartsWith("Warning:") && m.Contains("differ")));

        // Act / Assert — automatic weights with an unresolved link warn that the staleness
        // check cannot run.
        var unresolved = Isabella();
        unresolved.UseManualWeights = false;
        var (unresolvedValid, unresolvedMessages) = unresolved.Validate();
        Assert.IsTrue(unresolvedValid);
        Assert.IsTrue(unresolvedMessages.Any(m => m.StartsWith("Warning:") && m.Contains("unresolved or invalid")));

        // Act / Assert — manual mode carries no link messages regardless of the link state.
        var manual = Isabella();
        Assert.IsFalse(manual.Validate().ValidationMessages.Any(m => m.Contains("secondary hazard function")));
    }

    #endregion

    #region Grid Auto-Resize

    /// <summary>Verifies primary adds and removes resize the grid, preserving the overlap and zero-filling new rows.</summary>
    [TestMethod]
    public void Test_GridAutoResize_PrimaryAddRemove_PreservesOverlap()
    {
        // Arrange
        var response = Isabella();
        double preserved = response.ProbabilityValues[3, 5];

        // Act — add a fifth primary level.
        response.PrimaryHazardLevels.Add(1.0d);

        // Assert — 5×6 with the overlap preserved and the new row zero.
        Assert.AreEqual(5, response.ProbabilityValues.GetLength(0));
        Assert.AreEqual(6, response.ProbabilityValues.GetLength(1));
        Assert.AreEqual(preserved, response.ProbabilityValues[3, 5], 0d);
        Assert.AreEqual(0d, response.ProbabilityValues[4, 3], 0d);

        // Act — remove the last two levels.
        response.PrimaryHazardLevels.RemoveAt(4);
        response.PrimaryHazardLevels.RemoveAt(3);

        // Assert — 3×6 with the remaining overlap preserved.
        Assert.AreEqual(3, response.ProbabilityValues.GetLength(0));
        Assert.AreEqual(0.55d, response.ProbabilityValues[2, 4], 0d);
    }

    /// <summary>Verifies secondary adds and removes resize the columns, preserving the overlap.</summary>
    [TestMethod]
    public void Test_GridAutoResize_SecondaryAddRemove_PreservesOverlap()
    {
        // Arrange
        var response = Isabella();

        // Act — add a seventh secondary level.
        response.SecondaryHazardLevels.Add(new WeightedHazardLevel { Level = 1150d, Weight = 0d });

        // Assert — 4×7 with the overlap preserved and the new column zero.
        Assert.AreEqual(4, response.ProbabilityValues.GetLength(0));
        Assert.AreEqual(7, response.ProbabilityValues.GetLength(1));
        Assert.AreEqual(0.75d, response.ProbabilityValues[2, 5], 0d);
        Assert.AreEqual(0d, response.ProbabilityValues[1, 6], 0d);

        // Act — remove the first column's level.
        response.SecondaryHazardLevels.RemoveAt(6);
        response.SecondaryHazardLevels.RemoveAt(5);

        // Assert — 4×5; the overlap copies by INDEX (the exact v1.0 resize).
        Assert.AreEqual(5, response.ProbabilityValues.GetLength(1));
        Assert.AreEqual(0.85d, response.ProbabilityValues[3, 4], 0d);
    }

    /// <summary>
    /// Pins the v1.0 quirk: a Reset action (Clear) leaves the grid untouched, and validation
    /// then reports the dimension disagreement.
    /// </summary>
    [TestMethod]
    public void Test_GridAutoResize_ResetLeavesGridUntouched()
    {
        // Arrange
        var response = Isabella();
        var gridBefore = response.ProbabilityValues;

        // Act
        response.PrimaryHazardLevels.Clear();

        // Assert
        Assert.AreSame(gridBefore, response.ProbabilityValues);
        Assert.AreEqual(4, response.ProbabilityValues.GetLength(0));
        Assert.IsFalse(response.Validate().IsValid);
    }

    #endregion

    #region Joint Surface

    /// <summary>
    /// Verifies known surface points with untransformed axes: the four corners recover their
    /// cells exactly and an interior cell-center query averages its four surrounding cells.
    /// </summary>
    [TestMethod]
    public void Test_SurfaceProbability_KnownPoints()
    {
        // Arrange
        var response = Isabella();

        // Act / Assert — corners.
        Assert.AreEqual(0.00d, response.SurfaceProbability(0.2d, 1090d), 0d);
        Assert.AreEqual(0.10d, response.SurfaceProbability(0.2d, 1140d), 0d);
        Assert.AreEqual(0.15d, response.SurfaceProbability(0.8d, 1090d), 0d);
        Assert.AreEqual(0.95d, response.SurfaceProbability(0.8d, 1140d), 0d);

        // Act / Assert — the center of the first cell averages its four corners:
        // (0 + 0 + 0.01 + 0.02) / 4.
        Assert.AreEqual(0.0075d, response.SurfaceProbability(0.3d, 1095d), 1e-15);
    }

    /// <summary>
    /// Verifies the secondary-axis interpolation transform: under a logarithmic X2, the
    /// geometric midpoint of two levels averages their cells.
    /// </summary>
    [TestMethod]
    public void Test_SurfaceProbability_SecondaryLogTransform_GeometricMidpoint()
    {
        // Arrange — rows identical so the primary coordinate is irrelevant.
        var response = new BivariateResponse();
        Label(response);
        SetLevels(response, new[] { 0d, 1d }, new[] { (100d, 0.5d), (1000d, 0.5d) });
        response.ProbabilityValues = new[,] { { 0.2d, 0.4d }, { 0.2d, 0.4d } };
        response.SecondaryHazardTransform = Transform.Logarithmic;

        // Act / Assert
        Assert.AreEqual(0.3d, response.SurfaceProbability(0.5d, Math.Sqrt(100d * 1000d)), 1e-12);
    }

    /// <summary>
    /// Verifies the [0, 1] clamp lives in <c>SurfaceProbability</c>: cells outside the unit
    /// range (structurally usable, though invalid) clamp to 0 and 1, while the raw interpolator
    /// returns them unclamped.
    /// </summary>
    [TestMethod]
    public void Test_SurfaceProbability_ClampsToUnitRange()
    {
        // Arrange — finite cells outside [0, 1] pass the structural gate; Validate errors them.
        var response = new BivariateResponse();
        Label(response);
        response.ProbabilityValues = new[,] { { 1.5d, -0.5d }, { 0.5d, 0.5d } };
        Assert.IsFalse(response.Validate().IsValid);

        // Act / Assert — the clamp is the response's, not the interpolator's.
        Assert.AreEqual(1d, response.SurfaceProbability(0d, 0d), 0d);
        Assert.AreEqual(0d, response.SurfaceProbability(0d, 1d), 0d);
        Assert.AreEqual(1.5d, response.CreateInterpolator().Interpolate(0d, 0d), 0d);
        Assert.AreEqual(-0.5d, response.CreateInterpolator().Interpolate(0d, 1d), 0d);
    }

    /// <summary>
    /// Pins the extrapolation policy (native bilinear behavior): both coordinates out of range
    /// return the nearest corner cell exactly; one coordinate out clamps to its edge row or
    /// column with one-dimensional interpolation along the in-range axis.
    /// </summary>
    [TestMethod]
    public void Test_SurfaceProbability_ExtrapolationPolicy_CornerAndEdgeClamps()
    {
        // Arrange
        var response = Isabella();

        // Act / Assert — all four corner clamps, exact.
        Assert.AreEqual(0.00d, response.SurfaceProbability(0.1d, 1080d), 0d);
        Assert.AreEqual(0.10d, response.SurfaceProbability(0.1d, 1150d), 0d);
        Assert.AreEqual(0.15d, response.SurfaceProbability(0.9d, 1080d), 0d);
        Assert.AreEqual(0.95d, response.SurfaceProbability(0.9d, 1150d), 0d);

        // Act / Assert — one coordinate out: edge clamp with 1-D interpolation along the
        // in-range axis, at interval midpoints.
        Assert.AreEqual((0.00d + 0.01d) / 2d, response.SurfaceProbability(0.3d, 1080d), 1e-15);
        Assert.AreEqual((0.10d + 0.35d) / 2d, response.SurfaceProbability(0.3d, 1150d), 1e-15);
        Assert.AreEqual((0.00d + 0.00d) / 2d, response.SurfaceProbability(0.1d, 1095d), 1e-15);
        Assert.AreEqual((0.15d + 0.30d) / 2d, response.SurfaceProbability(0.9d, 1095d), 1e-15);
    }

    /// <summary>
    /// Verifies the interpolator discipline: each call returns a fresh instance sharing the
    /// function's surface array by reference, and its values match <c>SurfaceProbability</c>.
    /// </summary>
    [TestMethod]
    public void Test_CreateInterpolator_FreshPerCall_SharesGridArray()
    {
        // Arrange
        var response = Isabella();

        // Act
        var first = response.CreateInterpolator();
        var second = response.CreateInterpolator();

        // Assert — fresh per call; the surface array is shared, never copied.
        Assert.AreNotSame(first, second);
        Assert.AreSame(response.ProbabilityValues, first.YValues);
        Assert.AreEqual(response.SurfaceProbability(0.35d, 1112d), first.Interpolate(0.35d, 1112d), 0d);
    }

    /// <summary>
    /// Verifies the mode asymmetry on a single secondary level: the joint surface refuses
    /// (bilinear needs two levels per axis) while the preserved collapse keeps working.
    /// </summary>
    [TestMethod]
    public void Test_CreateInterpolator_SingleSecondaryLevel_ThrowsWhileCollapseWorks()
    {
        // Arrange
        var response = DecreasingCollapse();

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => response.CreateInterpolator());
        Assert.ThrowsException<InvalidOperationException>(() => response.SurfaceProbability(0.5d, 0d));
        Assert.AreEqual(0.9d, response.SampleFunction().CDF(0d), 1e-12);
    }

    #endregion

    #region Validation Matrix

    /// <summary>Verifies fewer than two primary levels is an error.</summary>
    [TestMethod]
    public void Test_Validate_FewerThanTwoPrimaryLevels_Error()
    {
        // Arrange
        var response = Isabella();
        response.PrimaryHazardLevels.RemoveAt(3);
        response.PrimaryHazardLevels.RemoveAt(2);
        response.PrimaryHazardLevels.RemoveAt(1);

        // Act / Assert
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("at least two primary")));
    }

    /// <summary>Verifies a non-finite primary level is an error (reported before ordering).</summary>
    [TestMethod]
    public void Test_Validate_NonFinitePrimaryLevel_Error()
    {
        // Arrange
        var response = Isabella();
        response.PrimaryHazardLevels[1] = double.NaN;

        // Act / Assert
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("primary hazard levels must all be finite")));
        Assert.IsFalse(messages.Any(m => m.Contains("primary hazard levels must be strictly ascending")));
    }

    /// <summary>Verifies tied or descending primary levels are an error.</summary>
    [TestMethod]
    public void Test_Validate_NonAscendingPrimaryLevels_Error()
    {
        // Arrange
        var response = Isabella();
        response.PrimaryHazardLevels[1] = 0.2d;

        // Act / Assert
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("primary hazard levels must be strictly ascending")));
    }

    /// <summary>Verifies an empty secondary collection is an error.</summary>
    [TestMethod]
    public void Test_Validate_NoSecondaryLevels_Error()
    {
        // Arrange
        var response = Isabella();
        response.SecondaryHazardLevels.Clear();

        // Act / Assert
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("at least one secondary")));
    }

    /// <summary>Verifies a non-finite secondary level is an error.</summary>
    [TestMethod]
    public void Test_Validate_NonFiniteSecondaryLevel_Error()
    {
        // Arrange
        var response = Isabella();
        response.SecondaryHazardLevels[2].Level = double.PositiveInfinity;

        // Act / Assert
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("secondary hazard levels must all be finite")));
    }

    /// <summary>Verifies tied or descending secondary levels are an error.</summary>
    [TestMethod]
    public void Test_Validate_NonAscendingSecondaryLevels_Error()
    {
        // Arrange
        var response = Isabella();
        response.SecondaryHazardLevels[1].Level = 1090d;

        // Act / Assert
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("secondary hazard levels must be strictly ascending")));
    }

    /// <summary>Verifies a grid whose shape disagrees with the level counts is an error.</summary>
    [TestMethod]
    public void Test_Validate_GridDimensionMismatch_Error()
    {
        // Arrange
        var response = Isabella();
        response.ProbabilityValues = new double[4, 5];

        // Act / Assert
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("one row of probabilities")));
    }

    /// <summary>Verifies a non-finite surface cell is an error.</summary>
    [TestMethod]
    public void Test_Validate_NonFiniteCell_Error()
    {
        // Arrange
        var response = Isabella();
        response.ProbabilityValues[2, 3] = double.NaN;

        // Act / Assert
        var (isValid, messages) = response.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("surface probabilities must all be finite")));
    }

    /// <summary>Verifies a surface cell outside [0, 1] is an error, in both directions.</summary>
    [TestMethod]
    public void Test_Validate_CellOutsideUnitRange_Error()
    {
        // Arrange
        var above = Isabella();
        above.ProbabilityValues[0, 0] = 1.5d;
        var below = Isabella();
        below.ProbabilityValues[0, 0] = -0.1d;

        // Act / Assert
        Assert.IsTrue(above.Validate().ValidationMessages.Any(m => m.StartsWith("Error:") && m.Contains("between 0 and 1")));
        Assert.IsTrue(below.Validate().ValidationMessages.Any(m => m.StartsWith("Error:") && m.Contains("between 0 and 1")));
    }

    /// <summary>Verifies a weight that is negative, above one, or non-finite is an error.</summary>
    [TestMethod]
    public void Test_Validate_WeightOutOfRange_Error()
    {
        foreach (double bad in new[] { -0.05d, 1.05d, double.NaN })
        {
            // Arrange
            var response = Isabella();
            response.SecondaryHazardLevels[0].Weight = bad;

            // Act / Assert
            var (isValid, messages) = response.Validate();
            Assert.IsFalse(isValid, $"weight {bad}");
            Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("between 0 and 1")), $"weight {bad}");
        }
    }

    /// <summary>
    /// Verifies the weight-sum rule: a sum off by more than 1e-8 is an error, while a sub-ulp
    /// disagreement inside the tolerance passes.
    /// </summary>
    [TestMethod]
    public void Test_Validate_WeightSum_ErrorAndToleranceBand()
    {
        // Arrange — total 0.9.
        var offSum = Isabella();
        offSum.SecondaryHazardLevels[3].Weight = 0.2d;

        // Act / Assert
        var (isValid, messages) = offSum.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("sum to 1")));

        // Arrange — a 5e-9 disagreement stays inside the pinned tolerance.
        var withinTolerance = Isabella();
        withinTolerance.SecondaryHazardLevels[3].Weight += 5e-9d;

        // Act / Assert
        Assert.IsFalse(withinTolerance.Validate().ValidationMessages.Any(m => m.Contains("sum to 1")));
    }

    /// <summary>
    /// Verifies the logarithmic primary-axis guard is the exact v1.0 below-zero rule: a negative
    /// level errors, a zero level is legal (the Numerics log floors it).
    /// </summary>
    [TestMethod]
    public void Test_Validate_LogHazardTransform_NegativePrimary_Error()
    {
        // Arrange — the labeled default axis {0, 1} under a log transform: legal.
        var zeroLegal = new BivariateResponse();
        Label(zeroLegal);
        zeroLegal.HazardTransform = Transform.Logarithmic;

        // Act / Assert
        Assert.IsTrue(zeroLegal.Validate().IsValid);

        // Arrange — a negative primary level errors.
        var negative = new BivariateResponse();
        Label(negative);
        negative.PrimaryHazardLevels[0] = -1d;
        negative.HazardTransform = Transform.Logarithmic;

        // Act / Assert
        var (isValid, messages) = negative.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("hazard interpolation transform cannot be logarithmic")));
    }

    /// <summary>Verifies the normal-Z primary-axis guard: values outside [0, 1] error, the unit axis is legal.</summary>
    [TestMethod]
    public void Test_Validate_NormalZHazardTransform_OutsideUnit_Error()
    {
        // Arrange — the default {0, 1} axis under normal-Z: legal (endpoints included).
        var unitLegal = new BivariateResponse();
        Label(unitLegal);
        unitLegal.HazardTransform = Transform.NormalZ;

        // Act / Assert
        Assert.IsTrue(unitLegal.Validate().IsValid);

        // Arrange — an axis reaching past one errors.
        var outside = Isabella();
        outside.HazardTransform = Transform.NormalZ;

        // Act / Assert — Isabella's primary axis is within [0, 1]; push a level outside.
        outside.PrimaryHazardLevels[3] = 1.2d;
        var (isValid, messages) = outside.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("hazard interpolation transform cannot be normal Z")));
    }

    /// <summary>Verifies the secondary-axis transform guards (logarithmic and normal-Z).</summary>
    [TestMethod]
    public void Test_Validate_SecondaryTransformGuards()
    {
        // Arrange — a negative secondary level under a log transform.
        var logNegative = new BivariateResponse();
        Label(logNegative);
        logNegative.SecondaryHazardLevels[0].Level = -5d;
        logNegative.SecondaryHazardTransform = Transform.Logarithmic;

        // Act / Assert
        var (logValid, logMessages) = logNegative.Validate();
        Assert.IsFalse(logValid);
        Assert.IsTrue(logMessages.Any(m => m.StartsWith("Error:") && m.Contains("secondary hazard interpolation transform cannot be logarithmic")));

        // Arrange — Isabella's pool elevations under normal-Z are far outside [0, 1].
        var zOutside = Isabella();
        zOutside.SecondaryHazardTransform = Transform.NormalZ;

        // Act / Assert
        var (zValid, zMessages) = zOutside.Validate();
        Assert.IsFalse(zValid);
        Assert.IsTrue(zMessages.Any(m => m.StartsWith("Error:") && m.Contains("secondary hazard interpolation transform cannot be normal Z")));
    }

    /// <summary>
    /// Verifies the probability-axis transforms are legal over unit-range cells, including the
    /// exact 0 and 1 endpoints (the saturated-surface rule: Numerics clamps 0 finite and sends 1
    /// to positive infinity in z-space).
    /// </summary>
    [TestMethod]
    public void Test_Validate_ProbabilityTransformGuards_UnitCellsLegal()
    {
        // Arrange — a saturated surface spanning exactly [0, 1].
        var response = new BivariateResponse();
        Label(response);
        response.ProbabilityValues = new[,] { { 0d, 0d }, { 1d, 1d } };

        // Act / Assert — both transforms legal over [0, 1] cells.
        response.ProbabilityTransform = Transform.Logarithmic;
        Assert.IsTrue(response.Validate().IsValid);
        response.ProbabilityTransform = Transform.NormalZ;
        Assert.IsTrue(response.Validate().IsValid);
    }

    /// <summary>
    /// Verifies the zero-row advisory (exact v1.0 condition): a first collapsed ordinate above
    /// 1e-8 warns; the zero-first-row Isabella fixture does not.
    /// </summary>
    [TestMethod]
    public void Test_Validate_ZeroRowWarning_ExactV10Condition()
    {
        // Arrange — Isabella's first collapsed ordinate is 0.0305.
        var warned = Isabella();

        // Act / Assert
        var warnedMessages = warned.Validate().ValidationMessages;
        Assert.IsTrue(warnedMessages.Any(m => m.StartsWith("Warning:") && m.Contains("Any hazard level evaluated below")));

        // Arrange — the labeled zero default collapses to zero everywhere.
        var clean = new BivariateResponse();
        Label(clean);

        // Act / Assert
        Assert.IsFalse(clean.Validate().ValidationMessages.Any(m => m.Contains("Any hazard level evaluated below")));
    }

    /// <summary>Verifies a non-monotone collapse is a warning ONLY — the function stays valid.</summary>
    [TestMethod]
    public void Test_Validate_NonMonotoneCollapse_WarningOnly()
    {
        // Act
        var (isValid, messages) = DecreasingCollapse().Validate();

        // Assert
        Assert.IsTrue(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Warning:") && m.Contains("not monotonically increasing")));
    }

    /// <summary>Verifies the single-secondary-level degeneracy warning.</summary>
    [TestMethod]
    public void Test_Validate_SingleSecondaryLevel_Warning()
    {
        // Act
        var messages = DecreasingCollapse().Validate().ValidationMessages;

        // Assert
        Assert.IsTrue(messages.Any(m => m.StartsWith("Warning:") && m.Contains("single secondary hazard level")));
    }

    #endregion

    #region Serialization

    /// <summary>
    /// Pins the legacy payload shape inside the v1.1 attribute envelope: pipe-joined
    /// <c>PrimaryHazardLevels</c>, <c>WeightedHazardLevel</c> children, and
    /// <c>Probability_Row</c> rows in that child order.
    /// </summary>
    [TestMethod]
    public void Test_ToXElement_LegacyChildShape_Pinned()
    {
        // Arrange
        var response = Isabella();
        response.HazardTransform = Transform.Logarithmic;

        // Act
        var element = response.ToXElement();

        // Assert — envelope.
        Assert.AreEqual(nameof(BivariateResponse), element.Name.LocalName);
        Assert.AreEqual("Isabella Fragility", element.Attribute("Name")?.Value);
        Assert.AreEqual("Logarithmic", element.Attribute("HazardTransform")?.Value);
        Assert.AreEqual("None", element.Attribute("SecondaryHazardTransform")?.Value);
        Assert.AreEqual("True", element.Attribute("UseManualWeights")?.Value);
        Assert.IsNull(element.Attribute("SecondaryHazardFunctionId"));
        Assert.IsNull(element.Attribute("FunctionType"));

        // Assert — the exact legacy child order and names.
        var children = element.Elements().ToArray();
        Assert.AreEqual(3, children.Length);
        Assert.AreEqual("PrimaryHazardLevels", children[0].Name.LocalName);
        Assert.AreEqual("SecondaryHazardLevels", children[1].Name.LocalName);
        Assert.AreEqual("ProbabilityValues", children[2].Name.LocalName);
        Assert.AreEqual("0.20000000000000001|0.40000000000000002|0.59999999999999998|0.80000000000000004", children[0].Value);
        Assert.AreEqual(6, children[1].Elements("WeightedHazardLevel").Count());
        Assert.AreEqual(4, children[2].Elements("Probability_Row").Count());
        Assert.AreEqual("0|0|0.01|0.02|0.050000000000000003|0.10000000000000001",
            children[2].Elements("Probability_Row").First().Value);
    }

    /// <summary>Verifies a full round trip is bit-equal and preserves every property.</summary>
    [TestMethod]
    public void Test_XmlRoundTrip_BitEqual()
    {
        // Arrange
        var original = Isabella();
        original.HazardTransform = Transform.Logarithmic;
        original.SecondaryHazardTransform = Transform.Logarithmic;
        original.ProbabilityTransform = Transform.NormalZ;
        original.UseManualWeights = false;
        original.Description = "The golden-example surface.";

        // Act
        var restored = new BivariateResponse(original.ToXElement());

        // Assert — bit-equal re-serialization and full property equality.
        Assert.AreEqual(original.ToXElement().ToString(), restored.ToXElement().ToString());
        Assert.AreEqual(original.Id, restored.Id);
        Assert.AreEqual(original.Name, restored.Name);
        Assert.AreEqual(original.Description, restored.Description);
        Assert.AreEqual(original.SpecifiedHazard, restored.SpecifiedHazard);
        Assert.AreEqual(original.SecondarySpecifiedHazard, restored.SecondarySpecifiedHazard);
        Assert.AreEqual(original.HazardTransform, restored.HazardTransform);
        Assert.AreEqual(original.SecondaryHazardTransform, restored.SecondaryHazardTransform);
        Assert.AreEqual(original.ProbabilityTransform, restored.ProbabilityTransform);
        Assert.AreEqual(original.UseManualWeights, restored.UseManualWeights);
        CollectionAssert.AreEqual(original.PrimaryHazardLevels, restored.PrimaryHazardLevels);
        Assert.AreEqual(6, restored.SecondaryLevelCount);
        for (int j = 0; j < 6; j++)
        {
            Assert.AreEqual(original.SecondaryHazardLevels[j].Level, restored.SecondaryHazardLevels[j].Level, 0d);
            Assert.AreEqual(original.SecondaryHazardLevels[j].Weight, restored.SecondaryHazardLevels[j].Weight, 0d);
        }
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 6; j++)
            {
                Assert.AreEqual(original.ProbabilityValues[i, j], restored.ProbabilityValues[i, j], 0d);
            }
        }
    }

    /// <summary>
    /// Verifies a dropped surface row reconstructs to the parsed 3×6 shape (never re-fitted to
    /// the level counts) and fails validation on the dimension rule.
    /// </summary>
    [TestMethod]
    public void Test_XmlRoundTrip_DroppedRow_PreservedAndInvalid()
    {
        // Arrange
        var element = Isabella().ToXElement();
        element.Element("ProbabilityValues")!.Elements("Probability_Row").Last().Remove();

        // Act
        var restored = new BivariateResponse(element);

        // Assert
        Assert.AreEqual(3, restored.ProbabilityValues.GetLength(0));
        Assert.AreEqual(6, restored.ProbabilityValues.GetLength(1));
        Assert.AreEqual(4, restored.PrimaryHazardLevels.Count);
        var (isValid, messages) = restored.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("one row of probabilities")));
    }

    /// <summary>
    /// Verifies a shortened surface row keeps its parsed cells with NaN fill (never fabricated
    /// zeros) and fails validation on the finiteness rule.
    /// </summary>
    [TestMethod]
    public void Test_XmlRoundTrip_ShortenedRow_NaNFillAndInvalid()
    {
        // Arrange
        var element = Isabella().ToXElement();
        element.Element("ProbabilityValues")!.Elements("Probability_Row").First().Value = "0.5|0.25";

        // Act
        var restored = new BivariateResponse(element);

        // Assert
        Assert.AreEqual(4, restored.ProbabilityValues.GetLength(0));
        Assert.AreEqual(6, restored.ProbabilityValues.GetLength(1));
        Assert.AreEqual(0.5d, restored.ProbabilityValues[0, 0], 0d);
        Assert.AreEqual(0.25d, restored.ProbabilityValues[0, 1], 0d);
        Assert.IsTrue(double.IsNaN(restored.ProbabilityValues[0, 2]));
        var (isValid, messages) = restored.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("surface probabilities must all be finite")));
    }

    /// <summary>
    /// Verifies a bare envelope reads permissively: empty collections, an empty grid, the
    /// manual-weight default — reported by validation, never a throw.
    /// </summary>
    [TestMethod]
    public void Test_XmlRoundTrip_MissingChildren_Permissive()
    {
        // Act
        var restored = new BivariateResponse(new XElement(nameof(BivariateResponse)));

        // Assert
        Assert.AreEqual(0, restored.PrimaryHazardLevels.Count);
        Assert.AreEqual(0, restored.SecondaryLevelCount);
        Assert.AreEqual(0, restored.ProbabilityValues.GetLength(0));
        Assert.IsTrue(restored.UseManualWeights);
        Assert.IsFalse(restored.Validate().IsValid);
        Assert.ThrowsException<ArgumentNullException>(() => new BivariateResponse(null!));
    }

    /// <summary>
    /// Verifies the factory case threads the resolver: a factory reconstruction re-attaches the
    /// stored link to the live instance.
    /// </summary>
    [TestMethod]
    public void Test_Factory_RoundTrip_ThreadsResolver()
    {
        // Arrange
        var hazard = PoolTable();
        var original = Isabella();
        original.SecondaryHazardFunction = hazard;

        // Act
        var restored = RiskFunctionFactory.CreateFromXElement(original.ToXElement(), ResolverOver(hazard));

        // Assert
        var response = (BivariateResponse)restored!;
        Assert.AreSame(hazard, response.SecondaryHazardFunction);
        Assert.AreEqual(original.Id, response.Id);
    }

    #endregion

    #region Canonical Hash

    /// <summary>
    /// Verifies metadata and provenance edits never move the hash: rename, re-describe, re-id,
    /// all four axis labels, the manual/automatic flag, and the stored link in every state
    /// (set, renamed, re-identified, cleared).
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataAndProvenanceInert()
    {
        // Arrange
        var response = Isabella();
        string baseline = HashOf(response);

        // Act / Assert — identity metadata.
        response.Name = "Renamed";
        response.Description = "Re-described";
        response.AssignNewId();
        Assert.AreEqual(baseline, HashOf(response));

        // Act / Assert — all four axis labels.
        response.SpecifiedHazard = "Different Hazard";
        response.HazardUnit = "different";
        response.SecondarySpecifiedHazard = "Different Secondary";
        response.SecondaryHazardUnit = "other";
        Assert.AreEqual(baseline, HashOf(response));

        // Act / Assert — weight provenance: the flag and the link in every state.
        response.UseManualWeights = false;
        Assert.AreEqual(baseline, HashOf(response));
        var link = PoolTable();
        response.SecondaryHazardFunction = link;
        Assert.AreEqual(baseline, HashOf(response));
        link.Name = "Renamed Link";
        link.AssignNewId();
        Assert.AreEqual(baseline, HashOf(response));
        response.SecondaryHazardFunction = null;
        Assert.AreEqual(baseline, HashOf(response));
    }

    /// <summary>
    /// THE provenance-link hash pin: the linked hazard's CONTENT never enters the response's hash —
    /// editing the linked hazard re-rolls nothing until weights are explicitly re-estimated.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_LinkedHazardContentEdit_Inert()
    {
        // Arrange
        var link = PoolTable();
        var response = Isabella();
        response.SecondaryHazardFunction = link;
        string baseline = HashOf(response);
        string linkBaseline = HashOf(link);

        // Act — a genuine compute edit on the linked hazard.
        link.UncertaintyValue = FunctionUncertainty.Hazard;

        // Assert — the link's own hash moved; the response's did not.
        Assert.AreNotEqual(linkBaseline, HashOf(link));
        Assert.AreEqual(baseline, HashOf(response));
    }

    /// <summary>
    /// Verifies every compute surface moves the hash: a primary level, a secondary level, a
    /// WEIGHT (compute content even in joint mode, where it is inert — mode is external state),
    /// a surface cell, and each of the three interpolation transforms.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_ComputeEdits_Move()
    {
        // Arrange
        string baseline = HashOf(Isabella());

        // Act / Assert — one isolated edit per fixture instance.
        var primaryEdit = Isabella();
        primaryEdit.PrimaryHazardLevels[0] = 0.1d;
        Assert.AreNotEqual(baseline, HashOf(primaryEdit));

        var secondaryLevelEdit = Isabella();
        secondaryLevelEdit.SecondaryHazardLevels[0].Level = 1091d;
        Assert.AreNotEqual(baseline, HashOf(secondaryLevelEdit));

        var weightEdit = Isabella();
        weightEdit.SecondaryHazardLevels[0].Weight = 0.06d;
        Assert.AreNotEqual(baseline, HashOf(weightEdit));

        var cellEdit = Isabella();
        cellEdit.ProbabilityValues[0, 0] = 0.001d;
        Assert.AreNotEqual(baseline, HashOf(cellEdit));

        var hazardTransformEdit = Isabella();
        hazardTransformEdit.HazardTransform = Transform.Logarithmic;
        Assert.AreNotEqual(baseline, HashOf(hazardTransformEdit));

        var secondaryTransformEdit = Isabella();
        secondaryTransformEdit.SecondaryHazardTransform = Transform.Logarithmic;
        Assert.AreNotEqual(baseline, HashOf(secondaryTransformEdit));

        var probabilityTransformEdit = Isabella();
        probabilityTransformEdit.ProbabilityTransform = Transform.NormalZ;
        Assert.AreNotEqual(baseline, HashOf(probabilityTransformEdit));
    }

    #endregion
}
