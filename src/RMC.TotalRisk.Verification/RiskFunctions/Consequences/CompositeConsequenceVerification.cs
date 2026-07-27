using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using Numerics.Sampling;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Consequences;

namespace RMC.TotalRisk.Verification.RiskFunctions.Consequences;

/// <summary>
/// Verification of the composite consequence function against the exact solutions from the 2024
/// verification report (§Composite Consequence Function): three child functions with zero
/// consequence at stage 0 and Normal life-loss distributions N(10, 2), N(20, 1), N(100, 5) at
/// stage 10, combined Additive, Average (weights 0.3/0.2/0.5), and Mixture (same weights).
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// Policy per <c>docs/verification.md</c>: the engine-side ensemble runs N = 1,000,000
/// realizations through the composite's own sampler (<c>SetupSampler(N, 12345,
/// LatinHypercube)</c>, evaluated at stage 10); expected values are exact closed forms or
/// Numerics primitives, never model-library compute. Assert tolerances are k·SE with k = 4,
/// derived per assert from the independent Monte Carlo standard error: mean SE = σ/√N, standard
/// deviation SE = √((μ₄ − σ⁴)/(4σ²N)), percentile SE = √(p(1 − p)/N)/f(x_p). Latin hypercube
/// stratification makes the engine estimators tighter than independent-sampling SE assumes, so
/// every tolerance here is conservative. Reproducibility (same seed → bit-identical;
/// metadata edits → bit-identical; compute edits → different stream) is pinned separately.
/// </para>
/// <para>
/// Exact solutions (report Tables 47–51): Additive is the sum of independent Normals —
/// N(130, √30); Average is the weighted average — N(57, √6.65); the Mixture has mean 57 and
/// standard deviation √1874.9 = 43.3001 (E[X²] = Σw(σ² + μ²) = 5123.9) with no closed-form
/// percentiles — the analytic oracle is the Numerics <c>Mixture</c> distribution's numerical
/// inverse CDF (q05 = 8.0652, q95 = 106.4078), cross-checked by an independent
/// <c>MersenneTwister(12345)</c> Monte Carlo oracle, and corroborated against the report's
/// 10M-sample constants (8.04, 106.38) at a widened tolerance that covers the report's own
/// sampling error and print rounding.
/// </para>
/// </remarks>
[TestClass]
public class CompositeConsequenceVerification
{
    /// <summary>The engine-side realization count (docs/verification.md standard).</summary>
    private const int Realizations = 1_000_000;

    /// <summary>The engine-side sampler seed (the legacy fixed-seed convention).</summary>
    private const int EngineSeed = 12345;

    /// <summary>The report's stage-10 child means.</summary>
    private static readonly double[] Means = { 10d, 20d, 100d };

    /// <summary>The report's stage-10 child standard deviations.</summary>
    private static readonly double[] Sds = { 2d, 1d, 5d };

    /// <summary>The report's Average/Mixture weights.</summary>
    private static readonly double[] Weights = { 0.3d, 0.2d, 0.5d };

