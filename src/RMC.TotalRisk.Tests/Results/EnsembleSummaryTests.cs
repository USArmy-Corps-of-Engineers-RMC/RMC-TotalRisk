using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data.Statistics;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="EnsembleSummary"/> — the scalar-measure percentile
/// reduction: agreement with direct percentile calls, NaN filtering, contribution and
/// diagnostic reduction, the convergence indicators, and the append-only serialization.
/// </summary>
[TestClass]
public class EnsembleSummaryTests
{
    /// <summary>Builds a synthetic ensemble whose system Total means are the given values (with per-realization diagnostics i + 1).</summary>
    private static EnsembleResults Ensemble(params double[] totalMeans)
    {
        var ensemble = new EnsembleResults(totalMeans.Length);
        for (int i = 0; i < totalMeans.Length; i++)
        {
            var realization = new SystemRiskResults();
            realization.Total.Mean = totalMeans[i];
            realization.Total.ValueAtRisk = 10d * totalMeans[i];
            realization.Fail.TotalProbability = 0.01d * (i + 1);
            realization.Fail.HazardThresholdProbability = double.NaN;
            realization.FunctionEvaluations = 100d * (i + 1);
            realization.StandardError = 0.001d * (i + 1);
            ensemble[i] = realization;
        }
        return ensemble;
    }

    /// <summary>
    /// Verifies the reducer against direct percentile calls: every slot equals
    /// <c>Statistics.Percentile</c> over the sorted values at the curve-band convention, the
    /// mean is the sequential mean, an all-NaN measure reduces to NaN, and the convergence
    /// aggregates match hand sums.
    /// </summary>
    [TestMethod]
    public void Test_Compute_ReducerMatchesDirectPercentiles()
    {
        // Arrange — five known values, deliberately unsorted.
        var values = new[] { 5d, 1d, 4d, 2d, 3d };
        var ensemble = Ensemble(values);

        // Act
        var summary = EnsembleSummary.Compute(ensemble, 0.8d)!;

        // Assert — percentile slots vs direct calls at tail = 0.1.
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        Assert.AreEqual(Statistics.Percentile(sorted, 0.1d, true), summary.Lower.Total.Mean, 0d);
        Assert.AreEqual(Statistics.Percentile(sorted, 0.9d, true), summary.Upper.Total.Mean, 0d);
        Assert.AreEqual(Statistics.Percentile(sorted, 0.5d, true), summary.Median.Total.Mean, 0d);
        Assert.AreEqual((5d + 1d + 4d + 2d + 3d) / 5d, summary.Mean.Total.Mean, 0d);

        // A dependent measure reduces on its own axis (VaR = 10 × mean here).
        Assert.AreEqual(Statistics.Percentile(new[] { 10d, 20d, 30d, 40d, 50d }, 0.9d, true), summary.Upper.Total.ValueAtRisk, 0d);

        // The all-NaN measure stays NaN in every slot.
        Assert.IsTrue(double.IsNaN(summary.Lower.Fail.HazardThresholdProbability));
        Assert.IsTrue(double.IsNaN(summary.Mean.Fail.HazardThresholdProbability));

        // Convergence aggregates: Σ and max of the per-realization diagnostics.
        Assert.AreEqual(100d + 200d + 300d + 400d + 500d, summary.Convergence.TotalFunctionEvaluations, 0d);
        Assert.AreEqual(500d, summary.Convergence.MaxFunctionEvaluations, 0d);
        Assert.AreEqual(0.003d, summary.Convergence.MedianStandardError, 1e-15);

        // The APF indicator: mean 0.03, SD/√N of (0.01…0.05).
        var apf = summary.Convergence.Indicators[0];
        Assert.AreEqual("Annualized Failure Probability", apf.Label);
        Assert.AreEqual(0.03d, apf.Mean, 1e-15);
        double sd = Math.Sqrt((0.0004d + 0.0001d + 0d + 0.0001d + 0.0004d) / 4d);
        Assert.AreEqual(sd / Math.Sqrt(5d), apf.EnsembleStandardError, 1e-15);
        Assert.AreEqual(summary.RealizationCount, 5);
    }

