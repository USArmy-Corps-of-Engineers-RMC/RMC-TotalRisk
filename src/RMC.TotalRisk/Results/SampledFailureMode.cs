using System;
using System.Collections.Generic;
using Numerics.Distributions;
using Numerics.Functions;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// One failure mode frozen for one Monte Carlo realization: the sampled transform curves,
    /// the sampled response distribution, and the weighted exposure branches of the paired
    /// failure and non-failure consequences.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>SampledFailureMode</c> onto the v1.1 sampler contract: functions are
    /// sampled by realization index from their pre-allocated percentile matrices (never by PRNG
    /// draw), and the failure/non-failure consequence pair shares one knowledge percentile from
    /// the failure mode's coupling matrix — the v1.0 shared-draw coherence (Q-N, resolved: the
    /// technical reference draws C_F and C_NF perfectly correlated within a mode so the
    /// incremental consequence stays consistent on one hazard scenario). Under ratified Q-V the
    /// consequences are held as weighted exposure branches: a mixture consequence contributes one
    /// branch per exposure state in every compute path, so each realization's loss exceedance
    /// curve carries the full day/night spread.
    /// </para>
    /// <para>
    /// Only single-response-stage modes compute in this phase: multi-stage response composition
    /// is deferred to the event-tree phase by ratified decision (end users are expected to want
    /// event-tree branch semantics, not a probability product), and the constructor throws the
    /// documented placeholder error on a multi-stage mode. The analysis validation gate rejects
    /// multi-stage modes before any sampling, so the throw is defense in depth.
    /// </para>
    /// <para>
    /// Q-U interim (ratified): risk math consumes the primary consequence
    /// (<c>ConsequenceFunctions[0]</c>) only; later positions stay coupled through the coupling
    /// matrix shape but are not evaluated until the multi-axis results design lands.
    /// </para>
    /// </remarks>
    public class SampledFailureMode
    {
        #region Construction

        /// <summary>
        /// Samples a failure mode for one realization.
        /// </summary>
        /// <param name="failureMode">The failure mode to sample. Its samplers must be set up (via the owning component) unless the mean is requested.</param>
        /// <param name="nonFailureMode">
        /// The component's non-failure mode, whose primary consequence is paired-sampled at this
        /// mode's coupling percentile for the excess computation; null when the component has
        /// none (or when sampling the non-failure mode itself).
        /// </param>
        /// <param name="realizationIndex">The realization index, or −1 for the mean functions.</param>
        /// <exception cref="ArgumentNullException">Thrown when the failure mode is null.</exception>
        /// <exception cref="NotSupportedException">
        /// Thrown when the mode carries more than one response stage — multi-stage response
        /// composition lands with the event-tree phase.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when sampling by realization index before the samplers have been set up.
        /// </exception>
        public SampledFailureMode(FailureMode failureMode, FailureMode? nonFailureMode, int realizationIndex = -1)
        {
            if (failureMode == null) throw new ArgumentNullException(nameof(failureMode));
            if (failureMode.ResponseStages.Count > 1)
            {
                throw new NotSupportedException(
                    "The failure mode has more than one response stage. Multi-stage response composition " +
                    "is deferred to the event-tree phase; the risk engine computes single-stage modes only.");
            }

            Name = failureMode.ResponseFunction.Name;
            IsNonFailureMode = failureMode.IsNonFailureMode;
            _consequencePosition = failureMode.ResolvedConsequenceHazardPosition;

            bool mean = realizationIndex < 0;

            // Stage-0 transforms and response, sampled from their own content-seeded matrices.
            var stageTransforms = failureMode.HazardToResponse;
            _hazardToResponse = new IUnivariateFunction[stageTransforms.Count];
            for (int i = 0; i < stageTransforms.Count; i++)
            {
                _hazardToResponse[i] = mean ? stageTransforms[i].SampleFunction() : stageTransforms[i].SampleFunction(realizationIndex);
            }
            var response = failureMode.ResponseFunction;
            _response = mean ? response.SampleFunction() : response.SampleFunction(realizationIndex);

            var trailing = failureMode.ResponseToConsequence;
            _responseToConsequence = new IUnivariateFunction[trailing.Count];
            for (int i = 0; i < trailing.Count; i++)
            {
                _responseToConsequence[i] = mean ? trailing[i].SampleFunction() : trailing[i].SampleFunction(realizationIndex);
            }

            // The primary consequence pair, branch-enumerated (Q-V) and coupled on one knowledge
            // percentile (Q-N). A consequence-free mode (reliability) carries the single
            // zero-consequence branch so failure probability still records.
            var primary = failureMode.ConsequenceFunction;
            if (primary == null)
            {
                _failureBranches = ZeroBranch;
            }
            else if (mean)
            {
                _failureBranches = primary.SampleExposureBranches();
            }
            else
            {
                double percentile = failureMode.CouplingPercentile(realizationIndex, 0);
                _failureBranches = primary.SampleExposureBranches(percentile);
            }

            var pairedNonFail = nonFailureMode?.ConsequenceFunction;
            if (pairedNonFail != null)
            {
                _nonFailureBranches = mean
                    ? pairedNonFail.SampleExposureBranches()
                    : pairedNonFail.SampleExposureBranches(failureMode.CouplingPercentile(realizationIndex, 0));
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// The single zero-consequence branch a consequence-free mode carries (reliability mode):
        /// unit weight, null function, evaluated as zero.
        /// </summary>
        private static readonly IReadOnlyList<(double Weight, IUnivariateFunction Function)> ZeroBranch =
            new (double Weight, IUnivariateFunction Function)[] { (1d, null!) };

        /// <summary>
        /// The sampled stage-0 transform curves, in chain order.
        /// </summary>
        private readonly IUnivariateFunction[] _hazardToResponse;

        /// <summary>
        /// The sampled response distribution (fragility): P[F|transformed hazard] = CDF.
        /// </summary>
        private readonly IUnivariateDistribution _response;

        /// <summary>
        /// The sampled trailing transform curves applied from the bound consequence position.
        /// </summary>
        private readonly IUnivariateFunction[] _responseToConsequence;

        /// <summary>
        /// The weighted exposure branches of the primary failure consequence at this
        /// realization's shared knowledge percentile.
        /// </summary>
        private readonly IReadOnlyList<(double Weight, IUnivariateFunction Function)> _failureBranches;

        /// <summary>
        /// The weighted exposure branches of the paired non-failure consequence, sampled at THIS
        /// mode's coupling percentile (the Q-N shared draw); null when no non-failure mode pairs.
        /// </summary>
        private readonly IReadOnlyList<(double Weight, IUnivariateFunction Function)>? _nonFailureBranches;

        /// <summary>
        /// The resolved consequence hazard position: 0 binds the raw hazard, k the signal after
        /// the k-th stage transform.
        /// </summary>
        private readonly int _consequencePosition;

        /// <summary>
        /// The failure mode's display name (the response function's name — v1.0 behavior).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Determines whether this is the component's non-failure mode.
        /// </summary>
        public bool IsNonFailureMode { get; }

        /// <summary>
        /// The number of weighted exposure branches the primary failure consequence carries.
        /// </summary>
        public int FailureBranchCount => _failureBranches.Count;

        #endregion

        #region Methods

        /// <summary>
        /// The system response probability (probability of failure) at a hazard level: the
        /// sampled response distribution's CDF of the transformed hazard, clamped to [0, 1].
        /// </summary>
        /// <param name="hazardLevel">The hazard level.</param>
        /// <returns>The response probability.</returns>
        public double SRP(double hazardLevel)
        {
            double signal = hazardLevel;
            for (int i = 0; i < _hazardToResponse.Length; i++)
            {
                signal = _hazardToResponse[i].Function(signal);
            }
            return Math.Max(0d, Math.Min(1d, _response.CDF(signal)));
        }

        /// <summary>
        /// The hazard level at which the response probability is reached — the inverse of
        /// <see cref="SRP"/> through the transform chain.
        /// </summary>
        /// <param name="probability">The response probability.</param>
        /// <returns>The hazard level.</returns>
        public double InverseSRP(double probability)
        {
            double signal = _response.InverseCDF(probability);
            for (int i = _hazardToResponse.Length - 1; i >= 0; i--)
            {
                signal = _hazardToResponse[i].InverseFunction(signal);
            }
            return signal;
        }

        /// <summary>
        /// The hazard signal feeding this mode's consequences: the chain signal at the bound
        /// position (0 = the raw hazard, k = after the k-th stage transform), folded through the
        /// trailing response-to-consequence transforms.
        /// </summary>
        /// <param name="hazardLevel">The raw hazard level.</param>
        /// <returns>The consequence input signal.</returns>
        public double ConsequenceInput(double hazardLevel)
        {
            double signal = hazardLevel;
            int bound = Math.Min(_consequencePosition, _hazardToResponse.Length);
            for (int i = 0; i < bound; i++)
            {
                signal = _hazardToResponse[i].Function(signal);
            }
            for (int i = 0; i < _responseToConsequence.Length; i++)
            {
                signal = _responseToConsequence[i].Function(signal);
            }
            return signal;
        }

        /// <summary>
        /// Evaluates this mode's own primary-consequence exposure branches at a hazard level —
        /// the component uses this on the non-failure mode to source the Background and NonFail
        /// entries from the mode's own sample (its own coupling draw).
        /// </summary>
        /// <param name="hazardLevel">The raw hazard level.</param>
        /// <param name="flags">The realization's computational-warning flags (negative values clamp with the matching flag).</param>
        /// <param name="weights">Receives the branch weights.</param>
        /// <param name="values">Receives the branch consequence values, clamped at zero.</param>
        /// <exception cref="ArgumentNullException">Thrown when the flags sink is null.</exception>
        public void EvaluateConsequenceBranches(double hazardLevel, RiskComputeFlags flags, out double[] weights, out double[] values)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));

            double signal = ConsequenceInput(hazardLevel);
            int count = _failureBranches.Count;
            weights = new double[count];
            values = new double[count];
            for (int i = 0; i < count; i++)
            {
                weights[i] = _failureBranches[i].Weight;
                double value = _failureBranches[i].Function?.Function(signal) ?? 0d;
                if (value < 0d)
                {
                    if (IsNonFailureMode)
                    {
                        flags.HasNegativeNonFailureConsequence = true;
                    }
                    else
                    {
                        flags.HasNegativeFailureConsequence = true;
                    }
                    value = 0d;
                }
                values[i] = value;
            }
        }

        /// <summary>
        /// Computes this mode's risk at one hazard evaluation point: the response probability,
        /// the branch-enumerated failure and excess consequences, and the recorded risk-point
        /// entries.
        /// </summary>
        /// <param name="probability">The hazard non-exceedance probability at the evaluation point.</param>
        /// <param name="hazardLevel">The hazard level.</param>
        /// <param name="nonFailureMode">
        /// The component's sampled non-failure mode — its transform chain locates the paired
        /// non-failure consequence input; null when the component has none.
        /// </param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="realization">The failure mode's realization sink.</param>
        /// <param name="recordOutput">True to record risk-point entries on the realization curves.</param>
        /// <returns>The mode's risk output at the evaluation point.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the flags or realization sink is null.</exception>
        /// <remarks>
        /// The v1.0 clamping rules are preserved: negative consequences clamp to zero with the
        /// matching warning flag (failure and excess flags raise only when the failure
        /// probability is positive — a negative consequence on an impossible event is not
        /// actionable). Excess is computed per failure/non-failure branch pair,
        /// <c>max(0, cF_i − cNF_j)</c>, so the recorded Excess entries carry the exact pair
        /// distribution; the output's excess LIST entries use the mean non-failure consequence
        /// (the documented 4b interim on <see cref="ComponentRiskOutput"/>), while the scalar
        /// mean excess is pair-exact.
        /// </remarks>
        public ComponentRiskOutput ComputeRisk(double probability, double hazardLevel, SampledFailureMode? nonFailureMode,
            RiskComputeFlags flags, FailureModeRealization realization, bool recordOutput = false)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (realization == null) throw new ArgumentNullException(nameof(realization));

            var output = new ComponentRiskOutput();
            double probabilityOfFailure = SRP(hazardLevel);

            // Paired non-failure consequence branches, evaluated at the NON-FAILURE mode's own
            // consequence input signal (its transform chain), with THIS mode's paired sample.
            int nonFailCount = 1;
            double meanNonFail = 0d;
            double[] nonFailWeights = _singleUnitWeight;
            double[] nonFailValues = _singleZeroValue;
            if (_nonFailureBranches != null && nonFailureMode != null)
            {
                double nonFailSignal = nonFailureMode.ConsequenceInput(hazardLevel);
                nonFailCount = _nonFailureBranches.Count;
                nonFailWeights = new double[nonFailCount];
                nonFailValues = new double[nonFailCount];
                for (int j = 0; j < nonFailCount; j++)
                {
                    nonFailWeights[j] = _nonFailureBranches[j].Weight;
                    double value = _nonFailureBranches[j].Function?.Function(nonFailSignal) ?? 0d;
                    if (value < 0d)
                    {
                        flags.HasNegativeNonFailureConsequence = true;
                        value = 0d;
                    }
                    nonFailValues[j] = value;
                    meanNonFail += nonFailWeights[j] * value;
                }
            }

            // Failure consequence branches at the bound consequence input signal.
            bool record = recordOutput && !IsNonFailureMode;
            var excessProbabilities = record ? new List<double>(_failureBranches.Count * nonFailCount) : null;
            var excessValues = record ? new List<double>(_failureBranches.Count * nonFailCount) : null;
            double consequenceSignal = ConsequenceInput(hazardLevel);
            double meanFailure = 0d;
            double meanExcess = 0d;
            for (int i = 0; i < _failureBranches.Count; i++)
            {
                double weight = _failureBranches[i].Weight;
                double failureValue = _failureBranches[i].Function?.Function(consequenceSignal) ?? 0d;
                if (failureValue < 0d)
                {
                    if (probabilityOfFailure > 0d) flags.HasNegativeFailureConsequence = true;
                    failureValue = 0d;
                }
                meanFailure += weight * failureValue;

                // Excess per failure/non-failure branch pair — the exact pair distribution.
                double pairedExcess = 0d;
                for (int j = 0; j < nonFailCount; j++)
                {
                    double excess = failureValue - nonFailValues[j];
                    if (excess < 0d)
                    {
                        if (probabilityOfFailure > 0d) flags.HasNegativeExcessConsequence = true;
                        excess = 0d;
                    }
                    pairedExcess += nonFailWeights[j] * excess;
                    if (record)
                    {
                        excessProbabilities!.Add(probabilityOfFailure * weight * nonFailWeights[j]);
                        excessValues!.Add(excess);
                    }
                }
                meanExcess += weight * pairedExcess;

                output.ResponseProbabilities.Add(probabilityOfFailure * weight);
                output.FailureConsequences.Add(failureValue);
                output.ExcessConsequences.Add(Math.Max(0d, failureValue - meanNonFail));
            }

            // Record the mode-level risk points: Fail entries per failure branch, Excess entries
            // per branch pair.
            if (record)
            {
                var failProbabilities = new List<double>(_failureBranches.Count);
                var failValues = new List<double>(_failureBranches.Count);
                for (int i = 0; i < _failureBranches.Count; i++)
                {
                    failProbabilities.Add(output.ResponseProbabilities[i]);
                    failValues.Add(output.FailureConsequences[i]);
                }
                realization.Curves.Fail.AddRiskPoint(hazardLevel, probability, failProbabilities, failValues);
                realization.Curves.Excess.AddRiskPoint(hazardLevel, probability, excessProbabilities!, excessValues!);
            }

            output.ProbabilityOfFailure = probabilityOfFailure;
            output.ProbabilityOfNonFailure = 1d - probabilityOfFailure;
            output.NonFailureConsequences = meanNonFail;
            output.MeanFailureConsequences = meanFailure;
            output.MeanExcessConsequences = meanExcess;
            return output;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// The shared single-unit-weight array for the no-non-failure case.
        /// </summary>
        private static readonly double[] _singleUnitWeight = { 1d };

        /// <summary>
        /// The shared single-zero-value array for the no-non-failure case.
        /// </summary>
        private static readonly double[] _singleZeroValue = { 0d };

        #endregion
    }
}
