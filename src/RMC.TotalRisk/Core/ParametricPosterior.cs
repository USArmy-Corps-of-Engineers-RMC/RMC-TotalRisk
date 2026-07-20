using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using Numerics.Mathematics.Optimization;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// Posterior-ensemble summary math shared by the parametric risk functions: builds Numerics
    /// <see cref="UncertaintyAnalysisResults"/> from an externally supplied posterior
    /// (<see cref="ParameterSet"/> list), and re-slices confidence intervals from a stored
    /// posterior at any requested width.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// This backs the v1.1 posterior-injection contract: the future UI importer passes fitted
    /// posteriors (e.g., RMC-BestFit univariate, Bulletin 17C, or point-process analyses) as
    /// already-parsed Numerics artifacts — no BestFit assembly reference ever enters the model
    /// library. The mean curve is the expected quantile per probability ordinate across the
    /// posterior; confidence bounds are the (1∓w)/2 sample percentiles of the same quantile
    /// ensemble — matching the bootstrap definition, so injected and bootstrapped posteriors
    /// summarize identically.
    /// </para>
    /// </remarks>
    public static class ParametricPosterior
    {
        /// <summary>
        /// Builds uncertainty results from an externally supplied posterior parameter-set ensemble.
        /// </summary>
        /// <param name="parentDistribution">The parent distribution the parameter sets configure.</param>
        /// <param name="parameterSets">The posterior parameter sets (one per posterior draw).</param>
        /// <param name="probabilities">The non-exceedance probabilities to summarize at.</param>
        /// <param name="alpha">The two-sided tail probability: bounds at α/2 and 1 − α/2.</param>
        /// <returns>
        /// Results carrying the parent distribution, the supplied parameter sets, the parent
        /// (mode) curve, the posterior mean curve, and the confidence intervals.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the posterior is empty or a parameter set has no values.</exception>
        public static UncertaintyAnalysisResults BuildResults(UnivariateDistributionBase parentDistribution,
            IList<ParameterSet> parameterSets, double[] probabilities, double alpha)
        {
            if (parentDistribution is null) throw new ArgumentNullException(nameof(parentDistribution));
            if (parameterSets is null) throw new ArgumentNullException(nameof(parameterSets));
            if (probabilities is null) throw new ArgumentNullException(nameof(probabilities));
            if (parameterSets.Count == 0) throw new ArgumentException("The posterior must contain at least one parameter set.", nameof(parameterSets));
            for (int i = 0; i < parameterSets.Count; i++)
            {
                if (parameterSets[i].Values is null)
                    throw new ArgumentException($"Posterior parameter set {i} has no values.", nameof(parameterSets));
            }

            var results = new UncertaintyAnalysisResults
            {
                ParentDistribution = parentDistribution.Clone(),
                ParameterSets = new ParameterSet[parameterSets.Count],
                ModeCurve = new double[probabilities.Length],
                MeanCurve = new double[probabilities.Length],
            };
            for (int i = 0; i < parameterSets.Count; i++)
                results.ParameterSets[i] = parameterSets[i];
            for (int i = 0; i < probabilities.Length; i++)
                results.ModeCurve[i] = parentDistribution.InverseCDF(probabilities[i]);

            double[,] quantiles = QuantileMatrix(parentDistribution, results.ParameterSets, probabilities);
            results.ConfidenceIntervals = new double[probabilities.Length, 2];
            int draws = parameterSets.Count;
            var row = new double[draws];
            for (int i = 0; i < probabilities.Length; i++)
            {
                double sum = 0d;
                for (int j = 0; j < draws; j++)
                {
                    row[j] = quantiles[i, j];
                    sum += row[j];
                }
                Array.Sort(row);
                results.MeanCurve[i] = sum / draws;
                results.ConfidenceIntervals[i, 0] = Statistics.Percentile(row, alpha / 2d, dataIsSorted: true);
                results.ConfidenceIntervals[i, 1] = Statistics.Percentile(row, 1d - alpha / 2d, dataIsSorted: true);
            }

            return results;
        }

        /// <summary>
        /// Re-slices confidence intervals from a stored posterior at a requested width.
        /// </summary>
        /// <param name="parentDistribution">The parent distribution the parameter sets configure.</param>
        /// <param name="parameterSets">The stored posterior parameter sets.</param>
        /// <param name="probabilities">The non-exceedance probabilities to summarize at.</param>
        /// <param name="confidenceIntervalWidth">The two-sided width in (0, 1).</param>
        /// <returns>The [ordinate, lower/upper] confidence bounds.</returns>
        public static double[,] SliceConfidenceIntervals(UnivariateDistributionBase parentDistribution,
            ParameterSet[] parameterSets, double[] probabilities, double confidenceIntervalWidth)
        {
            double alpha = 1d - confidenceIntervalWidth;
            double[,] quantiles = QuantileMatrix(parentDistribution, parameterSets, probabilities);
            var bounds = new double[probabilities.Length, 2];
            int draws = parameterSets.Length;
            var row = new double[draws];
            for (int i = 0; i < probabilities.Length; i++)
            {
                for (int j = 0; j < draws; j++)
                    row[j] = quantiles[i, j];
                Array.Sort(row);
                bounds[i, 0] = Statistics.Percentile(row, alpha / 2d, dataIsSorted: true);
                bounds[i, 1] = Statistics.Percentile(row, 1d - alpha / 2d, dataIsSorted: true);
            }
            return bounds;
        }

        /// <summary>
        /// Evaluates the posterior quantile ensemble: one configured-clone quantile per
        /// (probability ordinate, posterior draw) pair.
        /// </summary>
        /// <param name="parentDistribution">The parent distribution the parameter sets configure.</param>
        /// <param name="parameterSets">The posterior parameter sets.</param>
        /// <param name="probabilities">The non-exceedance probabilities to evaluate.</param>
        /// <returns>The [ordinate, draw] quantile matrix.</returns>
        /// <remarks>
        /// Parallel over draws with per-draw clones — thread-safe by construction (no shared
        /// mutable distribution state inside the loop).
        /// </remarks>
        private static double[,] QuantileMatrix(UnivariateDistributionBase parentDistribution,
            ParameterSet[] parameterSets, double[] probabilities)
        {
            var quantiles = new double[probabilities.Length, parameterSets.Length];
            Parallel.For(0, parameterSets.Length, j =>
            {
                var distribution = parentDistribution.Clone();
                distribution.SetParameters(parameterSets[j].Values);
                for (int i = 0; i < probabilities.Length; i++)
                    quantiles[i, j] = distribution.InverseCDF(probabilities[i]);
            });
            return quantiles;
        }
    }
}
