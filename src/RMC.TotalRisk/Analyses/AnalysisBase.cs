using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Numerics.Utilities;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Analyses
{
    /// <summary>
    /// The common base for analysis types: property change notification, the run lifecycle
    /// events, cancellation support, and the estimated flag — the BestFit <c>AnalysisBase</c>
    /// mirror.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Two deliberate divergences from the BestFit source: (1) the reprocess gate
    /// (<c>ReprocessIfEstimated</c>) is not ported — TotalRisk invalidates results when options
    /// change rather than reprocessing from retained chains, so there is no background reprocess
    /// to serialize against; (2) <see cref="ResetCancellationToken(CancellationToken)"/> links
    /// the fresh run source with a caller-supplied external token, so headless hosts cancel
    /// through their own token while <see cref="CancelAnalysis"/> keeps working (architecture doc
    /// §7.3).
    /// </para>
    /// </remarks>
    public abstract class AnalysisBase : IAnalysis
    {
        /// <summary>
        /// Backing field for <see cref="IsEstimated"/>.
        /// </summary>
        protected bool _isEstimated;

        /// <summary>
        /// The cancellation source for the current run, linked with any external token.
        /// </summary>
        protected CancellationTokenSource? _cancellationTokenSource;

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <inheritdoc/>
        public event EventHandler<CancelEventArgs>? AnalysisStarting;

        /// <inheritdoc/>
        public event EventHandler<AnalysisRunCompletedEventArgs>? AnalysisCompleted;

        /// <inheritdoc/>
        public bool IsEstimated
        {
            get { return _isEstimated; }
            protected set
            {
                if (_isEstimated != value)
                {
                    _isEstimated = value;
                    RaisePropertyChange(nameof(IsEstimated));
                }
            }
        }

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void RaisePropertyChange(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Raises the <see cref="AnalysisStarting"/> event.
        /// </summary>
        /// <param name="e">The cancelable arguments handlers can veto the run through.</param>
        protected virtual void OnAnalysisStarting(CancelEventArgs e)
        {
            AnalysisStarting?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the <see cref="AnalysisCompleted"/> event.
        /// </summary>
        /// <param name="e">The completion arguments describing the run outcome.</param>
        protected virtual void OnAnalysisCompleted(AnalysisRunCompletedEventArgs e)
        {
            AnalysisCompleted?.Invoke(this, e);
        }

        /// <summary>
        /// Disposes any existing cancellation source and creates a fresh one for the next run,
        /// linked with the caller's external token, returning the joined token derived classes
        /// pass into long-running operations.
        /// </summary>
        /// <param name="externalToken">The caller's cancellation token (may be default).</param>
        /// <returns>The joined cancellation token for the run.</returns>
        protected CancellationToken ResetCancellationToken(CancellationToken externalToken)
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            return _cancellationTokenSource.Token;
        }

        /// <inheritdoc/>
        public virtual void CancelAnalysis()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <inheritdoc/>
        public abstract Task RunAsync(SafeProgressReporter? progressReporter = null, CancellationToken cancellationToken = default);

        /// <inheritdoc/>
        public abstract (bool IsValid, List<string> ValidationMessages) Validate();
    }
}
