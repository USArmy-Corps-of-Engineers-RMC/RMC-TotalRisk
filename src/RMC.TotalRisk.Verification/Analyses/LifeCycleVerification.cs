using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Verification.Analyses;

/// <summary>
/// Life-cycle verification, greenfield: the deteriorating response's transform-equivalence
/// bit-oracle across ages, realization-for-realization knowledge parity on one content-seeded
/// stream, the age-zero base identity, an engine-level mean-only twin against the re-authored
/// base-plus-shift-transform model, and the family reproducibility pin.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// <b>Scenario tables.</b> The base fragility tabulates a Normal(150, 20) capacity CDF over
/// stages −10 to 310 at unit steps (321 ordinates); the uncertain variant carries per-ordinate
/// Uniform(p ± 0.05) probabilities clamped to [0, 1]. The deterioration law maps ages
/// {0, 25, 50, 100} to shifts {0, 5, 12, 30}; the uncertain variant pins age zero at an exact
/// zero (the degenerate uniform) and spreads later shifts Uniform(0.8Δ, 1.2Δ). Probe ages
/// {0, 10, 25, 60, 150} cover the age-zero identity, interpolated ages, a knot, and the
/// boundary hold; probe hazards span 60 to 260. The engine fixture drives a five-ordinate
/// deterministic stage-frequency hazard (exceedance 0.999 to 0.001 over stages 60 to 260) and
/// a five-knot deterministic stage-damage consequence through a single failure mode plus the
/// non-failure complement.
/// </para>
/// <para>
/// <b>Equivalence contract.</b> A deteriorating response at age t is exactly its base behind a
/// deterministic unit-slope linear transform with intercept Δ(t): with slope one the transform
/// computes Δ + h and the wrapper computes h + Δ, bit-identical in IEEE arithmetic, and both
/// paths evaluate the same sampled base product. The engine twin re-authors the wrapper model
/// as base-plus-stage-transform and must reproduce the annual failure probability, the expected
/// annual consequence, and the loss-exceedance ordinates with no delta — the twins' differing
/// canonical hashes and seeds are provably inert because every fixture function is
/// deterministic and the runs are mean-only (the configuration-risk family's twin discipline).
/// </para>
/// <para>
/// <b>Oracle mechanics.</b> The one-stream parity test seeds the wrapper once (64 realizations,
/// Latin hypercube, one fixed seed) and reconstructs every realization independently: the base's
/// product at its own child-stream row i composed with the law sampled at the wrapper's own
/// percentile for row i, interpolated at the probe age — proving the same state of knowledge
/// serves every age. The reproducibility pin runs the full-uncertainty wrapper scenario twice
/// from independently built models and compares the published ensemble and mean JSON byte
/// streams.
/// </para>
/// <para>
/// <b>Tolerances.</b> Every assert in this family is bit-exact (no-delta equality): the oracles
/// are algebraic identities of the same floating-point operations, so any deviation is a defect,
/// never statistical noise.
/// </para>
/// </remarks>
[TestClass]
public class LifeCycleVerification
{
    #region Fixtures

    /// <summary>The one fixed stream seed the family samples from.</summary>
    private const int StreamSeed = 20260906;

    /// <summary>The probe ages: the age-zero identity, interpolated ages, a knot, and the hold.</summary>
    private static readonly double[] ProbeAges = [0d, 10d, 25d, 60d, 150d];

    /// <summary>The probe hazards spanning the fragility range.</summary>
    private static readonly double[] ProbeHazards =
        [60d, 80d, 100d, 110d, 120d, 130d, 140d, 145d, 150d, 155d, 160d, 170d, 180d, 200d, 220d, 240d, 260d];

