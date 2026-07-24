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
    /// Bit-pins a fully deterministic scenario across the Phase 6.7 landing stages: a
    /// deterministic model draws nothing from the seeded samplers, so these exact bit patterns
    /// must survive the BranchPolarity/ResponseNodes hash event (which moves only seeds) and the
    /// state-group engine rework (whose all-singleton path must reduce to today's arithmetic).
    /// Captured 2026-07-24 at the Stage 1 landing; any drift means math moved, not seeds.
    /// Re-capture ONLY for a documented math change, never for a seed event.
    /// </summary>
    [TestMethod]
    public async Task Test_Deterministic_BitPin_CascadePhases()
    {
        // Arrange — the standard deterministic scenario, mean-only.
        var meanOnly = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)) });

        // Act
        await meanOnly.RunAsync();

        // Assert — exact bit patterns of the headline scalars.
        var summary = meanOnly.RiskResults![0]!;
        Assert.AreEqual(4633156764016115401L, BitConverter.DoubleToInt64Bits(summary.Total.Mean), "Total.Mean moved.");
        Assert.AreEqual(4631174406377053994L, BitConverter.DoubleToInt64Bits(summary.Fail.Mean), "Fail.Mean moved.");
        Assert.AreEqual(4629978888563543066L, BitConverter.DoubleToInt64Bits(summary.Excess.Mean), "Excess.Mean moved.");
        Assert.AreEqual(4627048968587273581L, BitConverter.DoubleToInt64Bits(summary.Background.Mean), "Background.Mean moved.");
        Assert.AreEqual(4597854407371364657L, BitConverter.DoubleToInt64Bits(summary.Fail.TotalProbability), "Fail.TotalProbability moved.");

        // A deterministic full-uncertainty run pins the ensemble path too (every realization is
        // identical by construction; the value differs from the mean pass only by the documented
        // ensemble integration discipline).
        var full = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)) });
        full.Options.EstimateMeanRiskOnly = false;
        full.Options.Realizations = 100;
        await full.RunAsync();
        Assert.AreEqual(4633156864729413265L, BitConverter.DoubleToInt64Bits(full.RiskResults![50]!.Total.Mean),
            "Mid-ensemble realization Total.Mean moved.");
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
    /// Verifies the Phase 6.7 multi-stage acceptance end to end: a component carrying a
    /// single-stage mode AND a two-stage progression chain (each its own singleton combination
    /// unit) integrates to the same mean-only total risk as a dense independent trapezoid over
    /// the same sampled math, whose per-mode probabilities now include the polarity product.
    /// </summary>
    [TestMethod]
    public async Task Test_MeanOnly_TwoStage_MeanVsDenseReference()
    {
        // Arrange — the standard component plus a two-stage chain (stage 1's wider fragility
        // rises over (10 → 0, 30 → 1)).
        var wide = Fragility();
        wide.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(10d, new Deterministic(0d)), new UncertainOrdinate(30d, new Deterministic(1d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
        var component = Component(Consequence("Failure Loss", 300d));
        component.AddFailureMode(new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction>(), Fragility()),
                new ResponseStage(new List<ITransformFunction>(), wide),
            },
            null, new List<IConsequenceFunction> { Consequence("Chained Loss", 200d) }));
        var analysis = new RiskAnalysis(new[] { component });

        // Act
        await analysis.RunAsync();

        // The reference: a dense trapezoid over the mean sampled component.
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

        // Assert — the engine mean matches the dense reference within 0.1%, and the chained
        // mode genuinely contributed (the union exceeds the single-mode component's).
        double engineMean = analysis.RiskResults![0]!.Total.Mean;
        Assert.AreEqual(reference, engineMean, 1e-3 * reference,
            $"Engine mean {engineMean} vs dense reference {reference}.");

        var single = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)) });
        await single.RunAsync();
        Assert.IsTrue(analysis.RiskResults[0]!.Fail.TotalProbability > single.RiskResults![0]!.Fail.TotalProbability,
            "The two-stage mode must add failure probability to the union.");
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
        double positiveJoint = await RunApf(FailureModeMethod.JointFailures, DependencyType.PerfectlyPositive);
        double positiveCommonCause = await RunApf(FailureModeMethod.CommonCauseFailures, DependencyType.PerfectlyPositive);

        // Assert — same marginals + same dependency ⇒ same union across the combination methods.
        Assert.AreEqual(negativeJoint, negativeCommonCause, 1e-6 * negativeJoint,
            "Joint and common-cause must produce the same failure union under the same copula.");
        Assert.AreEqual(negativeJoint, negativeCompeting, 1e-2 * negativeJoint,
            "Competing must match the union within its 200-bin cumulative-incidence discretization.");
        Assert.IsTrue(negativeJoint > independentJoint * 1.001d,
            $"Negative dependence must raise the failure union (negative {negativeJoint} vs independent {independentJoint}).");

        // The perfectly-positive union (the lower unimodal bound) agrees across methods too —
        // the common-cause factor faulted here before the Phase 5 correction (the Numerics
        // overload rejects a null matrix even though the positive kernel never reads it).
        Assert.AreEqual(positiveJoint, positiveCommonCause, 1e-6 * positiveJoint,
            "Joint and common-cause must produce the same failure union under perfect positive dependence.");
        Assert.IsTrue(positiveJoint < independentJoint,
            $"Positive dependence must lower the failure union (positive {positiveJoint} vs independent {independentJoint}).");
    }

    /// <summary>
    /// Verifies the validation catalog with its pinned messages: the empty analysis, the
    /// additive method's strict-independence requirement (ratified v0.13), the joint method's
    /// dimension limit and correlation-matrix checks, and the transitional Phase 6.7 cascade
    /// gate (single-terminal chains compute; state-group configurations wait for the group
    /// layer). Two independent additive components and reliability mode now validate — their
    /// Phase 4 gates are gone, as is the Q-X multi-stage gate.
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

        // The Q-X multi-stage gate is gone: a single-terminal two-stage chain validates and
        // computes (Phase 6.7 multi-stage acceptance).
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
        Assert.IsTrue(multiStageAnalysis.Validate().IsValid,
            string.Join(" | ", multiStageAnalysis.Validate().ValidationMessages));

        // The state-group layer accepts the cascade configurations the transitional Stage 2
        // gate held back: a Non-Fail-final end state (a branch-scoped non-failure consequence)
        // and both-port divergence now validate.
        var partialOnly = Component(Consequence("A", 300d));
        partialOnly.AddFailureMode(new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction>(), Fragility(), BranchPolarity.NonFail),
            },
            null, new List<IConsequenceFunction> { Consequence("Partial", 50d) }));
        Assert.IsTrue(new RiskAnalysis(new[] { partialOnly }).Validate().IsValid);

        var divergent = Component(Consequence("A", 300d));
        var response = divergent.Graph.GetElements<RMC.TotalRisk.Systems.Components.Graph.ResponseElement>().First();
        var partialTerminal = new RMC.TotalRisk.Systems.Components.Graph.ConsequenceElement("Partial Damages")
        {
            Input = new RMC.TotalRisk.Systems.Components.Graph.RiskConnection(response, 1),
        };
        partialTerminal.Functions.Add(Consequence("Partial", 50d));
        divergent.Graph.AddElement(partialTerminal);
        Assert.IsTrue(new RiskAnalysis(new[] { divergent }).Validate().IsValid);

        // The §7.9 gates that stay: a second claiming state group, and competing over an
        // else-chain failure state.
        var doubleClaim = Component(Consequence("A", 300d));
        var claimResponse = doubleClaim.Graph.GetElements<RMC.TotalRisk.Systems.Components.Graph.ResponseElement>().First();
        var claimOne = new RMC.TotalRisk.Systems.Components.Graph.ConsequenceElement("Partial One")
        {
            Input = new RMC.TotalRisk.Systems.Components.Graph.RiskConnection(claimResponse, 1),
        };
        claimOne.Functions.Add(Consequence("Partial 1", 50d));
        doubleClaim.Graph.AddElement(claimOne);
        var secondResponse = new RMC.TotalRisk.Systems.Components.Graph.ResponseElement("Second Response")
        {
            Function = Fragility(),
            Input = new RMC.TotalRisk.Systems.Components.Graph.RiskConnection(doubleClaim.Graph.GetElements<RMC.TotalRisk.Systems.Components.Graph.HazardElement>().First()),
        };
        var claimTwoFail = new RMC.TotalRisk.Systems.Components.Graph.ConsequenceElement("Second Failure")
        {
            Input = new RMC.TotalRisk.Systems.Components.Graph.RiskConnection(secondResponse),
        };
        claimTwoFail.Functions.Add(Consequence("Second Loss", 100d));
        var claimTwo = new RMC.TotalRisk.Systems.Components.Graph.ConsequenceElement("Partial Two")
        {
            Input = new RMC.TotalRisk.Systems.Components.Graph.RiskConnection(secondResponse, 1),
        };
        claimTwo.Functions.Add(Consequence("Partial 2", 25d));
        doubleClaim.Graph.AddElement(secondResponse);
        doubleClaim.Graph.AddElement(claimTwoFail);
        doubleClaim.Graph.AddElement(claimTwo);
        Assert.IsTrue(new RiskAnalysis(new[] { doubleClaim }).Validate().ValidationMessages
            .Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("one state group")));

        var elseChain = Component(Consequence("A", 300d));
        var elseRoot = elseChain.Graph.GetElements<RMC.TotalRisk.Systems.Components.Graph.ResponseElement>().First();
        var elseNext = new RMC.TotalRisk.Systems.Components.Graph.ResponseElement("Else Response")
        {
            Function = Fragility(),
            Input = new RMC.TotalRisk.Systems.Components.Graph.RiskConnection(elseRoot, 1),
        };
        var elseTerminal = new RMC.TotalRisk.Systems.Components.Graph.ConsequenceElement("Else Failure")
        {
            Input = new RMC.TotalRisk.Systems.Components.Graph.RiskConnection(elseNext),
        };
        elseTerminal.Functions.Add(Consequence("Else Loss", 100d));
        elseChain.Graph.AddElement(elseNext);
        elseChain.Graph.AddElement(elseTerminal);
        elseChain.FailureModeMethod = FailureModeMethod.CompetingFailures;
        Assert.IsTrue(new RiskAnalysis(new[] { elseChain }).Validate().ValidationMessages
            .Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("weak-link")));
        elseChain.FailureModeMethod = FailureModeMethod.JointFailures;
        Assert.IsTrue(new RiskAnalysis(new[] { elseChain }).Validate().IsValid,
            "The else-chain is legal outside the competing method.");
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

        // Assert — configuration only: the options child and the declared-axis child (empty
        // here — the legacy single-type declaration), nothing else.
        Assert.AreEqual(2, element.Elements().Count());
        Assert.AreEqual(nameof(RiskAnalysisOptions), element.Elements().First().Name.LocalName);
        Assert.AreEqual(nameof(RiskAnalysis.AdditionalConsequenceTypes), element.Elements().Skip(1).First().Name.LocalName);
        Assert.AreEqual(0, restored.AdditionalConsequenceTypes.Count);
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

    /// <summary>Builds a labeled deterministic damages consequence: linear from (0 → 0) to (30 → valueAtThirty).</summary>
    private static TabularConsequence Damages(string name, double valueAtThirty)
    {
        var damages = Consequence(name, valueAtThirty);
        damages.SpecifiedConsequence = "Damages";
        damages.ConsequenceUnit = "$";
        return damages;
    }

    /// <summary>Builds a component whose failure and non-failure paths both carry the two-type axis [Life Loss, Damages].</summary>
    private static SystemComponent TwoTypeComponent(IResponseFunction? response = null,
        double failureDamagesAtThirty = 5_000_000d, double nonFailureDamagesAtThirty = 1_000_000d)
    {
        var component = new SystemComponent { Name = "Dam" };
        component.HazardFunction = StageFrequency();
        var failure = new FailureMode(null, null, response ?? Fragility(), Consequence("Failure Loss", 300d));
        failure.ConsequenceFunctions.Add(Damages("Failure Damages", failureDamagesAtThirty));
        component.AddFailureMode(failure);
        var nonFailure = new FailureMode(null, null, null, Consequence("Non-Failure Loss", 60d));
        nonFailure.ConsequenceFunctions.Add(Damages("Non-Failure Damages", nonFailureDamagesAtThirty));
        component.AddFailureMode(nonFailure);
        return component;
    }

    /// <summary>Declares the [Life Loss, Damages] axis on an analysis over the given components.</summary>
    private static RiskAnalysis TwoTypeAnalysis(params SystemComponent[] components)
    {
        var analysis = new RiskAnalysis(components)
        {
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        analysis.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Damages", "$"));
        return analysis;
    }

    /// <summary>
    /// The Phase 6.5 mean-pass equivalence pin (Q-U closure): adding a second consequence type
    /// leaves the primary type's mean-pass results bit-identical to the single-type run (the
    /// mean pass is seed-free and refinement is primary-driven), and a secondary type that is an
    /// exact scalar multiple of the primary reproduces every stream scaled — with identical
    /// per-type failure probabilities.
    /// </summary>
    [TestMethod]
    public async Task Test_MultiConsequence_MeanPass_PrimaryBitIdentical_SecondaryScales()
    {
        // Arrange — damages are exactly 1000 × lives at every hazard level.
        const double scale = 1000d;
        var singleType = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)) });
        var twoType = TwoTypeAnalysis(TwoTypeComponent(null, 300d * scale, 60d * scale));

        // Act
        await singleType.RunAsync();
        await twoType.RunAsync();

        // Assert — the primary axis is bit-identical to the single-type run.
        var single = singleType.MeanRiskResults!.Curves;
        var primary = twoType.MeanRiskResults!.Curves;
        Assert.AreEqual(single.Total.Mean, primary.Total.Mean, 0d, "The primary mean-pass mean must be bit-identical.");
        Assert.AreEqual(single.Fail.TotalProbability, primary.Fail.TotalProbability, 0d, "The primary failure probability must be bit-identical.");
        Assert.AreEqual(single.Excess.Mean, primary.Excess.Mean, 0d, "The primary excess mean must be bit-identical.");
        CollectionAssert.AreEqual(single.Total.LECConsequences, primary.Total.LECConsequences, "The primary Total LEC must be bit-identical.");
        CollectionAssert.AreEqual(single.Total.LECProbabilities, primary.Total.LECProbabilities, "The primary Total LEC probabilities must be bit-identical.");

        // The secondary axis exists at every scope and scales exactly.
        Assert.AreEqual(1, twoType.MeanRiskResults.AdditionalCurves.Count);
        var secondary = twoType.MeanRiskResults.AdditionalCurves[0];
        Assert.AreEqual(primary.Total.Mean * scale, secondary.Total.Mean, 1e-9 * primary.Total.Mean * scale,
            "A secondary type that is 1000 × the primary must produce 1000 × the mean.");
        Assert.AreEqual(primary.Excess.Mean * scale, secondary.Excess.Mean, 1e-9 * Math.Max(1d, primary.Excess.Mean * scale));
        Assert.AreEqual(primary.NonFail.Mean * scale, secondary.NonFail.Mean, 1e-9 * Math.Max(1d, primary.NonFail.Mean * scale));

        // Probability streams are identical across types (weights per type sum to one).
        Assert.AreEqual(primary.Fail.TotalProbability, secondary.Fail.TotalProbability, 1e-12 * primary.Fail.TotalProbability,
            "Per-type failure probabilities must agree — the probability structure is shared.");
        Assert.AreEqual(primary.Total.MassBalance, secondary.Total.MassBalance, 1e-12,
            "Per-type exhaustive mass must agree.");

        // The component and failure-mode scopes carry the secondary axis too.
        var component = twoType.MeanRiskResults.Components[0];
        Assert.AreEqual(1, component.AdditionalCurves.Count);
        Assert.IsTrue(component.AdditionalCurves[0].Total.LECConsequences.Length > 2);
        Assert.AreEqual(1, component.FailureModes[0].AdditionalCurves.Count);
        Assert.IsTrue(component.FailureModes[0].AdditionalCurves[0].Fail.LECConsequences.Length > 2);

        // Secondary assurance is NaN (the threshold is declared in the primary type's units).
        Assert.IsTrue(double.IsNaN(secondary.Total.ConsequenceThresholdProbability));
        Assert.IsFalse(double.IsNaN(primary.Total.ConsequenceThresholdProbability));

        // The summary tree carries the secondary axis with the declared labels (Phase 6.5).
        var summary = twoType.RiskResults![0]!;
        Assert.AreEqual(1, summary.AdditionalConsequences.Count);
        Assert.AreEqual("Damages", summary.AdditionalConsequences[0].SpecifiedConsequence);
        Assert.AreEqual("$", summary.AdditionalConsequences[0].ConsequenceUnit);
        Assert.AreEqual(secondary.Total.Mean, summary.AdditionalConsequences[0].Total.Mean, 0d);
        CollectionAssert.AreEqual(new[] { "Life Loss", "Damages" }, summary.ConsequenceLabels);
        CollectionAssert.AreEqual(new[] { "lives", "$" }, summary.ConsequenceUnits);
        Assert.AreEqual(1, summary.ComponentResults[0].AdditionalConsequences.Count);
        Assert.AreEqual(1, summary.ComponentResults[0].FailureModeResults[0].AdditionalConsequences.Count);
    }

    /// <summary>
    /// Verifies the K = 2 full-uncertainty smoke: the ensemble runs, the percentile realizations
    /// carry the secondary axis on its own grid, and the secondary percentile curves order.
    /// </summary>
    [TestMethod]
    public async Task Test_MultiConsequence_FullUncertainty_Smoke()
    {
        // Arrange
        var analysis = TwoTypeAnalysis(TwoTypeComponent(UncertainFragility(), 300_000d, 60_000d));
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = 100;

        // Act
        await analysis.RunAsync();

        // Assert
        Assert.IsTrue(analysis.IsEstimated);
        Assert.AreEqual(1, analysis.MeanRiskResults!.AdditionalCurves.Count);
        Assert.AreEqual(1, analysis.LowerRiskResults!.AdditionalCurves.Count);
        Assert.IsTrue(analysis.MeanRiskResults.AdditionalCurves[0].Total.LECConsequences.Length > 2,
            "The secondary ensemble-mean curve must be assembled on its own grid.");

        double lower = analysis.LowerRiskResults!.AdditionalCurves[0].Fail.LEC.GetYFromX(50_000d, Transform.Logarithmic, Transform.Logarithmic);
        double median = analysis.MedianRiskResults!.AdditionalCurves[0].Fail.LEC.GetYFromX(50_000d, Transform.Logarithmic, Transform.Logarithmic);
        double upper = analysis.UpperRiskResults!.AdditionalCurves[0].Fail.LEC.GetYFromX(50_000d, Transform.Logarithmic, Transform.Logarithmic);
        Assert.IsTrue(lower <= median + 1e-12 && median <= upper + 1e-12,
            $"Secondary percentile curves must order: {lower} ≤ {median} ≤ {upper}.");
    }

    /// <summary>
    /// Verifies the K = 2 additive system: both types convolve onto system curves, the
    /// type-independent failure union is shared, and a proportional secondary type scales the
    /// convolved system mean.
    /// </summary>
    [TestMethod]
    public async Task Test_MultiConsequence_AdditiveSystem_SecondaryScales()
    {
        // Arrange — two independent two-type components, damages = 1000 × lives on both.
        const double scale = 1000d;
        var analysis = TwoTypeAnalysis(
            TwoTypeComponent(null, 300d * scale, 60d * scale),
            TwoTypeComponent(null, 300d * scale, 60d * scale));

        // Act
        await analysis.RunAsync();

        // Assert
        var system = analysis.MeanRiskResults!;
        Assert.AreEqual(1, system.AdditionalCurves.Count);
        var primary = system.Curves;
        var secondary = system.AdditionalCurves[0];
        Assert.IsTrue(secondary.Total.LECConsequences.Length > 2, "The secondary system Total must be convolved.");
        Assert.AreEqual(primary.Total.Mean * scale, secondary.Total.Mean, 1e-9 * primary.Total.Mean * scale,
            "The convolved secondary system mean must scale with the type.");
        Assert.AreEqual(primary.Fail.TotalProbability, secondary.Fail.TotalProbability, 0d,
            "The failure union is type-independent and shared verbatim.");
    }

    /// <summary>
    /// Verifies the K = 2 joint system: both types record through the VEGAS combination
    /// enumeration and a proportional secondary type scales the system mean under the additive
    /// joint-consequence rule.
    /// </summary>
    [TestMethod]
    public async Task Test_MultiConsequence_JointSystem_SecondaryScales()
    {
        // Arrange
        const double scale = 1000d;
        var analysis = TwoTypeAnalysis(
            TwoTypeComponent(null, 300d * scale, 60d * scale),
            TwoTypeComponent(null, 300d * scale, 60d * scale));
        analysis.Options.SystemRiskMethod = SystemRiskType.JointRiskMethod;
        analysis.Options.UseDefaults = false;
        analysis.Options.WarmupEvaluations = 500;
        analysis.Options.WarmupCycles = 2;
        analysis.Options.FinalEvaluations = 1000;

        // Act
        await analysis.RunAsync();

        // Assert
        var system = analysis.MeanRiskResults!;
        Assert.AreEqual(1, system.AdditionalCurves.Count);
        Assert.IsTrue(system.AdditionalCurves[0].Total.LECConsequences.Length > 2);
        Assert.AreEqual(system.Curves.Total.Mean * scale, system.AdditionalCurves[0].Total.Mean,
            1e-9 * system.Curves.Total.Mean * scale,
            "The joint secondary system mean must scale with the type.");
        Assert.AreEqual(system.Curves.Fail.TotalProbability, system.AdditionalCurves[0].Fail.TotalProbability,
            1e-12 * system.Curves.Fail.TotalProbability,
            "Per-type failure mass must agree on the joint path.");
    }

    /// <summary>
    /// Verifies reliability mode carries no consequence-type axis: consequence-free modes
    /// produce no additional curve sets.
    /// </summary>
    [TestMethod]
    public async Task Test_MultiConsequence_Reliability_NoAdditionalCurves()
    {
        // Arrange — a consequence-free reliability model with a declared (inert) second type.
        var component = new SystemComponent { Name = "Dam" };
        component.HazardFunction = StageFrequency();
        component.AddFailureMode(new FailureMode(null, null, Fragility(), null));
        var analysis = new RiskAnalysis(new[] { component });
        analysis.Options.Mode = RiskAnalysisMode.Reliability;
        analysis.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Damages", "$"));

        // Act
        await analysis.RunAsync();

        // Assert
        Assert.IsTrue(analysis.IsEstimated);
        Assert.AreEqual(0, analysis.MeanRiskResults!.AdditionalCurves.Count);
    }

    /// <summary>
    /// Verifies the declared consequence-type axis round-trips through the configuration
    /// serialization (Phase 6.5): order and labels survive, and an axis-free legacy form
    /// restores the single-type declaration.
    /// </summary>
    [TestMethod]
    public void Test_AdditionalConsequenceTypes_SerializationRoundTrip()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) })
        {
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        analysis.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Damages", "$"));
        analysis.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Environmental", "acres"));

        // Act
        var restored = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) }, analysis.ToXElement());

        // Assert — order and labels survive.
        Assert.AreEqual(2, restored.AdditionalConsequenceTypes.Count);
        Assert.AreEqual("Damages", restored.AdditionalConsequenceTypes[0].SpecifiedConsequence);
        Assert.AreEqual("$", restored.AdditionalConsequenceTypes[0].ConsequenceUnit);
        Assert.AreEqual("Environmental", restored.AdditionalConsequenceTypes[1].SpecifiedConsequence);
        Assert.AreEqual("acres", restored.AdditionalConsequenceTypes[1].ConsequenceUnit);

        // An axis-free legacy form restores the single-type declaration.
        var legacyElement = analysis.ToXElement();
        legacyElement.Element(nameof(RiskAnalysis.AdditionalConsequenceTypes))!.Remove();
        var legacy = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) }, legacyElement);
        Assert.AreEqual(0, legacy.AdditionalConsequenceTypes.Count);
    }

    /// <summary>
    /// Verifies the declared-axis count gate (Phase 6.5, user-ratified): every failure and
    /// non-failure path must carry exactly one consequence function per declared type — a
    /// two-type declaration over single-consequence paths errors, the two-type component
    /// satisfies it, and the two-type component under the legacy single-type declaration errors
    /// the other way.
    /// </summary>
    [TestMethod]
    public void Test_Validate_ConsequenceTypeAxis_Counts()
    {
        // A K = 2 declaration over single-consequence paths: every path errors.
        var underDeclared = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) });
        underDeclared.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Damages", "$"));
        var (isValid, messages) = underDeclared.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("declares 2 consequence type(s)")),
            string.Join("; ", messages));

        // The two-type component satisfies the K = 2 declaration.
        var matched = new RiskAnalysis(new[] { TwoTypeComponent() })
        {
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        matched.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Damages", "$"));
        var matchedResult = matched.Validate();
        Assert.IsTrue(matchedResult.IsValid, string.Join("; ", matchedResult.ValidationMessages));

        // The two-type component under a K = 1 declaration errors the other way.
        var overCarried = new RiskAnalysis(new[] { TwoTypeComponent() });
        var overResult = overCarried.Validate();
        Assert.IsFalse(overResult.IsValid);
        Assert.IsTrue(overResult.ValidationMessages.Any(m => m.Contains("declares 1 consequence type(s)")),
            string.Join("; ", overResult.ValidationMessages));
    }

    /// <summary>
    /// Verifies the declared-axis label gate: non-blank labels and units must agree per
    /// position (ordinal, case-insensitive), blank on either side is a wildcard, and
    /// reliability mode ignores the axis entirely.
    /// </summary>
    [TestMethod]
    public void Test_Validate_ConsequenceTypeAxis_Labels()
    {
        // A mismatched non-blank label at position 1 errors.
        var mismatched = new RiskAnalysis(new[] { TwoTypeComponent() })
        {
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        mismatched.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Environmental", "$"));
        var (labelValid, labelMessages) = mismatched.Validate();
        Assert.IsFalse(labelValid);
        Assert.IsTrue(labelMessages.Any(m => m.Contains("position 1") && m.Contains("'Damages'") && m.Contains("'Environmental'")),
            string.Join("; ", labelMessages));

        // A mismatched non-blank unit errors.
        var unitMismatch = new RiskAnalysis(new[] { TwoTypeComponent() })
        {
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        unitMismatch.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Damages", "EUR"));
        var (unitValid, unitMessages) = unitMismatch.Validate();
        Assert.IsFalse(unitValid);
        Assert.IsTrue(unitMessages.Any(m => m.Contains("unit '$'") && m.Contains("'EUR'")),
            string.Join("; ", unitMessages));

        // Blank declarations are wildcards: an all-blank K = 2 axis accepts labeled functions.
        var wildcard = new RiskAnalysis(new[] { TwoTypeComponent() });
        wildcard.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor(string.Empty, string.Empty));
        var wildcardResult = wildcard.Validate();
        Assert.IsTrue(wildcardResult.IsValid, string.Join("; ", wildcardResult.ValidationMessages));

        // Case difference is not a mismatch.
        var cased = new RiskAnalysis(new[] { TwoTypeComponent() })
        {
            SpecifiedConsequence = "LIFE LOSS",
            ConsequenceUnit = "LIVES",
        };
        cased.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("damages", "$"));
        Assert.IsTrue(cased.Validate().IsValid, string.Join("; ", cased.Validate().ValidationMessages));

        // Reliability mode ignores the axis (consequence-free models declare nothing usable).
        var reliability = new RiskAnalysis(new[] { Component(Consequence("A", 300d)) });
        reliability.Options.Mode = RiskAnalysisMode.Reliability;
        reliability.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Damages", "$"));
        Assert.IsTrue(reliability.Validate().IsValid, string.Join("; ", reliability.Validate().ValidationMessages));
    }

    /// <summary>
    /// Verifies the hazard-axis consistency advisory: components whose driving hazards disagree
    /// on non-blank labels warn (one analysis models one hazard axis) without invalidating.
    /// </summary>
    [TestMethod]
    public void Test_Validate_HazardAxis_MismatchWarns()
    {
        // Arrange — two additive components whose hazards are labeled differently.
        var flowComponent = Component(Consequence("B", 300d));
        flowComponent.Name = "Levee";
        flowComponent.HazardFunction!.SpecifiedHazard = "Flow";
        var analysis = new RiskAnalysis(new[] { Component(Consequence("A", 300d)), flowComponent });

        // Act
        var (isValid, messages) = analysis.Validate();

        // Assert — advisory only.
        Assert.IsTrue(isValid, string.Join("; ", messages));
        Assert.IsTrue(messages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("share the driving hazard axis")),
            string.Join("; ", messages));
    }

    /// <summary>
    /// Verifies the per-type consequence thresholds (Phase 6.6): a declared secondary threshold
    /// computes the secondary assurance measure at every scope — and on an exactly scaled
    /// secondary axis, the scaled threshold reads the same probability as the primary — while
    /// an undeclared secondary threshold preserves the Phase 6.5 primary-only interim (NaN).
    /// </summary>
    [TestMethod]
    public async Task Test_PerTypeConsequenceThresholds_ComputedAndInterimPreserved()
    {
        // Arrange — damages are exactly 1000 × lives, and the declared damages threshold is
        // exactly 1000 × the primary threshold, so both assurance reads must agree.
        const double scale = 1000d;
        const double primaryThreshold = 50d;
        var declared = new RiskAnalysis(new[] { TwoTypeComponent(null, 300d * scale, 60d * scale) })
        {
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        declared.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Damages", "$", primaryThreshold * scale));
        declared.Options.ConsequenceThreshold = primaryThreshold;

        // Act
        await declared.RunAsync();

        // Assert — the secondary assurance measure computes at system, component, and mode scope.
        var summary = declared.RiskResults![0]!;
        double primaryProbability = summary.Total.ConsequenceThresholdProbability;
        double secondaryProbability = summary.AdditionalConsequences[0].Total.ConsequenceThresholdProbability;
        Assert.IsTrue(primaryProbability > 0d && primaryProbability < 1d, "The primary threshold must read an interior probability.");
        Assert.AreEqual(primaryProbability, secondaryProbability, 1e-9 * primaryProbability,
            "The scaled threshold on the exactly scaled axis must read the same probability.");
        Assert.AreEqual(primaryProbability,
            summary.ComponentResults[0].AdditionalConsequences[0].Total.ConsequenceThresholdProbability,
            1e-9 * primaryProbability, "The component scope must carry the per-type threshold too.");

        // The undeclared shape preserves the primary-only interim: secondary assurance NaN.
        var interim = TwoTypeAnalysis(TwoTypeComponent(null, 300d * scale, 60d * scale));
        interim.Options.ConsequenceThreshold = primaryThreshold;
        await interim.RunAsync();
        Assert.IsFalse(double.IsNaN(interim.RiskResults![0]!.Total.ConsequenceThresholdProbability));
        Assert.IsTrue(double.IsNaN(interim.RiskResults[0]!.AdditionalConsequences[0].Total.ConsequenceThresholdProbability),
            "An undeclared per-type threshold must preserve the Phase 6.5 interim (NaN assurance).");
    }

    /// <summary>Builds a two-mode component with a non-failure path under the given combination method.</summary>
    private static SystemComponent TwoModeMethodComponent(FailureModeMethod method,
        JointConsequenceType jointConsequences = JointConsequenceType.Maximum)
    {
        var component = new SystemComponent { Name = "Two Modes" };
        component.HazardFunction = StageFrequency();
        var fragilityB = new TabularResponse
        {
            Name = "Mode B",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(10d, new Deterministic(0d)), new UncertainOrdinate(30d, new Deterministic(1d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
        component.AddFailureMode(new FailureMode(null, null, Fragility(), Consequence("A Loss", 300d)));
        component.AddFailureMode(new FailureMode(null, null, fragilityB, Consequence("B Loss", 600d)));
        component.AddFailureMode(new FailureMode(null, null, null, Consequence("Non-Failure Loss", 60d)));
        component.FailureModeMethod = method;
        component.JointConsequences = jointConsequences;
        return component;
    }

    /// <summary>
    /// Verifies the % contribution additivity identities across all four combination methods on
    /// the mean pass: Σ mode contributions ≡ the component's raw recorded totals — the Fail
    /// mass balance, the Fail mean, and the Excess mean — to floating-point association, and
    /// the compact summaries carry the same values.
    /// </summary>
    [TestMethod]
    public async Task Test_Contribution_SumIdentities_AllMethods()
    {
        foreach (FailureModeMethod method in new[]
        {
            FailureModeMethod.JointFailures, FailureModeMethod.CommonCauseFailures,
            FailureModeMethod.CompetingFailures, FailureModeMethod.MutuallyExclusive,
        })
        {
            // Arrange / Act
            var analysis = new RiskAnalysis(new[] { TwoModeMethodComponent(method) });
            await analysis.RunAsync();

            // Assert — Σ mode contributions ≡ the component's recorded totals.
            var component = analysis.MeanRiskResults!.Components[0];
            double sumProbability = 0d, sumFailure = 0d, sumExcess = 0d;
            for (int j = 0; j < component.FailureModes.Count; j++)
            {
                var contribution = component.FailureModes[j].Contribution;
                Assert.IsNotNull(contribution, $"Mode {j} must carry a contribution under {method}.");
                sumProbability += contribution!.FailureProbability;
                sumFailure += contribution.FailureMean;
                sumExcess += contribution.ExcessMean;
            }
            Assert.AreEqual(component.Curves.Fail.MassBalance, sumProbability, 1e-12 * component.Curves.Fail.MassBalance,
                $"Σ probability contributions must equal the Fail mass balance under {method}.");
            Assert.AreEqual(component.Curves.Fail.Mean, sumFailure, 1e-12 * component.Curves.Fail.Mean,
                $"Σ failure-mean contributions must equal the Fail mean under {method}.");
            Assert.AreEqual(component.Curves.Excess.Mean, sumExcess, 1e-12 * component.Curves.Excess.Mean,
                $"Σ excess-mean contributions must equal the Excess mean under {method}.");

            // The compact summary copies the realization values.
            var summary = analysis.RiskResults![0]!.ComponentResults[0];
            Assert.AreEqual(component.FailureModes[0].Contribution!.FailureMean,
                summary.FailureModeResults[0].Contribution!.FailureMean, 0d,
                "The summary must copy the realization contribution.");
        }
    }

    /// <summary>
    /// Verifies the joint-Additive attribution identity: under the Sum rule the
    /// consequence-proportional split credits each mode exactly its own consequence, so a
    /// mode's attributed failure mean equals its marginal recorded Fail mean.
    /// </summary>
    [TestMethod]
    public async Task Test_Contribution_JointAdditive_EqualsMarginalFailMean()
    {
        // Arrange / Act
        var analysis = new RiskAnalysis(new[] { TwoModeMethodComponent(FailureModeMethod.JointFailures, JointConsequenceType.Additive) });
        await analysis.RunAsync();

        // Assert — attribution ≡ the marginal mode Fail mean, mode by mode.
        var component = analysis.MeanRiskResults!.Components[0];
        for (int j = 0; j < component.FailureModes.Count; j++)
        {
            double marginal = component.FailureModes[j].Curves.Fail.Mean;
            double attributed = component.FailureModes[j].Contribution!.FailureMean;
            Assert.AreEqual(marginal, attributed, 1e-12 * Math.Max(1d, marginal),
                $"Under the Sum rule mode {j}'s attribution must equal its marginal Fail mean.");
        }
    }

    /// <summary>
    /// Verifies the additive system's per-component contribution: the Shapley split of the
    /// independent failure union matches a brute-force enumeration of the 2^D exclusive
    /// combinations with equal splits, Σ shares ≡ the folded union, and the mean contributions
    /// are the component means (which sum to the convolved system mean).
    /// </summary>
    [TestMethod]
    public async Task Test_Contribution_AdditiveSystem_ShapleyVsBruteForce()
    {
        // Arrange — three components with distinct consequences.
        var analysis = new RiskAnalysis(new[]
        {
            Component(Consequence("A", 300d)),
            Component(Consequence("B", 600d)),
            Component(Consequence("C", 900d)),
        });

        // Act
        await analysis.RunAsync();

        // Assert — brute-force Shapley over the 2^3 exclusive combinations.
        var components = analysis.MeanRiskResults!.Components;
        var probabilities = new double[3];
        for (int i = 0; i < 3; i++)
        {
            probabilities[i] = components[i].Curves.Fail.TotalProbability;
        }
        var expectedShares = new double[3];
        for (int mask = 1; mask < 8; mask++)
        {
            double mass = 1d;
            int participants = 0;
            for (int i = 0; i < 3; i++)
            {
                bool fails = (mask & (1 << i)) != 0;
                mass *= fails ? probabilities[i] : 1d - probabilities[i];
                if (fails) participants++;
            }
            for (int i = 0; i < 3; i++)
            {
                if ((mask & (1 << i)) != 0) expectedShares[i] += mass / participants;
            }
        }

        double sumShares = 0d;
        double sumMeans = 0d;
        for (int i = 0; i < 3; i++)
        {
            var contribution = components[i].SystemContribution;
            Assert.IsNotNull(contribution, $"Component {i} must carry a system contribution.");
            Assert.AreEqual(expectedShares[i], contribution!.FailureProbability, 1e-12,
                $"Component {i}'s Shapley share must match the brute-force enumeration.");
            Assert.AreEqual(components[i].Curves.Fail.Mean, contribution.FailureMean, 0d,
                "The mean contribution is the component's own Fail mean (means add exactly).");
            sumShares += contribution.FailureProbability;
            sumMeans += contribution.FailureMean;
        }
        var system = analysis.MeanRiskResults.Curves;
        Assert.AreEqual(system.Fail.TotalProbability, sumShares, 1e-12,
            "Σ Shapley shares must equal the folded independent union.");
        Assert.AreEqual(system.Fail.Mean, sumMeans, 1e-9 * system.Fail.Mean,
            "Σ mean contributions must equal the convolved system Fail mean.");

        // The compact summary copies the component contribution.
        Assert.AreEqual(expectedShares[0],
            analysis.RiskResults![0]!.ComponentResults[0].SystemContribution!.FailureProbability, 1e-12);
    }

    /// <summary>
    /// Verifies the joint system's per-component contribution identities within one run: Σ
    /// attributed probabilities ≡ the recorded system Fail mass balance and Σ attributed means
    /// ≡ the recorded system Fail mean (the attribution splits the same recorded entries), on
    /// a reduced VEGAS budget.
    /// </summary>
    [TestMethod]
    public async Task Test_Contribution_JointSystem_SumIdentities()
    {
        // Arrange — two components on the joint path at a lean budget.
        var analysis = new RiskAnalysis(new[]
        {
            Component(Consequence("A", 300d)),
            Component(Consequence("B", 600d)),
        });
        analysis.Options.SystemRiskMethod = SystemRiskType.JointRiskMethod;
        analysis.Options.UseDefaults = false;
        analysis.Options.WarmupEvaluations = 1000;
        analysis.Options.WarmupCycles = 2;
        analysis.Options.FinalEvaluations = 2000;

        // Act
        await analysis.RunAsync();

        // Assert
        var components = analysis.MeanRiskResults!.Components;
        double sumProbability = 0d, sumFailure = 0d, sumExcess = 0d;
        for (int i = 0; i < components.Count; i++)
        {
            var contribution = components[i].SystemContribution;
            Assert.IsNotNull(contribution, $"Component {i} must carry a system contribution on the joint path.");
            sumProbability += contribution!.FailureProbability;
            sumFailure += contribution.FailureMean;
            sumExcess += contribution.ExcessMean;
        }
        var system = analysis.MeanRiskResults.Curves;
        Assert.AreEqual(system.Fail.MassBalance, sumProbability, 1e-12 * system.Fail.MassBalance,
            "Σ probability contributions must equal the recorded system Fail mass balance.");
        Assert.AreEqual(system.Fail.Mean, sumFailure, 1e-12 * system.Fail.Mean,
            "Σ failure-mean contributions must equal the recorded system Fail mean.");
        Assert.AreEqual(system.Excess.Mean, sumExcess, 1e-12 * Math.Max(1d, system.Excess.Mean),
            "Σ excess-mean contributions must equal the recorded system Excess mean.");

        // The failure modes carry contributions through the VEGAS mass regime too.
        Assert.IsNotNull(components[0].FailureModes[0].Contribution,
            "Mode-level contributions must finalize under the joint path's weight masses.");
    }

    /// <summary>
    /// Verifies contribution availability semantics: full-uncertainty ensemble summaries carry
    /// per-realization contributions, the assembled percentile band trees carry none (they are
    /// grid assemblies, not computed realizations), and a pre-6.6 summary payload without the
    /// members loads forward as null.
    /// </summary>
    [TestMethod]
    public async Task Test_Contribution_EnsembleAndBands_AndForwardLoad()
    {
        // Arrange / Act — a small full-uncertainty run.
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d), UncertainFragility()) });
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = 100;
        await analysis.RunAsync();

        // Assert — every ensemble summary carries contributions; the band trees carry none.
        for (int i = 0; i < analysis.RiskResults!.Count; i++)
        {
            Assert.IsNotNull(analysis.RiskResults[i]!.ComponentResults[0].FailureModeResults[0].Contribution,
                $"Realization {i}'s summary must carry the mode contribution.");
            Assert.IsNotNull(analysis.RiskResults[i]!.ComponentResults[0].SystemContribution,
                $"Realization {i}'s summary must carry the component's system contribution.");
        }
        Assert.IsNull(analysis.MeanRiskResults!.Components[0].FailureModes[0].Contribution,
            "The assembled band trees must carry no contributions (not computed).");

        // A pre-6.6 summary payload (members absent) loads forward as null.
        string json = analysis.RiskResults.ToJson();
        Assert.IsTrue(json.Contains("\"Contribution\""), "The new members must serialize.");
        string legacyJson = System.Text.RegularExpressions.Regex.Replace(json,
            "\"(Contribution|SystemContribution)\":(\\{[^}]*\\}|null),?", string.Empty)
            .Replace(",}", "}").Replace(",]", "]");
        var legacy = EnsembleResults.FromJson(legacyJson);
        Assert.IsNotNull(legacy);
        Assert.IsNull(legacy!.Realizations[0]!.ComponentResults[0].FailureModeResults[0].Contribution,
            "A payload without the members must load forward as null.");
    }

    /// <summary>
    /// Verifies the ensemble scalar summary (Phase 6.6): a full-uncertainty run populates
    /// <c>RiskResults.Summary</c> with ordered percentile trees whose mean slot is the
    /// sequential ensemble mean, the stored summary reproduces bit-for-bit from a JSON
    /// round-trip via <c>ComputeSummary</c>, convergence indicators aggregate the integrator
    /// diagnostics, and a mean-only run carries no summary.
    /// </summary>
    [TestMethod]
    public async Task Test_EnsembleSummary_FullRun_ScalarIntervals()
    {
        // Arrange / Act
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d), UncertainFragility()) });
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = 200;
        await analysis.RunAsync();

        // Assert — presence, ordering, and the sequential mean identity.
        var summary = analysis.RiskResults!.Summary;
        Assert.IsNotNull(summary, "A full-uncertainty run must populate the scalar summary.");
        Assert.AreEqual(200, summary!.RealizationCount);
        Assert.IsTrue(summary.Lower.Total.Mean <= summary.Median.Total.Mean && summary.Median.Total.Mean <= summary.Upper.Total.Mean,
            "The percentile slots must be ordered.");
        Assert.IsTrue(summary.Lower.Fail.TotalProbability <= summary.Upper.Fail.TotalProbability);
        double sequentialMean = 0d;
        for (int i = 0; i < analysis.RiskResults.Count; i++)
        {
            sequentialMean += analysis.RiskResults[i]!.Total.Mean;
        }
        sequentialMean /= analysis.RiskResults.Count;
        Assert.AreEqual(sequentialMean, summary.Mean.Total.Mean, 0d,
            "The mean slot must be the sequential ensemble mean, bit for bit.");

        // Per-scope intervals exist down to the failure-mode contribution.
        Assert.IsNotNull(summary.Mean.ComponentResults[0].FailureModeResults[0].Contribution,
            "Contribution intervals must reduce alongside the measure catalog.");
        Assert.IsTrue(summary.Mean.ComponentResults[0].Fail.TotalProbability > 0d);

        // Convergence: totals aggregate, and the headline indicators are populated.
        Assert.IsTrue(summary.Convergence.TotalFunctionEvaluations > 0d);
        Assert.AreEqual("Annualized Failure Probability", summary.Convergence.Indicators[0].Label);
        Assert.IsTrue(summary.Convergence.Indicators[0].EnsembleStandardError > 0d);
        Assert.IsTrue(summary.Convergence.Indicators[0].CiHalfWidth > 0d);

        // The stored summary reproduces from a JSON round-trip.
        var restored = EnsembleResults.FromJson(analysis.RiskResults.ToJson())!;
        var recomputed = restored.ComputeSummary(analysis.Options.ConfidenceIntervalWidth)!;
        Assert.AreEqual(summary.Mean.Total.Mean, recomputed.Mean.Total.Mean, 0d);
        Assert.AreEqual(summary.Upper.Total.ConditionalValueAtRisk, recomputed.Upper.Total.ConditionalValueAtRisk, 0d);
        Assert.AreEqual(summary.Lower.Fail.TotalProbability, recomputed.Lower.Fail.TotalProbability, 0d);

        // Mean-only runs carry no summary.
        var meanOnly = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)) });
        await meanOnly.RunAsync();
        Assert.IsNull(meanOnly.RiskResults!.Summary, "Mean-only runs have a single realization — no scalar intervals.");
    }

    /// <summary>Builds the profile-remap engine fixture: flow hazard → rating (T(h) = h/2) → uncertain stage fragility → stage consequences.</summary>
    private static SystemComponent RemapEngineComponent()
    {
        var hazard = new TabularHazard
        {
            Name = "Flow Frequency",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            NoUncertaintyFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Deterministic(0d)),
                    new UncertainOrdinate(0.5d, new Deterministic(50d)),
                    new UncertainOrdinate(0.001d, new Deterministic(100d)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Deterministic),
        };
        var rating = new RMC.TotalRisk.RiskFunctions.Transforms.TabularTransform
        {
            Name = "Rating",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(50d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
        var fragility = UncertainFragility();
        var component = new SystemComponent { Name = "Dam" };
        component.HazardFunction = hazard;
        component.AddFailureMode(new FailureMode(new List<ITransformFunction> { rating }, null, fragility, Consequence("Failure Loss", 300d)));
        component.AddFailureMode(new FailureMode(null, null, null, Consequence("Non-Failure Loss", 60d)));
        component.HazardThreshold = 40d;
        return component;
    }

    /// <summary>
    /// Verifies the ratified Q-T seed-inertness contract end to end: selecting a profile hazard
    /// element leaves every Monte Carlo stream and every non-profile output bit-identical — the
    /// LECs and the scalar measures match to the last bit across the full-uncertainty ensemble —
    /// while the profile surfaces (hazard-frequency axis, hazard-threshold probability, hazard
    /// extents) move to the selected axis.
    /// </summary>
    [TestMethod]
    public async Task Test_ProfileRemap_SeedInert_OnlyProfileSurfacesMove()
    {
        // Arrange — the same model twice; only B selects the rating element as the profile axis.
        var componentA = RemapEngineComponent();
        var componentB = RemapEngineComponent();
        componentB.SetProfileHazardElement(componentB.Graph.GetElements<RMC.TotalRisk.Systems.Components.Graph.TransformElement>().Single());
        CollectionAssert.AreEqual(componentA.CanonicalHash(), componentB.CanonicalHash(),
            "The profile selection must be seed-inert at the component-hash level.");

        var analysisA = new RiskAnalysis(new[] { componentA });
        var analysisB = new RiskAnalysis(new[] { componentB });
        analysisA.Options.EstimateMeanRiskOnly = false;
        analysisA.Options.Realizations = 100;
        analysisB.Options.EstimateMeanRiskOnly = false;
        analysisB.Options.Realizations = 100;

        // Act
        await analysisA.RunAsync();
        await analysisB.RunAsync();

        // Assert — every non-profile output is bit-identical across the ensemble.
        var meanA = analysisA.MeanRiskResults!;
        var meanB = analysisB.MeanRiskResults!;
        CollectionAssert.AreEqual(meanA.Curves.Total.LECConsequences, meanB.Curves.Total.LECConsequences,
            "The Total LEC must be bit-identical — the remap labels recorded points only.");
        CollectionAssert.AreEqual(meanA.Curves.Fail.LECProbabilities, meanB.Curves.Fail.LECProbabilities);
        for (int i = 0; i < analysisA.RiskResults!.Count; i++)
        {
            var a = analysisA.RiskResults[i]!;
            var b = analysisB.RiskResults![i]!;
            Assert.AreEqual(a.Fail.TotalProbability, b.Fail.TotalProbability, 0d, $"APF must be bit-identical (realization {i}).");
            Assert.AreEqual(a.Total.Mean, b.Total.Mean, 0d, $"The mean must be bit-identical (realization {i}).");
            Assert.AreEqual(a.Total.StandardDeviation, b.Total.StandardDeviation, 0d);
            Assert.AreEqual(a.Total.ValueAtRisk, b.Total.ValueAtRisk, 0d);
            Assert.AreEqual(a.Total.ConditionalValueAtRisk, b.Total.ConditionalValueAtRisk, 0d);
        }

        // The profile surfaces move: B's hazard-frequency axis is the stage signal (half the
        // flow axis under the deterministic rating), and the threshold probability re-reads on
        // that axis (threshold 40 sits beyond B's stage domain but inside A's flow domain).
        var profileA = meanA.Components[0].Curves.Total.HazardFrequencyHazards;
        var profileB = meanB.Components[0].Curves.Total.HazardFrequencyHazards;
        Assert.AreEqual(profileA.Length, profileB.Length, "Same evaluations, different axis.");
        Assert.AreEqual(profileA[0] / 2d, profileB[0], 1e-9 * Math.Abs(profileA[0]),
            "B's profile axis must be the rating pushforward of A's.");
        Assert.AreNotEqual(
            analysisA.RiskResults[0]!.ComponentResults[0].Fail.HazardThresholdProbability,
            analysisB.RiskResults![0]!.ComponentResults[0].Fail.HazardThresholdProbability,
            "The hazard-threshold probability must re-read on the profile axis.");
    }

    /// <summary>
    /// Verifies the Phase 6.6 profile catalog on the mean pass: the cumulative failure
    /// probability (terminal ≡ the Fail mass balance) and system response profile on the
    /// primary Fail stream only, the cumulative expected consequence on every stream (terminal
    /// ≡ the stream mean), the 1D exceedance axis ≡ 1 − p, and the failure-mode profiles built
    /// on the mean tree.
    /// </summary>
    [TestMethod]
    public async Task Test_ProfileCatalog_MeanPass_CatalogAndModeProfiles()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d)) });

        // Act
        await analysis.RunAsync();

        // Assert — component scope, primary type.
        var component = analysis.MeanRiskResults!.Components[0];
        var fail = component.Curves.Fail;
        Assert.IsTrue(fail.CumulativeFailureProbabilities.Length > 2, "The cumulative failure probability must build on the Fail stream.");
        Assert.AreEqual(fail.MassBalance, fail.CumulativeFailureProbabilities[0], 1e-12 * fail.MassBalance,
            "The terminal ordinate must equal the annualized failure probability (the recorded mass balance).");
        Assert.AreEqual(fail.Mean, fail.CumulativeExpectedConsequences[0], 1e-12 * fail.Mean,
            "The Fail stream's cumulative expected consequence terminal must equal its mean.");
        var total = component.Curves.Total;
        Assert.IsTrue(total.CumulativeExpectedConsequences.Length > 2, "The cumulative expected consequence must build on every stream.");
        Assert.AreEqual(total.Mean, total.CumulativeExpectedConsequences[0], 1e-12 * total.Mean);
        Assert.AreEqual(0, total.CumulativeFailureProbabilities.Length, "The failure profiles are Fail-stream-only.");
        Assert.AreEqual(0, component.Curves.Excess.SystemResponseProbabilities.Length, "The response profile is Fail-stream-only.");

        // The system response profile: X strictly descending in (0, 1); on the 1D path the
        // exceedance coordinate is exactly 1 − p, so Y rises toward rare hazards and every
        // value stays a probability.
        var srpX = fail.SystemResponseExceedanceProbabilities;
        var srpY = fail.SystemResponseProbabilities;
        Assert.IsTrue(srpX.Length > 2, "The system response profile must build on the Fail stream.");
        Assert.AreEqual(srpX.Length, srpY.Length);
        for (int i = 0; i < srpX.Length; i++)
        {
            Assert.IsTrue(srpX[i] > 0d && srpX[i] < 1d, "Exceedance coordinates must be probabilities.");
            if (i > 0) Assert.IsTrue(srpX[i] < srpX[i - 1], "The exceedance axis must be strictly descending.");
            Assert.IsTrue(srpY[i] >= 0d && srpY[i] <= 1d, "Response ordinates must be probabilities.");
        }
        Assert.IsTrue(srpY[srpX.Length - 1] > srpY[0],
            "The combined response must rise toward rare (small-exceedance) hazards for a monotone fragility.");

        // Failure-mode profiles are built on the mean tree (Phase 6.6 — mean pass only).
        var mode = component.FailureModes[0].Curves.Fail;
        Assert.IsTrue(mode.HazardFrequencyHazards.Length > 2, "Mode-scope profiles must build on the mean pass.");
        Assert.IsTrue(mode.CumulativeFailureProbabilities.Length > 2);
        Assert.IsTrue(mode.SystemResponseProbabilities.Length > 2);
        Assert.AreEqual(mode.MassBalance, mode.CumulativeFailureProbabilities[0], 1e-12 * mode.MassBalance,
            "The mode's cumulative terminal carries its raw marginal mass (documented semantics).");
    }

    /// <summary>
    /// Verifies the Phase 6.6 five-stream banding parity restoration and the banded catalog on a
    /// full-uncertainty run: every stream's hazard-frequency band assembles (v1.0 banded all
    /// five; the Total-only interim was a parity gap), the banded cumulative failure
    /// probability's terminal equals the ensemble mean of the per-realization mass balances,
    /// and the banded response profile rides its own exceedance grid. Mode-scope profiles are
    /// deliberately absent from the band trees (mean-pass only — documented boundary).
    /// </summary>
    [TestMethod]
    public async Task Test_ProfileCatalog_FullUncertainty_FiveStreamBandsAndCatalog()
    {
        // Arrange
        var analysis = new RiskAnalysis(new[] { Component(Consequence("Failure Loss", 300d), UncertainFragility()) });
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = 100;

        // Act
        await analysis.RunAsync();

        // Assert — five-stream banded hazard-frequency profiles (the parity restoration).
        var meanBand = analysis.MeanRiskResults!.Components[0].Curves;
        Assert.IsTrue(meanBand.Total.HazardFrequencyHazards.Length > 2);
        Assert.IsTrue(meanBand.Fail.HazardFrequencyHazards.Length > 2, "v1.0 banded the Fail stream's profiles too.");
        Assert.IsTrue(meanBand.Excess.HazardFrequencyHazards.Length > 2);
        Assert.IsTrue(meanBand.Background.HazardFrequencyHazards.Length > 2);
        Assert.IsTrue(meanBand.NonFail.HazardFrequencyHazards.Length > 2);
        Assert.IsTrue(analysis.LowerRiskResults!.Components[0].Curves.Fail.HazardFrequencyHazards.Length > 2,
            "The lower band must carry the five-stream profiles as well.");

        // The banded cumulative failure probability: terminal ordinate (the largest grid
        // hazard) equals the sequential ensemble mean of the per-realization terminals — each
        // realization clamps to its own terminal there.
        Assert.IsTrue(meanBand.Fail.CumulativeFailureProbabilities.Length > 2);
        double meanApf = 0d;
        for (int i = 0; i < analysis.RiskResults!.Count; i++)
        {
            meanApf += analysis.RiskResults[i]!.ComponentResults[0].Fail.TotalProbability;
        }
        meanApf /= analysis.RiskResults.Count;
        Assert.AreEqual(meanApf, meanBand.Fail.CumulativeFailureProbabilities[0], 1e-9 * meanApf,
            "The mean band's terminal must equal the ensemble-mean annualized failure probability.");

        // The banded response profile rides its own log exceedance grid at the output length.
        Assert.AreEqual(analysis.Options.LECOutputLength, meanBand.Fail.SystemResponseExceedanceProbabilities.Length);
        Assert.IsTrue(meanBand.Fail.SystemResponseProbabilities.Length == analysis.Options.LECOutputLength);

        // Cumulative expected consequence bands on every stream; mode profiles stay mean-pass-only.
        Assert.IsTrue(meanBand.Total.CumulativeExpectedConsequences.Length > 2);
        Assert.AreEqual(0, analysis.MeanRiskResults.Components[0].FailureModes[0].Curves.Fail.HazardFrequencyHazards.Length,
            "Band trees do not carry mode-scope profiles (mean-pass only — documented).");
    }
}
