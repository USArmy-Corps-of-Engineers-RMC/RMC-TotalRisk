using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Tests.Analyses;

/// <summary>
/// Unit tests for <see cref="RiskAnalysis"/> — the mean-only and full-uncertainty smokes with
/// their decomposition identities and dense-reference parity, the ratified Q-V mixture-exposure
/// behavior, the stage gates with pinned messages, the event lifecycle, options-only
/// serialization, and same-seed reproducibility.
/// </summary>
[TestClass]
public class RiskAnalysisTests
{
    /// <summary>Builds the shared stage-frequency hazard (0.999 → 0 ft up to 0.001 → 30 ft).</summary>
    private static TabularHazard StageFrequency()
    {
        return new TabularHazard
        {
            Name = "Stage Frequency",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            NoUncertaintyFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Deterministic(0d)),
                    new UncertainOrdinate(0.5d, new Deterministic(10d)),
                    new UncertainOrdinate(0.001d, new Deterministic(30d)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a deterministic fragility rising linearly from (10 → 0) to (20 → 1).</summary>
    private static TabularResponse Fragility()
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

    /// <summary>Builds an uncertain fragility (triangular ordinates) for ensemble spread.</summary>
    private static TabularResponse UncertainFragility()
    {
        var response = Fragility();
        response.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(10d, new Triangular(0d, 0.05d, 0.1d)), new UncertainOrdinate(20d, new Triangular(0.7d, 0.9d, 1d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Triangular);
        return response;
    }

    /// <summary>Builds a labeled deterministic consequence: linear from (0 → 0) to (30 → valueAtThirty).</summary>
    private static TabularConsequence Consequence(string name, double valueAtThirty)
    {
        return new TabularConsequence
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(30d, new Deterministic(valueAtThirty)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds the standard single-component scenario with the given consequence function.</summary>
    private static SystemComponent Component(IConsequenceFunction failureConsequence, IResponseFunction? response = null)
    {
        var component = new SystemComponent { Name = "Dam" };
        component.HazardFunction = StageFrequency();
        component.AddFailureMode(new FailureMode(null, null, response ?? Fragility(), failureConsequence));
        component.AddFailureMode(new FailureMode(null, null, null, Consequence("Non-Failure Loss", 60d)));
        return component;
    }

    /// <summary>
    /// Verifies the mean-only smoke: a single-entry ensemble, the risk-type decomposition
    /// identities E[C_T] = E[C_F] + E[C_NF] = E[C_Δ] + E[C_B] (exact here — the failure
    /// consequence dominates the non-failure everywhere, so the excess clamp never binds), and
    /// live integrator diagnostics.
    /// </summary>
    [TestMethod]
    public async Task Test_MeanOnly_Smoke_DecompositionIdentities()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)) });

        // Act
        await analysis.RunAsync();

        // Assert
        Assert.IsTrue(analysis.IsEstimated);
        Assert.IsNotNull(analysis.RiskResults);
        Assert.AreEqual(1, analysis.RiskResults!.Count);
        var summary = analysis.RiskResults[0]!;
        Assert.IsTrue(summary.Fail.TotalProbability > 0d && summary.Fail.TotalProbability < 1d);
        Assert.AreEqual(summary.Total.Mean, summary.Fail.Mean + summary.NonFail.Mean, 1e-9 * summary.Total.Mean,
            "E[C_T] must decompose into failure plus non-failure risk.");
        Assert.AreEqual(summary.Total.Mean, summary.Excess.Mean + summary.Background.Mean, 1e-9 * summary.Total.Mean,
            "E[C_T] must decompose into incremental plus background risk.");
        Assert.IsTrue(summary.FunctionEvaluations > 100d, "The adaptive integrator must have run.");
        Assert.IsNotNull(analysis.MeanRiskResults);
        Assert.IsTrue(analysis.MeanRiskResults!.Curves.Total.LECConsequences.Length > 2);
    }

