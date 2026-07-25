using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Verification.RiskFunctions.Transforms;

/// <summary>
/// Verification of the composite transform function. The type is greenfield — v1.0 had no
/// composite transform and the 2024 report tabulates none — so every oracle here is either a
/// closed form or an independently coded Monte Carlo construction, never model-library compute.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// <b>Oracles.</b> A weighted average of linear children is itself linear, with
/// <c>α = Σ ωᵢαᵢ</c> and <c>β = Σ ωᵢβᵢ</c> — exact, so those asserts run at 1e-12. With
/// independent Gaussian residuals the ensemble at a fixed input is exactly
/// <c>Normal(Σ ωᵢ(αᵢ + βᵢx), √(Σ ωᵢ²σᵢ²))</c>, which gives closed-form mean, standard deviation,
/// and percentile oracles for the sampled ensemble. Mixed child families are checked against a
/// weighted sum computed independently from the children's own sampled curves.
/// </para>
/// <para>
/// <b>Tolerances</b> follow <c>docs/verification.md</c>: k·SE with k = 4, derived per assert from
/// the Monte Carlo standard error at N = 1,000,000 — mean ±4σ/√N, standard deviation ±4σ/√(2N)
/// (the Normal fourth moment gives μ₄ = 3σ⁴), and percentile ±4·√(p(1−p)/N)/f(x_p) with
/// <c>f</c> the Normal density at the quantile. Latin hypercube stratification makes the
/// estimators tighter than independent-sampling standard error assumes, so every tolerance here is
/// conservative.
/// </para>
/// </remarks>
[TestClass]
public class CompositeTransformVerification
{
    /// <summary>The ensemble realization count (docs/verification.md standard).</summary>
    private const int Realizations = 1_000_000;

    /// <summary>The sampler seed (the legacy fixed-seed convention).</summary>
    private const int EngineSeed = 12345;

    /// <summary>The input hazard the ensemble is evaluated at.</summary>
    private const double Probe = 40d;

    /// <summary>Builds a labeled linear child.</summary>
    /// <param name="name">The child name.</param>
    /// <param name="alpha">The intercept.</param>
    /// <param name="beta">The slope.</param>
    /// <param name="sigma">The residual standard deviation; zero builds a deterministic child.</param>
    /// <returns>The child.</returns>
    private static LinearTransform Linear(string name, double alpha, double beta, double sigma = 0d)
    {
        return new LinearTransform
        {
            Name = name,
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            Alpha = alpha,
            Beta = beta,
            Sigma = sigma,
            IsUncertain = sigma > 0d,
        };
    }

    /// <summary>Builds a labeled composite over the given (child, weight) pairs.</summary>
    /// <param name="entries">The weighted children.</param>
    /// <returns>The composite.</returns>
    private static CompositeTransform Composite(params (Core.Interfaces.ITransformFunction Function, double Weight)[] entries)
    {
        var composite = new CompositeTransform();
        foreach (var entry in entries)
        {
            composite.TransformFunctions.Add(new WeightedTransformFunction(entry.Function, entry.Weight));
        }
        composite.Name = "Blended rating";
        composite.SpecifiedHazard = "Flow";
        composite.HazardUnit = "cfs";
        composite.TransformedHazard = "Stage";
        composite.TransformedHazardUnit = "ft";
        return composite;
    }

    /// <summary>
    /// Verifies the exact closed form: a weighted average of linear children is the linear function
    /// with the weighted intercept and slope, forward and inverse.
    /// </summary>
    [TestMethod]
    public void Test_WeightedAverage_LinearChildren_ClosedForm()
    {
        // Arrange — 0.25·(2 + 3X) + 0.35·(10 + 4X) + 0.40·(−5 + 1.5X).
        double[] alphas = { 2d, 10d, -5d };
        double[] betas = { 3d, 4d, 1.5d };
        double[] weights = { 0.25d, 0.35d, 0.40d };
        var composite = Composite(
            (Linear("A", alphas[0], betas[0]), weights[0]),
            (Linear("B", alphas[1], betas[1]), weights[1]),
            (Linear("C", alphas[2], betas[2]), weights[2]));

        double expectedAlpha = (weights[0] * alphas[0]) + (weights[1] * alphas[1]) + (weights[2] * alphas[2]);
        double expectedBeta = (weights[0] * betas[0]) + (weights[1] * betas[1]) + (weights[2] * betas[2]);

        // Act
        var combined = composite.SampleFunction();

        // Assert — forward.
        for (double x = 0d; x <= 100d; x += 5d)
        {
            Assert.AreEqual(expectedAlpha + (expectedBeta * x), combined.Function(x), 1e-12, $"Forward at x = {x}.");
        }

        // Assert — inverse round-trips (the combined curve is strictly increasing).
        for (double x = 5d; x <= 95d; x += 15d)
        {
            Assert.AreEqual(x, combined.InverseFunction(combined.Function(x)), 1e-6, $"Inverse round-trip at x = {x}.");
        }

        // Assert — the transformed-hazard bounds are the combine at its own domain bounds.
        Assert.AreEqual(expectedAlpha, composite.MinTransformedHazard(true), 1e-12);
        Assert.AreEqual(expectedAlpha + (expectedBeta * 100d), composite.MaxTransformedHazard(true), 1e-12);
    }

