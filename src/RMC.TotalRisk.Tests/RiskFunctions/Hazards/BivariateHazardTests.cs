using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using Numerics.Distributions.Copulas;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components;
using RMC.TotalRisk.Systems.Components.Graph;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.RiskFunctions.Hazards;

/// <summary>
/// Unit tests for <see cref="BivariateHazard"/> — the defaults and coercions, the observable
/// marginal-link wiring, the nine-rule validation matrix, composite-forward-rule child seeding with
/// exact seed assertions, the primary/secondary delegation surfaces, the per-realization bivariate
/// snapshot, dual-mode serialization over the shared function-entry contract, and the
/// projected-identity hash with its mode/metadata inertness and compute sensitivities.
/// </summary>
[TestClass]
public class BivariateHazardTests
{
    #region Fixtures

    /// <summary>The declared primary-axis labels shared by these tests.</summary>
    private const string PrimaryHazard = "Peak Ground Acceleration";

    /// <summary>The declared primary-axis unit shared by these tests.</summary>
    private const string PrimaryUnit = "g";

    /// <summary>The declared secondary-axis labels shared by these tests.</summary>
    private const string SecondaryHazard = "Pool Duration";

    /// <summary>The declared secondary-axis unit shared by these tests.</summary>
    private const string SecondaryUnit = "days";

    /// <summary>Applies the four declared axis labels.</summary>
    private static void Label(BivariateHazard hazard)
    {
        hazard.SpecifiedHazard = PrimaryHazard;
        hazard.HazardUnit = PrimaryUnit;
        hazard.SecondarySpecifiedHazard = SecondaryHazard;
        hazard.SecondaryHazardUnit = SecondaryUnit;
    }

    /// <summary>
    /// Builds a deterministic parametric marginal over a Uniform parent — the identity quantile
    /// function on [min, max], so discretization and delegation values are exact.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="min">The uniform minimum.</param>
    /// <param name="max">The uniform maximum.</param>
    /// <param name="primaryAxis">True to label with the primary pair, false the secondary pair.</param>
    /// <returns>The marginal.</returns>
    private static ParametricUnivariateHazard UniformMarginal(string name, double min, double max, bool primaryAxis)
    {
        var marginal = new ParametricUnivariateHazard
        {
            Name = name,
            ParentDistribution = new Uniform(min, max),
            IsUncertain = false,
            SpecifiedHazard = primaryAxis ? PrimaryHazard : SecondaryHazard,
            HazardUnit = primaryAxis ? PrimaryUnit : SecondaryUnit,
        };
        marginal.Estimate();
        return marginal;
    }

