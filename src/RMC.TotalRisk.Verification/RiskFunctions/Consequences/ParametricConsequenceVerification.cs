using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using Numerics.Sampling;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Consequences;

namespace RMC.TotalRisk.Verification.RiskFunctions.Consequences;

/// <summary>
/// Verification of the parametric consequence function C(h) = clamp(α·max(h − h₀, 0)^β, 0, U)
/// with log-space coefficient uncertainty. This family is greenfield (no v1.0 ancestor and no
/// legacy oracle), so the expected values are exact closed forms — the deterministic curve
/// algebra and the lognormal distributions the coefficient scatter induces — plus an independent
/// Monte Carlo oracle built from Numerics primitives for the two-sigma configuration.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// Reference configuration: α = 10, β = 1.5, h₀ = 2, σ_α = 0.3, σ_β = 0.2. Key exact anchors:
/// with σ_β = 0 and no cap, C(h) is exactly lognormal at every hazard — LogN(ln(α·s^β), σ_α)
/// with s = h − h₀; and at the unit offset (h = h₀ + 1) the exponent drops out entirely, so C is
/// LogN(ln α, σ_α) regardless of σ_β — a sharp check that the two sampler dimensions do not
/// leak into each other. Engine ensembles run N = 1,000,000 realizations
/// (<c>SetupSampler(N, 12345, LatinHypercube)</c>); tolerances are k·SE with k = 4 per
/// <c>docs/verification.md</c> (mean SE = σ/√N; percentile SE = √(p(1−p)/N)/f(x_p) with the
/// exact lognormal density f(q) = φ(z_p)/(q·σ_α)). Latin hypercube stratification makes the
/// engine estimators tighter than these independent-sampling SEs, so every tolerance is
/// conservative.
/// </para>
/// <para>
/// <b>Documented omission:</b> no mean asserts exist for σ_β &gt; 0 with U = ∞ at hazards more
/// than one unit above the threshold — the exact mean is the lognormal moment-generating
/// function evaluated at ln(s) &gt; 0, which diverges, so the sample mean does not converge and
/// no tolerance can make such an assert meaningful (the model validates with a warning for this
/// configuration). Mean checks under exponent uncertainty use a finite cap, which bounds the
/// mean; percentiles are always well-defined and are checked in both configurations.
/// </para>
/// </remarks>
[TestClass]
public class ParametricConsequenceVerification
{
    /// <summary>The engine-side realization count (docs/verification.md standard).</summary>
    private const int Realizations = 1_000_000;

    /// <summary>The engine-side sampler seed (the legacy fixed-seed convention).</summary>
    private const int EngineSeed = 12345;

    /// <summary>The reference scale coefficient α.</summary>
    private const double Alpha = 10d;

    /// <summary>The reference exponent β.</summary>
    private const double Beta = 1.5d;

    /// <summary>The reference damage-initiation threshold h₀.</summary>
    private const double Threshold = 2d;

    /// <summary>The reference log-space scale sigma σ_α.</summary>
    private const double SigmaAlpha = 0.3d;

    /// <summary>The reference log-space exponent sigma σ_β.</summary>
    private const double SigmaBeta = 0.2d;

    /// <summary>
    /// Builds the labeled reference function with the requested uncertainty configuration.
    /// </summary>
    /// <param name="sigmaAlpha">The log-space sigma on α.</param>
    /// <param name="sigmaBeta">The log-space sigma on β.</param>
    /// <param name="upperBound">The saturation cap U.</param>
    /// <returns>The configured function.</returns>
    private static ParametricConsequence Build(double sigmaAlpha, double sigmaBeta, double upperBound)
    {
        return new ParametricConsequence
        {
            Name = "Parametric life loss",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            Alpha = Alpha,
            Beta = Beta,
            Threshold = Threshold,
            UpperBound = upperBound,
            IsUncertain = sigmaAlpha > 0d || sigmaBeta > 0d,
            SigmaAlpha = sigmaAlpha,
            SigmaBeta = sigmaBeta,
        };
    }