    /// <summary>
    /// Verifies the sampled ensemble against exact Normal theory: with independent Gaussian
    /// residuals the weighted average at a fixed input is <c>Normal(Σωᵢ(αᵢ + βᵢx), √(Σωᵢ²σᵢ²))</c>.
    /// </summary>
    /// <remarks>
    /// Tolerances are k·SE with k = 4 at N = 1,000,000: mean ±4σ/√N; standard deviation
    /// ±4σ/√(2N) from μ₄ = 3σ⁴ for a Normal; the 5th and 95th percentiles
    /// ±4·√(p(1−p)/N)/φ(z_p)·σ. Children are seeded independently by the composite's own
    /// content-derived recursion, which is what makes the variance additive in ω² rather than
    /// co-monotonic.
    /// </remarks>
    [TestMethod]
    public void Test_UncertainLinearChildren_VsExactNormalTheory()
    {
        // Arrange
        double[] alphas = { 2d, 10d };
        double[] betas = { 3d, 4d };
        double[] sigmas = { 1.5d, 2.5d };
        double[] weights = { 0.4d, 0.6d };
        var composite = Composite(
            (Linear("A", alphas[0], betas[0], sigmas[0]), weights[0]),
            (Linear("B", alphas[1], betas[1], sigmas[1]), weights[1]));
        composite.SetupSampler(Realizations, EngineSeed, SamplingScheme.LatinHypercube);

        double expectedMean = (weights[0] * (alphas[0] + (betas[0] * Probe)))
            + (weights[1] * (alphas[1] + (betas[1] * Probe)));
        double expectedSd = Math.Sqrt((weights[0] * weights[0] * sigmas[0] * sigmas[0])
            + (weights[1] * weights[1] * sigmas[1] * sigmas[1]));

        // Act — the ensemble at the probe input.
        var values = new double[Realizations];
        for (int k = 0; k < Realizations; k++)
        {
            values[k] = composite.SampleFunction(k).Function(Probe);
        }

        double sum = 0d;
        for (int k = 0; k < Realizations; k++) sum += values[k];
        double mean = sum / Realizations;

        double squares = 0d;
        for (int k = 0; k < Realizations; k++)
        {
            double d = values[k] - mean;
            squares += d * d;
        }
        double sd = Math.Sqrt(squares / (Realizations - 1));

        Array.Sort(values);
        double q05 = Statistics.Percentile(values, 0.05d, dataIsSorted: true);
        double q95 = Statistics.Percentile(values, 0.95d, dataIsSorted: true);

        // Assert — mean: 4σ/√N.
        Assert.AreEqual(expectedMean, mean, 4d * expectedSd / Math.Sqrt(Realizations), "Ensemble mean.");

        // Assert — standard deviation: 4σ/√(2N) (Normal μ₄ = 3σ⁴).
        Assert.AreEqual(expectedSd, sd, 4d * expectedSd / Math.Sqrt(2d * Realizations), "Ensemble standard deviation.");

        // Assert — percentiles: 4·√(p(1−p)/N)/f(x_p), with f the Normal density at the quantile.
        var exact = new Normal(expectedMean, expectedSd);
        double density05 = exact.PDF(exact.InverseCDF(0.05d));
        double density95 = exact.PDF(exact.InverseCDF(0.95d));
        Assert.AreEqual(exact.InverseCDF(0.05d), q05,
            4d * Math.Sqrt(0.05d * 0.95d / Realizations) / density05, "Ensemble 5th percentile.");
        Assert.AreEqual(exact.InverseCDF(0.95d), q95,
            4d * Math.Sqrt(0.05d * 0.95d / Realizations) / density95, "Ensemble 95th percentile.");
    }

