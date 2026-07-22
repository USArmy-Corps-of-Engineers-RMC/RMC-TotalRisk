using System;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The compact summary of one failure mode's realization: the Excess and Fail stream
    /// summaries (the decision-relevant per-mode measures — v1.0 scope, preserved).
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
        }

        /// <summary>
        /// Captures the summary of a finished failure-mode realization.
        /// </summary>
        /// <param name="failureModeRealization">The realization to summarize.</param>
        /// <exception cref="ArgumentNullException">Thrown when the realization is null.</exception>
        public FailureModeResults(FailureModeRealization failureModeRealization)
        {
            if (failureModeRealization == null) throw new ArgumentNullException(nameof(failureModeRealization));
            Excess = new SummaryRiskResults(failureModeRealization.Curves.Excess);
            Fail = new SummaryRiskResults(failureModeRealization.Curves.Fail);
        }

        /// <summary>
        /// The incremental (excess) risk summary.
        /// </summary>
        public SummaryRiskResults Excess { get; set; }

        /// <summary>
        /// The failure risk summary — its total probability is the mode's annualized failure
        /// probability.
        /// </summary>
        public SummaryRiskResults Fail { get; set; }
    }
}
