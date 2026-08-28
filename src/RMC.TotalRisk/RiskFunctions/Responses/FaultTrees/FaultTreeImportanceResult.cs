using System.Collections.Generic;

namespace RMC.TotalRisk.RiskFunctions.Responses.FaultTrees
{
    /// <summary>
    /// The exact fault-tree importance measures at one hazard level: the top-event probability
    /// and one entry per unified basic-event variable, in the plan's ordinal order.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// A plain query result — never serialized. Every value is exact algebra on the frozen
    /// decision diagram (no simulation, no seed): two conditional evaluations per variable on
    /// top of the baseline evaluation.
    /// </para>
    /// </remarks>
    public sealed class FaultTreeImportanceResult
    {
        /// <summary>
        /// Initializes the result.
        /// </summary>
        /// <param name="hazardLevel">The analyzed hazard level.</param>
        /// <param name="percentile">The evaluated knowledge percentile (−1 = the mean).</param>
        /// <param name="topEventProbability">The exact top-event probability at the baseline.</param>
        /// <param name="entries">The per-variable entries in plan ordinal order.</param>
        internal FaultTreeImportanceResult(double hazardLevel, double percentile,
            double topEventProbability, IReadOnlyList<FaultTreeImportanceEntry> entries)
        {
            HazardLevel = hazardLevel;
            Percentile = percentile;
            TopEventProbability = topEventProbability;
            Entries = entries;
        }

        /// <summary>
        /// The analyzed hazard level.
        /// </summary>
        public double HazardLevel { get; }

        /// <summary>
        /// The evaluated knowledge percentile (−1 = every source at its mean).
        /// </summary>
        public double Percentile { get; }

        /// <summary>
        /// The exact top-event probability at the baseline evaluation.
        /// </summary>
        public double TopEventProbability { get; }

        /// <summary>
        /// The per-variable entries, in the occurrence plan's ordinal order.
        /// </summary>
        public IReadOnlyList<FaultTreeImportanceEntry> Entries { get; }
    }
}