    /// <summary>
    /// Verifies the mean-only total risk against a dense independent trapezoid reference over
    /// the same sampled math (twenty thousand probability ordinates).
    /// </summary>
    [TestMethod]
    public async Task Test_MeanOnly_MeanVsDenseReference()
    {
        // Arrange
        var component = Component(Consequence("Failure Loss", 300d));
        var analysis = new RiskAnalysis(new[] { component });

        // Act
        await analysis.RunAsync();

        // The reference: a dense trapezoid over the mean sampled component (the engine's
        // SetupSamplers walk has run, so the mean sample is available).
        var sampled = component.Sample(-1);
        var scratch = new ComponentRealization(sampled.FailureModeCount);
        var flags = new RiskComputeFlags();
        int gridCount = 20_000;
        double lower = 1e-16;
        double upper = 1d - 1e-16;
        double step = (upper - lower) / gridCount;
        double reference = 0d;
        double previous = Integrand(lower);
        for (int i = 1; i <= gridCount; i++)
        {
            double current = Integrand(lower + i * step);
            reference += 0.5d * (previous + current) * step;
            previous = current;
        }

        double Integrand(double probability)
        {
            double hazard = sampled.Hazard.InverseCDF(probability);
            var output = sampled.ComputeRisk(probability, hazard, flags, scratch);
            return output.ProbabilityOfFailure * output.MeanFailureConsequences
                + output.ProbabilityOfNonFailure * output.NonFailureConsequences;
        }

        // Assert — the recorded-curve mean matches the dense reference within 0.1%.
        double engineMean = analysis.RiskResults![0]!.Total.Mean;
        Assert.AreEqual(reference, engineMean, 1e-3 * reference,
            $"Engine mean {engineMean} vs dense reference {reference}.");
    }

