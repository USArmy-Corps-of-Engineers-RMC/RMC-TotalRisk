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
    /// The perfectly-negative dependency regression (Phase 5 pre-flight finding): the derived
    /// dependency matrix previously never materialized on the run path — only the
    /// multivariate-normal getter built it — so the joint and common-cause kernels threw into
    /// the integrator's swallowing catch and published all-zero results with a success status,
    /// and the competing pre-processing faulted the run. Pins for all three dependent methods:
    /// the run estimates, risk is non-zero, the joint and common-cause unions agree tightly
    /// (both derive the same union from the same Gaussian copula), competing agrees within its
    /// 200-bin cumulative-incidence discretization, and negative dependence strictly raises the
    /// failure union above independence (the reversed unimodal bound).
    /// </summary>
    [TestMethod]
    public async Task Test_PerfectlyNegative_AllMethods_ComputeAndAgreeOnUnion()
    {
        // Arrange — two overlapping fragilities so the dependence direction matters, plus the
        // standard non-failure mode. A fresh component per run keeps the cases independent.
        static SystemComponent TwoModeComponent()
        {
            var secondFragility = new TabularResponse
            {
                Name = "Second Fragility",
                SpecifiedHazard = "Stage",
                HazardUnit = "ft",
                UncertainOrderedPairedData = new UncertainOrderedPairedData(
                    new[] { new UncertainOrdinate(12d, new Deterministic(0d)), new UncertainOrdinate(25d, new Deterministic(1d)) },
                    true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
            };
            var component = new SystemComponent { Name = "Dam" };
            component.HazardFunction = StageFrequency();
            component.AddFailureMode(new FailureMode(null, null, Fragility(), Consequence("Failure Loss A", 300d)));
            component.AddFailureMode(new FailureMode(null, null, secondFragility, Consequence("Failure Loss B", 200d)));
            component.AddFailureMode(new FailureMode(null, null, null, Consequence("Non-Failure Loss", 60d)));
            return component;
        }

        static async Task<double> RunApf(FailureModeMethod method, DependencyType dependency)
        {
            var component = TwoModeComponent();
            component.FailureModeMethod = method;
            component.FailureModeDependency = dependency;
            var analysis = new RiskAnalysis(new[] { component });
            await analysis.RunAsync();
            Assert.IsTrue(analysis.IsEstimated, $"{method} + {dependency} must estimate.");
            var summary = analysis.RiskResults![0]!;
            Assert.IsTrue(summary.Fail.TotalProbability > 0d, $"{method} + {dependency}: the failure union must be non-zero.");
            Assert.IsTrue(summary.Total.Mean > 0d, $"{method} + {dependency}: total risk must be non-zero.");
            return summary.Fail.TotalProbability;
        }

        // Act
        double independentJoint = await RunApf(FailureModeMethod.JointFailures, DependencyType.Independent);
        double negativeJoint = await RunApf(FailureModeMethod.JointFailures, DependencyType.PerfectlyNegative);
        double negativeCommonCause = await RunApf(FailureModeMethod.CommonCauseFailures, DependencyType.PerfectlyNegative);
        double negativeCompeting = await RunApf(FailureModeMethod.CompetingFailures, DependencyType.PerfectlyNegative);

        // Assert — same marginals + same dependency ⇒ same union across the combination methods.
        Assert.AreEqual(negativeJoint, negativeCommonCause, 1e-6 * negativeJoint,
            "Joint and common-cause must produce the same failure union under the same copula.");
        Assert.AreEqual(negativeJoint, negativeCompeting, 1e-2 * negativeJoint,
            "Competing must match the union within its 200-bin cumulative-incidence discretization.");
        Assert.IsTrue(negativeJoint > independentJoint * 1.001d,
            $"Negative dependence must raise the failure union (negative {negativeJoint} vs independent {independentJoint}).");
    }

    /// <summary>
    /// Verifies the validation catalog with its pinned messages: the empty analysis, the
    /// additive method's strict-independence requirement (ratified v0.13), the joint method's
    /// dimension limit and correlation-matrix checks, and the multi-stage response gate
    /// (event-tree phase). Two independent additive components and reliability mode now
    /// validate — their Phase 4 gates are gone.
    /// </summary>
    [TestMethod]
    public async Task Test_Validate_StageGates_PinnedMessages()
    {
        // No components.
        var empty = new RiskAnalysis(Array.Empty<SystemComponent>());
        Assert.IsTrue(empty.Validate().ValidationMessages.Any(m => m.Contains("no system components")));

        // Two independent additive components validate (the Phase 4b gate is gone).
        var two = new RiskAnalysis(new[] { Component(Consequence("A", 300d)), Component(Consequence("B", 300d)) });
        Assert.IsTrue(two.Validate().IsValid);

        // The additive method with a hazard dependence → the strict-independence error.
        var dependentAdditive = new RiskAnalysis(new[] { Component(Consequence("A", 300d)), Component(Consequence("B", 300d)) });
        dependentAdditive.Options.ComponentHazardDependency = DependencyType.PerfectlyPositive;
        Assert.IsTrue(dependentAdditive.Validate().ValidationMessages.Any(m => m.Contains("strictly independent")));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => dependentAdditive.RunAsync());

        // The joint method under the correlation-matrix dependency needs a valid matrix.
        var jointMatrix = new RiskAnalysis(new[] { Component(Consequence("A", 300d)), Component(Consequence("B", 300d)) });
        jointMatrix.Options.SystemRiskMethod = SystemRiskType.JointRiskMethod;
        jointMatrix.Options.ComponentHazardDependency = DependencyType.CorrelationMatrix;
        Assert.IsTrue(jointMatrix.Validate().ValidationMessages.Any(m => m.Contains("correlation matrix")),
            "A missing matrix under the correlation-matrix dependency must be an error.");
        jointMatrix.Options.HazardCorrelationMatrix = new[,] { { 1d, 2d }, { 2d, 1d } };
        Assert.IsTrue(jointMatrix.Validate().ValidationMessages.Any(m => m.Contains("positive-definite")),
            "A non-positive-definite matrix must be an error.");
        jointMatrix.Options.HazardCorrelationMatrix = new[,] { { 1d, 0.5d }, { 0.5d, 1d } };
        Assert.IsTrue(jointMatrix.Validate().IsValid, "A valid matrix passes.");

        // Reliability mode validates (the Phase 4c gate is gone).
        var reliability = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) });
        reliability.Options.Mode = RiskAnalysisMode.Reliability;
        Assert.IsTrue(reliability.Validate().IsValid);

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

    /// <summary>Builds a second, distinct component (steeper fragility, smaller consequences).</summary>
    private static SystemComponent ComponentB()
    {
        var fragility = new TabularResponse
        {
            Name = "Fragility B",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(14d, new Deterministic(0d)), new UncertainOrdinate(24d, new Deterministic(1d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
        var component = new SystemComponent { Name = "Levee" };
        component.HazardFunction = StageFrequency();
        component.AddFailureMode(new FailureMode(null, null, fragility, Consequence("Levee Failure Loss", 150d)));
        component.AddFailureMode(new FailureMode(null, null, null, Consequence("Levee Non-Failure Loss", 25d)));
        return component;
    }

    /// <summary>Builds a consequence-free component for reliability mode (Phase 4c).</summary>
    private static SystemComponent ReliabilityComponent(string name, IResponseFunction? response = null)
    {
        var component = new SystemComponent { Name = name };
        component.HazardFunction = StageFrequency();
        component.AddFailureMode(new FailureMode(null, null, response ?? Fragility(), null));
        return component;
    }

    /// <summary>
    /// Verifies the additive system aggregation (Phase 4b): the convolved system mean equals the
    /// sum of the component means (the v1.0 mean-parity gate, exact by construction), the system
    /// failure probability is the independent union with the v1.0 stream-probability semantics,
    /// independent variances add, the decomposition identity holds, and — the headline v1.1
    /// capability — a true system loss exceedance curve exists where v1.0 produced none.
    /// </summary>
    [TestMethod]
    public async Task Test_AdditiveSystem_TwoComponents_ExactAggregates()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)), ComponentB() });

        // Act
        await analysis.RunAsync();

        // Assert
        var summary = analysis.RiskResults![0]!;
        var componentA = summary.ComponentResults[0];
        var componentB = summary.ComponentResults[1];

        // Mean parity: system mean == Σ component means (per stream).
        double expectedTotalMean = componentA.Total.Mean + componentB.Total.Mean;
        Assert.AreEqual(expectedTotalMean, summary.Total.Mean, 1e-6 * expectedTotalMean,
            "The convolved system Total mean must equal the sum of the component means (the v1.0 additive answer).");
        double expectedFailMean = componentA.Fail.Mean + componentB.Fail.Mean;
        Assert.AreEqual(expectedFailMean, summary.Fail.Mean, 1e-6 * expectedFailMean,
            "The convolved system Fail mean must equal the sum of the component means.");

        // The failure union and the v1.0 stream-probability semantics.
        double union = 1d - (1d - componentA.Fail.TotalProbability) * (1d - componentB.Fail.TotalProbability);
        Assert.AreEqual(union, summary.Fail.TotalProbability, 1e-12, "System AFP must be the independent union.");
        Assert.AreEqual(union, summary.Excess.TotalProbability, 1e-12);
        Assert.AreEqual(1d - union, summary.NonFail.TotalProbability, 1e-12);

        // Independent variances add (within the lattice quantization).
        double expectedTotalVariance = componentA.Total.StandardDeviation * componentA.Total.StandardDeviation
            + componentB.Total.StandardDeviation * componentB.Total.StandardDeviation;
        double systemVariance = summary.Total.StandardDeviation * summary.Total.StandardDeviation;
        Assert.AreEqual(expectedTotalVariance, systemVariance, 1e-4 * expectedTotalVariance,
            "Independent component variances must add through the convolution.");

        // Decomposition identity survives aggregation.
        Assert.AreEqual(summary.Total.Mean, summary.Fail.Mean + summary.NonFail.Mean, 1e-9 * summary.Total.Mean);

        // The system LEC exists (v1.0's additive path produced no system curve at all).
        var systemLec = analysis.MeanRiskResults!.Curves.Total;
        Assert.IsTrue(systemLec.LECConsequences.Length > 2, "The additive system Total LEC must be produced.");
        Assert.IsTrue(systemLec.LECConsequences[0] > 300d,
            "The system curve support must extend beyond a single component's maximum (the summed tail).");
        Assert.AreEqual(1d, systemLec.MassBalance, 1e-9, "The exhaustive system budget must be exactly one.");
    }

    /// <summary>
    /// Verifies the additive full-uncertainty smoke: system percentile curves exist and order at
    /// a probe consequence.
    /// </summary>
    [TestMethod]
    public async Task Test_AdditiveSystem_FullUncertainty_Percentiles()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d), UncertainFragility()), ComponentB() });
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = 100;

        // Act
        await analysis.RunAsync();

        // Assert
        Assert.AreEqual(100, analysis.RiskResults!.Count);
        double lower = analysis.LowerRiskResults!.Curves.Total.LEC.GetYFromX(60d, Transform.Logarithmic, Transform.Logarithmic);
        double median = analysis.MedianRiskResults!.Curves.Total.LEC.GetYFromX(60d, Transform.Logarithmic, Transform.Logarithmic);
        double upper = analysis.UpperRiskResults!.Curves.Total.LEC.GetYFromX(60d, Transform.Logarithmic, Transform.Logarithmic);
        Assert.IsTrue(lower <= median + 1e-12 && median <= upper + 1e-12,
            $"System percentile curves must order: {lower} ≤ {median} ≤ {upper}.");
    }

    /// <summary>
    /// Verifies the joint method (Phase 4b) against the additive method on the same independent
    /// two-component system: the VEGAS estimate of the system mean must agree statistically with
    /// the exact convolution, the recorded exhaustive budget must self-normalize to exactly one,
    /// and the integration diagnostics must reflect the warm-up plus the five recording passes.
    /// </summary>
    [TestMethod]
    public async Task Test_JointSystem_IndependentMatchesAdditive()
    {
        // Arrange — identical components; only the aggregation method differs.
        var additive = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)), ComponentB() });
        var joint = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)), ComponentB() });
        joint.Options.SystemRiskMethod = SystemRiskType.JointRiskMethod;
        joint.Options.VegasTailFocusMode = VegasTailFocusMode.None;

        // Act
        await additive.RunAsync();
        await joint.RunAsync();

        // Assert — statistical agreement on the means (the joint path is Monte Carlo).
        var additiveSummary = additive.RiskResults![0]!;
        var jointSummary = joint.RiskResults![0]!;
        Assert.AreEqual(additiveSummary.Total.Mean, jointSummary.Total.Mean, 0.05d * additiveSummary.Total.Mean,
            $"Joint mean {jointSummary.Total.Mean} must agree with the exact additive mean {additiveSummary.Total.Mean}.");
        Assert.AreEqual(additiveSummary.Fail.TotalProbability, jointSummary.Fail.TotalProbability,
            0.10d * additiveSummary.Fail.TotalProbability,
            "The joint failure union must agree statistically with the exact independent union.");

        // The self-normalized exhaustive budget and the diagnostics.
        Assert.AreEqual(1d, joint.MeanRiskResults!.Curves.Total.MassBalance, 1e-9,
            "The joint Total budget must self-normalize to exactly one.");
        Assert.IsTrue(joint.MeanRiskResults.Curves.Total.LECConsequences.Length > 2, "The joint system LEC must be produced.");
        Assert.IsTrue(jointSummary.FunctionEvaluations > 90_000d,
            $"The warm-up plus five recording passes must evaluate; saw {jointSummary.FunctionEvaluations}.");
        Assert.IsTrue(jointSummary.ChiSquared >= 0d);

        // Component curves exist in the joint path too (recorded through the VEGAS weights).
        Assert.IsTrue(joint.MeanRiskResults.Components[0].Curves.Fail.LECConsequences.Length > 2);
        Assert.IsTrue(joint.MeanRiskResults.Components[1].Curves.Fail.LECConsequences.Length > 2);
    }

    /// <summary>
    /// Verifies joint-path reproducibility: two runs over equal-content components under the
    /// automatic tail focus (the default) are bit-identical — the content-derived VEGAS stream
    /// and the deterministic probe heuristic together.
    /// </summary>
    [TestMethod]
    public async Task Test_JointSystem_SameSeed_BitIdentical()
    {
        // Arrange
        static RiskAnalysis Build()
        {
            var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)), ComponentB() });
            analysis.Options.SystemRiskMethod = SystemRiskType.JointRiskMethod;
            analysis.Options.UseDefaults = false;
            analysis.Options.WarmupEvaluations = 500;
            analysis.Options.WarmupCycles = 2;
            analysis.Options.FinalEvaluations = 2000;
            return analysis;
        }
        var first = Build();
        var second = Build();

        // Act
        await first.RunAsync();
        await second.RunAsync();

        // Assert
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(first.RiskResults![0]!.Total.Mean),
            BitConverter.DoubleToInt64Bits(second.RiskResults![0]!.Total.Mean));
        CollectionAssert.AreEqual(
            first.MeanRiskResults!.Curves.Fail.LECProbabilities,
            second.MeanRiskResults!.Curves.Fail.LECProbabilities);
    }

    /// <summary>
    /// Verifies the tail-focus modes agree statistically on the mean: γ = 1 (None), a manual
    /// γ = 4, and the automatic probe-driven γ are all unbiased samplings of the same integral —
    /// the unit-level Jacobian audit (the verification family pins the k·SE budget).
    /// </summary>
    [TestMethod]
    public async Task Test_JointSystem_TailFocusModes_MeanConsistent()
    {
        // Arrange
        static RiskAnalysis Build(VegasTailFocusMode mode, double gamma = 1d)
        {
            var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)), ComponentB() });
            analysis.Options.SystemRiskMethod = SystemRiskType.JointRiskMethod;
            analysis.Options.VegasTailFocusMode = mode;
            analysis.Options.VegasTailFocusParameter = gamma;
            return analysis;
        }
        var none = Build(VegasTailFocusMode.None);
        var manual = Build(VegasTailFocusMode.Manual, 4d);
        var automatic = Build(VegasTailFocusMode.Automatic);

        // Act
        await none.RunAsync();
        await manual.RunAsync();
        await automatic.RunAsync();

        // Assert — the transform must not bias the mean or the recorded budget.
        double reference = none.RiskResults![0]!.Total.Mean;
        Assert.AreEqual(reference, manual.RiskResults![0]!.Total.Mean, 0.05d * reference,
            "A manual γ = 4 must leave the mean unbiased (the Jacobian reaches the weights).");
        Assert.AreEqual(reference, automatic.RiskResults![0]!.Total.Mean, 0.05d * reference,
            "The automatic tail focus must leave the mean unbiased.");
        Assert.AreEqual(1d, manual.MeanRiskResults!.Curves.Total.MassBalance, 1e-9);
        Assert.AreEqual(1d, automatic.MeanRiskResults!.Curves.Total.MassBalance, 1e-9);
    }

    /// <summary>
    /// Verifies reliability mode on a single consequence-free component (Phase 4c): risk-mode
    /// validation rejects the model, reliability-mode validation accepts it, and the annualized
    /// failure probability matches a dense independent reference at every level — failure mode,
    /// component, and system.
    /// </summary>
    [TestMethod]
    public async Task Test_Reliability_SingleComponent_AfpVsDenseReference()
    {
        // Arrange
        var component = ReliabilityComponent("Dam");
        var analysis = new RiskAnalysis(new[] { component });
        Assert.IsFalse(analysis.Validate().IsValid, "A consequence-free model must fail risk-mode validation.");
        analysis.Options.Mode = RiskAnalysisMode.Reliability;
        Assert.IsTrue(analysis.Validate().IsValid, "Reliability mode must accept a consequence-free model.");

        // Act
        await analysis.RunAsync();

        // The dense trapezoid reference over the same sampled math.
        var sampled = component.Sample(-1);
        var scratch = new ComponentRealization(sampled.FailureModeCount);
        var flags = new RiskComputeFlags();
        int gridCount = 20_000;
        double lower = 1e-16;
        double upper = 1d - 1e-16;
        double step = (upper - lower) / gridCount;
        double reference = 0d;
        double previous = FailureProbability(lower);
        for (int i = 1; i <= gridCount; i++)
        {
            double current = FailureProbability(lower + i * step);
            reference += 0.5d * (previous + current) * step;
            previous = current;
        }
        double FailureProbability(double probability)
        {
            return sampled.ComputeRisk(probability, sampled.Hazard.InverseCDF(probability), flags, scratch).ProbabilityOfFailure;
        }

        // Assert — the AFP at every level, and the degenerate consequence surface.
        Assert.IsTrue(analysis.IsEstimated);
        var summary = analysis.RiskResults![0]!;
        Assert.AreEqual(reference, summary.Fail.TotalProbability, 1e-3 * reference,
            $"System AFP {summary.Fail.TotalProbability} vs dense reference {reference}.");
        Assert.AreEqual(reference, analysis.MeanRiskResults!.Components[0].Curves.Fail.TotalProbability, 1e-3 * reference);
        Assert.AreEqual(reference, analysis.MeanRiskResults.Components[0].FailureModes[0].Curves.Fail.TotalProbability, 1e-3 * reference);
        Assert.AreEqual(0d, summary.Total.Mean, 1e-12, "A consequence-free model carries zero risk mean.");
        Assert.AreEqual(0d, analysis.ComputationWarnings.Count,
            "Reliability mode must not raise the mass-balance drift warning on its degenerate total stream.");
    }

    /// <summary>
    /// Verifies multi-component reliability (Phase 4b + 4c): the additive system annualized
    /// failure probability is the independent union of the component probabilities.
    /// </summary>
    [TestMethod]
    public async Task Test_Reliability_MultiComponent_UnionAfp()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { ReliabilityComponent("Dam"), ReliabilityComponent("Levee") });
        analysis.Options.Mode = RiskAnalysisMode.Reliability;

        // Act
        await analysis.RunAsync();

        // Assert
        var summary = analysis.RiskResults![0]!;
        double first = summary.ComponentResults[0].Fail.TotalProbability;
        double second = summary.ComponentResults[1].Fail.TotalProbability;
        Assert.IsTrue(first > 0d && second > 0d);
        double union = 1d - (1d - first) * (1d - second);
        Assert.AreEqual(union, summary.Fail.TotalProbability, 1e-12,
            "The reliability system AFP must be the independent union of the component AFPs.");
    }

    /// <summary>
    /// Verifies the reliability integrand forcing: in reliability mode the adaptive refinement
    /// objective is <see cref="RiskIntegrand.TotalProbabilityOfFailure"/> regardless of the
    /// configured option, so two runs differing only in <see cref="RiskAnalysisOptions.RiskIntegrand"/>
    /// are bit-identical.
    /// </summary>
    [TestMethod]
    public async Task Test_Reliability_EffectiveIntegrand_BitIdentical()
    {
        // Arrange
        static RiskAnalysis Build(RiskIntegrand integrand)
        {
            var analysis = new RiskAnalysis(new[] { ReliabilityComponent("Dam") });
            analysis.Options.Mode = RiskAnalysisMode.Reliability;
            analysis.Options.RiskIntegrand = integrand;
            return analysis;
        }
        var meanObjective = Build(RiskIntegrand.MeanTotalRisk);
        var failureObjective = Build(RiskIntegrand.TotalProbabilityOfFailure);

        // Act
        await meanObjective.RunAsync();
        await failureObjective.RunAsync();

        // Assert — identical refinement, identical bits.
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(meanObjective.RiskResults![0]!.Fail.TotalProbability),
            BitConverter.DoubleToInt64Bits(failureObjective.RiskResults![0]!.Fail.TotalProbability));
    }

    /// <summary>
    /// Verifies the joint full-uncertainty smoke at reduced budgets: the ensemble completes and
    /// the system percentile curves order.
    /// </summary>
    [TestMethod]
    public async Task Test_JointSystem_FullUncertainty_Smoke()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d), UncertainFragility()), ComponentB() });
        analysis.Options.SystemRiskMethod = SystemRiskType.JointRiskMethod;
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = 100;
        analysis.Options.UseDefaults = false;
        analysis.Options.WarmupEvaluations = 500;
        analysis.Options.WarmupCycles = 2;
        analysis.Options.FinalEvaluations = 1000;

        // Act
        await analysis.RunAsync();

        // Assert
        Assert.IsTrue(analysis.IsEstimated);
        Assert.AreEqual(100, analysis.RiskResults!.Count);
        Assert.IsNotNull(analysis.LowerRiskResults);
        Assert.IsNotNull(analysis.UpperRiskResults);
        double lower = analysis.LowerRiskResults!.Curves.Total.LEC.GetYFromX(60d, Transform.Logarithmic, Transform.Logarithmic);
        double upper = analysis.UpperRiskResults!.Curves.Total.LEC.GetYFromX(60d, Transform.Logarithmic, Transform.Logarithmic);
        Assert.IsTrue(lower <= upper + 1e-12, $"Joint percentile curves must order: {lower} ≤ {upper}.");
    }
}