    /// <summary>Builds the tabulated Normal(150, 20) fragility base.</summary>
    /// <param name="uncertain">Whether the ordinates carry Uniform(p ± 0.05) uncertainty.</param>
    /// <returns>The base response.</returns>
    private static TabularResponse FragilityTable(bool uncertain)
    {
        var capacity = new Normal(150d, 20d);
        var ordinates = new List<UncertainOrdinate>();
        for (double x = -10d; x <= 310d; x += 1d)
        {
            double p = capacity.CDF(x);
            ordinates.Add(uncertain
                ? new UncertainOrdinate(x, new Uniform(Math.Max(0d, p - 0.05d), Math.Min(1d, p + 0.05d)))
                : new UncertainOrdinate(x, new Deterministic(p)));
        }
        return new TabularResponse
        {
            Name = "Capacity fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(ordinates, true, SortOrder.Ascending,
                false, SortOrder.None,
                uncertain ? UnivariateDistributionType.Uniform : UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds the deterministic deterioration law: ages {0, 25, 50, 100} → shifts {0, 5, 12, 30}.</summary>
    /// <returns>The law.</returns>
    private static UncertainOrderedPairedData StandardLaw()
    {
        return new UncertainOrderedPairedData(
            new[]
            {
                new UncertainOrdinate(0d, new Deterministic(0d)),
                new UncertainOrdinate(25d, new Deterministic(5d)),
                new UncertainOrdinate(50d, new Deterministic(12d)),
                new UncertainOrdinate(100d, new Deterministic(30d)),
            },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
    }

    /// <summary>
    /// Builds the uncertain law: an exact zero at age zero (the degenerate uniform), later
    /// shifts Uniform(0.8Δ, 1.2Δ).
    /// </summary>
    /// <returns>The law.</returns>
    private static UncertainOrderedPairedData UncertainLaw()
    {
        return new UncertainOrderedPairedData(
            new[]
            {
                new UncertainOrdinate(0d, new Uniform(0d, 0d)),
                new UncertainOrdinate(25d, new Uniform(4d, 6d)),
                new UncertainOrdinate(50d, new Uniform(9.6d, 14.4d)),
                new UncertainOrdinate(100d, new Uniform(24d, 36d)),
            },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Uniform);
    }

    /// <summary>Builds a labeled wrapper over the given base and law.</summary>
    /// <param name="baseResponse">The base response.</param>
    /// <param name="law">The deterioration law.</param>
    /// <returns>The wrapper.</returns>
    private static DeterioratingResponse Wrapper(IResponseFunction baseResponse, UncertainOrderedPairedData law)
    {
        return new DeterioratingResponse
        {
            Name = "Aging fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            BaseResponse = baseResponse,
            DeteriorationLaw = law,
        };
    }

    /// <summary>Builds the unit-slope shift transform with intercept Δ and wide bounds.</summary>
    /// <param name="shift">The intercept Δ.</param>
    /// <returns>The transform.</returns>
    private static LinearTransform ShiftTransform(double shift)
    {
        return new LinearTransform
        {
            Name = "Deterioration shift",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            Alpha = shift,
            Beta = 1d,
            IsUncertain = false,
            Minimum = -1e9d,
            Maximum = 1e9d,
        };
    }

    /// <summary>Builds the deterministic five-ordinate stage-frequency hazard over stages 60 to 260.</summary>
    /// <returns>The hazard.</returns>
    private static TabularHazard StageFrequency()
    {
        return new TabularHazard
        {
            Name = "Stage frequency",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            NoUncertaintyFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Deterministic(60d)),
                    new UncertainOrdinate(0.5d, new Deterministic(120d)),
                    new UncertainOrdinate(0.1d, new Deterministic(160d)),
                    new UncertainOrdinate(0.01d, new Deterministic(200d)),
                    new UncertainOrdinate(0.001d, new Deterministic(260d)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending,
                UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds the deterministic five-knot stage-damage consequence.</summary>
    /// <returns>The consequence.</returns>
    private static TabularConsequence StageDamages()
    {
        return new TabularConsequence
        {
            Name = "Failure damages",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(60d, new Deterministic(0d)),
                    new UncertainOrdinate(100d, new Deterministic(100d)),
                    new UncertainOrdinate(140d, new Deterministic(500d)),
                    new UncertainOrdinate(200d, new Deterministic(2000d)),
                    new UncertainOrdinate(250d, new Deterministic(5000d)),
                },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a single-component analysis whose failure mode carries the given stage chain.</summary>
    /// <param name="stageTransforms">The stage transforms ahead of the response; null for none.</param>
    /// <param name="response">The stage response.</param>
    /// <param name="consequencePosition">
    /// The consequence hazard position, or null for the last-response-input default; position 0
    /// binds the consequences to the raw hazard ahead of any stage transform.
    /// </param>
    /// <returns>The analysis.</returns>
    private static RiskAnalysis BuildAnalysis(List<ITransformFunction>? stageTransforms, IResponseFunction response,
        int? consequencePosition = null)
    {
        var component = new SystemComponent { Name = "Dam" };
        component.HazardFunction = StageFrequency();
        var mode = new FailureMode(stageTransforms, null, response, StageDamages())
        {
            ConsequenceHazardPosition = consequencePosition,
        };
        component.AddFailureMode(mode);
        return new RiskAnalysis(new[] { component })
        {
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
        };
    }

    #endregion

    /// <summary>
    /// The transform-equivalence bit-oracle across ages: the wrapper at every probe age equals
    /// the base behind the authored unit-slope shift transform with intercept Δ(age), with no
    /// delta at every probe hazard, for the deterministic base and the uncertain base at its
    /// mean.
    /// </summary>
    [TestMethod]
    public void Test_FunctionLevel_TransformEquivalence_BitExact_AcrossAges()
    {
        foreach (bool uncertain in new[] { false, true })
        {
            // Arrange
            var baseResponse = FragilityTable(uncertain);
            var wrapper = Wrapper(baseResponse, StandardLaw());
            var reference = baseResponse.SampleFunction();

            foreach (double age in ProbeAges)
            {
                // Act — the authored twin transform carries the identical resolved shift.
                double shift = wrapper.DeteriorationLaw.CurveSample().GetYFromX(age);
                var transform = ShiftTransform(shift).SampleFunction();
                var sampled = wrapper.SampleFunctionAtAge(age);

                // Assert — bit-exact: Δ + h with unit slope is h + Δ in IEEE arithmetic.
                foreach (double h in ProbeHazards)
                {
                    Assert.AreEqual(reference.CDF(transform.Function(h)), sampled.CDF(h),
                        $"uncertain {uncertain}, age {age}, hazard {h}");
                }
            }
        }
    }

    /// <summary>
    /// Realization-for-realization knowledge parity on one stream: after a single sampler setup,
    /// every realization at every probe age equals the independently reconstructed composition of
    /// the base's own child-stream row and the law at the wrapper's sampled percentile — the same
    /// state of knowledge serves every age.
    /// </summary>
    [TestMethod]
    public void Test_FunctionLevel_UncertaintyParity_RealizationForRealization_OneStream()
    {
        // Arrange — one setup serves every age.
        var baseResponse = FragilityTable(uncertain: true);
        var wrapper = Wrapper(baseResponse, UncertainLaw());
        wrapper.SetupSampler(64, StreamSeed, SamplingScheme.LatinHypercube);

        for (int i = 0; i < 64; i++)
        {
            // Act — the independent reconstruction for realization i.
            var reference = baseResponse.SampleFunction(i);
            double lawPercentile = wrapper.SampledPercentile(i, 0);
            var sampledLaw = wrapper.DeteriorationLaw.CurveSample(lawPercentile);

            foreach (double age in ProbeAges)
            {
                double shift = sampledLaw.GetYFromX(age);
                var sampled = wrapper.SampleFunctionAtAge(i, age);

                // Assert
                foreach (double h in ProbeHazards)
                {
                    Assert.AreEqual(reference.CDF(h + shift), sampled.CDF(h),
                        $"realization {i}, age {age}, hazard {h}");
                }
            }
        }
    }

    /// <summary>
    /// The age-zero identity: with a zero shift at age zero, the wrapper reproduces its base
    /// bit-for-bit, realization for realization, on the same stream.
    /// </summary>
    [TestMethod]
    public void Test_FunctionLevel_AgeZero_EquivalentToBase()
    {
        // Arrange
        var baseResponse = FragilityTable(uncertain: true);
        var wrapper = Wrapper(baseResponse, UncertainLaw());
        wrapper.SetupSampler(64, StreamSeed, SamplingScheme.LatinHypercube);

        for (int i = 0; i < 64; i++)
        {
            // Act
            var sampled = wrapper.SampleFunctionAtAge(i, 0d);
            var reference = baseResponse.SampleFunction(i);

            // Assert
            foreach (double h in ProbeHazards)
            {
                Assert.AreEqual(reference.CDF(h), sampled.CDF(h), $"realization {i}, hazard {h}");
            }
        }
    }

    /// <summary>
    /// The engine-level mean-only twin: a model whose failure mode carries the wrapper at age 50
    /// reproduces the re-authored model carrying the base behind a stage shift transform with
    /// intercept 12 — annual failure probability, expected annual consequence, and the
    /// loss-exceedance ordinates, all with no delta.
    /// </summary>
    [TestMethod]
    public void Test_Engine_MeanOnlyTwin_BitExact()
    {
        // Arrange — run A: the wrapper at age 50.
        var wrapper = Wrapper(FragilityTable(uncertain: false), StandardLaw());
        wrapper.EvaluationAge = 50d;
        var wrapperRun = BuildAnalysis(null, wrapper);

        // Arrange — run B: the re-authored base behind the equivalent stage transform. The
        // shift is internal to the response path, so the twin pins its consequence input to the
        // raw hazard (position 0) — the wrapper model's binding — rather than the transformed
        // signal the last-response-input default would select.
        var twinRun = BuildAnalysis(
            new List<ITransformFunction> { ShiftTransform(12d) },
            FragilityTable(uncertain: false),
            consequencePosition: 0);

        // Act
        wrapperRun.RunAsync().GetAwaiter().GetResult();
        twinRun.RunAsync().GetAwaiter().GetResult();

        // Assert — bit-exact twins (deterministic functions make the differing seeds inert).
        var wrapperCurves = wrapperRun.MeanRiskResults!.Curves;
        var twinCurves = twinRun.MeanRiskResults!.Curves;
        Assert.AreEqual(twinCurves.Fail.TotalProbability, wrapperCurves.Fail.TotalProbability);
        Assert.AreEqual(twinCurves.Total.Mean, wrapperCurves.Total.Mean);
        CollectionAssert.AreEqual(twinCurves.Total.LECConsequences, wrapperCurves.Total.LECConsequences);
        CollectionAssert.AreEqual(twinCurves.Total.LECProbabilities, wrapperCurves.Total.LECProbabilities);

        // The deterioration is material: the aged model fails more than the base would.
        var baselineRun = BuildAnalysis(null, FragilityTable(uncertain: false));
        baselineRun.RunAsync().GetAwaiter().GetResult();
        Assert.IsTrue(wrapperCurves.Fail.TotalProbability
            > baselineRun.MeanRiskResults!.Curves.Fail.TotalProbability);
    }

    /// <summary>
    /// The family reproducibility pin: the full-uncertainty wrapper scenario run twice from
    /// independently built models publishes byte-identical ensemble and mean JSON.
    /// </summary>
    [TestMethod]
    public void Test_Engine_Reproducibility_SameSeedBitIdentical()
    {
        // Arrange
        static RiskAnalysis Build()
        {
            var wrapper = Wrapper(FragilityTable(uncertain: true), UncertainLaw());
            wrapper.EvaluationAge = 50d;
            var analysis = BuildAnalysis(null, wrapper);
            analysis.Options.EstimateMeanRiskOnly = false;
            analysis.Options.Realizations = 128;
            return analysis;
        }
        var first = Build();
        var second = Build();

        // Act
        first.RunAsync().GetAwaiter().GetResult();
        second.RunAsync().GetAwaiter().GetResult();

        // Assert — byte-identical published results.
        Assert.AreEqual(first.RiskResults!.ToJson(), second.RiskResults!.ToJson());
        Assert.AreEqual(first.MeanRiskResults!.ToJson(), second.MeanRiskResults!.ToJson());
    }
}
