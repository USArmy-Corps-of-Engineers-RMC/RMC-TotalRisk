using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The compact summary of one failure mode's realization: the Excess and Fail stream
    /// summaries (the decision-relevant per-mode measures — v1.0 scope, preserved), per
    /// consequence type.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class FailureModeResults
    {
        /// <summary>
        /// Initializes an empty summary (deserialization support).
        /// </summary>
        public FailureModeResults()
        {
            Excess = new SummaryRiskResults();
            Fail = new SummaryRiskResults();
            AdditionalConsequences = new List<ConsequenceResults>();
        }

        /// <summary>
        /// Captures the summary of a finished failure-mode realization, including every
        /// additional consequence type.
        /// </summary>
        /// <param name="failureModeRealization">The realization to summarize.</param>
        /// <exception cref="ArgumentNullException">Thrown when the realization is null.</exception>
        public FailureModeResults(FailureModeRealization failureModeRealization)
        {
            if (failureModeRealization == null) throw new ArgumentNullException(nameof(failureModeRealization));
            Excess = new SummaryRiskResults(failureModeRealization.Curves.Excess);
            Fail = new SummaryRiskResults(failureModeRealization.Curves.Fail);
            AdditionalConsequences = new List<ConsequenceResults>(failureModeRealization.AdditionalCurves.Count);
            for (int k = 0; k < failureModeRealization.AdditionalCurves.Count; k++)
            {
                AdditionalConsequences.Add(new ConsequenceResults(failureModeRealization.AdditionalCurves[k]));
            }
        }

        /// <summary>
        /// The incremental (excess) risk summary (primary consequence type).
        /// </summary>
        public SummaryRiskResults Excess { get; set; }

        /// <summary>
        /// The failure risk summary — its total probability is the mode's annualized failure
        /// probability (primary consequence type).
        /// </summary>
        public SummaryRiskResults Fail { get; set; }

        /// <summary>
        /// The additional consequence types' summaries, in declared order (entry k − 1 is type
        /// k; the mode scope records Excess and Fail only, so the other streams summarize
        /// empty). Empty on a single-type analysis.
        /// </summary>
        public List<ConsequenceResults> AdditionalConsequences { get; set; }
    }
}
