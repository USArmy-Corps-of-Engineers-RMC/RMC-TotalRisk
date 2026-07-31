using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// One node's importance statistics: an event-tree analysis produces one entry per expanded
    /// non-root occurrence, while a fault-tree analysis produces one entry per unified Boolean
    /// basic-event variable.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The probability summary describes the recorded per-node probability of the joint pass: an
    /// event occurrence records its absolute path probability, and a fault variable records its
    /// sampled event probability. The correlation is the Pearson coefficient between the entry's
    /// effective conditional probability and the sampled aggregate, and the first-order index is
    /// the one-at-a-time aggregate variance divided by the joint-pass aggregate variance.
    /// </para>
    /// </remarks>
    public sealed class TreeNodeImportanceEntry
    {
        /// <summary>Initializes one computed entry.</summary>
        /// <param name="nodeId">The authored node's persistent id.</param>
        /// <param name="name">The authored display name.</param>
        /// <param name="canonicalPath">The canonical occurrence path.</param>
        /// <param name="hasUncertainty">Whether the entry's probability source is uncertain.</param>
        /// <param name="probabilitySummary">The five-number summary of the recorded probability.</param>
        /// <param name="aggregateCorrelation">The Pearson correlation with the aggregate.</param>
        /// <param name="firstOrderIndex">The first-order variance-ratio index.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        internal TreeNodeImportanceEntry(Guid nodeId, string name, string canonicalPath,
            bool hasUncertainty, IReadOnlyList<double> probabilitySummary,
            double aggregateCorrelation, double firstOrderIndex)
        {
            NodeId = nodeId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            CanonicalPath = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath));
            HasUncertainty = hasUncertainty;
            ProbabilitySummary = probabilitySummary ?? throw new ArgumentNullException(nameof(probabilitySummary));
            AggregateCorrelation = aggregateCorrelation;
            FirstOrderIndex = firstOrderIndex;
        }

        /// <summary>The authored node's persistent id.</summary>
        public Guid NodeId { get; }

        /// <summary>The authored display name.</summary>
        public string Name { get; }

        /// <summary>The canonical occurrence path that disambiguates repeated occurrences.</summary>
        public string CanonicalPath { get; }

        /// <summary>Whether the entry's probability source carries knowledge uncertainty.</summary>
        public bool HasUncertainty { get; }

        /// <summary>
        /// The five-number summary (minimum, lower quartile, median, upper quartile, maximum) of
        /// the recorded per-node probability across the joint pass.
        /// </summary>
        public IReadOnlyList<double> ProbabilitySummary { get; }

        /// <summary>
        /// The Pearson correlation between the entry's effective conditional probability and the
        /// sampled aggregate. It is <see cref="double.NaN"/> when either series has zero variance.
        /// </summary>
        public double AggregateCorrelation { get; }

        /// <summary>
        /// The first-order variance-ratio index: the aggregate variance with only this entry
        /// varying divided by the joint-pass aggregate variance. It is zero for a deterministic
        /// source and <see cref="double.NaN"/> when the joint-pass aggregate variance is zero.
        /// </summary>
        public double FirstOrderIndex { get; }
    }
}
