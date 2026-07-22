using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Numerics.Utilities;
using RMC.TotalRisk.Analyses;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// The analysis lifecycle contract: run, cancel, completion events, and the estimated flag —
    /// the BestFit <c>IAnalysis</c> mirror, with an explicit cancellation-token parameter for
    /// headless hosts.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Events fire on the thread that invokes <see cref="RunAsync"/>; a UI layer wraps its own
    /// dispatcher. Exceptions surface through the completion event's error state rather than
    /// propagating out of <see cref="RunAsync"/> (the BestFit convention), so hosts observe one
    /// uniform completion channel.
    /// </para>
    /// </remarks>
    public interface IAnalysis : INotifyPropertyChanged
    {
        /// <summary>
        /// Occurs immediately before <see cref="RunAsync"/> begins executing. Handlers can set
        /// <see cref="CancelEventArgs.Cancel"/> to prevent the run — the recommended place for a
        /// consuming layer's last-minute checks.
        /// </summary>
        event EventHandler<CancelEventArgs> AnalysisStarting;

        /// <summary>
        /// Occurs after <see cref="RunAsync"/> has completed — successfully, with an error, or
        /// due to cancellation — as described by the event arguments.
        /// </summary>
        event EventHandler<AnalysisRunCompletedEventArgs> AnalysisCompleted;

        /// <summary>
        /// Runs the analysis.
        /// </summary>
        /// <param name="progressReporter">The optional progress sink.</param>
        /// <param name="cancellationToken">
        /// The optional external cancellation token, joined with the analysis's own
        /// <see cref="CancelAnalysis"/> source.
        /// </param>
        /// <returns>The running analysis task.</returns>
        Task RunAsync(SafeProgressReporter? progressReporter = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests cancellation of the running analysis.
        /// </summary>
        void CancelAnalysis();

        /// <summary>
        /// Determines whether the analysis currently holds estimated results.
        /// </summary>
        bool IsEstimated { get; }

        /// <summary>
        /// Validates the current state of the analysis and reports any issues found.
        /// </summary>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item>
        /// <description><c>IsValid</c>: <c>true</c> if the analysis passes all validation checks; otherwise <c>false</c>.</description>
        /// </item>
        /// <item>
        /// <description><c>ValidationMessages</c>: messages describing validation errors ("Error: …", invalidating) and warnings ("Warning: …", advisory).</description>
        /// </item>
        /// </list>
        /// </returns>
        (bool IsValid, List<string> ValidationMessages) Validate();
    }
}
