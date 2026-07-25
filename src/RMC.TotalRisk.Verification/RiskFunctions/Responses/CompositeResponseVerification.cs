using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Responses;

namespace RMC.TotalRisk.Verification.RiskFunctions.Responses;

/// <summary>
/// Verification of the composite response function against the 2024 verification report
/// (§Composite Hazard and Response Functions, Tables 44–46) and against closed-form probability
/// identities for the weakest-link rule the report does not tabulate.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// <b>Shared scenario.</b> The report verifies the composite hazard and the composite response
/// with one scenario, noting that "the composite response function produces the same results, but
/// it is plotted as a CDF with the hazard levels versus the non-exceedance probabilities, whereas
/// the composite hazard function plots the exceedance probabilities versus the hazard levels."
/// This family therefore reuses the Table 44 children — N(10, 2), N(20, 1), N(30, 5) at effective
/// record length 100, weights 0.3/0.2/0.5 — and checks the mixture on the probability axis a
/// fragility is read on, against the same published Table 45 constants the hazard family checks on
/// the magnitude axis.
/// </para>
/// <para>
/// <b>Weakest link.</b> The competing-risks mode has no published table, so it is anchored on
/// exact probability identities instead: under independence the combined conditional failure
/// probability is <c>1 − ∏(1 − pᵢ(h))</c>, and under comonotonic dependence it is the largest
/// child probability. Both are exact, so the deltas are inversion noise rather than sampling
/// error.
/// </para>
/// </remarks>
[TestClass]
public class CompositeResponseVerification
{
    /// <summary>The report Table 44 child means (capacity, in the fragility reading).</summary>
    private static readonly double[] Means = { 10d, 20d, 30d };

    /// <summary>The report Table 44 child standard deviations.</summary>
    private static readonly double[] Sds = { 2d, 1d, 5d };

    /// <summary>The report Table 44 mixture weights.</summary>
    private static readonly double[] Weights = { 0.3d, 0.2d, 0.5d };

    /// <summary>The report Table 44 effective record length, shared by all three children.</summary>
    private const int EffectiveRecordLength = 100;

    /// <summary>The report's bootstrap realization count.</summary>
    private const int Realizations = 10_000;

    /// <summary>The legacy per-child bootstrap seeds (<c>Test_Composite_Uncertainty</c>).</summary>
    private static readonly int[] Seeds = { 12345, 67891, 45678 };

    /// <summary>The report Table 45/46 annual exceedance probabilities.</summary>
    private static readonly double[] Aeps =
    {
        1.0E-06d, 2.0E-06d, 5.0E-06d, 1.0E-05d, 2.0E-05d, 5.0E-05d, 1.0E-04d, 2.0E-04d, 5.0E-04d,
        1.0E-03d, 2.0E-03d, 5.0E-03d, 1.0E-02d, 2.0E-02d, 5.0E-02d, 1.0E-01d, 2.0E-01d, 3.0E-01d,
        5.0E-01d, 7.0E-01d, 8.0E-01d, 9.0E-01d, 9.5E-01d, 9.8E-01d, 9.9E-01d,
    };

    /// <summary>Report Table 45 — the R <c>mistr</c> mixture curve, index-aligned with <see cref="Aeps"/>.</summary>
    private static readonly double[] MistrCurve =
    {
        53.10d, 52.34d, 51.33d, 50.54d, 49.72d, 48.60d, 47.70d, 46.76d, 45.45d,
        44.39d, 43.26d, 41.63d, 40.27d, 38.75d, 36.40d, 34.21d, 31.26d, 28.72d,
        21.38d, 15.58d, 10.88d, 9.15d, 8.07d, 7.00d, 6.34d,
    };

    /// <summary>Builds a labeled parametric fragility child with the report's configuration.</summary>
    /// <param name="index">The child index (0, 1, or 2).</param>
    /// <param name="uncertain">True for the bootstrapped posterior, false for the parent only.</param>
    /// <returns>The estimated child.</returns>
    private static ParametricResponse Child(int index, bool uncertain)
    {
        var child = new ParametricResponse
        {
            Name = $"Mechanism {index + 1}",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            ParentDistribution = new Normal(Means[index], Sds[index]),
            EffectiveRecordLength = EffectiveRecordLength,
            Realizations = Realizations,
            PRNGSeed = Seeds[index],
            IsUncertain = uncertain,
        };
        child.Estimate();
        return child;
    }

