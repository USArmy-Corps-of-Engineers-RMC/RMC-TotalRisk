using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="Curve"/> — the exact loss-exceedance-curve construction, the stable
/// two-pass weighted central moments (with the implicit zero atom on defective curves), the
/// tail-preserving thinning, the mass post-processing, and the risk-measure catalog with its two
/// v1.0 fixes (VaR beyond the total probability, explicit CVaR integration caps).
/// </summary>
[TestClass]
public class CurveTests
{
    /// <summary>
    /// Verifies the exact exceedance construction against a hand-computed discrete distribution,
    /// including the interpolation anchors above the largest and at zero consequence.
    /// </summary>
    [TestMethod]
    public void Test_CreateCurve_ExactExceedance_VsAnalytic()
    {
        // Arrange — an exhaustive three-atom loss distribution.
        var curve = new Curve();
        var pairs = new List<(double Mass, double Consequence)> { (0.2d, 10d), (0.3d, 5d), (0.5d, 1d) };

        // Act
        curve.CreateCurve(pairs, outputLength: 200);

        // Assert — exact reverse-cumulative exceedance with the two anchors.
        CollectionAssert.AreEqual(new[] { 10d * (1d + 1e-8), 10d, 5d, 1d, 0d }, curve.LECConsequences);
        CollectionAssert.AreEqual(new[] { 0d, 0.2d, 0.5d, 1d, 1d }, curve.LECProbabilities);
        Assert.AreEqual(1d, curve.TotalProbability, 0d);
        Assert.AreEqual(1d, curve.MassBalance, 1e-15);

        // Exact moments: E[X] = 4.0, Var = 28 − 16 = 12.
        Assert.AreEqual(4.0d, curve.Mean, 1e-14);
        Assert.AreEqual(Math.Sqrt(12d), curve.StandardDeviation, 1e-13);
    }

    /// <summary>
    /// Verifies defective-curve semantics: the total probability is the recorded mass, and the
    /// moments include the implicit zero-consequence atom so the mean stays unconditional (the
    /// v1.0 semantics, computed stably).
    /// </summary>
    [TestMethod]
    public void Test_CreateCurve_Defective_ZeroAtomMoments()
    {
        // Arrange — the same distribution at one-tenth probability, on a defective stream.
        var curve = new Curve { IsExhaustive = false };
        var pairs = new List<(double Mass, double Consequence)> { (0.02d, 10d), (0.03d, 5d), (0.05d, 1d) };

        // Act
        curve.CreateCurve(pairs, outputLength: 200);

        // Assert — TP = 0.1; unconditional E[X] = 0.4; Var = 2.8 − 0.16 = 2.64 (zero atom carries 0.9).
        Assert.AreEqual(0.1d, curve.TotalProbability, 1e-15);
        Assert.AreEqual(0.4d, curve.Mean, 1e-15);
        Assert.AreEqual(Math.Sqrt(2.64d), curve.StandardDeviation, 1e-13);
        Assert.AreEqual(4.0d, curve.ConditionalMean, 1e-13);

        // The bottom anchor carries the defective total probability at zero consequence.
        Assert.AreEqual(0d, curve.LECConsequences[^1], 0d);
        Assert.AreEqual(0.1d, curve.LECProbabilities[^1], 1e-15);
    }

    /// <summary>
    /// Verifies the catastrophic-cancellation fixture the two-pass accumulation exists for: a
    /// distribution with mean 1e6 and standard deviation 1e-2. The v1.0 raw power sums
    /// (√(u2 − u1²)) lose all significant digits here — u2 ≈ 1e12 has representable spacing
    /// ~1.2e-4, the same order as the entire variance.
    /// </summary>
    [TestMethod]
    public void Test_CreateCurve_WeightedCentralMoments_NoCancellation()
    {
        // Arrange — symmetric two-point distribution: mean 1e6, sd exactly 0.01.
        var curve = new Curve();
        var pairs = new List<(double Mass, double Consequence)> { (0.5d, 1e6 - 0.01d), (0.5d, 1e6 + 0.01d) };

        // Act
        curve.CreateCurve(pairs, outputLength: 200);

        // Assert
        Assert.AreEqual(1e6, curve.Mean, 1e-9);
        Assert.AreEqual(0.01d, curve.StandardDeviation, 1e-9);
        Assert.AreEqual(0d, curve.Skewness, 1e-6);
        Assert.AreEqual(1d, curve.Kurtosis, 1e-6, "A symmetric two-point distribution has plain kurtosis one.");
    }

    /// <summary>
    /// Verifies a degenerate (zero-variance) distribution reports NaN shape moments instead of
    /// dividing by zero.
    /// </summary>
    [TestMethod]
    public void Test_CreateCurve_Degenerate_NaNShapeMoments()
    {
        // Arrange
        var curve = new Curve();
        var pairs = new List<(double Mass, double Consequence)> { (0.6d, 7d), (0.4d, 7d) };

        // Act
        curve.CreateCurve(pairs, outputLength: 200);

        // Assert
        Assert.AreEqual(7d, curve.Mean, 1e-14);
        Assert.AreEqual(0d, curve.StandardDeviation, 1e-14);
        Assert.IsTrue(double.IsNaN(curve.Skewness));
        Assert.IsTrue(double.IsNaN(curve.Kurtosis));
    }

