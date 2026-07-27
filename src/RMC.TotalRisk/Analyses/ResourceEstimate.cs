using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Analyses
{
    /// <summary>
    /// The severity of a resource-estimate finding.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public enum ResourceSeverity
    {
        /// <summary>A reported quantity that needs no action.</summary>
        Informational,

        /// <summary>A quantity large enough to be worth the caller's attention.</summary>
        Warning,

        /// <summary>A quantity that will not run on a normal machine.</summary>
        Error,
    }

    /// <summary>
    /// One line of a resource estimate: what it measures, how large it is, and — when it is large
    /// enough to matter — which option to change.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class ResourceEstimateItem
    {
        /// <summary>
        /// Initializes an estimate line.
        /// </summary>
        /// <param name="label">What the line measures.</param>
        /// <param name="bytes">The estimated bytes, or zero when the line measures work rather than memory.</param>
        /// <param name="operations">The estimated operation count, or zero when the line measures memory.</param>
        /// <param name="isRetained">True when the memory lives for the whole run rather than one realization.</param>
        /// <param name="severity">The finding's severity.</param>
        /// <param name="message">The caller-facing message; names the option to change when the severity is not informational.</param>
        public ResourceEstimateItem(string label, long bytes, double operations, bool isRetained,
            ResourceSeverity severity, string message)
        {
            Label = label ?? string.Empty;
            Bytes = bytes;
            Operations = operations;
            IsRetained = isRetained;
            Severity = severity;
            Message = message ?? string.Empty;
        }

        /// <summary>What the line measures.</summary>
        public string Label { get; }

        /// <summary>The estimated bytes, or zero when the line measures work.</summary>
        public long Bytes { get; }

        /// <summary>The estimated operation count, or zero when the line measures memory.</summary>
        public double Operations { get; }

        /// <summary>True when the memory lives for the whole run rather than one realization.</summary>
        public bool IsRetained { get; }

        /// <summary>The finding's severity.</summary>
        public ResourceSeverity Severity { get; }

        /// <summary>The caller-facing message.</summary>
        public string Message { get; }
    }

    /// <summary>
    /// What a run is expected to cost before it starts: the memory it will hold and the work that
    /// dominates it.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// These are peak LIVE figures, not total allocation. A long ensemble run churns far more
    /// memory than it holds at any instant, so a measured allocation counter will read much higher
    /// than <see cref="PeakLiveBytes"/> and the two are not comparable.
    /// </para>
    /// </remarks>
    public sealed class ResourceEstimate
    {
        /// <summary>
        /// Initializes an estimate from its lines.
        /// </summary>
        /// <param name="items">The estimate lines.</param>
        /// <param name="concurrency">The number of realizations expected to be in flight at once.</param>
        /// <param name="integrandEvaluationFloor">The lower bound on integrand evaluations for the run.</param>
        /// <param name="genzEvaluations">The estimated multivariate-normal rectangle evaluations for the run.</param>
        /// <exception cref="ArgumentNullException">Thrown when the item list is null.</exception>
        public ResourceEstimate(IReadOnlyList<ResourceEstimateItem> items, int concurrency,
            double integrandEvaluationFloor, double genzEvaluations)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            Concurrency = concurrency;
            EstimatedIntegrandEvaluationFloor = integrandEvaluationFloor;
            EstimatedGenzEvaluations = genzEvaluations;

            long retained = 0;
            long transient = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].IsRetained) retained += items[i].Bytes;
                else transient += items[i].Bytes;
            }
            RetainedBytes = retained;
            PeakTransientBytes = transient;
            PeakLiveBytes = retained + transient;
        }

        /// <summary>The estimate lines, in reporting order.</summary>
        public IReadOnlyList<ResourceEstimateItem> Items { get; }

        /// <summary>The number of realizations expected to be in flight at once.</summary>
        public int Concurrency { get; }

        /// <summary>Memory held for the whole run — principally the stored ensemble.</summary>
        public long RetainedBytes { get; }

        /// <summary>Memory held by the realizations in flight.</summary>
        public long PeakTransientBytes { get; }

        /// <summary>The estimated peak live memory.</summary>
        public long PeakLiveBytes { get; }

        /// <summary>
        /// A lower bound on integrand evaluations for the run. A floor, not a bound: adaptive
        /// refinement is limited only by <see cref="RiskAnalysisOptions.MaxEvaluations"/>.
        /// </summary>
        public double EstimatedIntegrandEvaluationFloor { get; }

        /// <summary>
        /// The estimated multivariate-normal rectangle evaluations, which dependent competing-risk
        /// components incur while building their incidence functions.
        /// </summary>
        public double EstimatedGenzEvaluations { get; }

        /// <summary>
        /// The highest severity across the estimate's lines.
        /// </summary>
        public ResourceSeverity Severity
        {
            get
            {
                var worst = ResourceSeverity.Informational;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].Severity > worst) worst = Items[i].Severity;
                }
                return worst;
            }
        }

        /// <summary>
        /// The messages of every line at or above the given severity.
        /// </summary>
        /// <param name="minimum">The lowest severity to report.</param>
        /// <returns>The matching messages, in reporting order.</returns>
        public List<string> Messages(ResourceSeverity minimum)
        {
            var messages = new List<string>();
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Severity >= minimum) messages.Add(Items[i].Message);
            }
            return messages;
        }
    }
}