    /// <summary>Builds the report's three-child composite response.</summary>
    /// <param name="uncertain">True for bootstrapped children, false for parent-only children.</param>
    /// <param name="combination">The combination rule.</param>
    /// <returns>The composite.</returns>
    private static CompositeResponse ReportComposite(bool uncertain, CompositeCombinationType combination)
    {
        return new CompositeResponse(new[]
        {
            new WeightedResponseFunction(Child(0, uncertain), Weights[0]),
            new WeightedResponseFunction(Child(1, uncertain), Weights[1]),
            new WeightedResponseFunction(Child(2, uncertain), Weights[2]),
        })
        {
            Name = "Report mixture",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            CompositeCombinationType = combination,
        };
    }

    /// <summary>The analytic mixture conditional failure probability at a hazard level.</summary>
    /// <param name="h">The hazard level.</param>
    /// <returns>The weighted sum of the child fragilities.</returns>
    private static double ExactMixture(double h)
    {
        return (Weights[0] * new Normal(Means[0], Sds[0]).CDF(h))
            + (Weights[1] * new Normal(Means[1], Sds[1]).CDF(h))
            + (Weights[2] * new Normal(Means[2], Sds[2]).CDF(h));
    }

    /// <summary>
    /// Verifies the mixture rule exactly: the combined conditional failure probability at every
    /// hazard level is the weighted sum of the child fragilities.
    /// </summary>
    [TestMethod]
    public void Test_Mixture_EqualsWeightedSumOfChildFragilities()
    {
        // Arrange
        var combined = ReportComposite(false, CompositeCombinationType.Mixture).SampleFunction();

        // Act / Assert
        for (double h = 0d; h <= 55d; h += 0.5d)
        {
            Assert.AreEqual(ExactMixture(h), combined.CDF(h), 1e-12, $"Mixture fragility at stage {h}.");
        }
    }

    /// <summary>
    /// Cross-checks the composite response against the published Table 45 constants on the
    /// probability axis a fragility is read on.
    /// </summary>
    /// <remarks>
    /// The report's Table 45 lists the hazard magnitude at each annual exceedance probability; read
    /// as a fragility, the combined conditional failure probability at that magnitude must be the
    /// corresponding non-exceedance probability <c>1 − AEP</c>. Tolerance is 1% of the
    /// non-exceedance probability — the report's own "very good" band — because the published
    /// magnitudes carry the small mid-distribution error the hazard family documents, which shows
    /// up here as a probability offset rather than a magnitude one.
    /// </remarks>
    [TestMethod]
    public void Test_MixtureFragility_AtReportMagnitudes_MatchesNonExceedance()
    {
        // Arrange
        var combined = ReportComposite(false, CompositeCombinationType.Mixture).SampleFunction();

        // Act / Assert
        for (int i = 0; i < Aeps.Length; i++)
        {
            double expected = 1d - Aeps[i];
            Assert.AreEqual(expected, combined.CDF(MistrCurve[i]), 0.01d * expected,
                $"Combined fragility at the Table 45 magnitude for AEP {Aeps[i]:E1}.");
        }
    }

    /// <summary>
    /// Verifies the competing-risks weakest-link rule in closed form across dependence options.
    /// </summary>
    /// <remarks>
    /// Under the minimum rule any mechanism can fail the system, so the combined conditional
    /// failure probability is the union of the child failure events: <c>1 − ∏(1 − pᵢ(h))</c> when
    /// they are independent, and the largest child probability when they are comonotonic.
    /// </remarks>
    [TestMethod]
    public void Test_CompetingRisks_WeakestLink_ClosedForms()
    {
        // Arrange
        var parents = new[] { new Normal(10d, 2d), new Normal(20d, 1d), new Normal(30d, 5d) };
        var composite = ReportComposite(false, CompositeCombinationType.CompetingRisks);

        // Independent: the union of the child failure events.
        var independent = composite.SampleFunction();
        for (double h = 0d; h <= 55d; h += 1d)
        {
            double survival = (1d - parents[0].CDF(h)) * (1d - parents[1].CDF(h)) * (1d - parents[2].CDF(h));
            Assert.AreEqual(1d - survival, independent.CDF(h), 1e-10, $"Independent weakest link at stage {h}.");
        }

        // Perfectly positive: the largest child probability.
        composite.Dependency = DependencyType.PerfectlyPositive;
        var comonotonic = composite.SampleFunction();
        for (double h = 5d; h <= 50d; h += 5d)
        {
            double expected = Math.Max(parents[0].CDF(h), Math.Max(parents[1].CDF(h), parents[2].CDF(h)));
            Assert.AreEqual(expected, comonotonic.CDF(h), 1e-6, $"Comonotonic weakest link at stage {h}.");
        }
    }

