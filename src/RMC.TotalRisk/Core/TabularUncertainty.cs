using System;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// Builds Numerics <see cref="UncertaintyAnalysisResults"/> summaries for tabular risk
    /// functions by exact co-monotonic percentile evaluation — the shared implementation behind
    /// <see cref="IRiskFunction.ComputeUncertaintyResults(double)"/> for every tabular type.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// No simulation is involved: because tabular knowledge uncertainty is sampled
    /// co-monotonically (one percentile drives every ordinate), the p-th percentile CURVE is
    /// exactly the curve of per-ordinate p-th percentiles. The mean curve is the per-ordinate
    /// mean, the mode curve carries the median (50th-percentile) curve, and the confidence bounds
    /// are the (1∓w)/2 percentile curves. Curves are index-aligned with the table ordinates;
    /// callers pair them with the ordinate X values. This replaces the v1.0 app-layer code-behind
    /// plotting math with a single model-side implementation shared by the UI, the risk analysis
    /// uncertainty options, and the REST API.
    /// </para>
    /// </remarks>
    public static class TabularUncertainty
    {
        /// <summary>
        /// Computes the uncertainty summary of a co-monotonic uncertain table.
        /// </summary>
        /// <param name="table">The uncertain paired-data table.</param>
        /// <param name="confidenceIntervalWidth">The two-sided confidence-interval width in (0, 1).</param>
        /// <returns>
        /// The summary: MeanCurve = per-ordinate means; ModeCurve = median curve;
        /// ConfidenceIntervals[i, 0..1] = lower/upper percentile curves. Null when the table is
        /// null or empty.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the confidence-interval width is outside (0, 1).
        /// </exception>
        public static UncertaintyAnalysisResults? FromCoMonotonicTable(UncertainOrderedPairedData? table, double confidenceIntervalWidth)
        {
            if (confidenceIntervalWidth <= 0d || confidenceIntervalWidth >= 1d)
                throw new ArgumentOutOfRangeException(nameof(confidenceIntervalWidth), "The confidence interval width must be between 0 and 1.");
            if (table is null || table.Count == 0)
                return null;

            double alpha = (1d - confidenceIntervalWidth) / 2d;
            int count = table.Count;
            var results = new UncertaintyAnalysisResults
            {
                MeanCurve = new double[count],
                ModeCurve = new double[count],
                ConfidenceIntervals = new double[count, 2],
            };

            for (int i = 0; i < count; i++)
            {
                var y = table[i].Y!;
                results.MeanCurve[i] = y.Mean;
                results.ModeCurve[i] = y.InverseCDF(0.5d);
                results.ConfidenceIntervals[i, 0] = y.InverseCDF(alpha);
                results.ConfidenceIntervals[i, 1] = y.InverseCDF(1d - alpha);
            }

            return results;
        }
    }
}
