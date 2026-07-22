using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The compact summary of one system component's realization: the five stream summaries and
    /// the per-failure-mode summaries.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class ComponentResults
    {
        /// <summary>
        /// Initializes an empty summary (deserialization support).
        /// </summary>
        public ComponentResults()
        {
            FailureModeResults = new List<FailureModeResults>();
            Excess = new SummaryRiskResults();
            Background = new SummaryRiskResults();
            Total = new SummaryRiskResults();
            Fail = new SummaryRiskResults();
            NonFail = new SummaryRiskResults();
        }

        /// <summary>
        /// Captures the summary of a finished component realization.
        /// </summary>
        /// <param name="componentRealization">The realization to summarize.</param>
        /// <exception cref="ArgumentNullException">Thrown when the realization is null.</exception>
        public ComponentResults(ComponentRealization componentRealization)
        {
            if (componentRealization == null) throw new ArgumentNullException(nameof(componentRealization));
            Excess = new SummaryRiskResults(componentRealization.Curves.Excess);
            Background = new SummaryRiskResults(componentRealization.Curves.Background);
            Total = new SummaryRiskResults(componentRealization.Curves.Total);
            Fail = new SummaryRiskResults(componentRealization.Curves.Fail);
            NonFail = new SummaryRiskResults(componentRealization.Curves.NonFail);

            FailureModeResults = new List<FailureModeResults>(componentRealization.FailureModes.Count);
            for (int i = 0; i < componentRealization.FailureModes.Count; i++)
            {
                FailureModeResults.Add(new FailureModeResults(componentRealization.FailureModes[i]));
            }
        }

        /// <summary>
        /// The per-failure-mode summaries, in the component's projected failure-mode order.
        /// </summary>
        public List<FailureModeResults> FailureModeResults { get; set; }

        /// <summary>
        /// The incremental (excess) risk summary.
        /// </summary>
        public SummaryRiskResults Excess { get; set; }

        /// <summary>
        /// The background (irreducible) risk summary.
        /// </summary>
        public SummaryRiskResults Background { get; set; }

        /// <summary>
        /// The total risk summary.
        /// </summary>
        public SummaryRiskResults Total { get; set; }

        /// <summary>
        /// The failure risk summary — its total probability is the component's annualized failure
        /// probability.
        /// </summary>
        public SummaryRiskResults Fail { get; set; }

        /// <summary>
        /// The non-failure risk summary.
        /// </summary>
        public SummaryRiskResults NonFail { get; set; }
    }
}