    /// <summary>
    /// Verifies the argument and degenerate contracts: an empty ensemble reduces to null,
    /// null-entry realizations are excluded, and an out-of-range width throws.
    /// </summary>
    [TestMethod]
    public void Test_Compute_Contracts()
    {
        // Empty → null.
        Assert.IsNull(EnsembleSummary.Compute(new EnsembleResults(0), 0.9d));

        // Null entries are excluded from the count.
        var sparse = Ensemble(1d, 2d, 3d);
        sparse[1] = null;
        var summary = EnsembleSummary.Compute(sparse, 0.9d)!;
        Assert.AreEqual(2, summary.RealizationCount);
        Assert.AreEqual(2d, summary.Mean.Total.Mean, 0d);

        // Width bounds.
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => EnsembleSummary.Compute(Ensemble(1d), 0d));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => EnsembleSummary.Compute(Ensemble(1d), 1d));
        Assert.ThrowsException<ArgumentNullException>(() => EnsembleSummary.Compute(null!, 0.9d));
    }

    /// <summary>
    /// Verifies the contribution reduction: scopes whose template realization carries a
    /// contribution reduce all three values; scopes without stay null.
    /// </summary>
    [TestMethod]
    public void Test_Compute_ContributionReduction()
    {
        // Arrange — two realizations, one component with one mode carrying contributions.
        var ensemble = new EnsembleResults(2);
        for (int i = 0; i < 2; i++)
        {
            var realization = new SystemRiskResults();
            var component = new ComponentResults
            {
                SystemContribution = new RiskContribution { FailureProbability = 0.1d * (i + 1), FailureMean = 10d * (i + 1), ExcessMean = 5d * (i + 1) },
            };
            component.FailureModeResults.Add(new FailureModeResults
            {
                Contribution = new RiskContribution { FailureProbability = 0.05d * (i + 1), FailureMean = 4d * (i + 1), ExcessMean = 2d * (i + 1) },
            });
            realization.ComponentResults.Add(component);
            ensemble[i] = realization;
        }

        // Act
        var summary = EnsembleSummary.Compute(ensemble, 0.9d)!;

        // Assert
        Assert.AreEqual(0.15d, summary.Mean.ComponentResults[0].SystemContribution!.FailureProbability, 1e-15);
        Assert.AreEqual(15d, summary.Mean.ComponentResults[0].SystemContribution!.FailureMean, 1e-12);
        Assert.AreEqual(6d, summary.Mean.ComponentResults[0].FailureModeResults[0].Contribution!.FailureMean, 1e-12);
        Assert.AreEqual(3d, summary.Mean.ComponentResults[0].FailureModeResults[0].Contribution!.ExcessMean, 1e-12);
    }

    /// <summary>
    /// Verifies the append-only serialization: a stored summary round-trips through the
    /// ensemble JSON, and a payload without the member loads forward as null while
    /// <see cref="EnsembleResults.ComputeSummary"/> rebuilds it from the loaded realizations.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_AppendOnly_AndRecompute()
    {
        // Arrange
        var ensemble = Ensemble(1d, 2d, 3d, 4d);
        ensemble.Summary = ensemble.ComputeSummary(0.8d);

        // Act — round-trip.
        var restored = EnsembleResults.FromJson(ensemble.ToJson())!;

        // Assert
        Assert.IsNotNull(restored.Summary);
        Assert.AreEqual(ensemble.Summary!.Mean.Total.Mean, restored.Summary!.Mean.Total.Mean, 0d);
        Assert.AreEqual(ensemble.Summary.Lower.Total.Mean, restored.Summary.Lower.Total.Mean, 0d);
        Assert.AreEqual(ensemble.Summary.Convergence.TotalFunctionEvaluations, restored.Summary.Convergence.TotalFunctionEvaluations, 0d);

        // The earlier shape (member absent) loads forward as null, and the reduction rebuilds
        // identically from the loaded realizations.
        string legacyJson = System.Text.RegularExpressions.Regex.Replace(ensemble.ToJson(), "\"Summary\":\\{.*\\}(?=,\"|\\}$)", "\"Summary\":null");
        var legacy = EnsembleResults.FromJson(legacyJson)!;
        Assert.IsNull(legacy.Summary);
        var recomputed = legacy.ComputeSummary(0.8d)!;
        Assert.AreEqual(ensemble.Summary.Mean.Total.Mean, recomputed.Mean.Total.Mean, 0d);
        Assert.AreEqual(ensemble.Summary.Upper.Total.ValueAtRisk, recomputed.Upper.Total.ValueAtRisk, 0d);
    }
}