    /// <summary>
    /// Verifies thinning is output-resolution only: moments and total probability are identical
    /// to the unthinned construction, and the stored curve retains the extreme-tail and terminal
    /// ordinates.
    /// </summary>
    [TestMethod]
    public void Test_CreateCurve_Thinning_PreservesMomentsAndTail()
    {
        // Arrange — ten thousand pairs spanning four orders of magnitude.
        var pairs = new List<(double Mass, double Consequence)>(10_000);
        for (int i = 0; i < 10_000; i++)
        {
            pairs.Add((1e-4, Math.Pow(10d, 4d * i / 9_999d)));
        }
        var full = new Curve();
        var thinned = new Curve();

        // Act
        full.CreateCurve(pairs, outputLength: 1000);
        thinned.CreateCurve(pairs, outputLength: 50);

        // Assert — identical exact statistics; bounded storage; tail and terminals retained.
        Assert.AreEqual(full.Mean, thinned.Mean, 0d);
        Assert.AreEqual(full.StandardDeviation, thinned.StandardDeviation, 0d);
        Assert.AreEqual(full.TotalProbability, thinned.TotalProbability, 0d);
        Assert.IsTrue(thinned.LECConsequences.Length <= 52 + 2, $"Stored {thinned.LECConsequences.Length} ordinates for output length 50.");
        Assert.AreEqual(full.LECConsequences[0], thinned.LECConsequences[0], 0d, "The extreme-tail anchor must be retained.");
        Assert.AreEqual(full.LECConsequences[^1], thinned.LECConsequences[^1], 0d, "The terminal ordinate must be retained.");
        Assert.AreEqual(full.LECProbabilities[^1], thinned.LECProbabilities[^1], 0d);
    }

    /// <summary>
    /// Verifies the mass post-processing: sort, duplicate-probability merge, the midpoint
    /// trapezoid partition, and the telescoping budget.
    /// </summary>
    [TestMethod]
    public void Test_ProcessHazardProbabilities_TrapezoidPartition_AndDuplicateMerge()
    {
        // Arrange — three probabilities plus a duplicate that must merge its entries.
        var curve = new Curve();
        curve.AddRiskPoint(1d, 0.5d, 0.2d, 10d);
        curve.AddRiskPoint(2d, 0.1d, 0.3d, 20d);
        curve.AddRiskPoint(3d, 0.9d, 0.4d, 30d);
        curve.AddRiskPoint(4d, 0.5d, 0.6d, 40d);

        // Act
        curve.ProcessHazardProbabilities();

        // Assert — masses (0.1, 0.5, 0.9) → (0.3, 0.4, 0.3); the duplicate p = 0.5 merged.
        Assert.AreEqual(3, curve.RiskPoints.Count);
        Assert.AreEqual(0.3d, curve.RiskPoints[0].HazardProbabilityMass, 1e-15);
        Assert.AreEqual(0.4d, curve.RiskPoints[1].HazardProbabilityMass, 1e-15);
        Assert.AreEqual(0.3d, curve.RiskPoints[2].HazardProbabilityMass, 1e-15);
        Assert.AreEqual(2, curve.RiskPoints[1].ResponseProbabilities.Count, "The duplicate-probability point must merge its entries.");
        Assert.AreEqual(0.2d, curve.RiskPoints[1].ResponseProbabilities[0], 0d);
        Assert.AreEqual(0.6d, curve.RiskPoints[1].ResponseProbabilities[1], 0d);
    }

    /// <summary>
    /// Verifies the value-at-risk fix: when the exceedance level exceeds the curve's total
    /// probability the consequence is not realized and the value at risk is zero — v1.0 returned
    /// the curve's smallest consequence.
    /// </summary>
    [TestMethod]
    public void Test_ComputeRiskMeasures_VaR_ZeroBeyondTotalProbability()
    {
        // Arrange — a defective curve with total probability 0.1.
        var curve = new Curve { IsExhaustive = false };
        curve.CreateCurve(new List<(double Mass, double Consequence)> { (0.02d, 10d), (0.03d, 5d), (0.05d, 1d) }, 200);

        // Act / Assert — above the total probability: zero. Below: a genuine quantile.
        curve.ComputeRiskMeasures(consequenceThreshold: 0d, alpha: 0.2d);
        Assert.AreEqual(0d, curve.ValueAtRisk, 0d, "α above the total probability must yield zero, not the smallest consequence.");

        curve.ComputeRiskMeasures(consequenceThreshold: 0d, alpha: 0.05d);
        Assert.AreEqual(5d, curve.ValueAtRisk, 1e-12, "α = 0.05 sits exactly on the (5, 0.02 + 0.03) ordinate.");
    }