    /// <summary>
    /// Builds one report child: zero consequence at stage 0 (a degenerate Normal point mass) and
    /// N(mean, sd) life loss at stage 10.
    /// </summary>
    /// <param name="name">The child name.</param>
    /// <param name="mean">The stage-10 mean life loss.</param>
    /// <param name="sd">The stage-10 standard deviation.</param>
    /// <returns>The labeled tabular child.</returns>
    private static TabularConsequence Child(string name, double mean, double sd)
    {
        return new TabularConsequence
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Normal(0d, 0d)), new UncertainOrdinate(10d, new Normal(mean, sd)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal),
        };
    }

    /// <summary>
    /// Builds the report composite over the three children in the requested mode.
    /// </summary>
    /// <param name="type">The combination mode.</param>
    /// <returns>The labeled composite.</returns>
    private static CompositeConsequence BuildComposite(CompositeFunctionType type)
    {
        var composite = new CompositeConsequence(new[]
        {
            new WeightedConsequenceFunction(Child("Function 1", Means[0], Sds[0]), Weights[0]),
            new WeightedConsequenceFunction(Child("Function 2", Means[1], Sds[1]), Weights[1]),
            new WeightedConsequenceFunction(Child("Function 3", Means[2], Sds[2]), Weights[2]),
        })
        {
            Name = "Report composite",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            CompositeFunctionType = type,
        };
        return composite;
    }

    /// <summary>
    /// Runs the engine-side ensemble: sets up the composite's sampler and evaluates every
    /// realization curve at stage 10.
    /// </summary>
    /// <param name="composite">The composite under test.</param>
    /// <returns>The N realization values at stage 10.</returns>
    private static double[] SampleStageTen(CompositeConsequence composite)
    {
        composite.SetupSampler(Realizations, EngineSeed, SamplingScheme.LatinHypercube);
        var values = new double[Realizations];
        for (int i = 0; i < Realizations; i++)
        {
            values[i] = composite.SampleFunction(i).Function(10d);
        }
        return values;
    }

    /// <summary>
    /// Computes the sample mean and standard deviation (two-pass, numerically stable).
    /// </summary>
    /// <param name="values">The sample.</param>
    /// <returns>The sample mean and standard deviation.</returns>
    private static (double Mean, double Sd) MeanSd(double[] values)
    {
        double sum = 0d;
        for (int i = 0; i < values.Length; i++) sum += values[i];
        double mean = sum / values.Length;
        double squares = 0d;
        for (int i = 0; i < values.Length; i++)
        {
            double d = values[i] - mean;
            squares += d * d;
        }
        return (mean, Math.Sqrt(squares / (values.Length - 1)));
    }

    /// <summary>
    /// Verifies the Additive composite against the exact sum of independent Normals, N(130, √30).
    /// </summary>
    /// <remarks>
    /// Report Table 48. Exact: mean 130, sd √30 = 5.4772, q05 = 120.99, q95 = 139.01. Tolerances
    /// (k = 4, N = 10⁶): mean ±4σ/√N = ±0.0219; sd ±4σ/√(2N) = ±0.0155 (Normal μ₄ = 3σ⁴); q05/q95
    /// ±4·√(0.05·0.95/N)/f(x_p) with f = φ(1.6449)/σ = 0.018831 → ±0.0463.
    /// </remarks>
    [TestMethod]
    public void Test_Additive_ThreeNormalChildren_MeanSdPercentiles_VsExact()
    {
        // Arrange — exact solution: the sum of independent Normals.
        var exact = new Normal(130d, Math.Sqrt(30d));

        // Act
        double[] values = SampleStageTen(BuildComposite(CompositeFunctionType.Additive));
        var (mean, sd) = MeanSd(values);
        Array.Sort(values);
        double q05 = Statistics.Percentile(values, 0.05d, dataIsSorted: true);
        double q95 = Statistics.Percentile(values, 0.95d, dataIsSorted: true);
        Console.WriteLine($"Additive: mean={mean:F4} sd={sd:F4} q05={q05:F4} q95={q95:F4}");

        // Assert
        Assert.AreEqual(130d, mean, 0.0219d);
        Assert.AreEqual(Math.Sqrt(30d), sd, 0.0155d);
        Assert.AreEqual(exact.InverseCDF(0.05d), q05, 0.0463d);
        Assert.AreEqual(exact.InverseCDF(0.95d), q95, 0.0463d);
    }

    /// <summary>
    /// Verifies the Average composite against the exact weighted average of independent Normals,
    /// N(57, √6.65) — independent pooling, variance Σw²σ².
    /// </summary>
    /// <remarks>
    /// Report Table 49. Exact: mean 0.3·10 + 0.2·20 + 0.5·100 = 57; sd √(0.09·4 + 0.04·1 +
    /// 0.25·25) = √6.65 = 2.5788; q05 = 52.76, q95 = 61.24. Tolerances (k = 4, N = 10⁶): mean
    /// ±0.0103; sd ±4σ/√(2N) = ±0.0073; q05/q95 f = φ(1.6449)/σ = 0.039996 → ±0.0218.
    /// </remarks>
    [TestMethod]
    public void Test_Average_WeightedChildren_MeanSdPercentiles_VsExact()
    {
        // Arrange
        var exact = new Normal(57d, Math.Sqrt(6.65d));

        // Act
        double[] values = SampleStageTen(BuildComposite(CompositeFunctionType.Average));
        var (mean, sd) = MeanSd(values);
        Array.Sort(values);
        double q05 = Statistics.Percentile(values, 0.05d, dataIsSorted: true);
        double q95 = Statistics.Percentile(values, 0.95d, dataIsSorted: true);
        Console.WriteLine($"Average: mean={mean:F4} sd={sd:F4} q05={q05:F4} q95={q95:F4}");

        // Assert
        Assert.AreEqual(57d, mean, 0.0103d);
        Assert.AreEqual(Math.Sqrt(6.65d), sd, 0.0073d);
        Assert.AreEqual(exact.InverseCDF(0.05d), q05, 0.0218d);
        Assert.AreEqual(exact.InverseCDF(0.95d), q95, 0.0218d);
    }

    /// <summary>
    /// Verifies the Mixture composite's mean and standard deviation against the exact mixture
    /// moments: mean Σwμ = 57 (identical to Average), sd √(Σw(σ² + μ²) − 57²) = √1874.9 = 43.3001
    /// (far larger than Average — the report's central point about day/night exposure).
    /// </summary>
    /// <remarks>
    /// Report Table 50. Tolerances (k = 4, N = 10⁶): mean ±4σ/√N = ±0.1732; sd via SE(s) =
    /// √((μ₄ − σ⁴)/(4σ²N)) with the exact mixture fourth central moment μ₄ = Σw·((μᵢ−57)⁴ +
    /// 6(μᵢ−57)²σᵢ² + 3σᵢ⁴) = 3,705,312 and σ⁴ = 3,515,250 → SE = 0.0050 → ±0.0204.
    /// </remarks>
    [TestMethod]
    public void Test_Mixture_WeightedChildren_MeanSd_VsExact()
    {
        // Act
        double[] values = SampleStageTen(BuildComposite(CompositeFunctionType.Mixture));
        var (mean, sd) = MeanSd(values);
        Console.WriteLine($"Mixture moments: mean={mean:F4} sd={sd:F4} (exact sd={Math.Sqrt(1874.9d):F4})");

        // Assert
        Assert.AreEqual(57d, mean, 0.1732d);
        Assert.AreEqual(Math.Sqrt(1874.9d), sd, 0.0204d);
    }

    /// <summary>
    /// Verifies the Mixture composite's percentiles against the analytic oracle — the Numerics
    /// <c>Mixture</c> distribution's numerical inverse CDF — with an independent Monte Carlo
    /// oracle cross-checking the analytic values.
    /// </summary>
    /// <remarks>
    /// The mixture has no closed-form percentiles; the analytic oracle inverts the exact mixture
    /// CDF numerically (Brent): q05 = 8.0652, q95 = 106.4078. Engine tolerances (k = 4, N = 10⁶),
    /// percentile SE = √(p(1−p)/N)/f(x_p) with the exact mixture density: f(q05) = 0.03745 →
    /// ±0.0233; f(q95) = 0.017550 → ±0.0497. The independent MC oracle (MersenneTwister(12345),
    /// two draws per realization: component by cumulative weight, then the component's inverse
    /// CDF) carries the same SE and is checked against the analytic values at the same k·SE.
    /// </remarks>
    [TestMethod]
    public void Test_Mixture_Percentiles_VsMixtureInverseCdfOracle()
    {
        // Arrange — the analytic oracle over the exact stage-10 mixture.
        var analytic = new Mixture(Weights,
            new UnivariateDistributionBase[] { new Normal(Means[0], Sds[0]), new Normal(Means[1], Sds[1]), new Normal(Means[2], Sds[2]) });
        double exactQ05 = analytic.InverseCDF(0.05d);
        double exactQ95 = analytic.InverseCDF(0.95d);

        // Act — the engine ensemble.
        double[] values = SampleStageTen(BuildComposite(CompositeFunctionType.Mixture));
        Array.Sort(values);
        double engineQ05 = Statistics.Percentile(values, 0.05d, dataIsSorted: true);
        double engineQ95 = Statistics.Percentile(values, 0.95d, dataIsSorted: true);
        Console.WriteLine($"Mixture percentiles: engine q05={engineQ05:F4} q95={engineQ95:F4}; analytic q05={exactQ05:F4} q95={exactQ95:F4}");

        // Assert — engine vs analytic.
        Assert.AreEqual(exactQ05, engineQ05, 0.0233d);
        Assert.AreEqual(exactQ95, engineQ95, 0.0497d);

        // Cross-check — an independent MC oracle from Numerics primitives (never the model
        // library) reproduces the analytic percentiles within its own k·SE.
        var prng = new MersenneTwister(EngineSeed);
        var oracle = new double[Realizations];
        for (int i = 0; i < Realizations; i++)
        {
            double u = prng.NextDouble();
            int component = u <= Weights[0] ? 0 : u <= Weights[0] + Weights[1] ? 1 : 2;
            oracle[i] = new Normal(Means[component], Sds[component]).InverseCDF(prng.NextDouble());
        }
        Array.Sort(oracle);
        Assert.AreEqual(exactQ05, Statistics.Percentile(oracle, 0.05d, dataIsSorted: true), 0.0233d);
        Assert.AreEqual(exactQ95, Statistics.Percentile(oracle, 0.95d, dataIsSorted: true), 0.0497d);
    }

    /// <summary>
    /// Corroborates the engine against the 2024 report's published 10M-sample Monte Carlo
    /// constants for the Mixture composite.
    /// </summary>
    /// <remarks>
    /// Report Tables 50–51: mean 57.00, sd 43.30, q05 8.04, q95 106.38. The report constants are
    /// themselves Monte Carlo estimates printed to two decimals — the report's q05 sits ≈0.025
    /// from the analytic 8.0652 — so the pin tolerance covers both samplings plus rounding:
    /// |engine − report| ≤ |exact − report| + 4·SE_engine, giving ±0.06 (q05) and ±0.11 (q95);
    /// mean ±0.18 and sd ±0.03 similarly. The analytic oracle in
    /// <see cref="Test_Mixture_Percentiles_VsMixtureInverseCdfOracle"/> is the primary check;
    /// this pin is corroboration against the published report — never tighten one toward the
    /// other.
    /// </remarks>
    [TestMethod]
    public void Test_Mixture_ReportConstants_Pinned()
    {
        // Act
        double[] values = SampleStageTen(BuildComposite(CompositeFunctionType.Mixture));
        var (mean, sd) = MeanSd(values);
        Array.Sort(values);
        double q05 = Statistics.Percentile(values, 0.05d, dataIsSorted: true);
        double q95 = Statistics.Percentile(values, 0.95d, dataIsSorted: true);
        Console.WriteLine($"Mixture vs report: mean={mean:F4} sd={sd:F4} q05={q05:F4} q95={q95:F4}");

        // Assert — the published report values.
        Assert.AreEqual(57.00d, mean, 0.18d);
        Assert.AreEqual(43.30d, sd, 0.03d);
        Assert.AreEqual(8.04d, q05, 0.06d);
        Assert.AreEqual(106.38d, q95, 0.11d);
    }

    /// <summary>
    /// Pins engine reproducibility: identical content and seed produce a bit-identical
    /// realization stream at any time.
    /// </summary>
    [TestMethod]
    public void Test_Reproducibility_SameSeed_BitIdentical()
    {
        // Arrange
        var first = BuildComposite(CompositeFunctionType.Mixture);
        var second = BuildComposite(CompositeFunctionType.Mixture);
        first.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);
        second.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);

        // Assert — bit-exact agreement across independently built instances.
        for (int i = 0; i < 4096; i += 97)
        {
            Assert.AreEqual(first.SampleFunction(i).Function(10d), second.SampleFunction(i).Function(10d), 0d);
        }
    }

    /// <summary>
    /// Pins the seed-identity contract: renaming, re-identifying, and relabeling the composite
    /// and its children never move the realization stream (the v1.0 seed-dependency bug
    /// regression, per docs/verification.md step 4).
    /// </summary>
    [TestMethod]
    public void Test_Reproducibility_MetadataEdits_BitIdentical()
    {
        // Arrange — capture the baseline stream.
        var composite = BuildComposite(CompositeFunctionType.Mixture);
        composite.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);
        var baseline = new double[43];
        for (int k = 0; k < baseline.Length; k++)
        {
            baseline[k] = composite.SampleFunction(k * 95).Function(10d);
        }

        // Act — metadata edits everywhere, then re-setup.
        composite.Name = "Renamed composite";
        composite.AssignNewId();
        composite.SpecifiedConsequence = "Damages";
        composite.ConsequenceUnit = "$";
        foreach (var entry in composite.ConsequenceFunctions)
        {
            entry.ConsequenceFunction!.Name += " (renamed)";
            entry.ConsequenceFunction!.AssignNewId();
            entry.ConsequenceFunction!.HazardUnit = "m";
        }
        composite.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);

        // Assert
        for (int k = 0; k < baseline.Length; k++)
        {
            Assert.AreEqual(baseline[k], composite.SampleFunction(k * 95).Function(10d), 0d,
                "Metadata edits must never move Monte Carlo results.");
        }
    }

    /// <summary>
    /// Pins the converse: a compute-relevant edit (an Average weight nudge) moves the canonical
    /// hash and the realization stream.
    /// </summary>
    [TestMethod]
    public void Test_Reproducibility_ComputeEdit_MovesStream()
    {
        // Arrange
        var composite = BuildComposite(CompositeFunctionType.Average);
        byte[] hashBefore = composite.CanonicalHash();
        composite.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);
        var baseline = new double[17];
        for (int k = 0; k < baseline.Length; k++)
        {
            baseline[k] = composite.SampleFunction(k * 241).Function(10d);
        }

        // Act — a weight nudge is compute-relevant in Average mode.
        composite.ConsequenceFunctions[0].Weight = 0.25d;
        composite.ConsequenceFunctions[1].Weight = 0.25d;
        byte[] hashAfter = composite.CanonicalHash();
        composite.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);

        // Assert — the hash moves, and the stream differs somewhere.
        CollectionAssert.AreNotEqual(hashBefore, hashAfter);
        bool anyDifferent = false;
        for (int k = 0; k < baseline.Length; k++)
        {
            if (baseline[k] != composite.SampleFunction(k * 241).Function(10d))
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.IsTrue(anyDifferent, "A compute edit must change the realization stream.");
    }

    /// <summary>
    /// Verifies the 10,000-realization uncertainty summary is deterministic, content-seeded, and
    /// isolated from the live sampler used by engine realizations.
    /// </summary>
    [TestMethod]
    public void Test_UncertaintySummary_DeterministicAndSamplerIsolated()
    {
        var composite = BuildComposite(CompositeFunctionType.Average);
        composite.SetupSampler(128, EngineSeed, SamplingScheme.LatinHypercube);
        double liveDraw = composite.SampleFunction(5).Function(10d);

        var first = composite.ComputeUncertaintyResults(0.90d)!;
        var second = composite.ComputeUncertaintyResults(0.90d)!;

        CollectionAssert.AreEqual(first.MeanCurve, second.MeanCurve);
        Assert.AreEqual(liveDraw, composite.SampleFunction(5).Function(10d), 0d);
        composite.Name = "Renamed";
        composite.ConsequenceFunctions[0].ConsequenceFunction!.AssignNewId();
        CollectionAssert.AreEqual(first.MeanCurve, composite.ComputeUncertaintyResults(0.90d)!.MeanCurve);
        CollectionAssert.AreEqual(new[] { 0d, 10d }, composite.UncertaintySummaryHazards());
        Assert.AreEqual(57d, first.MeanCurve![1], 0.5d);
        Assert.IsTrue(first.ConfidenceIntervals![1, 0] < first.ConfidenceIntervals[1, 1]);
    }
}