    /// <summary>
    /// Verifies a mixed-family composite (linear plus power children) against a weighted sum
    /// computed independently from the children's own sampled curves, realization for realization.
    /// </summary>
    /// <remarks>
    /// This is an exact identity rather than a statistical comparison: the composite's contract is
    /// that realization <c>k</c> of the combine is the weighted sum of realization <c>k</c> of each
    /// child, where each child draws from its own content-derived seed. The oracle reproduces that
    /// seeding recipe independently, so any drift in the recursion surfaces here.
    /// </remarks>
    [TestMethod]
    public void Test_MixedChildFamilies_EqualWeightedSumOfChildRealizations()
    {
        // Arrange
        var linear = Linear("Linear", 2d, 3d, 1.5d);
        var power = new PowerTransform
        {
            Name = "Power",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            Alpha = 5d,
            Beta = 0.5d,
            Xi = 0d,
            Sigma = 0.05d,
            IsUncertain = true,
        };
        double[] weights = { 0.4d, 0.6d };
        var composite = Composite((linear, weights[0]), (power, weights[1]));
        composite.SetupSampler(10_000, EngineSeed, SamplingScheme.LatinHypercube);

        // Arrange — the oracle reproduces the documented child-seed recipe independently.
        var oracleLinear = Linear("Replica A", 2d, 3d, 1.5d);
        var oraclePower = new PowerTransform
        {
            Name = "Replica B",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            Alpha = 5d,
            Beta = 0.5d,
            Xi = 0d,
            Sigma = 0.05d,
            IsUncertain = true,
        };
        oracleLinear.SetupSampler(10_000,
            Core.SeedHelpers.HashCombine(EngineSeed, oracleLinear.CanonicalHash(), 0), SamplingScheme.LatinHypercube);
        oraclePower.SetupSampler(10_000,
            Core.SeedHelpers.HashCombine(EngineSeed, oraclePower.CanonicalHash(), 1), SamplingScheme.LatinHypercube);

        // Act / Assert
        for (int k = 0; k < 10_000; k += 13)
        {
            double expected = (weights[0] * oracleLinear.SampleFunction(k).Function(Probe))
                + (weights[1] * oraclePower.SampleFunction(k).Function(Probe));
            Assert.AreEqual(expected, composite.SampleFunction(k).Function(Probe), 1e-12,
                $"Combine at realization {k}.");
        }
    }

    /// <summary>
    /// Verifies the deterministic uncertainty summary is exact: with deterministic children every
    /// band collapses onto the closed-form combined curve, with no simulation.
    /// </summary>
    [TestMethod]
    public void Test_UncertaintySummary_DeterministicChildren_IsExact()
    {
        // Arrange — 0.25·(2 + 3X) + 0.75·(10 + 4X) = 8 + 3.75X.
        var composite = Composite((Linear("A", 2d, 3d), 0.25d), (Linear("B", 10d, 4d), 0.75d));

        // Act
        var results = composite.ComputeUncertaintyResults(0.9d);
        double[] hazards = composite.UncertaintySummaryHazards();

        // Assert
        Assert.IsNotNull(results);
        for (int i = 0; i < hazards.Length; i++)
        {
            double exact = 8d + (3.75d * hazards[i]);
            Assert.AreEqual(exact, results!.MeanCurve![i], 1e-12, $"Mean curve at x = {hazards[i]}.");
            Assert.AreEqual(exact, results.ModeCurve![i], 1e-12, $"Mode curve at x = {hazards[i]}.");
            Assert.AreEqual(exact, results.ConfidenceIntervals![i, 0], 1e-12, $"Lower band at x = {hazards[i]}.");
            Assert.AreEqual(exact, results.ConfidenceIntervals[i, 1], 1e-12, $"Upper band at x = {hazards[i]}.");
        }
    }

