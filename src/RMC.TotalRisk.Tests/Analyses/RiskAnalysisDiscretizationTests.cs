using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Analyses;

/// <summary>
/// Unit tests for the secondary-axis discretization diagnostic: the null and guard contracts,
/// the bit agreement of the configured-count evaluation with a mean-only run, determinism, and
/// the diagnostic snapshot's bit-exact grid at the configured count.
/// </summary>
[TestClass]
public class RiskAnalysisDiscretizationTests
{
    #region Fixtures

    /// <summary>Builds a marginal whose ordinates carry Normal knowledge uncertainty.</summary>
    private static TabularHazard UncertainMarginal(string name, string hazard, double median, double extreme)
    {
        return new TabularHazard
        {
            Name = name,
            SpecifiedHazard = hazard,
            HazardUnit = "ft",
            UncertaintyValue = FunctionUncertainty.Hazard,
            HazardUncertainFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Normal(0d, 0.01d)),
                    new UncertainOrdinate(0.5d, new Normal(median, median * 0.05d)),
                    new UncertainOrdinate(0.001d, new Normal(extreme, extreme * 0.05d)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Normal),
        };
    }

    /// <summary>Builds a deterministic consequence rising linearly from (0 → 0) to (max → valueAtMax).</summary>
    private static TabularConsequence Damages(string name, string hazard, double max, double valueAtMax)
    {
        return new TabularConsequence
        {
            Name = name,
            SpecifiedHazard = hazard,
            HazardUnit = "ft",
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(max, new Deterministic(valueAtMax)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>
    /// Builds the bivariate fixture: an independence-copula hazard over uncertain marginals, a
    /// Secondary-bound pool fragility with pool-signal damages, and a primary background
    /// terminal.
    /// </summary>
    private static SystemComponent BivariateComponent(int bins)
    {
        var joint = new BivariateHazard(
            UncertainMarginal("Surge Marginal", "Surge", 10d, 30d),
            UncertainMarginal("Pool Marginal", "Pool Elevation", 50d, 100d))
        {
            Name = "Joint Hazard",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
            SecondaryIntegrationBins = bins,
        };
        var component = new SystemComponent(joint) { Name = "Joint Component" };
        var hazard = component.Graph.GetElements<HazardElement>().Single();
        var breach = new ResponseElement("Pool Breach")
        {
            Function = new TabularResponse
            {
                Name = "Pool Fragility",
                SpecifiedHazard = "Pool Elevation",
                HazardUnit = "ft",
                UncertainOrderedPairedData = new UncertainOrderedPairedData(
                    new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(1d)) },
                    true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
            },
            Input = new RiskConnection(hazard, 1),
        };
        component.Graph.AddElement(breach);
        var failure = new ConsequenceElement("Failure Damages") { Input = new RiskConnection(breach) };
        failure.Functions.Add(Damages("Failure Loss", "Pool Elevation", 100d, 600d));
        component.Graph.AddElement(failure);
        var background = new ConsequenceElement("Baseline Damages") { Input = new RiskConnection(hazard) };
        background.Functions.Add(Damages("Baseline Loss", "Surge", 30d, 60d));
        component.Graph.AddElement(background);
        return component;
    }

    /// <summary>Builds the analysis over a fresh bivariate component.</summary>
    private static RiskAnalysis Analysis(int bins)
    {
        var analysis = new RiskAnalysis(new[] { BivariateComponent(bins) });
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = 100;
        return analysis;
    }

    #endregion

    /// <summary>
    /// Verifies the null and guard contracts: an out-of-range index throws, a univariate
    /// component returns null, and a bin count whose quarter falls below the three-bin floor
    /// returns null.
    /// </summary>
    [TestMethod]
    public void Test_Guards_NullContracts()
    {
        // Arrange — a univariate analysis and an under-binned bivariate one.
        var univariate = new SystemComponent { Name = "Plain" };
        univariate.HazardFunction = UncertainMarginal("Stage Frequency", "Stage", 10d, 30d);
        univariate.AddFailureMode(new FailureMode(null, null, new TabularResponse
        {
            Name = "Fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(30d, new Deterministic(1d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        }, Damages("Loss", "Stage", 30d, 300d)));
        univariate.AddFailureMode(new FailureMode(null, null, null, Damages("Background", "Stage", 30d, 30d)));
        var univariateAnalysis = new RiskAnalysis(new[] { univariate });
        univariateAnalysis.Options.Realizations = 100;
        var underBinned = Analysis(bins: 8);

        // Act / Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => underBinned.EstimateSecondaryDiscretizationError(5));
        Assert.IsNull(univariateAnalysis.EstimateSecondaryDiscretizationError(0), "A univariate component has no secondary axis.");
        Assert.IsNull(underBinned.EstimateSecondaryDiscretizationError(0), "Eight bins cannot support the quartered level.");
    }

    /// <summary>
    /// Verifies the configured-count evaluation agrees with a mean-only run bit-exactly: the
    /// diagnostic's top level integrates the identical mean snapshot through the identical
    /// engine path, so the annual failure probability and the mean incremental risk must match
    /// the published mean realization to the bit.
    /// </summary>
    [TestMethod]
    public async Task Test_ConfiguredLevel_MatchesMeanOnlyRun_BitExact()
    {
        // Arrange — the diagnostic on one instance, the published mean pass on a fresh twin.
        var diagnostic = Analysis(bins: 20).EstimateSecondaryDiscretizationError(0);
        var meanRun = Analysis(bins: 20);
        meanRun.Options.EstimateMeanRiskOnly = true;
        await meanRun.RunAsync();
        var mean = meanRun.MeanRiskResults!.Components[0];

        // Assert
        Assert.IsNotNull(diagnostic);
        Assert.AreEqual(20, diagnostic!.Bins);
        Assert.AreEqual(10, diagnostic.HalfBins);
        Assert.AreEqual(5, diagnostic.QuarterBins);
        Assert.AreEqual(mean.Curves.Fail.TotalProbability, diagnostic.FailureProbability.Value, 0d,
            "The configured-count level is the mean pass, bit-exactly.");
        Assert.AreEqual(mean.Curves.Excess.Mean, diagnostic.MeanRisk[0].Value, 0d,
            "The mean incremental risk matches the published mean realization.");
        Assert.AreEqual(1, diagnostic.MeanRisk.Count, "One consequence type in the fixture.");
        Assert.IsTrue(double.IsFinite(diagnostic.FailureProbability.HalfValue));
        Assert.IsTrue(double.IsFinite(diagnostic.FailureProbability.QuarterValue));
    }

    /// <summary>
    /// Verifies determinism: two diagnostic calls agree bit-for-bit.
    /// </summary>
    [TestMethod]
    public void Test_Diagnostic_Deterministic()
    {
        // Arrange
        var analysis = Analysis(bins: 20);

        // Act
        var first = analysis.EstimateSecondaryDiscretizationError(0);
        var second = analysis.EstimateSecondaryDiscretizationError(0);

        // Assert
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first!.FailureProbability.Value, second!.FailureProbability.Value, 0d);
        Assert.AreEqual(first.FailureProbability.HalfValue, second.FailureProbability.HalfValue, 0d);
        Assert.AreEqual(first.FailureProbability.QuarterValue, second.FailureProbability.QuarterValue, 0d);
        Assert.AreEqual(first.MeanRisk[0].Value, second.MeanRisk[0].Value, 0d);
    }

    /// <summary>
    /// Verifies the diagnostic snapshot at the configured count reproduces the stored grid
    /// bit-exactly (the shared grid derivation), and the count guards throw.
    /// </summary>
    [TestMethod]
    public void Test_SampleBivariateAt_ConfiguredCount_MatchesStoredGrid()
    {
        // Arrange — set up the component's samplers, then compare snapshots.
        var component = BivariateComponent(bins: 20);
        component.SetupSamplers(100, 12345, SamplingScheme.LatinHypercube);
        var hazard = (BivariateHazard)component.HazardFunction!;
        var stored = hazard.SampleBivariate();
        var diagnostic = hazard.SampleBivariateAt(20);
        var storedNodes = new double[stored.ConditionalNodeCount];
        var storedWeights = new double[stored.ConditionalNodeCount];
        var diagnosticNodes = new double[diagnostic.ConditionalNodeCount];
        var diagnosticWeights = new double[diagnostic.ConditionalNodeCount];

        // Act
        stored.FillConditionalBins(0.5d, storedNodes, storedWeights);
        diagnostic.FillConditionalBins(0.5d, diagnosticNodes, diagnosticWeights);

        // Assert
        Assert.AreEqual(stored.ConditionalNodeCount, diagnostic.ConditionalNodeCount);
        for (int j = 0; j < storedNodes.Length; j++)
        {
            Assert.AreEqual(storedNodes[j], diagnosticNodes[j], 0d, "The diagnostic grid derivation is the stored one.");
            Assert.AreEqual(storedWeights[j], diagnosticWeights[j], 0d);
        }
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => hazard.SampleBivariateAt(2));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => hazard.SampleBivariateAt(1001));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => component.SampleWithConditionalBins(1200));
    }
}