    /// <summary>
    /// Verifies the full-uncertainty smoke: the ensemble count, ordered percentile curves, and
    /// a deterministic scenario collapsing the band to a single curve.
    /// </summary>
    [TestMethod]
    public async Task Test_FullUncertainty_PercentileOrdering()
    {
        // Arrange — an uncertain fragility gives the ensemble genuine spread.
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d), UncertainFragility()) });
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = 100;

        // Act
        await analysis.RunAsync();

        // Assert
        Assert.IsTrue(analysis.IsEstimated);
        Assert.AreEqual(100, analysis.RiskResults!.Count);
        Assert.IsNotNull(analysis.LowerRiskResults);
        Assert.IsNotNull(analysis.UpperRiskResults);
        Assert.IsNotNull(analysis.MedianRiskResults);
        Assert.IsNotNull(analysis.MeanRiskResults);

        double lower = analysis.LowerRiskResults!.Curves.Fail.LEC.GetYFromX(50d, Transform.Logarithmic, Transform.Logarithmic);
        double median = analysis.MedianRiskResults!.Curves.Fail.LEC.GetYFromX(50d, Transform.Logarithmic, Transform.Logarithmic);
        double upper = analysis.UpperRiskResults!.Curves.Fail.LEC.GetYFromX(50d, Transform.Logarithmic, Transform.Logarithmic);
        Assert.IsTrue(lower <= median + 1e-12 && median <= upper + 1e-12,
            $"Percentile curves must order: {lower} ≤ {median} ≤ {upper}.");

        // A deterministic scenario collapses the band.
        var deterministic = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)) });
        deterministic.Options.EstimateMeanRiskOnly = false;
        deterministic.Options.Realizations = 100;
        await deterministic.RunAsync();
        double deterministicLower = deterministic.LowerRiskResults!.Curves.Fail.LEC.GetYFromX(50d, Transform.Logarithmic, Transform.Logarithmic);
        double deterministicUpper = deterministic.UpperRiskResults!.Curves.Fail.LEC.GetYFromX(50d, Transform.Logarithmic, Transform.Logarithmic);
        Assert.AreEqual(deterministicLower, deterministicUpper, 1e-12, "A deterministic model has a zero-width band.");
    }

    /// <summary>
    /// Verifies the ratified Q-V mixture exposure: the mean-only mean matches the flattened
    /// (average) composite exactly, while the loss-exceedance spread is strictly larger because
    /// the branches are enumerated instead of collapsed — the corrected v1.0 day/night defect.
    /// </summary>
    [TestMethod]
    public async Task Test_MixtureExposure_MeanParity_SigmaInflation()
    {
        // Arrange — identical children and weights; only the combine mode differs.
        static CompositeConsequence Composite(CompositeFunctionType type)
        {
            return new CompositeConsequence(new[]
            {
                new WeightedConsequenceFunction(Consequence("Day", 100d), 0.55d),
                new WeightedConsequenceFunction(Consequence("Night", 300d), 0.45d),
            })
            {
                Name = "Day/Night",
                SpecifiedHazard = "Stage",
                HazardUnit = "ft",
                SpecifiedConsequence = "Life Loss",
                ConsequenceUnit = "lives",
                CompositeFunctionType = type,
            };
        }
        var mixtureAnalysis = new RiskAnalysis(new[] { Component(Composite(CompositeFunctionType.Mixture)) });
        var averageAnalysis = new RiskAnalysis(new[] { Component(Composite(CompositeFunctionType.Average)) });

        // Act
        await mixtureAnalysis.RunAsync();
        await averageAnalysis.RunAsync();

        // Assert — the mixture identity Σ wᵢ·P_F·fᵢ = P_F·Σ wᵢ·fᵢ keeps the mean unchanged...
        var mixture = mixtureAnalysis.RiskResults![0]!;
        var average = averageAnalysis.RiskResults![0]!;
        Assert.AreEqual(average.Fail.Mean, mixture.Fail.Mean, 1e-9 * average.Fail.Mean,
            "Branch enumeration must not move the mean (the free regression gate).");

        // ...while the enumerated branches carry the exposure spread the flattened curve destroys.
        Assert.IsTrue(mixture.Fail.StandardDeviation > average.Fail.StandardDeviation * 1.05d,
            $"Mixture σ {mixture.Fail.StandardDeviation} must exceed flattened σ {average.Fail.StandardDeviation}.");
    }

    /// <summary>
    /// Verifies the stage gates with their pinned messages: component count (Phase 4b),
    /// reliability mode (Phase 4c), multi-stage response composition (event-tree phase), and
    /// the empty analysis.
    /// </summary>
    [TestMethod]
    public async Task Test_Validate_StageGates_PinnedMessages()
    {
        // No components.
        var empty = new RiskAnalysis(Array.Empty<SystemComponent>());
        Assert.IsTrue(empty.Validate().ValidationMessages.Any(m => m.Contains("no system components")));

        // Two components → Phase 4b gate.
        var two = new RiskAnalysis(new[] { Component(Consequence("A", 300d)), Component(Consequence("B", 300d)) });
        Assert.IsTrue(two.Validate().ValidationMessages.Any(m => m.Contains("Phase 4b")));

        // Reliability mode → Phase 4c gate.
        var reliability = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) });
        reliability.Options.Mode = RiskAnalysisMode.Reliability;
        Assert.IsTrue(reliability.Validate().ValidationMessages.Any(m => m.Contains("Phase 4c")));

        // A multi-stage mode → event-tree gate (authoring stays valid; the engine refuses).
        var multiStage = Component(Consequence("A", 300d));
        var chained = new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction>(), Fragility()),
                new ResponseStage(new List<ITransformFunction>(), Fragility()),
            },
            null, new List<IConsequenceFunction> { Consequence("Chained", 100d) });
        multiStage.AddFailureMode(chained);
        var multiStageAnalysis = new RiskAnalysis(new[] { multiStage });
        Assert.IsTrue(multiStageAnalysis.Validate().ValidationMessages.Any(m => m.Contains("event-tree")));

        // RunAsync throws on validation errors (a caller error, not a run outcome).
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => two.RunAsync());
    }

    /// <summary>
    /// Verifies the event lifecycle: a vetoed start completes as canceled without running; a
    /// successful run raises the succeeded completion; an already-canceled token surfaces as a
    /// canceled completion, never a thrown exception.
    /// </summary>
    [TestMethod]
    public async Task Test_Events_Lifecycle()
    {
        // Vetoed start.
        var vetoed = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) });
        AnalysisRunCompletedEventArgs? vetoedArgs = null;
        vetoed.AnalysisStarting += (_, e) => e.Cancel = true;
        vetoed.AnalysisCompleted += (_, e) => vetoedArgs = e;
        await vetoed.RunAsync();
        Assert.IsNotNull(vetoedArgs);
        Assert.IsTrue(vetoedArgs!.Cancelled);
        Assert.IsFalse(vetoed.IsEstimated);

        // Successful run.
        var success = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) });
        AnalysisRunCompletedEventArgs? successArgs = null;
        success.AnalysisCompleted += (_, e) => successArgs = e;
        await success.RunAsync();
        Assert.IsNotNull(successArgs);
        Assert.IsTrue(successArgs!.Succeeded);
        Assert.IsTrue(success.IsEstimated);

        // A pre-canceled external token.
        var canceled = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) });
        AnalysisRunCompletedEventArgs? canceledArgs = null;
        canceled.AnalysisCompleted += (_, e) => canceledArgs = e;
        using var source = new CancellationTokenSource();
        source.Cancel();
        await canceled.RunAsync(cancellationToken: source.Token);
        Assert.IsNotNull(canceledArgs);
        Assert.IsTrue(canceledArgs!.Cancelled);
        Assert.IsFalse(canceled.IsEstimated);
    }

    /// <summary>
    /// Verifies options-only serialization (architecture doc §8) and the results-through-
    /// constructor round trip.
    /// </summary>
    [TestMethod]
    public async Task Test_ToXElement_OptionsOnly_CtorRoundTrip()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) })
        {
            Name = "Levee Study",
            Description = "Stage 4 smoke",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        analysis.Options.Alpha = 0.02d;
        await analysis.RunAsync();

        // Act
        var element = analysis.ToXElement();
        var restored = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) }, element,
            analysis.RiskResults, analysis.MeanRiskResults);

        // Assert — configuration only: one options child, nothing else.
        Assert.AreEqual(1, element.Elements().Count());
        Assert.AreEqual(nameof(RiskAnalysisOptions), element.Elements().First().Name.LocalName);
        Assert.AreEqual("Levee Study", restored.Name);
        Assert.AreEqual(0.02d, restored.Options.Alpha, 0d);
        Assert.IsTrue(restored.IsEstimated, "Supplied results restore the estimated state.");
        Assert.AreSame(analysis.RiskResults, restored.RiskResults);

        // Option edits invalidate the results.
        restored.Options.Alpha = 0.05d;
        Assert.IsFalse(restored.IsEstimated);
    }

    /// <summary>
    /// Verifies same-seed reproducibility: two runs over equal-content components produce
    /// bit-identical results (the content-based seeding contract; the full shuffle/rename
    /// matrix is pinned in the verification suite).
    /// </summary>
    [TestMethod]
    public async Task Test_SameSeed_BitIdentical()
    {
        // Arrange — two independent analyses over equal-content components.
        static RiskAnalysis Build()
        {
            var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d), UncertainFragility()) });
            analysis.Options.EstimateMeanRiskOnly = false;
            analysis.Options.Realizations = 100;
            return analysis;
        }
        var first = Build();
        var second = Build();

        // Act
        await first.RunAsync();
        await second.RunAsync();

        // Assert — bit-identical summaries and curves.
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(first.RiskResults![0]!.Total.Mean),
            BitConverter.DoubleToInt64Bits(second.RiskResults![0]!.Total.Mean));
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(first.RiskResults[42]!.Fail.StandardDeviation),
            BitConverter.DoubleToInt64Bits(second.RiskResults[42]!.Fail.StandardDeviation));
        CollectionAssert.AreEqual(
            first.MeanRiskResults!.Curves.Total.LECProbabilities,
            second.MeanRiskResults!.Curves.Total.LECProbabilities);
    }
}