    /// <summary>
    /// Verifies the uncertain uncertainty summary against exact Normal theory at every summary
    /// input, on the internal 10,000-realization median-LHS design the summary uses.
    /// </summary>
    /// <remarks>
    /// The summary runs a clone at 10,000 median-Latin-hypercube realizations, so the tolerance is
    /// k·SE with k = 4 at N = 10,000 rather than at the ensemble count: mean ±4σ/√N, bands
    /// ±4·√(p(1−p)/N)/f(x_p). Median LHS is a stratified design, so these are conservative.
    /// </remarks>
    [TestMethod]
    public void Test_UncertaintySummary_UncertainChildren_VsExactNormalTheory()
    {
        // Arrange
        double[] alphas = { 2d, 10d };
        double[] betas = { 3d, 4d };
        double[] sigmas = { 1.5d, 2.5d };
        double[] weights = { 0.4d, 0.6d };
        var composite = Composite(
            (Linear("A", alphas[0], betas[0], sigmas[0]), weights[0]),
            (Linear("B", alphas[1], betas[1], sigmas[1]), weights[1]));

        const int SummaryRealizations = 10_000;
        double expectedSd = Math.Sqrt((weights[0] * weights[0] * sigmas[0] * sigmas[0])
            + (weights[1] * weights[1] * sigmas[1] * sigmas[1]));

        // Act
        var results = composite.ComputeUncertaintyResults(0.9d);
        double[] hazards = composite.UncertaintySummaryHazards();
        Assert.IsNotNull(results);

        // Assert
        double meanTolerance = 4d * expectedSd / Math.Sqrt(SummaryRealizations);
        var standard = new Normal(0d, 1d);
        double bandTolerance = 4d * Math.Sqrt(0.05d * 0.95d / SummaryRealizations)
            / (standard.PDF(standard.InverseCDF(0.05d)) / expectedSd);

        for (int i = 0; i < hazards.Length; i += 11)
        {
            double expectedMean = (weights[0] * (alphas[0] + (betas[0] * hazards[i])))
                + (weights[1] * (alphas[1] + (betas[1] * hazards[i])));
            var exact = new Normal(expectedMean, expectedSd);

            Assert.AreEqual(expectedMean, results!.MeanCurve![i], meanTolerance, $"Mean at x = {hazards[i]}.");
            Assert.AreEqual(exact.InverseCDF(0.05d), results.ConfidenceIntervals![i, 0], bandTolerance,
                $"Lower band at x = {hazards[i]}.");
            Assert.AreEqual(exact.InverseCDF(0.95d), results.ConfidenceIntervals[i, 1], bandTolerance,
                $"Upper band at x = {hazards[i]}.");
        }
    }

    /// <summary>
    /// Pins reproducibility: identical compute content reproduces bit-identical draws regardless of
    /// names and ids, and a compute edit moves the stream.
    /// </summary>
    /// <remarks>
    /// Unlike the hazard and response families, this one compares two independently built
    /// composites directly: closed-form transform children carry no estimated posterior, so they
    /// are free of the upstream bootstrap nondeterminism those families have to work around.
    /// </remarks>
    [TestMethod]
    public void Test_Reproducibility_SeedAndMetadataPins()
    {
        // Arrange
        var first = Composite((Linear("A", 2d, 3d, 1.5d), 0.4d), (Linear("B", 10d, 4d, 2.5d), 0.6d));
        var second = Composite((Linear("X", 2d, 3d, 1.5d), 0.4d), (Linear("Y", 10d, 4d, 2.5d), 0.6d));
        second.Name = "A different name entirely";
        second.AssignNewId();

        // Assert — identical compute content hashes identically despite different names and ids.
        CollectionAssert.AreEqual(first.CanonicalHash(), second.CanonicalHash());

        first.SetupSampler(100_000, EngineSeed, SamplingScheme.LatinHypercube);
        second.SetupSampler(100_000, EngineSeed, SamplingScheme.LatinHypercube);
        for (int k = 0; k < 100_000; k += 997)
        {
            Assert.AreEqual(first.SampleFunction(k).Function(Probe), second.SampleFunction(k).Function(Probe), 0d,
                $"Metadata edits moved the draw at realization {k}.");
        }

        // A compute edit moves the hash — and therefore every child's derived seed.
        var edited = Composite((Linear("A", 2d, 3d, 1.5d), 0.5d), (Linear("B", 10d, 4d, 2.5d), 0.5d));
        CollectionAssert.AreNotEqual(first.CanonicalHash(), edited.CanonicalHash());
    }
}