    /// <summary>
    /// Verifies the two rules bracket as theory requires, and that they are genuinely different
    /// models — the weakest link is never less likely to fail than the most fragile mechanism,
    /// while the mixture lies between the child fragilities.
    /// </summary>
    [TestMethod]
    public void Test_CombinationRules_Bracketing()
    {
        // Arrange
        var parents = new[] { new Normal(10d, 2d), new Normal(20d, 1d), new Normal(30d, 5d) };
        var mixture = ReportComposite(false, CompositeCombinationType.Mixture).SampleFunction();
        var competing = ReportComposite(false, CompositeCombinationType.CompetingRisks).SampleFunction();
        bool everDiffers = false;

        // Act / Assert
        for (double h = 0d; h <= 55d; h += 1d)
        {
            double smallest = Math.Min(parents[0].CDF(h), Math.Min(parents[1].CDF(h), parents[2].CDF(h)));
            double largest = Math.Max(parents[0].CDF(h), Math.Max(parents[1].CDF(h), parents[2].CDF(h)));

            Assert.IsTrue(competing.CDF(h) >= largest - 1e-10,
                $"The weakest link failed less readily than its most fragile child at stage {h}.");
            Assert.IsTrue(mixture.CDF(h) >= smallest - 1e-10 && mixture.CDF(h) <= largest + 1e-10,
                $"The mixture fell outside the child envelope at stage {h}.");
            if (Math.Abs(competing.CDF(h) - mixture.CDF(h)) > 0.05d) everDiffers = true;
        }

        Assert.IsTrue(everDiffers, "The two combination rules must be materially different models.");
    }

    /// <summary>
    /// Verifies weights are genuinely inert under competing risks: a weight edit moves neither the
    /// sampled distribution nor the canonical hash, so it cannot re-roll a Monte Carlo seed.
    /// </summary>
    [TestMethod]
    public void Test_CompetingRisks_WeightEdits_AreInert()
    {
        // Arrange
        var composite = ReportComposite(false, CompositeCombinationType.CompetingRisks);
        byte[] hashBefore = composite.CanonicalHash();
        var before = composite.SampleFunction();

        // Act
        composite.ResponseFunctions[0].Weight = 0.9d;
        composite.ResponseFunctions[1].Weight = 0.05d;
        composite.ResponseFunctions[2].Weight = 0.05d;

        // Assert
        CollectionAssert.AreEqual(hashBefore, composite.CanonicalHash(),
            "A weight edit must be hash-inert under competing risks.");
        var after = composite.SampleFunction();
        for (double h = 5d; h <= 50d; h += 5d)
        {
            Assert.AreEqual(before.CDF(h), after.CDF(h), 0d, $"A weight edit moved the weakest-link curve at stage {h}.");
        }
    }

