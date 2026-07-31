using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// The result of a tree node-importance analysis: the echoed analysis inputs, the sampled
    /// aggregate summary statistics, and one importance entry per analyzed node.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class TreeNodeImportanceResult
    {
        /// <summary>Initializes one computed result.</summary>
        /// <param name="hazardLevel">The analyzed authored hazard level.</param>
        /// <param name="iterations">The Monte Carlo iteration count.</param>
        /// <param name="seed">The base seed.</param>
        /// <param name="aggregateSummary">The aggregate five-number summary.</param>
        /// <param name="aggregateVariance">The joint-pass aggregate variance.</param>
        /// <param name="entries">The per-node importance entries.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        internal TreeNodeImportanceResult(double hazardLevel, int iterations, int seed,
            IReadOnlyList<double> aggregateSummary, double aggregateVariance,
            IReadOnlyList<TreeNodeImportanceEntry> entries)
        {
            HazardLevel = hazardLevel;
            Iterations = iterations;
            Seed = seed;
            AggregateSummary = aggregateSummary ?? throw new ArgumentNullException(nameof(aggregateSummary));
            AggregateVariance = aggregateVariance;
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        /// <summary>The analyzed authored hazard level.</summary>
        public double HazardLevel { get; }

        /// <summary>The Monte Carlo iteration count used by both passes.</summary>
        public int Iterations { get; }

        /// <summary>The base seed the pass streams were derived from.</summary>
        public int Seed { get; }

        /// <summary>
        /// The five-number summary (minimum, lower quartile, median, upper quartile, maximum) of
        /// the sampled aggregate failure probability across the joint pass.
        /// </summary>
        public IReadOnlyList<double> AggregateSummary { get; }

        /// <summary>The joint-pass aggregate variance that normalizes the first-order indices.</summary>
        public double AggregateVariance { get; }

        /// <summary>The per-node importance entries in canonical order.</summary>
        public IReadOnlyList<TreeNodeImportanceEntry> Entries { get; }
    }
}