    /// <summary>
    /// Verifies the conditional-value-at-risk integral against a closed form: a log-log linear
    /// LEC X(p) = p^(−1/2) has CVaR(α) = (1/α)·2(√α − √ε) ≈ 2/√α.
    /// </summary>
    [TestMethod]
    public void Test_ComputeRiskMeasures_CVaR_VsClosedForm()
    {
        // Arrange — the power-law curve sampled on a log grid from 1e-16 to 1 (81 ordinates), so
        // log-log interpolation reproduces X(p) exactly over the whole integration domain.
        var probabilities = new double[81];
        var consequences = new double[81];
        for (int j = 0; j <= 80; j++)
        {
            double p = Math.Pow(10d, -16d + 0.2d * j);
            probabilities[j] = p;
            consequences[j] = Math.Pow(p, -0.5d);
        }
        var curve = new Curve
        {
            LECConsequences = consequences,
            LECProbabilities = probabilities,
            TotalProbability = 1d,
        };

        // Act
        curve.ComputeRiskMeasures(consequenceThreshold: 10d, alpha: 0.01d);

        // Assert — VaR(0.01) = 0.01^(−1/2) = 10 (an exact grid ordinate); the assurance measure at
        // threshold 10 reads back 0.01; CVaR = 100·2(√0.01 − √1e-16) = 20 − 2e-6.
        Assert.AreEqual(10d, curve.ValueAtRisk, 1e-9);
        Assert.AreEqual(0.01d, curve.ConsequenceThresholdProbability, 1e-9);
        Assert.AreEqual(20d - 2e-6, curve.ConditionalValueAtRisk, 20d * 1e-6, "CVaR must match the closed form within 1e-6 relative.");
    }

    /// <summary>
    /// Verifies the risk profiles: descending hazard, cumulative exceedance, and the conditional
    /// expected consequence ratio.
    /// </summary>
    [TestMethod]
    public void Test_CreateProfiles_CumulativeExceedanceAndCEN()
    {
        // Arrange — three points; masses are already final (no post-processing).
        var curve = new Curve();
        curve.AddRiskPoint(1d, 0.5d, 0.1d, 10d);
        curve.AddRiskPoint(2d, 0.3d, 0.2d, 20d);
        curve.AddRiskPoint(3d, 0.2d, 0.5d, 30d);

        // Act
        curve.CreateProfiles();

        // Assert — hazard descending; EP accumulates 0.1, 0.16, 0.21; CEN = ΣEAC/ΣEP.
        CollectionAssert.AreEqual(new[] { 3d, 2d, 1d }, curve.HazardFrequencyHazards);
        Assert.AreEqual(0.10d, curve.HazardFrequencyProbabilities[0], 1e-15);
        Assert.AreEqual(0.16d, curve.HazardFrequencyProbabilities[1], 1e-15);
        Assert.AreEqual(0.21d, curve.HazardFrequencyProbabilities[2], 1e-15);
        Assert.AreEqual(30d, curve.HazardVsCenConsequences[0], 1e-12);
        Assert.AreEqual(4.2d / 0.16d, curve.HazardVsCenConsequences[1], 1e-12);
        Assert.AreEqual(4.7d / 0.21d, curve.HazardVsCenConsequences[2], 1e-12);
    }

    /// <summary>
    /// Verifies Clone copies every stored value deeply and DumpMemory clears only the runtime
    /// risk points.
    /// </summary>
    [TestMethod]
    public void Test_CloneAndDumpMemory()
    {
        // Arrange
        var curve = new Curve { IsExhaustive = false };
        curve.AddRiskPoint(1d, 0.5d, 0.2d, 10d);
        curve.CreateCurve(new List<(double Mass, double Consequence)> { (0.1d, 10d), (0.2d, 5d) }, 200);
        curve.ComputeRiskMeasures(1d, 0.05d);

        // Act
        var clone = curve.Clone();
        clone.LECConsequences[0] = -999d;
        curve.DumpMemory();

        // Assert
        Assert.AreEqual(curve.Mean, clone.Mean, 0d);
        Assert.AreEqual(curve.TotalProbability, clone.TotalProbability, 0d);
        Assert.AreEqual(curve.ValueAtRisk, clone.ValueAtRisk, 0d);
        Assert.AreNotEqual(-999d, curve.LECConsequences[0], "Clone must copy the arrays, not share them.");
        Assert.AreEqual(0, curve.RiskPoints.Count, "DumpMemory must clear the runtime risk points.");
        Assert.IsTrue(curve.LECConsequences.Length > 0, "DumpMemory must not touch the stored curve.");
    }

    /// <summary>
    /// Verifies construction argument contracts: null pairs and an output length below two throw.
    /// </summary>
    [TestMethod]
    public void Test_CreateCurve_ArgumentContracts()
    {
        // Act / Assert
        var curve = new Curve();
        Assert.ThrowsException<ArgumentNullException>(() => curve.CreateCurve(null!, 200));
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => curve.CreateCurve(new List<(double Mass, double Consequence)> { (0.5d, 1d) }, 1));
    }
}