    /// <summary>
    /// Runs the engine-side ensemble: one realization curve per index, evaluated at the given
    /// hazards (one values array per hazard, index-aligned).
    /// </summary>
    /// <param name="function">The function under test.</param>
    /// <param name="hazards">The hazards to evaluate each realization curve at.</param>
    /// <returns>One N-realization value array per hazard.</returns>
    private static double[][] Sample(ParametricConsequence function, params double[] hazards)
    {
        function.SetupSampler(Realizations, EngineSeed, SamplingScheme.LatinHypercube);
        var values = new double[hazards.Length][];
        for (int h = 0; h < hazards.Length; h++) values[h] = new double[Realizations];
        for (int i = 0; i < Realizations; i++)
        {
            var curve = function.SampleFunction(i);
            for (int h = 0; h < hazards.Length; h++)
            {
                values[h][i] = curve.Function(hazards[h]);
            }
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
    /// Verifies the deterministic curve against the exact closed form at the threshold, the unit
    /// offset, the power segment, the saturation crossing, and beyond.
    /// </summary>
    /// <remarks>
    /// Pure double algebra — the tolerance is 1e-12 (relative where the magnitudes warrant), not
    /// a statistical band. Crossing for U = 500: h₀ + (U/α)^(1/β) = 2 + 50^(2/3) = 15.5721.
    /// </remarks>
    [TestMethod]
    public void Test_DeterministicCurve_ClosedFormPoints_Exact()
    {
        // Arrange
        var function = Build(0d, 0d, 500d);
        var curve = function.SampleFunction();
        double crossing = 2d + Math.Pow(50d, 2d / 3d);

        // Assert
        Assert.AreEqual(0d, curve.Function(1d), 0d);
        Assert.AreEqual(0d, curve.Function(2d), 0d);
        Assert.AreEqual(10d, curve.Function(3d), 1e-12);
        Assert.AreEqual(80d, curve.Function(6d), 1e-12);
        Assert.AreEqual(10d * Math.Pow(10d, 1.5d), curve.Function(12d), 1e-9);
        Assert.AreEqual(500d, curve.Function(crossing), 1e-9);
        Assert.AreEqual(500d, curve.Function(crossing + 10d), 0d);
        Assert.AreEqual(crossing, function.MaxHazard(), 1e-12);
    }

    /// <summary>
    /// Verifies the σ_α-only ensemble against the exact lognormal at two hazards: with σ_β = 0
    /// and no cap, C(h) = α·s^β·e^{σ_α·Z} is exactly LogN(ln(α·s^β), σ_α).
    /// </summary>
    /// <remarks>
    /// Exact values at h = 3 (s = 1, base 10): mean = 10·e^{0.045} = 10.4603, q05 =
    /// 10·e^{−0.3·1.6449} = 6.1048, q95 = 16.3807; at h = 6 (s = 4, base 80) all values scale by
    /// 8. Tolerances (k = 4, N = 10⁶): mean SE = mean·√(e^{σ²}−1)/√N → ±0.0129 (h=3), ±0.1027
    /// (h=6); percentile SE = √(p(1−p)/N)/f(q) with f(q) = φ(z_p)/(q·σ_α) → q05 ±0.0155/±0.1239,
    /// q95 ±0.0416/±0.3323.
    /// </remarks>
    [TestMethod]
    public void Test_SigmaAlphaOnly_LognormalMeanAndPercentiles_VsExact()
    {
        // Arrange — exact lognormal anchors.
        double z95 = Normal.StandardZ(0.95d);
        double meanFactor = Math.Exp(SigmaAlpha * SigmaAlpha / 2d);
        double q05Factor = Math.Exp(-SigmaAlpha * z95);
        double q95Factor = Math.Exp(SigmaAlpha * z95);

        // Act
        double[][] values = Sample(Build(SigmaAlpha, 0d, double.PositiveInfinity), 3d, 6d);

        // Assert — h = 3, base α = 10.
        var (mean3, _) = MeanSd(values[0]);
        Array.Sort(values[0]);
        double q05At3 = Statistics.Percentile(values[0], 0.05d, dataIsSorted: true);
        double q95At3 = Statistics.Percentile(values[0], 0.95d, dataIsSorted: true);
        Console.WriteLine($"SigmaAlpha-only h=3: mean={mean3:F4} q05={q05At3:F4} q95={q95At3:F4} (exact {10d * meanFactor:F4}/{10d * q05Factor:F4}/{10d * q95Factor:F4})");
        Assert.AreEqual(10d * meanFactor, mean3, 0.0129d);
        Assert.AreEqual(10d * q05Factor, q05At3, 0.0155d);
        Assert.AreEqual(10d * q95Factor, q95At3, 0.0416d);

        // h = 6, base α·4^1.5 = 80 (all anchors and tolerances scale by 8).
        var (mean6, _) = MeanSd(values[1]);
        Array.Sort(values[1]);
        double q05At6 = Statistics.Percentile(values[1], 0.05d, dataIsSorted: true);
        double q95At6 = Statistics.Percentile(values[1], 0.95d, dataIsSorted: true);
        Console.WriteLine($"SigmaAlpha-only h=6: mean={mean6:F4} q05={q05At6:F4} q95={q95At6:F4} (exact {80d * meanFactor:F4}/{80d * q05Factor:F4}/{80d * q95Factor:F4})");
        Assert.AreEqual(80d * meanFactor, mean6, 0.1027d);
        Assert.AreEqual(80d * q05Factor, q05At6, 0.1239d);
        Assert.AreEqual(80d * q95Factor, q95At6, 0.3323d);
    }

    /// <summary>
    /// Verifies the two-sigma ensemble at the unit offset (h = h₀ + 1): the exponent drops out of
    /// C = α·e^{σ_α·Z₁}·1^{β_i}, so the distribution is exactly LogN(ln α, σ_α) no matter what
    /// σ_β and Z₂ do — a sharp check that the two sampler dimensions do not leak into each other.
    /// </summary>
    /// <remarks>
    /// Exact: median 10, mean 10.4603, q05 6.1048, q95 16.3807. Tolerances as in the σ_α-only
    /// family at h = 3, plus the median: f(10) = φ(0)/(10·σ_α) = 0.13298 → ±4·√(0.25/N)/f =
    /// ±0.0151.
    /// </remarks>
    [TestMethod]
    public void Test_TwoSigma_UnitOffset_LognormalPercentiles_VsExact()
    {
        // Arrange
        double z95 = Normal.StandardZ(0.95d);

        // Act — both sigmas active, no cap, evaluated exactly at the unit offset.
        double[][] values = Sample(Build(SigmaAlpha, SigmaBeta, double.PositiveInfinity), Threshold + 1d);
        var (mean, _) = MeanSd(values[0]);
        Array.Sort(values[0]);
        double median = Statistics.Percentile(values[0], 0.50d, dataIsSorted: true);
        double q05 = Statistics.Percentile(values[0], 0.05d, dataIsSorted: true);
        double q95 = Statistics.Percentile(values[0], 0.95d, dataIsSorted: true);
        Console.WriteLine($"Two-sigma unit offset: mean={mean:F4} median={median:F4} q05={q05:F4} q95={q95:F4}");

        // Assert
        Assert.AreEqual(10d * Math.Exp(SigmaAlpha * SigmaAlpha / 2d), mean, 0.0129d);
        Assert.AreEqual(10d, median, 0.0151d);
        Assert.AreEqual(10d * Math.Exp(-SigmaAlpha * z95), q05, 0.0155d);
        Assert.AreEqual(10d * Math.Exp(SigmaAlpha * z95), q95, 0.0416d);
    }

    /// <summary>
    /// Verifies the full two-sigma configuration with a finite cap against an independent Monte
    /// Carlo oracle built from Numerics primitives: mean, median, and tail percentiles at h = 6.
    /// </summary>
    /// <remarks>
    /// The cap (U = 200) keeps the mean finite under exponent uncertainty (see the class-level
    /// omission note). Oracle: MersenneTwister(12345), two uniforms per draw mapped through the
    /// standard normal inverse CDF to α_i = α·e^{σ_α·z₁} and β_i = β·e^{σ_β·z₂}, then
    /// C = min(α_i·4^{β_i}, 200). Both sides are Monte Carlo, so tolerances are computed in-run
    /// as k·(SE_oracle + SE_engine) with k = 4 and SE_engine bounded by the oracle's SE (the
    /// engine's Latin hypercube is tighter): mean SE = σ̂/√N; percentile SE = √(p(1−p)/N)/f̂
    /// with the density estimated from the sorted oracle by the central difference
    /// f̂(q_p) ≈ 0.01/(q_{p+0.005} − q_{p−0.005}). The 95th percentile sits on the cap atom
    /// (more than 5% of the mass clamps to U), where the estimated density is infinite and the
    /// percentile SE is exactly zero — both estimators return the cap itself, so the comparison
    /// is exact by construction rather than statistical.
    /// </remarks>
    [TestMethod]
    public void Test_TwoSigma_McOracle_PercentilesAndClampedMean_VsSampler()
    {
        // Arrange — the independent oracle.
        const double UpperBound = 200d;
        var prng = new MersenneTwister(EngineSeed);
        var oracle = new double[Realizations];
        for (int i = 0; i < Realizations; i++)
        {
            double alpha = Alpha * Math.Exp(SigmaAlpha * Normal.StandardZ(prng.NextDouble()));
            double beta = Beta * Math.Exp(SigmaBeta * Normal.StandardZ(prng.NextDouble()));
            double value = alpha * Math.Pow(4d, beta);
            oracle[i] = value >= UpperBound ? UpperBound : value;
        }
        var (oracleMean, oracleSd) = MeanSd(oracle);
        Array.Sort(oracle);

        // Act — the engine ensemble at the same hazard.
        double[][] values = Sample(Build(SigmaAlpha, SigmaBeta, UpperBound), 6d);
        var (engineMean, _) = MeanSd(values[0]);
        Array.Sort(values[0]);

        // Assert — mean at k·(SE_oracle + SE_engine) = 8·σ̂/√N.
        double meanTolerance = 8d * oracleSd / Math.Sqrt(Realizations);
        Console.WriteLine($"Two-sigma capped mean: engine={engineMean:F4} oracle={oracleMean:F4} (tolerance ±{meanTolerance:F4})");
        Assert.AreEqual(oracleMean, engineMean, meanTolerance);

        // Percentiles at 8·SE_p with the central-difference density estimate.
        foreach (double p in new[] { 0.05d, 0.50d, 0.95d })
        {
            double qOracle = Statistics.Percentile(oracle, p, dataIsSorted: true);
            double qEngine = Statistics.Percentile(values[0], p, dataIsSorted: true);
            double density = 0.01d / (Statistics.Percentile(oracle, p + 0.005d, dataIsSorted: true)
                                      - Statistics.Percentile(oracle, p - 0.005d, dataIsSorted: true));
            double tolerance = 8d * Math.Sqrt(p * (1d - p) / Realizations) / density;
            Console.WriteLine($"Two-sigma capped q{p:0.00}: engine={qEngine:F4} oracle={qOracle:F4} (tolerance ±{tolerance:F4})");
            Assert.AreEqual(qOracle, qEngine, tolerance, $"Percentile {p:0.00} disagreed beyond k·SE.");
        }
    }

    /// <summary>
    /// Pins engine reproducibility: identical content and seed produce a bit-identical
    /// realization stream across independently built instances.
    /// </summary>
    [TestMethod]
    public void Test_Reproducibility_SameSeed_BitIdentical()
    {
        // Arrange
        var first = Build(SigmaAlpha, SigmaBeta, 200d);
        var second = Build(SigmaAlpha, SigmaBeta, 200d);
        first.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);
        second.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);

        // Assert
        for (int i = 0; i < 4096; i += 89)
        {
            Assert.AreEqual(first.SampleFunction(i).Function(6d), second.SampleFunction(i).Function(6d), 0d);
        }
    }

