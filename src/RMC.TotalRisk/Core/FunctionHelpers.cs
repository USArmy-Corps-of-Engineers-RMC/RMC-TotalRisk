using System;
using Numerics;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// Shared numerical helpers for sampled risk-function curves.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from the v1.0 <c>FunctionHelpers</c> with the BinaryFormatter-based
    /// <c>GenerateSeedFromObject</c> dropped — content-based seeding now derives from
    /// <see cref="CanonicalContentHasher"/> + <see cref="SeedHelpers"/>.
    /// </para>
    /// </remarks>
    public static class FunctionHelpers
    {
        /// <summary>
        /// Forces a sampled curve to be strictly monotonic per its declared X and Y sort orders by
        /// nudging each violating ordinate minimally past its predecessor in a single forward pass.
        /// Curves without a declared order on either axis are left untouched.
        /// </summary>
        /// <param name="opd">The sampled curve, mutated in place.</param>
        /// <exception cref="ArgumentNullException">Thrown when the curve is null.</exception>
        /// <remarks>
        /// <para>
        /// <b>Improved over v1.0.</b> The legacy repair nudged by the absolute machine epsilon
        /// (≈1.11e−16), which is (a) not representable at magnitudes ≥ 1 — at real hazard scales
        /// (stages, flows) the nudge rounded away and the violation survived — and (b) exactly the
        /// tolerance the <see cref="Ordinate"/> equality operator treats as "equal", so the
        /// tolerance-checking ordinate setter silently discarded tie repairs at any scale. v1.1
        /// nudges to the further of one representable step (<see cref="Math.BitIncrement(double)"/> /
        /// <see cref="Math.BitDecrement(double)"/>) and two machine epsilons past the predecessor:
        /// scale-appropriate, minimal, strictly ordered at every magnitude, and always distinct
        /// enough to land through the tolerance-equal setter.
        /// </para>
        /// <para>
        /// The violation checks match the four v1.0 sort-order branches exactly; only the nudge
        /// arithmetic is improved.
        /// </para>
        /// </remarks>
        public static void ForceMonotonic(OrderedPairedData opd)
        {
            if (opd is null) throw new ArgumentNullException(nameof(opd));

            int directionX = Direction(opd.OrderX);
            int directionY = Direction(opd.OrderY);
            if (directionX == 0 || directionY == 0) return;

            for (int i = 1; i < opd.Count; i++)
            {
                bool xViolates = directionX > 0 ? opd[i].X <= opd[i - 1].X : opd[i].X >= opd[i - 1].X;
                bool yViolates = directionY > 0 ? opd[i].Y <= opd[i - 1].Y : opd[i].Y >= opd[i - 1].Y;
                if (xViolates || yViolates)
                {
                    double x = xViolates ? Nudge(opd[i - 1].X, directionX) : opd[i].X;
                    double y = yViolates ? Nudge(opd[i - 1].Y, directionY) : opd[i].Y;
                    opd[i] = new Ordinate(x, y);
                }
            }
        }

        /// <summary>
        /// Maps a sort order onto a monotonic direction: +1 ascending, −1 descending, 0 none.
        /// </summary>
        /// <param name="order">The declared sort order.</param>
        /// <returns>The direction sign.</returns>
        private static int Direction(SortOrder order)
        {
            return order == SortOrder.Ascending ? 1 : order == SortOrder.Descending ? -1 : 0;
        }

        /// <summary>
        /// Returns the minimal value strictly past <paramref name="value"/> in the given direction
        /// that also clears the ordinate-equality tolerance.
        /// </summary>
        /// <param name="value">The predecessor value to move past.</param>
        /// <param name="direction">+1 to move up, −1 to move down.</param>
        /// <returns>The nudged value.</returns>
        /// <remarks>
        /// The further of one representable step and two machine epsilons: the representable step
        /// dominates at large magnitudes (where an absolute epsilon rounds away), the two-epsilon
        /// floor dominates near zero (where one ulp is smaller than the ordinate-equality
        /// tolerance).
        /// </remarks>
        private static double Nudge(double value, int direction)
        {
            return direction > 0
                ? Math.Max(Math.BitIncrement(value), value + 2d * Tools.DoubleMachineEpsilon)
                : Math.Min(Math.BitDecrement(value), value - 2d * Tools.DoubleMachineEpsilon);
        }

        /// <summary>
        /// Summarizes one ordinate's ensemble of sampled values into an uncertainty-results row:
        /// the mean, the median as the mode curve, and the two confidence-interval bounds.
        /// </summary>
        /// <param name="values">The ensemble, indexed [ordinate, realization].</param>
        /// <param name="index">The ordinate to summarize.</param>
        /// <param name="row">
        /// A caller-owned scratch buffer of length equal to the realization count, reused across
        /// ordinates; overwritten and sorted in place.
        /// </param>
        /// <param name="tail">The one-sided tail probability, <c>(1 − confidence width) / 2</c>.</param>
        /// <param name="results">The results being filled.</param>
        /// <exception cref="ArgumentNullException">Thrown when the ensemble, buffer, or results are null.</exception>
        /// <remarks>
        /// The single implementation behind every function cluster's
        /// <c>ComputeUncertaintyResults</c> — the composites and the parametric consequence carried
        /// byte-identical copies of it. The copy-and-sum share one pass in this order deliberately:
        /// the mean is a forward sequential sum over the UNSORTED ensemble, so it does not change
        /// when the buffer is sorted for the percentiles.
        /// </remarks>
        public static void SummarizeEnsembleRow(double[,] values, int index, double[] row, double tail,
            UncertaintyAnalysisResults results)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (row == null) throw new ArgumentNullException(nameof(row));

            double sum = 0d;
            for (int k = 0; k < row.Length; k++)
            {
                row[k] = values[index, k];
                sum += row[k];
            }
            SummarizeEnsembleRow(row, sum, index, tail, results);
        }

        /// <summary>
        /// Summarizes an already-filled ensemble buffer into an uncertainty-results row — the form
        /// for callers that compute their ordinate values directly rather than reading them out of
        /// an ensemble matrix.
        /// </summary>
        /// <param name="row">The ordinate's sampled values; sorted in place.</param>
        /// <param name="sum">The sum of <paramref name="row"/>, accumulated as it was filled.</param>
        /// <param name="index">The ordinate being summarized.</param>
        /// <param name="tail">The one-sided tail probability, <c>(1 − confidence width) / 2</c>.</param>
        /// <param name="results">The results being filled.</param>
        /// <exception cref="ArgumentNullException">Thrown when the buffer or results are null.</exception>
        public static void SummarizeEnsembleRow(double[] row, double sum, int index, double tail,
            UncertaintyAnalysisResults results)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (results == null) throw new ArgumentNullException(nameof(results));

            Array.Sort(row);
            results.MeanCurve![index] = sum / row.Length;
            results.ModeCurve![index] = Statistics.Percentile(row, 0.5d, dataIsSorted: true);
            results.ConfidenceIntervals![index, 0] = Statistics.Percentile(row, tail, dataIsSorted: true);
            results.ConfidenceIntervals[index, 1] = Statistics.Percentile(row, 1d - tail, dataIsSorted: true);
        }
    }
}