    /// <summary>
    /// Verifies the uncertainty bands against an index-parity oracle rebuilt in-test from the
    /// Numerics <c>BootstrapAnalysis</c> and <c>Mixture</c> primitives, on the fragility
    /// (probability) axis.
    /// </summary>
    /// <remarks>
    /// The response composite summarizes conditional failure probability across a hazard grid,
    /// whereas the hazard composite inverts across a probability grid — this test pins that second
    /// axis. Because parametric children are posterior-indexed, the composite consumes the same
    /// bootstrap streams the oracle builds, in the same order, so the comparison is a wiring-parity
    /// check. The oracle mirrors the engine's empirical-CDF construction so both sides evaluate
    /// through the same path; the residual delta is 1e-9.
    /// </remarks>
    [TestMethod]
    public void Test_UncertaintyBands_VsIndexParityOracle()
    {
        // Arrange — the engine side.
        var composite = ReportComposite(true, CompositeCombinationType.Mixture);
        var results = composite.ComputeUncertaintyResults(0.9d);
        Assert.IsNotNull(results);
        double[] hazards = composite.UncertaintySummaryHazards();

        // Arrange — the oracle side.
        var streams = new IUnivariateDistribution[3][];
        for (int c = 0; c < 3; c++)
        {
            streams[c] = new BootstrapAnalysis(
                new Normal(Means[c], Sds[c]), ParameterEstimationMethod.MethodOfMoments,
                EffectiveRecordLength, Realizations, Seeds[c]).Distributions();
        }

        var column = new double[Realizations];
        for (int i = 0; i < hazards.Length; i += 7)
        {
            for (int k = 0; k < Realizations; k++)
            {
                var mixture = new Mixture(Weights, new[] { streams[0][k], streams[1][k], streams[2][k] });
                mixture.CreateEmpiricalCDF();
                column[k] = mixture.CDF(hazards[i]);
            }
            Array.Sort(column);

            Assert.AreEqual(Statistics.Percentile(column, 0.05d, dataIsSorted: true),
                results!.ConfidenceIntervals![i, 0], 1e-9, $"Lower band at stage {hazards[i]}.");
            Assert.AreEqual(Statistics.Percentile(column, 0.95d, dataIsSorted: true),
                results.ConfidenceIntervals[i, 1], 1e-9, $"Upper band at stage {hazards[i]}.");
        }
    }

    /// <summary>
    /// Verifies the parent fragility lies inside the bootstrap bands at every summary hazard — a
    /// structural check independent of any published constant.
    /// </summary>
    [TestMethod]
    public void Test_ParentFragility_LiesInsideBands()
    {
        // Arrange
        var composite = ReportComposite(true, CompositeCombinationType.Mixture);
        var results = composite.ComputeUncertaintyResults(0.9d);
        double[] hazards = composite.UncertaintySummaryHazards();

        // Act / Assert
        for (int i = 0; i < hazards.Length; i++)
        {
            double central = ExactMixture(hazards[i]);
            Assert.IsTrue(central >= results!.ConfidenceIntervals![i, 0] - 1e-9,
                $"The parent fragility fell below the 5% band at stage {hazards[i]}.");
            Assert.IsTrue(central <= results.ConfidenceIntervals[i, 1] + 1e-9,
                $"The parent fragility rose above the 95% band at stage {hazards[i]}.");
        }
    }

    /// <summary>
    /// Pins the composite's reproducibility contract: a serialization round-trip plus metadata
    /// edits move neither the hash nor the draws, and a compute edit moves the stream.
    /// </summary>
    /// <remarks>
    /// The round-trip stands in for a stored-and-reloaded project. It is used instead of a second
    /// independently estimated composite because the Numerics bootstrap summary assembly is
    /// order-nondeterministic, so two fresh <c>Estimate()</c> calls do not produce a bit-identical
    /// posterior — see the composite hazard family, which pins that upstream finding directly.
    /// </remarks>
    [TestMethod]
    public void Test_Reproducibility_RoundTripAndMetadataPins()
    {
        // Arrange
        var first = ReportComposite(true, CompositeCombinationType.Mixture);
        var second = new CompositeResponse(first.ToXElement());
        second.Name = "A different name entirely";
        second.AssignNewId();
        second.ResponseFunctions[0].ResponseFunction!.Name = "Renamed child";

        // Assert
        CollectionAssert.AreEqual(first.CanonicalHash(), second.CanonicalHash(),
            "A round-trip plus metadata edits must leave the canonical hash unmoved.");

        first.SetupSampler(Realizations, 12345, SamplingScheme.LatinHypercube);
        second.SetupSampler(Realizations, 12345, SamplingScheme.LatinHypercube);
        for (int k = 0; k < Realizations; k += 977)
        {
            Assert.AreEqual(first.SampleFunction(k).CDF(25d), second.SampleFunction(k).CDF(25d), 0d,
                $"Metadata edits moved the draw at realization {k}.");
        }

        // A compute edit moves the hash — and therefore every child's derived seed.
        var edited = new CompositeResponse(first.ToXElement());
        edited.ResponseFunctions[0].Weight = 0.35d;
        edited.ResponseFunctions[1].Weight = 0.15d;
        CollectionAssert.AreNotEqual(first.CanonicalHash(), edited.CanonicalHash());
    }
}