    /// <summary>
    /// Pins the seed-identity contract at the function level: renaming, re-identifying, and
    /// relabeling never move the realization stream.
    /// </summary>
    [TestMethod]
    public void Test_Reproducibility_MetadataEdits_BitIdentical()
    {
        // Arrange — capture the baseline stream.
        var function = Build(SigmaAlpha, SigmaBeta, 200d);
        function.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);
        var baseline = new double[41];
        for (int k = 0; k < baseline.Length; k++)
        {
            baseline[k] = function.SampleFunction(k * 99).Function(6d);
        }

        // Act — metadata edits, then re-setup.
        function.Name = "Renamed";
        function.AssignNewId();
        function.SpecifiedConsequence = "Damages";
        function.ConsequenceUnit = "$";
        function.SetupSampler(4096, EngineSeed, SamplingScheme.LatinHypercube);

        // Assert
        for (int k = 0; k < baseline.Length; k++)
        {
            Assert.AreEqual(baseline[k], function.SampleFunction(k * 99).Function(6d), 0d,
                "Metadata edits must never move Monte Carlo results.");
        }
    }

    /// <summary>
    /// Verifies the 10,000-realization parametric uncertainty summary is deterministic,
    /// content-seeded, grid-aligned, and exact when coefficient uncertainty is absent.
    /// </summary>
    [TestMethod]
    public void Test_UncertaintySummary_DeterministicAndGridAligned()
    {
        var function = Build(SigmaAlpha, SigmaBeta, 200d);

        var first = function.ComputeUncertaintyResults(0.90d)!;
        var second = function.ComputeUncertaintyResults(0.90d)!;

        CollectionAssert.AreEqual(first.MeanCurve, second.MeanCurve);
        CollectionAssert.AreEqual(first.ModeCurve, second.ModeCurve);
        double[] grid = function.UncertaintySummaryHazards();
        Assert.AreEqual(grid.Length, first.MeanCurve!.Length);
        Assert.AreEqual(Threshold, grid[0], 0d);
        Assert.AreEqual(0d, first.MeanCurve[0], 0d);

        function.Name = "Renamed";
        function.AssignNewId();
        CollectionAssert.AreEqual(first.MeanCurve, function.ComputeUncertaintyResults(0.90d)!.MeanCurve);

        var deterministic = Build(0d, 0d, 200d);
        var exact = deterministic.ComputeUncertaintyResults(0.90d)!;
        var nominal = deterministic.SampleFunction();
        double[] deterministicGrid = deterministic.UncertaintySummaryHazards();
        for (int i = 0; i < deterministicGrid.Length; i++)
        {
            Assert.AreEqual(nominal.Function(deterministicGrid[i]), exact.MeanCurve![i], 0d);
            Assert.AreEqual(exact.MeanCurve[i], exact.ConfidenceIntervals![i, 0], 0d);
        }
    }
}
