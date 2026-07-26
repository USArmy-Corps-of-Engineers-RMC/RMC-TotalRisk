using System;
using System.Collections.Generic;
using Numerics;
using Numerics.Distributions;
using Numerics.Functions;
using RMC.TotalRisk.Core.Enums;
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
    /// Multi-stage acceptance (Phase 6.7, arch doc §7.9): every response stage is captured —
    /// per-stage transform slices, sampled fragilities, and branch polarities — and the system
    /// response probability is the polarity product ∏ᵢ (polarityᵢ = Fail ? pᵢ(h) : 1 − pᵢ(h)),
    /// each stage's fragility evaluated at its own stage-transformed signal. A single-stage
    /// Fail-polarity mode reproduces the pre-6.7 arithmetic bit-identically (the product's single
    /// factor multiplies 1.0 exactly). This replaced the Q-X constructor throw.
    /// </para>
    /// <para>
    /// Q-U closure (Phase 6.5): every consequence position of the mode's ordered
    /// <c>ConsequenceFunctions</c> list is sampled and computed — position k reads the coupling
    /// matrix at column k (the per-type Q-N shared draw pairing the failure and non-failure
    /// consequences of the same type), and type k's results record into the realization's
    /// primary curves (k = 0) or <c>AdditionalCurves[k − 1]</c>. Probability structure (SRP,
    /// pathway decomposition) is computed once and shared by every type; adaptive refinement is
    /// driven by the primary type, with the secondary types riding the same hazard nodes.
    /// Secondary types are evaluated only when recording or when the caller supplies a per-type
    /// output sink, so probe and warm-up evaluations pay the single-type cost.
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
        /// <exception cref="InvalidOperationException">
        /// Thrown when sampling by realization index before the samplers have been set up.
        /// </exception>
        public SampledFailureMode(FailureMode failureMode, FailureMode? nonFailureMode, int realizationIndex = -1)
        {
            if (failureMode == null) throw new ArgumentNullException(nameof(failureMode));

            Name = failureMode.ResponseFunction.Name;
            IsNonFailureMode = failureMode.IsNonFailureMode;
            _consequencePosition = failureMode.ResolvedConsequenceHazardPosition;

            bool mean = realizationIndex < 0;

            // Every stage's transforms and response, sampled from their own content-seeded
            // matrices (multi-stage acceptance — Phase 6.7; the sampler walk has always covered
            // all stages). Transforms flatten into one chain array with per-stage offsets so the
            // polarity-product SRP and the consequence-input fold index without allocation.
            var stages = failureMode.ResponseStages;
            int stageCount = stages.Count;
            int totalTransforms = 0;
            for (int s = 0; s < stageCount; s++)
            {
                totalTransforms += stages[s].Transforms.Count;
            }
            _stageTransforms = new IUnivariateFunction[totalTransforms];
            _stageTransformOffsets = new int[stageCount + 1];
            _stageResponses = new IUnivariateDistribution[stageCount];
            _stagePolarities = new BranchPolarity[stageCount];
            int cursor = 0;
            for (int s = 0; s < stageCount; s++)
            {
                _stageTransformOffsets[s] = cursor;
                var transforms = stages[s].Transforms;
                for (int i = 0; i < transforms.Count; i++)
                {
                    _stageTransforms[cursor++] = mean ? transforms[i].SampleFunction() : transforms[i].SampleFunction(realizationIndex);
                }
                var response = stages[s].Response;
                _stageResponses[s] = mean ? response.SampleFunction() : response.SampleFunction(realizationIndex);
                _stagePolarities[s] = stages[s].BranchPolarity;
            }
            _stageTransformOffsets[stageCount] = cursor;

            var trailing = failureMode.ResponseToConsequence;
            _responseToConsequence = new IUnivariateFunction[trailing.Count];
            for (int i = 0; i < trailing.Count; i++)
            {
                _responseToConsequence[i] = mean ? trailing[i].SampleFunction() : trailing[i].SampleFunction(realizationIndex);
            }

            // Every consequence position, branch-enumerated (Q-V) and coupled per type on one
            // knowledge percentile (per-type Q-N: position k reads coupling column k, so the
            // failure and non-failure consequences of one type share a draw while distinct types
            // draw independently — the coupling matrix has carried K columns since Phase 3). A
            // consequence-free mode (reliability) carries the single zero-consequence branch so
            // failure probability still records.
            var consequences = failureMode.ConsequenceFunctions;
            int typeCount = Math.Max(1, consequences.Count);
            _failureBranchesByType = new IReadOnlyList<(double Weight, IUnivariateFunction Function)>[typeCount];
            for (int k = 0; k < typeCount; k++)
            {
                var consequence = k < consequences.Count ? consequences[k] : null;
                if (consequence == null)
                {
                    _failureBranchesByType[k] = ZeroBranch;
                }
                else if (mean)
                {
                    _failureBranchesByType[k] = consequence.SampleExposureBranches();
                }
                else
                {
                    _failureBranchesByType[k] = consequence.SampleExposureBranches(failureMode.CouplingPercentile(realizationIndex, k));
                }
            }

            var pairedConsequences = nonFailureMode?.ConsequenceFunctions;
            if (pairedConsequences != null && pairedConsequences.Count > 0)
            {
                _nonFailureBranchesByType = new IReadOnlyList<(double Weight, IUnivariateFunction Function)>[typeCount];
                for (int k = 0; k < typeCount; k++)
                {
                    var paired = k < pairedConsequences.Count ? pairedConsequences[k] : null;
                    if (paired == null)
                    {
                        _nonFailureBranchesByType[k] = ZeroBranch;
                    }
                    else
                    {
                        _nonFailureBranchesByType[k] = mean
                            ? paired.SampleExposureBranches()
                            : paired.SampleExposureBranches(failureMode.CouplingPercentile(realizationIndex, k));
                    }
                }
            }

            // Compute-workspace scratch (Phase 6.5): branch counts are fixed for the life of the
            // sampled mode, so every per-evaluation buffer is sized exactly once here and reused
            // for the realization's thousands of integrand evaluations — a sampled mode is
            // realization-owned, never shared across threads.
            _scratchOutputs = new ComponentRiskOutput[typeCount];
            _scratchOwnWeights = new double[typeCount][];
            _scratchOwnValues = new double[typeCount][];
            for (int k = 0; k < typeCount; k++)
            {
                _scratchOutputs[k] = new ComponentRiskOutput();
                int ownCount = _failureBranchesByType[k].Count;
                _scratchOwnWeights[k] = new double[ownCount];
                _scratchOwnValues[k] = new double[ownCount];
            }
            if (_nonFailureBranchesByType != null)
            {
                _scratchNonFailWeights = new double[typeCount][];
                _scratchNonFailValues = new double[typeCount][];
                for (int k = 0; k < typeCount; k++)
                {
                    int pairedCount = _nonFailureBranchesByType[k].Count;
                    _scratchNonFailWeights[k] = new double[pairedCount];
                    _scratchNonFailValues[k] = new double[pairedCount];
                }
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
        /// The sampled stage transform curves, flattened across all stages in chain order —
        /// stage s owns the slice [<see cref="_stageTransformOffsets"/>[s],
        /// <see cref="_stageTransformOffsets"/>[s + 1]).
        /// </summary>
        private readonly IUnivariateFunction[] _stageTransforms;

        /// <summary>
        /// The per-stage offsets into <see cref="_stageTransforms"/> (length = stage count + 1).
        /// </summary>
        private readonly int[] _stageTransformOffsets;

        /// <summary>
        /// The sampled per-stage response distributions (fragilities): stage s's
        /// P[F|transformed hazard] = CDF.
        /// </summary>
        private readonly IUnivariateDistribution[] _stageResponses;

        /// <summary>
        /// The per-stage branch polarities: Fail contributes p(h), Non-Fail contributes
        /// 1 − p(h) to the polarity-product response probability (arch doc §7.9).
        /// </summary>
        private readonly BranchPolarity[] _stagePolarities;

        /// <summary>
        /// The sampled trailing transform curves applied from the bound consequence position.
        /// </summary>
        private readonly IUnivariateFunction[] _responseToConsequence;

        /// <summary>
        /// The weighted exposure branches of each failure consequence type at this realization's
        /// per-type shared knowledge percentiles (entry k is consequence-type position k).
        /// </summary>
        private readonly IReadOnlyList<(double Weight, IUnivariateFunction Function)>[] _failureBranchesByType;

        /// <summary>
        /// The weighted exposure branches of each paired non-failure consequence type, sampled at
        /// THIS mode's per-type coupling percentiles (the per-type Q-N shared draw); null when no
        /// non-failure mode pairs.
        /// </summary>
        private readonly IReadOnlyList<(double Weight, IUnivariateFunction Function)>[]? _nonFailureBranchesByType;

        /// <summary>
        /// The resolved consequence hazard position: 0 binds the raw hazard, k the signal after
        /// the k-th stage transform.
        /// </summary>
        private readonly int _consequencePosition;

        /// <summary>
        /// The reusable per-type output scratch — handed out by <see cref="ComputeRisk"/> and
        /// valid until the next evaluation on this mode (Phase 6.5 allocation elimination).
        /// </summary>
        private readonly ComponentRiskOutput[] _scratchOutputs;

        /// <summary>
        /// The reusable per-type weight buffers behind <see cref="EvaluateConsequenceBranches(double, int, RiskComputeFlags, out double[], out double[])"/>.
        /// </summary>
        private readonly double[][] _scratchOwnWeights;

        /// <summary>
        /// The reusable per-type value buffers behind <see cref="EvaluateConsequenceBranches(double, int, RiskComputeFlags, out double[], out double[])"/>.
        /// </summary>
        private readonly double[][] _scratchOwnValues;

        /// <summary>
        /// The reusable per-type paired non-failure weight buffers; null when no non-failure
        /// mode pairs.
        /// </summary>
        private readonly double[][]? _scratchNonFailWeights;

        /// <summary>
        /// The reusable per-type paired non-failure value buffers; null when no non-failure
        /// mode pairs.
        /// </summary>
        private readonly double[][]? _scratchNonFailValues;

        /// <summary>
        /// The failure mode's display name (the response function's name — v1.0 behavior).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Determines whether this is the component's non-failure mode.
        /// </summary>
        public bool IsNonFailureMode { get; }

        /// <summary>
        /// Suppresses this mode's own Fail/Excess recording inside <see cref="ComputeRisk"/> —
        /// set by the sampled component on claimed non-failure states (arch doc §7.9.2): a
        /// Non-Fail-final end state is not a failure, so its entries are recorded by the
        /// component's complement decomposition (into the NonFail streams) rather than here.
        /// </summary>
        internal bool SuppressModeRecording { get; set; }

        /// <summary>
        /// The number of weighted exposure branches the primary failure consequence carries.
        /// </summary>
        public int FailureBranchCount => _failureBranchesByType[0].Count;

        /// <summary>
        /// The number of consequence types this mode carries (the declared-axis length; one for a
        /// consequence-free reliability mode).
        /// </summary>
        public int ConsequenceTypeCount => _failureBranchesByType.Length;

        /// <summary>
        /// The number of exposure branches this mode's consequence carries at one type position
        /// (the last position's count when the index runs past the mode's axis — a defensive
        /// clamp; validation aligns the counts).
        /// </summary>
        /// <param name="typeIndex">The consequence-type position.</param>
        /// <returns>The branch count.</returns>
        internal int BranchCount(int typeIndex)
        {
            return _failureBranchesByType[Math.Min(typeIndex, _failureBranchesByType.Length - 1)].Count;
        }

        #endregion

        #region Methods

        /// <summary>
        /// The end state's weight (system response probability) at a hazard level: the polarity
        /// product over the stages — each stage's sampled fragility CDF, evaluated at that
        /// stage's transformed signal and clamped to [0, 1], contributes p under a Fail polarity
        /// and 1 − p under Non-Fail (arch doc §7.9). A single-stage Fail mode reproduces the
        /// pre-6.7 single-CDF arithmetic bit-identically.
        /// </summary>
        /// <param name="hazardLevel">The hazard level.</param>
        /// <returns>The response probability.</returns>
        public double SRP(double hazardLevel)
        {
            double signal = hazardLevel;
            double weight = 1d;
            for (int s = 0; s < _stageResponses.Length; s++)
            {
                for (int i = _stageTransformOffsets[s]; i < _stageTransformOffsets[s + 1]; i++)
                {
                    signal = _stageTransforms[i].Function(signal);
                }
                double p = Tools.Clamp(_stageResponses[s].CDF(signal), 0d, 1d);
                weight *= _stagePolarities[s] == BranchPolarity.Fail ? p : 1d - p;
            }
            return weight;
        }

        /// <summary>
        /// The hazard level at which the response probability is reached — the inverse of
        /// <see cref="SRP"/> through the transform chain. Exact for a single-stage Fail-polarity
        /// mode only: a multi-stage polarity product is not monotone in the hazard, so no closed
        /// inverse exists (arch doc §7.9; no engine path consumes this member — numeric
        /// inversion lands if a consumer ever does).
        /// </summary>
        /// <param name="probability">The response probability.</param>
        /// <returns>The hazard level.</returns>
        /// <exception cref="NotSupportedException">
        /// Thrown for a multi-stage or Non-Fail-polarity mode, whose polarity product has no
        /// monotone inverse.
        /// </exception>
        public double InverseSRP(double probability)
        {
            if (_stageResponses.Length != 1 || _stagePolarities[0] != BranchPolarity.Fail)
            {
                throw new NotSupportedException(
                    "InverseSRP is exact only for a single-stage Fail-polarity mode; a cascade's polarity product is not monotone in the hazard.");
            }

            double signal = _stageResponses[0].InverseCDF(probability);
            for (int i = _stageTransformOffsets[1] - 1; i >= 0; i--)
            {
                signal = _stageTransforms[i].InverseFunction(signal);
            }
            return signal;
        }

        /// <summary>
        /// The hazard signal feeding this mode's consequences: the chain signal at the bound
        /// position (0 = the raw hazard, k = after the k-th stage transform, counted across all
        /// stages), folded through the trailing response-to-consequence transforms. Phase 6.7
        /// fixed the pre-cascade fold, which truncated the bound at stage 0's transform count.
        /// </summary>
        /// <param name="hazardLevel">The raw hazard level.</param>
        /// <returns>The consequence input signal.</returns>
        public double ConsequenceInput(double hazardLevel)
        {
            double signal = hazardLevel;
            int bound = Math.Min(_consequencePosition, _stageTransforms.Length);
            for (int i = 0; i < bound; i++)
            {
                signal = _stageTransforms[i].Function(signal);
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
            EvaluateConsequenceBranches(hazardLevel, 0, flags, out weights, out values);
        }

        /// <summary>
        /// Evaluates this mode's own exposure branches at a hazard level for one consequence
        /// type (position k of the declared axis). The consequence input signal is
        /// type-independent — the chain and bound position are shared — so only the branch set
        /// changes with the type.
        /// </summary>
        /// <param name="hazardLevel">The raw hazard level.</param>
        /// <param name="typeIndex">The consequence-type position (0 is the primary).</param>
        /// <param name="flags">The realization's computational-warning flags (negative values clamp with the matching flag).</param>
        /// <param name="weights">Receives the branch weights — a reused per-type buffer, valid until the next evaluation at this type on this mode (never retain it).</param>
        /// <param name="values">Receives the branch consequence values, clamped at zero — the same reuse contract.</param>
        /// <exception cref="ArgumentNullException">Thrown when the flags sink is null.</exception>
        public void EvaluateConsequenceBranches(double hazardLevel, int typeIndex, RiskComputeFlags flags, out double[] weights, out double[] values)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));

            double signal = ConsequenceInput(hazardLevel);
            var branches = _failureBranchesByType[typeIndex];
            int count = branches.Count;
            weights = _scratchOwnWeights[typeIndex];
            values = _scratchOwnValues[typeIndex];
            for (int i = 0; i < count; i++)
            {
                weights[i] = branches[i].Weight;
                double value = branches[i].Function?.Function(signal) ?? 0d;
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
        /// the branch-enumerated failure and excess consequences per consequence type, and the
        /// recorded risk-point entries.
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
        /// <param name="typeOutputs">
        /// The optional per-type output sink, length at least <see cref="ConsequenceTypeCount"/>
        /// (entry k receives type k's output; entry 0 is the returned primary). Secondary types
        /// are computed only when recording or when this sink is supplied — probe evaluations
        /// pay the single-type cost.
        /// </param>
        /// <param name="recordedHazard">
        /// The hazard coordinate recorded risk points carry — the component's profile-axis
        /// signal when a profile hazard element is selected (Q-T closure); NaN (the default)
        /// records the raw <paramref name="hazardLevel"/>. Evaluation always uses the raw level;
        /// this parameter labels the recorded points only.
        /// </param>
        /// <param name="hazardExceedanceProbability">
        /// The driving hazard's annual exceedance probability at the evaluation — the system
        /// response profile's X coordinate (Phase 6.6); NaN (the default) skips that profile.
        /// </param>
        /// <returns>
        /// The mode's primary-type risk output at the evaluation point. The returned output
        /// (and every sink entry) is workspace-backed scratch, valid until the next evaluation
        /// on this mode — consume or copy it before evaluating again (Phase 6.5 allocation
        /// elimination; the engine's call sites consume within the evaluation).
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when the flags or realization sink is null.</exception>
        /// <remarks>
        /// The v1.0 clamping rules are preserved: negative consequences clamp to zero with the
        /// matching warning flag (failure and excess flags raise only when the failure
        /// probability is positive — a negative consequence on an impossible event is not
        /// actionable). Excess is computed per failure/non-failure branch pair within each type,
        /// <c>max(0, cF_i − cNF_j)</c>, so the recorded Excess entries carry the exact pair
        /// distribution; the output's excess LIST entries use the mean non-failure consequence
        /// (the documented 4b interim on <see cref="ComponentRiskOutput"/>), while the scalar
        /// mean excess is pair-exact. Type k's points record into the realization's primary
        /// curves (k = 0) or <c>AdditionalCurves[k − 1]</c>; the analysis validation gate
        /// guarantees the realization carries a slot per declared type.
        /// </remarks>
        public ComponentRiskOutput ComputeRisk(double probability, double hazardLevel, SampledFailureMode? nonFailureMode,
            RiskComputeFlags flags, FailureModeRealization realization, bool recordOutput = false,
            ComponentRiskOutput[]? typeOutputs = null, double recordedHazard = double.NaN,
            double hazardExceedanceProbability = double.NaN)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (realization == null) throw new ArgumentNullException(nameof(realization));

            double probabilityOfFailure = SRP(hazardLevel);
            bool record = recordOutput && !IsNonFailureMode && !SuppressModeRecording;
            double recordedLevel = double.IsNaN(recordedHazard) ? hazardLevel : recordedHazard;

            // Secondary types ride along only when their results are consumed: the recorded
            // curves or the caller's per-type sink. Probes and warm-up evaluations stay
            // single-type.
            int computedTypes = record || typeOutputs != null ? _failureBranchesByType.Length : 1;

            // Type-independent signals: the consequence input chain and the paired non-failure
            // mode's chain are shared by every consequence type.
            double consequenceSignal = ConsequenceInput(hazardLevel);
            bool hasPairedNonFailure = _nonFailureBranchesByType != null && nonFailureMode != null;
            double nonFailSignal = hasPairedNonFailure ? nonFailureMode!.ConsequenceInput(hazardLevel) : 0d;

            ComponentRiskOutput primary = null!;
            for (int k = 0; k < computedTypes; k++)
            {
                var output = ComputeTypeRisk(k, probability, recordedLevel, probabilityOfFailure,
                    consequenceSignal, nonFailSignal, hasPairedNonFailure, flags, realization, record, hazardExceedanceProbability);
                if (k == 0) primary = output;
                if (typeOutputs != null) typeOutputs[k] = output;
            }
            return primary;
        }

        /// <summary>
        /// Computes one consequence type's risk at one hazard evaluation point — the per-type
        /// kernel behind <see cref="ComputeRisk"/>: the paired non-failure branches at the
        /// type's shared draw, the failure branches, the exact excess pairs, and the recorded
        /// entries on the type's curve set.
        /// </summary>
        /// <param name="typeIndex">The consequence-type position (0 is the primary).</param>
        /// <param name="probability">The hazard non-exceedance probability at the evaluation point.</param>
        /// <param name="recordedLevel">The hazard coordinate recorded risk points carry (the profile-axis signal when a profile is selected — Q-T; evaluation signals arrive precomputed).</param>
        /// <param name="probabilityOfFailure">The mode's response probability at the hazard level (type-independent).</param>
        /// <param name="consequenceSignal">This mode's consequence input signal (type-independent).</param>
        /// <param name="nonFailSignal">The paired non-failure mode's consequence input signal.</param>
        /// <param name="hasPairedNonFailure">Whether a paired non-failure consequence exists.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="realization">The failure mode's realization sink.</param>
        /// <param name="record">True to record risk-point entries on the type's curves.</param>
        /// <param name="hazardExceedanceProbability">The driving hazard's exceedance probability (the system response profile's X coordinate; NaN skips it).</param>
        /// <returns>The type's risk output at the evaluation point.</returns>
        private ComponentRiskOutput ComputeTypeRisk(int typeIndex, double probability, double recordedLevel,
            double probabilityOfFailure, double consequenceSignal, double nonFailSignal, bool hasPairedNonFailure,
            RiskComputeFlags flags, FailureModeRealization realization, bool record,
            double hazardExceedanceProbability = double.NaN)
        {
            var output = _scratchOutputs[typeIndex];
            output.Reset();
            var failureBranches = _failureBranchesByType[typeIndex];

            // Paired non-failure consequence branches, evaluated at the NON-FAILURE mode's own
            // consequence input signal (its transform chain), with THIS mode's paired sample at
            // this type's coupling column.
            int nonFailCount = 1;
            double meanNonFail = 0d;
            double[] nonFailWeights = _singleUnitWeight;
            double[] nonFailValues = _singleZeroValue;
            if (hasPairedNonFailure)
            {
                var nonFailureBranches = _nonFailureBranchesByType![typeIndex];
                nonFailCount = nonFailureBranches.Count;
                nonFailWeights = _scratchNonFailWeights![typeIndex];
                nonFailValues = _scratchNonFailValues![typeIndex];
                for (int j = 0; j < nonFailCount; j++)
                {
                    nonFailWeights[j] = nonFailureBranches[j].Weight;
                    double value = nonFailureBranches[j].Function?.Function(nonFailSignal) ?? 0d;
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
            var excessProbabilities = record ? new List<double>(failureBranches.Count * nonFailCount) : null;
            var excessValues = record ? new List<double>(failureBranches.Count * nonFailCount) : null;
            double meanFailure = 0d;
            double meanExcess = 0d;
            for (int i = 0; i < failureBranches.Count; i++)
            {
                double weight = failureBranches[i].Weight;
                double failureValue = failureBranches[i].Function?.Function(consequenceSignal) ?? 0d;
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

            // Record the mode-level risk points on this type's curves: Fail entries per failure
            // branch, Excess entries per branch pair.
            if (record)
            {
                var target = typeIndex == 0 ? realization.Curves : realization.AdditionalCurves[typeIndex - 1];
                var failProbabilities = new List<double>(failureBranches.Count);
                var failValues = new List<double>(failureBranches.Count);
                for (int i = 0; i < failureBranches.Count; i++)
                {
                    failProbabilities.Add(output.ResponseProbabilities[i]);
                    failValues.Add(output.FailureConsequences[i]);
                }
                target.Fail.AddRiskPoint(recordedLevel, probability, failProbabilities, failValues, hazardExceedanceProbability);
                target.Excess.AddRiskPoint(recordedLevel, probability, excessProbabilities!, excessValues!);
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
