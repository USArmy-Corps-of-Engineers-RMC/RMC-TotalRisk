using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The runtime side-car that accumulates one scope's attributed contribution samples across
    /// a realization's recording evaluations and finalizes them into a
    /// <see cref="RiskContribution"/> under the engine's two mass regimes (Phase 6.6). Never
    /// serialized; rows are cleared with the realization's recorded points.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// One row per recording evaluation carries the evaluation's probability coordinate and the
    /// scope's attributed (probability, failure-value, excess-value) sums at that evaluation.
    /// <see cref="FinalizeTrapezoid"/> replicates <c>Curve.ProcessHazardProbabilities</c>'s mass
    /// derivation — sort by probability, merge equal coordinates by summing, midpoint-trapezoid
    /// partition — so the accumulated rows see the <i>identical</i> mass multiset the recorded
    /// curves see, and Σ scope contributions telescopes to the parent's recorded totals to
    /// floating-point association. <see cref="FinalizeDirect"/> serves the VEGAS path, where the
    /// probability coordinate is already the recorded weight and the caller supplies the
    /// self-normalization scale. The accumulation is deliberately kept in separate chains from
    /// the recorded curves — the existing floating-point op order is untouched, the
    /// bit-identity guard for every pinned result.
    /// </para>
    /// </remarks>
    internal sealed class ContributionAccumulator
    {
        /// <summary>
        /// One recording evaluation's attributed sums.
        /// </summary>
        private readonly struct ContributionRow
        {
            /// <summary>
            /// Initializes a row.
            /// </summary>
            /// <param name="probability">The evaluation's probability coordinate (non-exceedance on the 1D path; the VEGAS weight on the joint path).</param>
            /// <param name="probabilityShare">The attributed probability at this evaluation.</param>
            /// <param name="failureShare">The attributed probability × failure consequence.</param>
            /// <param name="excessShare">The attributed probability × excess consequence.</param>
            public ContributionRow(double probability, double probabilityShare, double failureShare, double excessShare)
            {
                Probability = probability;
                ProbabilityShare = probabilityShare;
                FailureShare = failureShare;
                ExcessShare = excessShare;
            }

            /// <summary>The evaluation's probability coordinate.</summary>
            public double Probability { get; }

            /// <summary>The attributed probability at this evaluation.</summary>
            public double ProbabilityShare { get; }

            /// <summary>The attributed probability × failure consequence.</summary>
            public double FailureShare { get; }

            /// <summary>The attributed probability × excess consequence.</summary>
            public double ExcessShare { get; }
        }

        /// <summary>
        /// The accumulated rows, one per recording evaluation (appended in evaluation order).
        /// </summary>
        private List<ContributionRow> _rows = new List<ContributionRow>();

        /// <summary>
        /// Appends one recording evaluation's attributed sums.
        /// </summary>
        /// <param name="probability">The evaluation's probability coordinate.</param>
        /// <param name="probabilityShare">The attributed probability.</param>
        /// <param name="failureShare">The attributed probability × failure consequence.</param>
        /// <param name="excessShare">The attributed probability × excess consequence.</param>
        public void Add(double probability, double probabilityShare, double failureShare, double excessShare)
        {
            _rows.Add(new ContributionRow(probability, probabilityShare, failureShare, excessShare));
        }

        /// <summary>
        /// Finalizes under the one-dimensional path's mass regime: sort by probability, merge
        /// equal coordinates by summing their shares (the recorded curves concatenate entries at
        /// merged points, which sums the same way), then apply the midpoint-trapezoid partition
        /// and integrate. Fewer than two distinct coordinates finalize to zero — the same guard
        /// under which the recorded curves decline to build.
        /// </summary>
        /// <returns>The finalized contribution.</returns>
        public RiskContribution FinalizeTrapezoid()
        {
            var rows = _rows;
            if (rows.Count < 2) return new RiskContribution();

            rows.Sort((x, y) => x.Probability.CompareTo(y.Probability));
            var merged = new List<ContributionRow>(rows.Count) { rows[0] };
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var last = merged[merged.Count - 1];
                if (row.Probability == last.Probability)
                {
                    merged[merged.Count - 1] = new ContributionRow(last.Probability,
                        last.ProbabilityShare + row.ProbabilityShare,
                        last.FailureShare + row.FailureShare,
                        last.ExcessShare + row.ExcessShare);
                }
                else
                {
                    merged.Add(row);
                }
            }
            if (merged.Count < 2) return new RiskContribution();

            int n = merged.Count;
            var contribution = new RiskContribution();
            for (int i = 0; i < n; i++)
            {
                double mass;
                if (i == 0)
                {
                    mass = (merged[0].Probability + merged[1].Probability) / 2d;
                }
                else if (i == n - 1)
                {
                    mass = 1d - (merged[n - 2].Probability + merged[n - 1].Probability) / 2d;
                }
                else
                {
                    mass = (merged[i].Probability + merged[i + 1].Probability) / 2d
                         - (merged[i].Probability + merged[i - 1].Probability) / 2d;
                }
                contribution.FailureProbability += mass * merged[i].ProbabilityShare;
                contribution.FailureMean += mass * merged[i].FailureShare;
                contribution.ExcessMean += mass * merged[i].ExcessShare;
            }
            return contribution;
        }

        /// <summary>
        /// Finalizes under the VEGAS path's mass regime: the probability coordinate is the
        /// recorded weight, scaled by the joint path's self-normalization factor.
        /// </summary>
        /// <param name="scale">The self-normalization scale (the reciprocal of the realized weight sum).</param>
        /// <returns>The finalized contribution.</returns>
        public RiskContribution FinalizeDirect(double scale)
        {
            var contribution = new RiskContribution();
            var rows = _rows;
            for (int i = 0; i < rows.Count; i++)
            {
                contribution.FailureProbability += rows[i].Probability * rows[i].ProbabilityShare;
                contribution.FailureMean += rows[i].Probability * rows[i].FailureShare;
                contribution.ExcessMean += rows[i].Probability * rows[i].ExcessShare;
            }
            contribution.FailureProbability *= scale;
            contribution.FailureMean *= scale;
            contribution.ExcessMean *= scale;
            return contribution;
        }

        /// <summary>
        /// Clears the accumulated rows (the realization's memory dump).
        /// </summary>
        public void Clear()
        {
            _rows = new List<ContributionRow>();
        }
    }
}