    /// <summary>
    /// Builds a sampler-driven marginal: a hazard-uncertain tabular curve (D = 1) whose draws come
    /// from its own content-seeded sampler, so ordinal-in-seed independence is observable.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="lowMean">The mean hazard at the frequent (0.999) exceedance ordinate.</param>
    /// <param name="highMean">The mean hazard at the rare (0.001) exceedance ordinate.</param>
    /// <param name="sd">The ordinate standard deviation.</param>
    /// <param name="primaryAxis">True to label with the primary pair, false the secondary pair.</param>
    /// <returns>The marginal.</returns>
    private static TabularHazard TabularMarginal(string name, double lowMean, double highMean, double sd, bool primaryAxis)
    {
        return new TabularHazard
        {
            Name = name,
            UncertaintyValue = FunctionUncertainty.Hazard,
            HazardUncertainFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Normal(lowMean, sd)),
                    new UncertainOrdinate(0.001d, new Normal(highMean, sd)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Normal),
            SpecifiedHazard = primaryAxis ? PrimaryHazard : SecondaryHazard,
            HazardUnit = primaryAxis ? PrimaryUnit : SecondaryUnit,
        };
    }

    /// <summary>
    /// Builds the standard valid fixture: uniform X on [0, 4], uniform Y on [0, 30], independence
    /// copula, default bins, all four labels declared.
    /// </summary>
    /// <returns>The configured bivariate hazard.</returns>
    private static BivariateHazard Configured()
    {
        var hazard = new BivariateHazard(
            UniformMarginal("PGA Frequency", 0d, 4d, primaryAxis: true),
            UniformMarginal("Pool Duration Curve", 0d, 30d, primaryAxis: false))
        {
            Name = "Seismic-Pool Coupling",
        };
        Label(hazard);
        return hazard;
    }

    /// <summary>Collects the property names an instance raises.</summary>
    /// <param name="hazard">The instance under observation.</param>
    /// <returns>The live list of raised names.</returns>
    private static List<string> Observe(BivariateHazard hazard)
    {
        var raised = new List<string>();
        hazard.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    /// <summary>A resolver over the given stored functions (id authoritative, name lenient).</summary>
    /// <param name="functions">The stored live instances.</param>
    /// <returns>The resolver.</returns>
    private static RiskFunctionResolver Resolver(params IRiskFunction[] functions)
    {
        return new RiskFunctionResolver(
            id => functions.FirstOrDefault(f => f.Id == id),
            name => functions.FirstOrDefault(f => f.Name == name));
    }

    #endregion

    #region Defaults, coercions, and notification

    /// <summary>Pins the default-constructor state.</summary>
    [TestMethod]
    public void Test_DefaultConstructor_Defaults()
    {
        // Act
        var hazard = new BivariateHazard();

        // Assert
        Assert.IsInstanceOfType<IndependenceCopula>(hazard.Copula);
        Assert.AreEqual(20, hazard.SecondaryIntegrationBins);
        Assert.AreEqual(21, hazard.ConditionalNodeCount);
        Assert.IsNull(hazard.MarginalX);
        Assert.IsNull(hazard.MarginalY);
        Assert.AreEqual(HazardFunctionType.Bivariate, hazard.FunctionType);
        Assert.IsTrue(hazard.IsDeterministic);
        Assert.AreEqual(0, hazard.SamplingDimensions);
        Assert.AreEqual(string.Empty, hazard.SecondarySpecifiedHazard);
        Assert.AreEqual(string.Empty, hazard.SecondaryHazardUnit);
        Assert.AreEqual(3, BivariateHazard.MinimumSecondaryIntegrationBins);
        Assert.AreEqual(1000, BivariateHazard.MaximumSecondaryIntegrationBins);
    }

    /// <summary>Verifies the convenience constructor assigns marginals and the optional copula.</summary>
    [TestMethod]
    public void Test_ConvenienceConstructor_AssignsMarginalsAndCopula()
    {
        // Arrange
        var x = UniformMarginal("X", 0d, 1d, primaryAxis: true);
        var y = UniformMarginal("Y", 0d, 1d, primaryAxis: false);

        // Act
        var defaulted = new BivariateHazard(x, y);
        var clayton = new BivariateHazard(x, y, new ClaytonCopula(2d));

        // Assert
        Assert.AreSame(x, defaulted.MarginalX);
        Assert.AreSame(y, defaulted.MarginalY);
        Assert.IsInstanceOfType<IndependenceCopula>(defaulted.Copula);
        Assert.IsInstanceOfType<ClaytonCopula>(clayton.Copula);
        Assert.AreEqual(2d, clayton.CopulaTheta);
    }

    /// <summary>Verifies assigning a null copula coerces to a fresh independence copula.</summary>
    [TestMethod]
    public void Test_Copula_NullAssignment_CoercesToIndependence()
    {
        // Arrange
        var hazard = new BivariateHazard { Copula = new ClaytonCopula(2d) };
        var raised = Observe(hazard);

        // Act
        hazard.Copula = null!;

        // Assert
        Assert.IsInstanceOfType<IndependenceCopula>(hazard.Copula);
        CollectionAssert.Contains(raised, nameof(BivariateHazard.Copula));
    }

    /// <summary>Verifies every own property raises change notification, and same-value writes raise nothing.</summary>
    [TestMethod]
    public void Test_PropertyChanges_RaiseNotifications()
    {
        // Arrange
        var hazard = new BivariateHazard { Copula = new ClaytonCopula(2d) };
        var x = UniformMarginal("X", 0d, 1d, primaryAxis: true);
        var raised = Observe(hazard);

        // Act
        hazard.MarginalX = x;
        hazard.MarginalY = UniformMarginal("Y", 0d, 1d, primaryAxis: false);
        hazard.Copula = new NormalCopula(0.5d);
        hazard.CopulaTheta = 0.7d;
        hazard.SecondarySpecifiedHazard = SecondaryHazard;
        hazard.SecondaryHazardUnit = SecondaryUnit;
        hazard.SecondaryIntegrationBins = 40;

        // Assert
        CollectionAssert.AreEqual(new[]
        {
            nameof(BivariateHazard.MarginalX),
            nameof(BivariateHazard.MarginalY),
            nameof(BivariateHazard.Copula),
            nameof(BivariateHazard.CopulaTheta),
            nameof(BivariateHazard.SecondarySpecifiedHazard),
            nameof(BivariateHazard.SecondaryHazardUnit),
            nameof(BivariateHazard.SecondaryIntegrationBins),
        }, raised);

        // Act — same-value writes.
        raised.Clear();
        hazard.MarginalX = x;
        hazard.CopulaTheta = 0.7d;
        hazard.SecondarySpecifiedHazard = SecondaryHazard;
        hazard.SecondaryHazardUnit = SecondaryUnit;
        hazard.SecondaryIntegrationBins = 40;

        // Assert
        Assert.AreEqual(0, raised.Count);
    }

    /// <summary>
    /// Verifies the marginal subscription semantics: edits to a linked marginal re-raise as the
    /// link property, and edits to a swapped-out marginal raise nothing.
    /// </summary>
    [TestMethod]
    public void Test_MarginalSubscription_SwapSemantics()
    {
        // Arrange
        var first = UniformMarginal("First", 0d, 1d, primaryAxis: true);
        var second = UniformMarginal("Second", 0d, 1d, primaryAxis: true);
        var hazard = new BivariateHazard(first, UniformMarginal("Y", 0d, 1d, primaryAxis: false));
        var raised = Observe(hazard);

        // Act — an edit on the linked marginal bubbles as the link property.
        first.Description = "Updated study.";

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(BivariateHazard.MarginalX) }, raised);

        // Act — swap the link, then edit the old marginal.
        hazard.MarginalX = second;
        raised.Clear();
        first.Description = "No longer wired.";

        // Assert
        Assert.AreEqual(0, raised.Count);

        // Act — the current marginal still bubbles.
        second.Description = "Still wired.";

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(BivariateHazard.MarginalX) }, raised);
    }

    /// <summary>Verifies the θ passthrough reads and writes the copula's own parameter.</summary>
    [TestMethod]
    public void Test_CopulaTheta_PassesThroughToCopula()
    {
        // Arrange
        var copula = new ClaytonCopula(2d);
        var hazard = new BivariateHazard { Copula = copula };

        // Act
        hazard.CopulaTheta = 3.5d;

        // Assert
        Assert.AreEqual(3.5d, copula.Theta);
        Assert.AreEqual(3.5d, hazard.CopulaTheta);
    }

    #endregion

    #region Validation

    /// <summary>Rule 1: all four axis labels are required.</summary>
    [TestMethod]
    public void Test_Validate_EmptyLabels_Errors()
    {
        // Arrange — valid links, no labels.
        var hazard = new BivariateHazard(
            UniformMarginal("X", 0d, 1d, primaryAxis: true),
            UniformMarginal("Y", 0d, 1d, primaryAxis: false));

        // Act
        var (isValid, messages) = hazard.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Contains("Error: The bivariate hazard function does not have a specified primary hazard type."));
        Assert.IsTrue(messages.Contains("Error: The bivariate hazard function does not have a specified primary hazard unit."));
        Assert.IsTrue(messages.Contains("Error: The bivariate hazard function does not have a specified secondary hazard type."));
        Assert.IsTrue(messages.Contains("Error: The bivariate hazard function does not have a specified secondary hazard unit."));
    }

    /// <summary>Rule 2: a serialized reference that could not be resolved is reported precisely.</summary>
    [TestMethod]
    public void Test_Validate_UnresolvedReference_Errors()
    {
        // Arrange — a by-reference form read with no resolver records both links unresolved.
        var configured = Configured();
        var restored = new BivariateHazard(configured.ToXElement(RiskSerializationMode.ByReference));

        // Act
        var (isValid, messages) = restored.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal)
            && m.Contains("marginal X hazard function 'PGA Frequency'") && m.Contains("was not found")));
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal)
            && m.Contains("marginal Y hazard function 'Pool Duration Curve'") && m.Contains("was not found")));
    }

    /// <summary>Rule 3: undefined marginals are errors.</summary>
    [TestMethod]
    public void Test_Validate_NullMarginals_Errors()
    {
        // Arrange
        var hazard = new BivariateHazard();
        Label(hazard);

        // Act
        var (isValid, messages) = hazard.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Contains("Error: The marginal X hazard function has not been defined for the bivariate hazard."));
        Assert.IsTrue(messages.Contains("Error: The marginal Y hazard function has not been defined for the bivariate hazard."));
    }

    /// <summary>Rule 4: a bivariate marginal is rejected loudly (nested dependence structures are unsupported).</summary>
    [TestMethod]
    public void Test_Validate_BivariateMarginal_Error()
    {
        // Arrange — a second bivariate hazard as the X marginal.
        var nested = Configured();
        nested.Name = "Nested";
        var hazard = new BivariateHazard(nested, UniformMarginal("Y", 0d, 1d, primaryAxis: false));
        Label(hazard);

        // Act
        var (isValid, messages) = hazard.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Contains(
            "Error: The marginal X hazard function 'Nested' must be a univariate hazard function — nested bivariate marginals are not supported."));
    }

    /// <summary>Rule 5: the two marginals must be distinct instances.</summary>
    [TestMethod]
    public void Test_Validate_SameInstanceMarginals_Error()
    {
        // Arrange
        var shared = UniformMarginal("Shared", 0d, 1d, primaryAxis: true);
        var hazard = new BivariateHazard(shared, shared);
        Label(hazard);

        // Act
        var (isValid, messages) = hazard.Validate();

        // Assert — two equal-content instances are legal; ONE instance is not.
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error: The marginal X and marginal Y hazard functions reference the same function instance", StringComparison.Ordinal)));

        var distinct = new BivariateHazard(
            UniformMarginal("Twin", 0d, 1d, primaryAxis: true),
            UniformMarginal("Twin", 0d, 1d, primaryAxis: false));
        Label(distinct);
        Assert.IsFalse(distinct.Validate().ValidationMessages.Any(m => m.Contains("same function instance")));
    }

    /// <summary>Rule 6: an invalid marginal is reported as a summary line.</summary>
    [TestMethod]
    public void Test_Validate_InvalidMarginal_Error()
    {
        // Arrange — a default tabular hazard carries no table and is invalid on its own.
        var hazard = new BivariateHazard(
            new TabularHazard { Name = "Empty Curve" },
            UniformMarginal("Y", 0d, 1d, primaryAxis: false));
        Label(hazard);

        // Act
        var (isValid, messages) = hazard.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Contains("Error: The selected marginal X hazard function 'Empty Curve' is invalid."));
    }

    /// <summary>Rule 7: invalid copula parameters are errors, surfacing the copula's own diagnostic.</summary>
    [TestMethod]
    public void Test_Validate_InvalidCopulaParameters_Error()
    {
        // Arrange
        var hazard = Configured();
        hazard.Copula = new ClaytonCopula(2d);
        hazard.CopulaTheta = double.NaN;

        // Act
        var (isValid, messages) = hazard.Validate();

        // Assert
        Assert.IsFalse(hazard.Copula.ParametersValid);
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error: The copula parameters are invalid.", StringComparison.Ordinal)));
    }

    /// <summary>Rule 8: the bin count validates on [3, 1000] with no silent clamp.</summary>
    [TestMethod]
    public void Test_Validate_BinsRange()
    {
        // Arrange
        var hazard = Configured();

        // Act / Assert — both edges of invalid.
        foreach (int invalid in new[] { 2, 1001 })
        {
            hazard.SecondaryIntegrationBins = invalid;
            var (isValid, messages) = hazard.Validate();
            Assert.IsFalse(isValid, $"bins = {invalid}");
            Assert.IsTrue(messages.Contains("Error: The number of secondary integration bins must be between 3 and 1000."));
            Assert.AreEqual(invalid, hazard.SecondaryIntegrationBins, "No silent clamp.");
        }

        // Act / Assert — both edges of valid.
        foreach (int valid in new[] { 3, 1000 })
        {
            hazard.SecondaryIntegrationBins = valid;
            Assert.IsTrue(hazard.Validate().IsValid, $"bins = {valid}");
        }
    }

    /// <summary>Rule 9: marginal axis labels that do not match the declared pairs warn without invalidating.</summary>
    [TestMethod]
    public void Test_Validate_LabelMismatch_Warnings()
    {
        // Arrange — the marginals' own labels disagree with the declared axis pairs.
        var hazard = new BivariateHazard(
            UniformMarginal("X", 0d, 1d, primaryAxis: false),
            UniformMarginal("Y", 0d, 1d, primaryAxis: true));
        Label(hazard);

        // Act
        var (isValid, messages) = hazard.Validate();

        // Assert — four warnings (type and unit per axis), still valid.
        Assert.IsTrue(isValid);
        Assert.AreEqual(4, messages.Count(m => m.StartsWith("Warning:", StringComparison.Ordinal)));
        Assert.IsTrue(messages.Any(m => m.Contains("does not match the primary hazard type")));
        Assert.IsTrue(messages.Any(m => m.Contains("does not match the secondary hazard unit")));
    }

    /// <summary>The standard fixture validates clean.</summary>
    [TestMethod]
    public void Test_Validate_ValidConfiguration_NoMessages()
    {
        // Act
        var (isValid, messages) = Configured().Validate();

        // Assert
        Assert.IsTrue(isValid, string.Join(" | ", messages));
        Assert.AreEqual(0, messages.Count);
    }

    #endregion

    #region Sampler setup and seeding

    /// <summary>An unusable configuration cannot set up its sampler.</summary>
    [TestMethod]
    public void Test_SetupSampler_ThrowsBeforeValid()
    {
        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(
            () => new BivariateHazard().SetupSampler(100, 12345, SamplingScheme.LatinHypercube));
    }

    /// <summary>
    /// THE seeding contract: each marginal's sampler is seeded with
    /// <c>HashCombine(seed, marginal.CanonicalHash(), ordinal)</c> at ordinal 0 for X and 1 for Y —
    /// asserted bit-exactly against independently constructed content-clone expectations.
    /// </summary>
    [TestMethod]
    public void Test_SetupSampler_SeedsChildrenWithContentDerivedSeeds()
    {
        // Arrange — sampler-driven marginals plus standalone content clones.
        const int sampleSize = 64;
        const int seed = 987654;
        var x = TabularMarginal("PGA", 0.1d, 2d, 0.2d, primaryAxis: true);
        var y = TabularMarginal("Pool", 5d, 25d, 3d, primaryAxis: false);
        var hazard = new BivariateHazard(x, y);
        Label(hazard);

        var expectedX = TabularMarginal("PGA", 0.1d, 2d, 0.2d, primaryAxis: true);
        var expectedY = TabularMarginal("Pool", 5d, 25d, 3d, primaryAxis: false);
        CollectionAssert.AreEqual(x.CanonicalHash(), expectedX.CanonicalHash());

        // Act
        hazard.SetupSampler(sampleSize, seed, SamplingScheme.LatinHypercube);
        expectedX.SetupSampler(sampleSize, SeedHelpers.HashCombine(seed, expectedX.CanonicalHash(), 0), SamplingScheme.LatinHypercube);
        expectedY.SetupSampler(sampleSize, SeedHelpers.HashCombine(seed, expectedY.CanonicalHash(), 1), SamplingScheme.LatinHypercube);

        // Assert — the percentile streams are bit-identical to the forward rule's expectation.
        foreach (int k in new[] { 0, 17, 63 })
        {
            Assert.AreEqual(expectedX.SampledPercentile(k, 0), x.SampledPercentile(k, 0), 0d, $"X realization {k}");
            Assert.AreEqual(expectedY.SampledPercentile(k, 0), y.SampledPercentile(k, 0), 0d, $"Y realization {k}");
        }
    }

    /// <summary>
    /// Equal-content distinct marginals draw independently: the ordinal in the seed separates
    /// their streams, so the sampled percentile columns are uncorrelated.
    /// </summary>
    [TestMethod]
    public void Test_SetupSampler_EqualContentMarginals_DrawIndependently()
    {
        // Arrange — identical content, distinct instances (equal hashes).
        const int sampleSize = 2000;
        var x = TabularMarginal("Twin", 0.1d, 2d, 0.2d, primaryAxis: true);
        var y = TabularMarginal("Twin", 0.1d, 2d, 0.2d, primaryAxis: true);
        CollectionAssert.AreEqual(x.CanonicalHash(), y.CanonicalHash());
        var hazard = new BivariateHazard(x, y);
        Label(hazard);

        // Act
        hazard.SetupSampler(sampleSize, 24680, SamplingScheme.LatinHypercube);

        // Assert — the two streams differ and are uncorrelated (|r| < 0.1 is ~4.5 sd for two
        // independent stratified permutations at N = 2000, sd ≈ 1/√(N−1) ≈ 0.022).
        double sumX = 0d, sumY = 0d, sumXX = 0d, sumYY = 0d, sumXY = 0d;
        bool anyDifference = false;
        for (int k = 0; k < sampleSize; k++)
        {
            double px = x.SampledPercentile(k, 0);
            double py = y.SampledPercentile(k, 0);
            if (px != py) anyDifference = true;
            sumX += px; sumY += py; sumXX += px * px; sumYY += py * py; sumXY += px * py;
        }
        Assert.IsTrue(anyDifference, "Ordinal-distinct seeds must produce distinct streams.");
        double n = sampleSize;
        double correlation = (n * sumXY - sumX * sumY)
            / Math.Sqrt((n * sumXX - sumX * sumX) * (n * sumYY - sumY * sumY));
        Assert.AreEqual(0d, correlation, 0.1d, "Equal-content marginals must draw independently.");
    }

    /// <summary>A posterior-indexed marginal whose capacity is below the sample size fails at setup.</summary>
    [TestMethod]
    public void Test_SetupSampler_PosteriorCapacityTooSmall_Throws()
    {
        // Arrange — an uncertain parametric marginal with a 100-deep posterior (the smallest
        // legal Realizations), set up for a larger sample size.
        var shallow = new ParametricUnivariateHazard
        {
            Name = "Shallow Posterior",
            ParentDistribution = new Normal(1d, 0.2d),
            EffectiveRecordLength = 50,
            Realizations = 100,
            SpecifiedHazard = PrimaryHazard,
            HazardUnit = PrimaryUnit,
        };
        shallow.Estimate();
        var hazard = new BivariateHazard(shallow, UniformMarginal("Y", 0d, 30d, primaryAxis: false));
        Label(hazard);

        // Act
        var exception = Assert.ThrowsException<InvalidOperationException>(
            () => hazard.SetupSampler(200, 12345, SamplingScheme.LatinHypercube));

        // Assert
        StringAssert.Contains(exception.Message, "posterior of only 100");
    }

    /// <summary>The coupling consumes no draw of its own; setup records the sample size.</summary>
    [TestMethod]
    public void Test_SetupSampler_RecordsSampleSizeWithZeroDimensions()
    {
        // Arrange
        var hazard = Configured();

        // Act
        hazard.SetupSampler(128, 12345, SamplingScheme.LatinHypercubeMedian);

        // Assert
        Assert.AreEqual(0, hazard.SamplingDimensions);
        Assert.AreEqual(128, hazard.SampleSize);
    }

    #endregion

    #region Sampling delegation

    /// <summary>The primary sampling trio delegates to marginal X.</summary>
    [TestMethod]
    public void Test_SampleFunction_DelegatesToMarginalX()
    {
        // Arrange
        var hazard = Configured();
        hazard.SetupSampler(16, 12345, SamplingScheme.LatinHypercube);

        // Act / Assert — the deterministic uniform X marginal inverts identically on every path.
        Assert.AreEqual(2d, hazard.SampleFunction().InverseCDF(0.5d), 1e-12);
        Assert.AreEqual(1d, hazard.SampleFunction(0.42d).InverseCDF(0.25d), 1e-12);
        Assert.AreEqual(3d, hazard.SampleFunction(7).InverseCDF(0.75d), 1e-12);
    }

    /// <summary>The secondary sampling trio delegates to marginal Y.</summary>
    [TestMethod]
    public void Test_SampleSecondaryFunction_DelegatesToMarginalY()
    {
        // Arrange
        var hazard = Configured();
        hazard.SetupSampler(16, 12345, SamplingScheme.LatinHypercube);

        // Act / Assert — the deterministic uniform Y marginal on [0, 30].
        Assert.AreEqual(15d, hazard.SampleSecondaryFunction().InverseCDF(0.5d), 1e-12);
        Assert.AreEqual(7.5d, hazard.SampleSecondaryFunction(0.42d).InverseCDF(0.25d), 1e-12);
        Assert.AreEqual(22.5d, hazard.SampleSecondaryFunction(7).InverseCDF(0.75d), 1e-12);
    }

    /// <summary>The hazard bounds delegate per axis, bit-equal to the marginals' own reports.</summary>
    [TestMethod]
    public void Test_HazardBounds_DelegateToMarginals()
    {
        // Arrange
        var hazard = Configured();

        // Act / Assert — whatever bound policy the marginal applies, the bivariate reports it.
        foreach (bool meanOnly in new[] { true, false })
        {
            Assert.AreEqual(hazard.MarginalX!.MinHazard(meanOnly), hazard.MinHazard(meanOnly), 0d);
            Assert.AreEqual(hazard.MarginalX.MaxHazard(meanOnly), hazard.MaxHazard(meanOnly), 0d);
            Assert.AreEqual(hazard.MarginalY!.MinHazard(meanOnly), hazard.MinSecondaryHazard(meanOnly), 0d);
            Assert.AreEqual(hazard.MarginalY.MaxHazard(meanOnly), hazard.MaxSecondaryHazard(meanOnly), 0d);
        }

        // Assert — the axes are genuinely distinct (X on [0, 4], Y on [0, 30]).
        Assert.IsTrue(hazard.MaxSecondaryHazard(true) > hazard.MaxHazard(true));
    }

    /// <summary>Every sampling surface throws on an unusable configuration.</summary>
    [TestMethod]
    public void Test_SamplingSurfaces_ThrowWhenUnusable()
    {
        // Arrange — no marginals.
        var hazard = new BivariateHazard();
        Label(hazard);

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleFunction(0.5d));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleFunction(0));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleSecondaryFunction());
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleSecondaryFunction(0.5d));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleSecondaryFunction(0));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.MinHazard(true));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.MaxHazard(true));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.MinSecondaryHazard(true));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.MaxSecondaryHazard(true));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleBivariate(0));
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleConditionalYGivenX(0, 1d));
    }

    /// <summary>
    /// <see cref="BivariateHazard.SampleBivariate(int)"/> requires sampler setup, and a bin-count edit
    /// invalidates the previous setup so the snapshot geometry can never disagree with the
    /// configured count.
    /// </summary>
    [TestMethod]
    public void Test_SampleBivariate_BeforeSetup_Throws()
    {
        // Arrange
        var hazard = Configured();

        // Act / Assert — valid but not set up.
        var exception = Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleBivariate(0));
        StringAssert.Contains(exception.Message, "SetupSampler");

        // Act / Assert — set up, then stale after a bin-count edit.
        hazard.SetupSampler(8, 12345, SamplingScheme.LatinHypercube);
        Assert.AreEqual(21, hazard.SampleBivariate(0).ConditionalNodeCount);
        hazard.SecondaryIntegrationBins = 40;
        Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleBivariate(0));
    }

    /// <summary>The snapshot carries the sampled Y marginal, a cloned copula, and the bin geometry.</summary>
    [TestMethod]
    public void Test_SampleBivariate_SnapshotShape()
    {
        // Arrange
        var hazard = Configured();
        hazard.Copula = new ClaytonCopula(2d);
        hazard.SetupSampler(8, 12345, SamplingScheme.LatinHypercube);

        // Act
        var snapshot = hazard.SampleBivariate(3);

        // Assert
        Assert.AreEqual(20, snapshot.SecondaryIntegrationBins);
        Assert.AreEqual(21, snapshot.ConditionalNodeCount);
        Assert.IsInstanceOfType<ClaytonCopula>(snapshot.Copula);
        Assert.AreNotSame(hazard.Copula, snapshot.Copula);
        Assert.AreEqual(2d, snapshot.Copula.Theta);
        Assert.AreEqual(15d, snapshot.MarginalY.InverseCDF(0.5d), 1e-12);
    }

    /// <summary>
    /// The mean overload freezes the Y marginal's mean frequency curve with the same cloned
    /// copula and bin geometry — the shape the engine's mean pass and deterministic probes
    /// consume.
    /// </summary>
    [TestMethod]
    public void Test_SampleBivariate_MeanOverload_FreezesMeanMarginal()
    {
        // Arrange
        var hazard = Configured();
        hazard.Copula = new ClaytonCopula(2d);

        // Act / Assert — the mean overload shares the setup gate.
        var exception = Assert.ThrowsException<InvalidOperationException>(() => hazard.SampleBivariate());
        StringAssert.Contains(exception.Message, "SetupSampler");

        hazard.SetupSampler(8, 12345, SamplingScheme.LatinHypercube);
        var snapshot = hazard.SampleBivariate();
        var expected = hazard.SampleSecondaryFunction();

        // Assert — bin geometry, cloned copula, and the mean Y surface.
        Assert.AreEqual(20, snapshot.SecondaryIntegrationBins);
        Assert.AreEqual(21, snapshot.ConditionalNodeCount);
        Assert.IsInstanceOfType<ClaytonCopula>(snapshot.Copula);
        Assert.AreNotSame(hazard.Copula, snapshot.Copula);
        foreach (double probability in new[] { 0.1d, 0.5d, 0.9d })
        {
            Assert.AreEqual(expected.InverseCDF(probability), snapshot.MarginalY.InverseCDF(probability), 0d,
                $"mean marginal at p = {probability}");
        }
    }

    /// <summary>The allocating diagnostic overload agrees with the buffer kernel.</summary>
    [TestMethod]
    public void Test_SampleConditionalYGivenX_AgreesWithBufferKernel()
    {
        // Arrange
        var hazard = Configured();
        hazard.Copula = new ClaytonCopula(2d);
        hazard.SetupSampler(8, 12345, SamplingScheme.LatinHypercube);
        const double xLevel = 1d;

        // Act — the buffer path at the same conditioning point the diagnostic derives.
        var snapshot = hazard.SampleBivariate(2);
        double u = Math.Clamp(hazard.SampleFunction(2).CDF(xLevel), 1e-16, 1d - 1e-16);
        var yNodes = new double[snapshot.ConditionalNodeCount];
        var weights = new double[snapshot.ConditionalNodeCount];
        snapshot.FillConditionalBins(u, yNodes, weights);

        var convenience = hazard.SampleConditionalYGivenX(2, xLevel);

        // Assert
        Assert.AreEqual(snapshot.ConditionalNodeCount, convenience.Count);
        double weightSum = 0d;
        for (int j = 0; j < convenience.Count; j++)
        {
            Assert.AreEqual(yNodes[j], convenience[j].Y, 0d, $"node {j}");
            Assert.AreEqual(weights[j], convenience[j].Weight, 0d, $"weight {j}");
            weightSum += convenience[j].Weight;
        }
        Assert.AreEqual(1d, weightSum, 1e-12);
    }

    /// <summary>
    /// The uncertainty summary delegates to the primary marginal, guarding the width and
    /// returning null on an invalid configuration.
    /// </summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_DelegatesToMarginalX()
    {
        // Arrange
        var hazard = Configured();
        var expected = hazard.MarginalX!.ComputeUncertaintyResults();

        // Act
        var results = hazard.ComputeUncertaintyResults();

        // Assert
        Assert.IsNotNull(expected);
        Assert.IsNotNull(results);
        CollectionAssert.AreEqual(expected.MeanCurve, results.MeanCurve);

        // Act / Assert — width guard and invalid-configuration null.
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => hazard.ComputeUncertaintyResults(0d));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => hazard.ComputeUncertaintyResults(1d));
        Assert.IsNull(new BivariateHazard().ComputeUncertaintyResults());
    }

    #endregion

    #region Serialization

    /// <summary>The self-contained form round-trips bit-equal and hash-equal.</summary>
    [TestMethod]
    public void Test_ToXElement_SelfContained_RoundTripsBitEqual()
    {
        // Arrange
        var hazard = Configured();
        hazard.Copula = new ClaytonCopula(2d);
        hazard.SecondaryIntegrationBins = 35;
        hazard.Description = "The standard fixture.";

        // Act
        var xml = hazard.ToXElement();
        var restored = new BivariateHazard(xml);

        // Assert — byte-equal re-serialization, equal hash, equal state.
        Assert.AreEqual(xml.ToString(), restored.ToXElement().ToString());
        CollectionAssert.AreEqual(hazard.CanonicalHash(), restored.CanonicalHash());
        Assert.AreEqual(hazard.Id, restored.Id);
        Assert.AreEqual(35, restored.SecondaryIntegrationBins);
        Assert.AreEqual(SecondaryHazard, restored.SecondarySpecifiedHazard);
        Assert.AreEqual(SecondaryUnit, restored.SecondaryHazardUnit);
        Assert.IsInstanceOfType<ClaytonCopula>(restored.Copula);
        Assert.AreEqual(2d, restored.CopulaTheta);
        Assert.IsNotNull(restored.MarginalX);
        Assert.AreNotSame(hazard.MarginalX, restored.MarginalX);
        CollectionAssert.AreEqual(hazard.MarginalX!.CanonicalHash(), restored.MarginalX!.CanonicalHash());
    }

    /// <summary>The by-reference form writes markers, never marginal content.</summary>
    [TestMethod]
    public void Test_ToXElement_ByReference_WritesMarkers()
    {
        // Arrange
        var hazard = Configured();

        // Act
        var xml = hazard.ToXElement(RiskSerializationMode.ByReference);

        // Assert — no marginal content anywhere; one id-and-name marker per link; the copula
        // element still rides inline (it is owned state, not a stored function).
        Assert.IsFalse(xml.Descendants().Any(e => e.Name.LocalName == nameof(ParametricUnivariateHazard)));
        var references = xml.Descendants("FunctionReference").ToList();
        Assert.AreEqual(2, references.Count);
        foreach (var reference in references)
        {
            Assert.IsTrue(Guid.TryParse(reference.Attribute("Id")?.Value, out _));
            Assert.IsFalse(string.IsNullOrEmpty(reference.Attribute("Name")?.Value));
        }
        Assert.IsNotNull(xml.Element("Copula"));
        Assert.AreEqual(1, xml.Element(nameof(BivariateHazard.MarginalX))!.Elements().Count());
    }

    /// <summary>A by-reference round-trip re-attaches the live stored marginal instances.</summary>
    [TestMethod]
    public void Test_ByReference_RoundTrip_ReattachesLiveInstances()
    {
        // Arrange
        var hazard = Configured();
        var x = hazard.MarginalX!;
        var y = hazard.MarginalY!;

        // Act
        var restored = new BivariateHazard(hazard.ToXElement(RiskSerializationMode.ByReference), Resolver(x, y));

        // Assert
        Assert.AreSame(x, restored.MarginalX);
        Assert.AreSame(y, restored.MarginalY);
        Assert.IsTrue(restored.Validate().IsValid);
        CollectionAssert.AreEqual(hazard.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>A serialized reference id that resolves to nothing is stale, and stale ids throw.</summary>
    [TestMethod]
    public void Test_ByReference_StaleId_Throws()
    {
        // Arrange
        var xml = Configured().ToXElement(RiskSerializationMode.ByReference);

        // Act
        var exception = Assert.ThrowsException<InvalidOperationException>(
            () => new BivariateHazard(xml, Resolver()));

        // Assert
        StringAssert.Contains(exception.Message, "is not available");
    }

    /// <summary>
    /// A name-only reference that finds nothing is lenient: the marginal resolves to null and
    /// validation reports it by name.
    /// </summary>
    [TestMethod]
    public void Test_ByReference_LenientNameMiss_IsNullAndReported()
    {
        // Arrange — strip the id from the Y marker so only the lenient name path remains.
        var hazard = Configured();
        var xml = hazard.ToXElement(RiskSerializationMode.ByReference);
        xml.Element(nameof(BivariateHazard.MarginalY))!.Element("FunctionReference")!.Attribute("Id")!.Remove();

        // Act — resolve against a store holding only the X marginal.
        var restored = new BivariateHazard(xml, Resolver(hazard.MarginalX!));
        var (isValid, messages) = restored.Validate();

        // Assert
        Assert.IsNotNull(restored.MarginalX);
        Assert.IsNull(restored.MarginalY);
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("marginal Y hazard function 'Pool Duration Curve'") && m.Contains("was not found")));
    }

    /// <summary>Reading a by-reference form with no resolver degrades safely to reported nulls.</summary>
    [TestMethod]
    public void Test_ByReference_WithoutResolver_ReportsUnresolved()
    {
        // Arrange
        var xml = Configured().ToXElement(RiskSerializationMode.ByReference);

        // Act
        var restored = new BivariateHazard(xml);

        // Assert
        Assert.IsNull(restored.MarginalX);
        Assert.IsNull(restored.MarginalY);
        Assert.IsFalse(restored.Validate().IsValid);
    }

    /// <summary>A two-parameter copula (Student-t ρ and ν) round-trips both parameters.</summary>
    [TestMethod]
    public void Test_StudentTCopula_RoundTripsBothParameters()
    {
        // Arrange
        var hazard = Configured();
        hazard.Copula = new StudentTCopula(0.5d, 7d);

        // Act
        var restored = new BivariateHazard(hazard.ToXElement());

        // Assert
        Assert.IsInstanceOfType<StudentTCopula>(restored.Copula);
        CollectionAssert.AreEqual(new[] { 0.5d, 7d }, restored.Copula.GetCopulaParameters);
        CollectionAssert.AreEqual(hazard.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>A serialized form with no copula element reads as the independence default.</summary>
    [TestMethod]
    public void Test_MissingCopulaElement_ReadsAsIndependence()
    {
        // Arrange
        var hazard = Configured();
        var xml = hazard.ToXElement();
        xml.Element("Copula")!.Remove();

        // Act
        var restored = new BivariateHazard(xml);

        // Assert — independence restored, and the hash agrees with the independence-configured
        // original (the identity form sees the same copula state).
        Assert.IsInstanceOfType<IndependenceCopula>(restored.Copula);
        CollectionAssert.AreEqual(hazard.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>The model-library factory reconstructs the type, threading the resolver.</summary>
    [TestMethod]
    public void Test_FactoryRoundTrip_PreservesTypeAndHash()
    {
        // Arrange
        var hazard = Configured();

        // Act — self-contained through the factory; by-reference through the resolver overload.
        var restored = RiskFunctionFactory.CreateFromXElement(hazard.ToXElement());
        var resolved = RiskFunctionFactory.CreateHazardFunction(
            hazard.ToXElement(RiskSerializationMode.ByReference),
            Resolver(hazard.MarginalX!, hazard.MarginalY!));

        // Assert
        Assert.IsInstanceOfType<BivariateHazard>(restored);
        CollectionAssert.AreEqual(hazard.CanonicalHash(), restored.CanonicalHash());
        Assert.IsInstanceOfType<BivariateHazard>(resolved);
        Assert.AreSame(hazard.MarginalX, ((BivariateHazard)resolved!).MarginalX);
    }

    #endregion

    #region Canonical hash

    /// <summary>
    /// Metadata and persistence-mode inertness: renames, re-ids, axis labels, and the
    /// serialization mode can never move the projected identity hash.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataAndModeInert()
    {
        // Arrange
        var hazard = Configured();
        byte[] baseline = hazard.CanonicalHash();

        // Act / Assert — own metadata.
        HashInvariance.AssertMetadataInvariant(hazard);

        // Act / Assert — marginal metadata: rename and re-id both marginals.
        hazard.MarginalX!.Name = "Renamed X";
        hazard.MarginalX.AssignNewId();
        hazard.MarginalY!.Name = "Renamed Y";
        hazard.MarginalY.AssignNewId();
        CollectionAssert.AreEqual(baseline, hazard.CanonicalHash(), "Marginal metadata must be inert.");

        // Act / Assert — all four axis labels.
        hazard.SpecifiedHazard = "Different";
        hazard.HazardUnit = "Different";
        hazard.SecondarySpecifiedHazard = "Different";
        hazard.SecondaryHazardUnit = "Different";
        CollectionAssert.AreEqual(baseline, hazard.CanonicalHash(), "Axis labels must be inert.");
        Label(hazard);

        // Act / Assert — the persistence mode: both round-tripped forms hash to the baseline.
        var selfContained = new BivariateHazard(hazard.ToXElement());
        var byReference = new BivariateHazard(hazard.ToXElement(RiskSerializationMode.ByReference),
            Resolver(hazard.MarginalX, hazard.MarginalY));
        CollectionAssert.AreEqual(baseline, selfContained.CanonicalHash());
        CollectionAssert.AreEqual(baseline, byReference.CanonicalHash(),
            "The serialization mode is a persistence concern and must never move a seed.");
    }

    /// <summary>
    /// Compute sensitivity: θ, ν, the copula family, the bin count, marginal content, and the
    /// X↔Y role assignment each move the hash.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_ComputeEditsMoveIt()
    {
        // Arrange
        var hazard = Configured();
        byte[] baseline = hazard.CanonicalHash();

        // Act / Assert — copula family.
        hazard.Copula = new ClaytonCopula(2d);
        byte[] clayton = hazard.CanonicalHash();
        CollectionAssert.AreNotEqual(baseline, clayton, "The copula family must be hashed.");

        // Act / Assert — θ.
        hazard.CopulaTheta = 3d;
        byte[] theta = hazard.CanonicalHash();
        CollectionAssert.AreNotEqual(clayton, theta, "θ must be hashed.");

        // Act / Assert — ν on the two-parameter family.
        hazard.Copula = new StudentTCopula(0.5d, 7d);
        byte[] student = hazard.CanonicalHash();
        hazard.Copula = new StudentTCopula(0.5d, 8d);
        CollectionAssert.AreNotEqual(student, hazard.CanonicalHash(), "ν must be hashed.");

        // Act / Assert — the bin count.
        hazard.Copula = new IndependenceCopula();
        CollectionAssert.AreEqual(baseline, hazard.CanonicalHash(), "Restoring the copula restores the hash.");
        hazard.SecondaryIntegrationBins = 50;
        CollectionAssert.AreNotEqual(baseline, hazard.CanonicalHash(), "The bin count must be hashed.");
        hazard.SecondaryIntegrationBins = 20;

        // Act / Assert — marginal content.
        ((ParametricUnivariateHazard)hazard.MarginalX!).ParentDistribution = new Uniform(0d, 5d);
        CollectionAssert.AreNotEqual(baseline, hazard.CanonicalHash(), "Marginal content must be hashed.");
    }

    /// <summary>The marginal roles are asymmetric: swapping X and Y moves the hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_XYSwap_MovesHash()
    {
        // Arrange
        var hazard = Configured();
        var swapped = new BivariateHazard(hazard.MarginalY, hazard.MarginalX);
        Label(swapped);

        // Act / Assert
        CollectionAssert.AreNotEqual(hazard.CanonicalHash(), swapped.CanonicalHash(),
            "Swapping the marginal roles is a compute edit.");
    }

    /// <summary>
    /// Component-level mode invariance: a system component whose hazard element wraps a bivariate
    /// hazard hashes identically whichever mode its graph was written in — the identity form
    /// embeds the hazard self-contained, marginal content inline.
    /// </summary>
    [TestMethod]
    public void Test_ComponentLevel_ModeInvariance()
    {
        // Arrange — a levee-style chain with the hazard swapped to bivariate (all wiring port 0).
        var bivariate = Configured();
        var response = new TabularResponse { Name = "Fragility", SpecifiedHazard = PrimaryHazard, HazardUnit = PrimaryUnit };
        var damages = new TabularConsequence
        {
            Name = "Damages",
            SpecifiedHazard = PrimaryHazard,
            HazardUnit = PrimaryUnit,
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
        };

        var component = new SystemComponent { Name = "Dam" };
        var hazardElement = new HazardElement("Hazard") { Function = bivariate };
        var responseElement = new ResponseElement("Breach")
        {
            Function = response,
            Input = new RiskConnection(hazardElement),
        };
        var consequenceElement = new ConsequenceElement("Damages") { Input = new RiskConnection(responseElement) };
        consequenceElement.Functions.Add(damages);
        component.Graph.AddElement(hazardElement);
        component.Graph.AddElement(responseElement);
        component.Graph.AddElement(consequenceElement);

        byte[] baseline = component.CanonicalHash();

        // Act — round-trip through each mode.
        var selfContained = new SystemComponent(component.ToXElement(RiskSerializationMode.SelfContained));
        var byReference = new SystemComponent(
            component.ToXElement(RiskSerializationMode.ByReference),
            Resolver(bivariate, response, damages));

        // Assert
        CollectionAssert.AreEqual(baseline, selfContained.CanonicalHash());
        CollectionAssert.AreEqual(baseline, byReference.CanonicalHash(),
            "The serialization mode must never move a component seed.");
    }

    #endregion
}
