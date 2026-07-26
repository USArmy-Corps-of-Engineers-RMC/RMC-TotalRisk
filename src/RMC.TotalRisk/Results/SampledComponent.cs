using System;
using System.Collections.Generic;
using Numerics;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using Numerics.Functions;
using Numerics.Sampling;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// One system component frozen for one Monte Carlo realization: the sampled hazard
    /// distribution, the sampled failure modes, the competing-risks pre-processing, and the
    /// failure-mode combination kernel the risk integrand evaluates.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>SampledComponent</c> onto the v1.1 sampler contract, with the four
    /// failure-mode combination methods preserved verbatim in structure: joint failures through
    /// the inclusion–exclusion pathway probabilities
    /// (<c>Probability.IndependentExclusive</c> / <c>PositivelyDependentExclusive</c> /
    /// <c>ExclusivePCM</c> over the component's cached indicator combinations), weak-link
    /// competing failures through cumulative incidence functions pre-processed over 200
    /// stratified hazard levels, the common-cause adjustment, and the mutually-exclusive
    /// normalization (with its probability-above-one warning). The profile-axis remap (Q-T
    /// closure, Phase 6.6): when the component selects a profile hazard element, the sampled
    /// profile transform chain remaps every recorded hazard level — component and mode scope —
    /// onto the profile axis for this realization; unset, recorded hazard levels are the raw
    /// driving hazard, bit-identical to the pre-6.6 engine.
    /// </para>
    /// <para>
    /// The ratified Q-V generalization: consequences are weighted exposure branches, so recorded
    /// pathway entries enumerate branch combinations. Joint pathways take the cross product over
    /// the failing modes' branch sets (weights multiply; the combined consequence follows the
    /// joint-consequence rule) and cross the component's own non-failure branches for exact
    /// excess pairs. The per-mode methods (competing, common cause, mutually exclusive) record
    /// per-branch failure entries and use each mode's paired excess (exact in the scalar; the
    /// entry lists collapse the paired non-failure spread to its mean — the documented
    /// <see cref="ComponentRiskOutput"/> interim).
    /// </para>
    /// <para>
    /// Q-U closure (Phase 6.5): the probability structure — response probabilities, pathway
    /// decomposition, combination adjustments, and the total failure probability — is computed
    /// once per evaluation and shared by every consequence type; the consequence kernels then
    /// run per type over that type's branch entries (types never cross), recording into the
    /// realization's primary curves (type 0) or <c>AdditionalCurves[k − 1]</c>, with per-type
    /// consequence extents. Secondary types are evaluated only when recording or when the caller
    /// supplies a per-type output sink, so probe and warm-up evaluations pay the single-type
    /// cost.
    /// </para>
    /// </remarks>
    public class SampledComponent
    {
        #region Construction

        /// <summary>
        /// Samples a system component for one realization, deriving the end-state group layout
        /// from the projected modes (the engine path supplies the frozen layout directly).
        /// </summary>
        /// <param name="component">The component to sample (supplies the combination configuration and caches).</param>
        /// <param name="projectedModes">The component's projected failure modes, captured once per run.</param>
        /// <param name="nonFailureMode">The projected non-failure mode, or null when the component has none.</param>
        /// <param name="realizationIndex">The realization index, or −1 for the mean functions.</param>
        /// <exception cref="ArgumentNullException">Thrown when the component or mode list is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the component has no hazard function, or when sampling by realization
        /// index before the samplers have been set up.
        /// </exception>
        public SampledComponent(SystemComponent component, IReadOnlyList<FailureMode> projectedModes,
            FailureMode? nonFailureMode, int realizationIndex = -1)
            : this(component, projectedModes, nonFailureMode, EndStateGroupLayout.Build(projectedModes), realizationIndex)
        {
        }

        /// <summary>
        /// Samples a system component for one realization against a frozen end-state group
        /// layout (arch doc §7.9).
        /// </summary>
        /// <param name="component">The component to sample (supplies the combination configuration and caches).</param>
        /// <param name="projectedModes">The component's projected failure modes, captured once per run.</param>
        /// <param name="nonFailureMode">The projected non-failure mode, or null when the component has none.</param>
        /// <param name="layout">The end-state group layout over the projected modes, frozen with them.</param>
        /// <param name="realizationIndex">The realization index, or −1 for the mean functions.</param>
        /// <exception cref="ArgumentNullException">Thrown when the component, mode list, or layout is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the component has no hazard function, or when sampling by realization
        /// index before the samplers have been set up.
        /// </exception>
        internal SampledComponent(SystemComponent component, IReadOnlyList<FailureMode> projectedModes,
            FailureMode? nonFailureMode, EndStateGroupLayout layout, int realizationIndex = -1)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (projectedModes == null) throw new ArgumentNullException(nameof(projectedModes));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            var hazardFunction = component.HazardFunction
                ?? throw new InvalidOperationException("The system component has no hazard function. Call Validate() and correct the reported errors before sampling.");

            Name = component.Name;
            _failureModeMethod = component.FailureModeMethod;
            _failureModeDependency = component.FailureModeDependency;
            _jointConsequences = component.JointConsequences;
            _correlationMatrix = component.CorrelationMatrix;
            _indicators = component.FailureModeIndicators;
            _binomialCombinations = component.FailureModeBinomialCombinations;

            Hazard = realizationIndex < 0 ? hazardFunction.SampleFunction() : hazardFunction.SampleFunction(realizationIndex);

            // The profile-axis remap (Q-T closure): sample the component's resolved profile
            // transform chain for this realization. The chain functions are the same seeded
            // instances the failure-mode chains draw from, so a shared transform samples the
            // identical curve here and in the modes — dedup coherence for free. Null when the
            // primary hazard is the profile axis (the default), making the remap a single null
            // check per evaluation.
            var profileChain = component.ProfileTransformFunctions;
            if (profileChain != null)
            {
                _profileTransforms = new IUnivariateFunction[profileChain.Length];
                for (int i = 0; i < profileChain.Length; i++)
                {
                    _profileTransforms[i] = realizationIndex < 0 ? profileChain[i].SampleFunction() : profileChain[i].SampleFunction(realizationIndex);
                }
            }

            // Resolve each end state's projected pairing partner (arch doc §7.9.4): the
            // flipped-final sibling terminal when wired, else the background non-failure mode
            // (v1.0 parity — every pre-6.7 layout resolves to the background). State indexes
            // count the non-background modes in projection order — the layout's index space.
            var stateProjected = new List<FailureMode>(projectedModes.Count);
            for (int i = 0; i < projectedModes.Count; i++)
            {
                if (!projectedModes[i].IsNonFailureMode) stateProjected.Add(projectedModes[i]);
            }

            _failureModes = new List<SampledFailureMode>(projectedModes.Count);
            _fModes = new List<SampledFailureMode>(projectedModes.Count);
            int consequenceTypeCount = 1;
            int stateIndex = 0;
            for (int i = 0; i < projectedModes.Count; i++)
            {
                FailureMode? pairing = null;
                bool claimed = false;
                if (!projectedModes[i].IsNonFailureMode)
                {
                    int partner = _layout.PairingPartnerState[stateIndex];
                    pairing = partner >= 0 ? stateProjected[partner] : nonFailureMode;
                    claimed = !_layout.IsFailureState[stateIndex];
                    stateIndex++;
                }
                var sampled = new SampledFailureMode(projectedModes[i], pairing, realizationIndex);
                if (claimed) sampled.SuppressModeRecording = true;
                _failureModes.Add(sampled);
                consequenceTypeCount = Math.Max(consequenceTypeCount, sampled.ConsequenceTypeCount);
                if (sampled.IsNonFailureMode)
                {
                    _nfMode = sampled;
                }
                else
                {
                    _fModes.Add(sampled);
                }
            }
            ConsequenceTypeCount = consequenceTypeCount;

            // The sampled pairing partners, resolved after every mode exists (a sibling partner
            // is itself one of the sampled states).
            _pairedSampled = new SampledFailureMode?[_fModes.Count];
            for (int j = 0; j < _fModes.Count; j++)
            {
                int partner = _layout.PairingPartnerState[j];
                _pairedSampled[j] = partner >= 0 ? _fModes[partner] : _nfMode;
            }

            // Compute-workspace scratch (Phase 6.5): mode and type counts are fixed for the life
            // of the sampled component, so every per-evaluation buffer is sized exactly once
            // here and reused across the realization's thousands of integrand evaluations — a
            // sampled component is realization-owned, never shared across threads. Recording
            // sites still allocate fresh lists where a RiskPoint adopts them.
            _scratchModeOutputs = new ComponentRiskOutput[_fModes.Count];
            _scratchModeTypeOutputs = new ComponentRiskOutput[_fModes.Count][];
            for (int j = 0; j < _fModes.Count; j++)
            {
                _scratchModeTypeOutputs[j] = new ComponentRiskOutput[consequenceTypeCount];
            }
            _scratchTypeColumns = new ComponentRiskOutput[consequenceTypeCount][];
            _scratchTypeOutputs = new ComponentRiskOutput[consequenceTypeCount];
            _scratchExcessProbabilities = new List<double>[consequenceTypeCount];
            _scratchExcessValues = new List<double>[consequenceTypeCount];
            for (int k = 0; k < consequenceTypeCount; k++)
            {
                _scratchTypeColumns[k] = new ComponentRiskOutput[_fModes.Count];
                _scratchTypeOutputs[k] = new ComponentRiskOutput();
                _scratchExcessProbabilities[k] = new List<double>();
                _scratchExcessValues[k] = new List<double>();
            }
            _scratchResponseProbabilities = new List<double>(_fModes.Count);
            _scratchAdjustedProbabilities = new double[_fModes.Count];
            _scratchParticipating = new List<int>(_fModes.Count);
            _scratchBranchPick = new int[_fModes.Count];
            _scratchContributionProbability = new double[_fModes.Count];
            _scratchContributionFailure = new double[_fModes.Count];
            _scratchContributionExcess = new double[_fModes.Count];
            _scratchTupleValues = new double[_fModes.Count];
            _scratchUnitProbabilities = new List<double>(_layout.CombinationUnitCount);
            _scratchClaimedConditional = _layout.ClaimedStateCount > 0 ? new double[_fModes.Count] : null;
            _scratchPickedStates = new int[_fModes.Count];

            // With claimed states the complement pair baseline is the conditional mixture
            // (remainder-scaled background + q-scaled claimed branches — §7.9.5); size the
            // per-type mixture buffers once.
            if (_layout.ClaimedStateCount > 0)
            {
                _scratchPairWeights = new double[consequenceTypeCount][];
                _scratchPairValues = new double[consequenceTypeCount][];
                for (int k = 0; k < consequenceTypeCount; k++)
                {
                    int size = _nfMode?.BranchCount(k) ?? 0;
                    for (int j = 0; j < _fModes.Count; j++)
                    {
                        if (!_layout.IsFailureState[j]) size += _fModes[j].BranchCount(k);
                    }
                    _scratchPairWeights[k] = new double[size];
                    _scratchPairValues[k] = new double[size];
                }
            }

            // Weak-link competing failures: pre-process the cumulative incidence functions over
            // 200 stratified hazard levels (v1.0 constants). A single mode short-circuits to its
            // own response probability, so the pre-processing is skipped then.
            if (_failureModeMethod == FailureModeMethod.CompetingFailures && _layout.CombinationUnitCount > 1)
            {
                double minHazard = Hazard.InverseCDF(ProbabilityFloor);
                double maxHazard = Hazard.InverseCDF(1d - ProbabilityFloor);
                HazardBins = Stratify.XValues(new StratificationOptions(minHazard, maxHazard, 200), false);

                // The competing marginals are the combination units' failure masses — a unit's
                // mass sums its exclusive members' polarity-product weights (validation admits
                // only all-Fail signatures under competing, so every mass is monotone and the
                // ascending curve construction holds — arch doc §7.9.6). A singleton unit's sum
                // reproduces the pre-6.7 per-mode value bit-identically.
                int unitCount = _layout.CombinationUnitCount;
                var distributions = new EmpiricalDistribution[unitCount];
                var responseValues = new List<double>[unitCount];
                var hazardValues = new List<double>[unitCount];
                for (int j = 0; j < unitCount; j++)
                {
                    hazardValues[j] = new List<double>(HazardBins.Count + 1);
                    responseValues[j] = new List<double>(HazardBins.Count + 1);
                }

                double level = HazardBins[0].LowerBound;
                for (int j = 0; j < unitCount; j++)
                {
                    hazardValues[j].Add(level);
                    responseValues[j].Add(UnitMassAt(j, level));
                }
                for (int i = 0; i < HazardBins.Count; i++)
                {
                    level = HazardBins[i].UpperBound;
                    for (int j = 0; j < unitCount; j++)
                    {
                        hazardValues[j].Add(level);
                        responseValues[j].Add(UnitMassAt(j, level));
                    }
                }
                for (int j = 0; j < unitCount; j++)
                {
                    // Non-strict ascending probabilities: a fragility legitimately plateaus at 0
                    // below its onset and at 1 past saturation, and the strict two-list
                    // constructor would reject those ties.
                    var responseCurve = new OrderedPairedData(hazardValues[j], responseValues[j],
                        true, SortOrder.Ascending, false, SortOrder.Ascending);
                    distributions[j] = new EmpiricalDistribution(responseCurve);
                }

                var competingRisks = new CompetingRisks(distributions)
                {
                    Dependency = _failureModeDependency switch
                    {
                        DependencyType.Independent => Probability.DependencyType.Independent,
                        DependencyType.PerfectlyPositive => Probability.DependencyType.PerfectlyPositive,
                        _ => Probability.DependencyType.CorrelationMatrix,
                    },
                };
                if (_correlationMatrix != null)
                {
                    competingRisks.CorrelationMatrix = _correlationMatrix;
                }

                // Interim: the Numerics CIF factory builds its outputs with the strict two-list
                // constructor, but a cumulative incidence function legitimately plateaus wherever
                // a mode contributes no hazard — rebuild each output as a non-strict ascending
                // curve so its CDF is queryable (Numerics follow-up item alongside N7–N9).
                var rawIncidenceFunctions = competingRisks.CumulativeIncidenceFunctions(HazardBins);
                _cumulativeIncidenceFunctions = new List<EmpiricalDistribution>(rawIncidenceFunctions.Count);
                for (int j = 0; j < rawIncidenceFunctions.Count; j++)
                {
                    var incidenceCurve = new OrderedPairedData(rawIncidenceFunctions[j].XValues, rawIncidenceFunctions[j].ProbabilityValues,
                        true, SortOrder.Ascending, false, SortOrder.Ascending);
                    _cumulativeIncidenceFunctions.Add(new EmpiricalDistribution(incidenceCurve));
                }
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// The lower/upper probability clamp shared with the legacy engine.
        /// </summary>
        private const double ProbabilityFloor = 1e-16;

        /// <summary>
        /// All sampled modes, in projected order (the non-failure mode included).
        /// </summary>
        private readonly List<SampledFailureMode> _failureModes;

        /// <summary>
        /// The sampled failure modes only, in projected order.
        /// </summary>
        private readonly List<SampledFailureMode> _fModes;

        /// <summary>
        /// The frozen end-state group layout over the states (arch doc §7.9): combination
        /// units, failure/claimed classification, and pairing partners. Trivial for every
        /// pre-6.7 model, where the kernels reduce to the pre-cascade arithmetic.
        /// </summary>
        private readonly EndStateGroupLayout _layout;

        /// <summary>
        /// Each state's sampled excess pairing partner (§7.9.4): the flipped-final sibling
        /// state, or the background non-failure mode (null when neither exists).
        /// </summary>
        private readonly SampledFailureMode?[] _pairedSampled;

        /// <summary>
        /// The sampled non-failure mode; null when the component has none.
        /// </summary>
        private readonly SampledFailureMode? _nfMode;

        /// <summary>
        /// The captured combination configuration (v1.0 members, captured so the compute loop
        /// never touches the live component).
        /// </summary>
        private readonly FailureModeMethod _failureModeMethod;

        /// <summary>
        /// The captured failure-mode dependency.
        /// </summary>
        private readonly DependencyType _failureModeDependency;

        /// <summary>
        /// The captured joint-consequence combination rule.
        /// </summary>
        private readonly JointConsequenceType _jointConsequences;

        /// <summary>
        /// The captured correlation matrix (dependency-mode content).
        /// </summary>
        private readonly double[,]? _correlationMatrix;

        /// <summary>
        /// The captured failure on/off indicator combinations.
        /// </summary>
        private readonly int[,]? _indicators;

        /// <summary>
        /// The captured binomial subset counts.
        /// </summary>
        private readonly int[]? _binomialCombinations;

        /// <summary>
        /// The pre-processed cumulative incidence functions (competing failures with two or more
        /// modes); null otherwise.
        /// </summary>
        private readonly List<EmpiricalDistribution>? _cumulativeIncidenceFunctions;

        /// <summary>
        /// The sampled profile transform chain mapping the driving hazard onto the selected
        /// profile axis for this realization (Q-T); null when the primary hazard is the axis.
        /// </summary>
        private readonly IUnivariateFunction[]? _profileTransforms;

        /// <summary>
        /// The reusable per-mode primary-output view (Phase 6.5 allocation elimination — every
        /// scratch buffer below is realization-owned and reused per evaluation).
        /// </summary>
        private readonly ComponentRiskOutput[] _scratchModeOutputs;

        /// <summary>
        /// The reusable per-mode, per-type output sinks the modes fill.
        /// </summary>
        private readonly ComponentRiskOutput[][] _scratchModeTypeOutputs;

        /// <summary>
        /// The reusable per-type column views over the per-mode outputs.
        /// </summary>
        private readonly ComponentRiskOutput[][] _scratchTypeColumns;

        /// <summary>
        /// The reusable per-type component outputs — handed out by <see cref="ComputeRisk"/> and
        /// valid until the next evaluation on this component.
        /// </summary>
        private readonly ComponentRiskOutput[] _scratchTypeOutputs;

        /// <summary>
        /// The reusable per-type excess entry-probability accumulators for non-recording
        /// evaluations (recording evaluations allocate fresh lists — a risk point adopts them).
        /// </summary>
        private readonly List<double>[] _scratchExcessProbabilities;

        /// <summary>
        /// The reusable per-type excess entry-value accumulators for non-recording evaluations.
        /// </summary>
        private readonly List<double>[] _scratchExcessValues;

        /// <summary>
        /// The reusable per-mode response-probability list.
        /// </summary>
        private readonly List<double> _scratchResponseProbabilities;

        /// <summary>
        /// The reusable per-mode combination-adjusted probabilities.
        /// </summary>
        private readonly double[] _scratchAdjustedProbabilities;

        /// <summary>
        /// The reusable participating-mode index list of the joint kernel.
        /// </summary>
        private readonly List<int> _scratchParticipating;

        /// <summary>
        /// The reusable branch odometer of the joint kernel.
        /// </summary>
        private readonly int[] _scratchBranchPick;

        /// <summary>
        /// The reusable per-mode attributed-probability sums of one recording evaluation (the
        /// % contribution diagnostic, Phase 6.6).
        /// </summary>
        private readonly double[] _scratchContributionProbability;

        /// <summary>
        /// The reusable per-mode attributed probability × failure-consequence sums.
        /// </summary>
        private readonly double[] _scratchContributionFailure;

        /// <summary>
        /// The reusable per-mode attributed probability × excess-consequence sums.
        /// </summary>
        private readonly double[] _scratchContributionExcess;

        /// <summary>
        /// The reusable per-participant branch consequence values of one joint tuple (the
        /// consequence-proportional split's weights).
        /// </summary>
        private readonly double[] _scratchTupleValues;

        /// <summary>
        /// The reusable combination-unit failure-mass list the method kernels operate over
        /// (§7.9): entry u is the exact sum of unit u's exclusive member weights.
        /// </summary>
        private readonly List<double> _scratchUnitProbabilities;

        /// <summary>
        /// The reusable per-state conditional complement shares of the claimed non-failure
        /// states (§7.9.5, q = w / (1 − P_g)); null when the layout claims nothing.
        /// </summary>
        private readonly double[]? _scratchClaimedConditional;

        /// <summary>
        /// The reusable per-participant picked-state indexes of one joint tuple (the
        /// contribution attribution's landing states).
        /// </summary>
        private readonly int[] _scratchPickedStates;

        /// <summary>
        /// The reusable per-type complement-mixture pair weights (§7.9.5 — remainder-scaled
        /// background plus q-scaled claimed branches); null when the layout claims nothing.
        /// </summary>
        private readonly double[][]? _scratchPairWeights;

        /// <summary>
        /// The reusable per-type complement-mixture pair values, parallel to the weights.
        /// </summary>
        private readonly double[][]? _scratchPairValues;

        /// <summary>
        /// The component's display name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The sampled hazard distribution for this realization.
        /// </summary>
        public IUnivariateDistribution Hazard { get; }

        /// <summary>
        /// The stratified hazard levels behind the competing-risks pre-processing; null when not
        /// pre-processed.
        /// </summary>
        public List<StratificationBin>? HazardBins { get; }

        /// <summary>
        /// The sampled modes, in projected order (the non-failure mode included).
        /// </summary>
        public IReadOnlyList<SampledFailureMode> FailureModes => _failureModes;

        /// <summary>
        /// The number of sampled failure modes (the non-failure mode excluded).
        /// </summary>
        public int FailureModeCount => _fModes.Count;

        /// <summary>
        /// The number of consequence types the component carries (the declared-axis length; the
        /// analysis validation gate guarantees every path agrees).
        /// </summary>
        public int ConsequenceTypeCount { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Computes the component's risk at one hazard evaluation point: the per-mode responses,
        /// the failure-mode combination, the branch-enumerated pathway entries per consequence
        /// type, and the recorded risk points.
        /// </summary>
        /// <param name="probability">The hazard non-exceedance probability at the evaluation point (the recorded probability coordinate; the VEGAS path passes its weight).</param>
        /// <param name="hazardLevel">The hazard level.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="realization">The component's realization sink (with one failure-mode realization per failure mode, in projected order).</param>
        /// <param name="recordOutput">True to record risk-point entries on the realization curves.</param>
        /// <param name="typeOutputs">
        /// The optional per-type output sink, length at least <see cref="ConsequenceTypeCount"/>
        /// (entry k receives type k's output; entry 0 is the returned primary). Secondary types
        /// are computed only when recording or when this sink is supplied.
        /// </param>
        /// <returns>
        /// The component's primary-type risk output at the evaluation point. The returned output
        /// (and every sink entry) is workspace-backed scratch, valid until the next evaluation
        /// on this component — consume or copy it before evaluating again (Phase 6.5 allocation
        /// elimination; the engine's call sites consume within the evaluation).
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when the flags or realization sink is null.</exception>
        public ComponentRiskOutput ComputeRisk(double probability, double hazardLevel, RiskComputeFlags flags,
            ComponentRealization realization, bool recordOutput = false, ComponentRiskOutput[]? typeOutputs = null)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (realization == null) throw new ArgumentNullException(nameof(realization));

            // The profile-axis remap (Q-T closure): recorded hazard levels are the raw driving
            // hazard unless a profile transform chain is selected, in which case every recorded
            // point — component and mode scope alike — carries the composed profile signal for
            // this realization. A single null check when unset keeps the default bit-identical.
            double recordedHazard = hazardLevel;
            var profile = _profileTransforms;
            if (profile != null)
            {
                for (int i = 0; i < profile.Length; i++)
                {
                    recordedHazard = profile[i].Function(recordedHazard);
                }
            }

            // The driving hazard's exceedance probability at this evaluation — the system
            // response profile's X coordinate (Phase 6.6). Computed from the sampled hazard so
            // it is correct on both integration paths (the 1D path's probability argument is
            // the non-exceedance coordinate, but the VEGAS path passes its weight), and only
            // when the coordinate will be recorded.
            double hazardExceedance = recordOutput
                ? Tools.Clamp(1d - Hazard.CDF(hazardLevel), 0d, 1d)
                : double.NaN;

            // Secondary types ride along only when their results are consumed (recording, or the
            // caller's per-type sink); probes and warm-up evaluations stay single-type.
            bool wantSecondary = ConsequenceTypeCount > 1 && (recordOutput || typeOutputs != null);
            int componentTypes = wantSecondary ? ConsequenceTypeCount : 1;

            // Per-mode risk at this hazard level — every computed consequence type in one pass
            // per mode, into the reused per-mode sinks.
            var modeOutputs = _scratchModeOutputs;
            var modeTypeOutputs = _scratchModeTypeOutputs;
            var responseProbabilities = _scratchResponseProbabilities;
            responseProbabilities.Clear();
            for (int j = 0; j < _fModes.Count; j++)
            {
                if (wantSecondary)
                {
                    _fModes[j].ComputeRisk(probability, hazardLevel, _pairedSampled[j], flags, realization.FailureModes[j], recordOutput, modeTypeOutputs[j], recordedHazard, hazardExceedance);
                    modeOutputs[j] = modeTypeOutputs[j][0];
                }
                else
                {
                    modeOutputs[j] = _fModes[j].ComputeRisk(probability, hazardLevel, _pairedSampled[j], flags, realization.FailureModes[j], recordOutput, null, recordedHazard, hazardExceedance);
                }
                responseProbabilities.Add(modeOutputs[j].ProbabilityOfFailure);
            }

            // The combination structure is type-independent — compute it once and share it with
            // every consequence kernel. The methods operate over the combination units (arch
            // doc §7.9): a unit's failure mass is the exact sum of its exclusive members'
            // weights, and the adjusted mass distributes back to the members conditionally. A
            // trivial layout (every pre-6.7 model) reduces every step to the pre-cascade
            // per-mode arithmetic bit-identically (singleton sums add zero; the conditional
            // ratio is exactly one).
            List<double>? pathwayProbabilities = null;
            List<int[]>? pathwayIndicators = null;
            double[]? adjustedProbabilities = null;
            double totalProbabilityOfFailure = 0d;
            int unitCount = _layout.CombinationUnitCount;
            var unitProbabilities = _scratchUnitProbabilities;
            unitProbabilities.Clear();
            if (_fModes.Count > 0 && unitCount > 0)
            {
                for (int u = 0; u < unitCount; u++)
                {
                    var members = _layout.CombinationUnitStates[u];
                    double mass = 0d;
                    for (int m = 0; m < members.Length; m++)
                    {
                        mass += responseProbabilities[members[m]];
                    }
                    unitProbabilities.Add(mass);
                }

                if (_failureModeMethod == FailureModeMethod.JointFailures)
                {
                    ComputePathwayDecomposition(unitProbabilities, out pathwayProbabilities, out pathwayIndicators);
                    for (int j = 0; j < pathwayProbabilities.Count; j++)
                    {
                        totalProbabilityOfFailure += pathwayProbabilities[j];
                    }
                }
                else
                {
                    adjustedProbabilities = _scratchAdjustedProbabilities;
                    double commonCauseFactor = _failureModeMethod == FailureModeMethod.CommonCauseFailures
                        ? CommonCauseFactor(unitProbabilities)
                        : 0d;
                    double normalization = 1d;
                    if (_failureModeMethod == FailureModeMethod.MutuallyExclusive)
                    {
                        normalization = Probability.MutuallyExclusiveAdjustment(unitProbabilities);
                        if (normalization < 1d) flags.HasProbabilityGreaterThanOne = true;
                    }
                    for (int u = 0; u < unitCount; u++)
                    {
                        double adjusted;
                        if (_failureModeMethod == FailureModeMethod.CompetingFailures)
                        {
                            adjusted = unitCount == 1 ? unitProbabilities[0] : _cumulativeIncidenceFunctions![u].CDF(hazardLevel);
                        }
                        else if (_failureModeMethod == FailureModeMethod.CommonCauseFailures)
                        {
                            adjusted = unitProbabilities[u] * commonCauseFactor;
                        }
                        else
                        {
                            adjusted = unitProbabilities[u] * normalization;
                        }
                        totalProbabilityOfFailure += adjusted;

                        // Distribute the adjusted unit mass to its member states conditionally
                        // (w / P_g; exactly one for a singleton member).
                        var members = _layout.CombinationUnitStates[u];
                        double unitMass = unitProbabilities[u];
                        for (int m = 0; m < members.Length; m++)
                        {
                            int state = members[m];
                            adjustedProbabilities[state] = unitMass > 0d
                                ? adjusted * (responseProbabilities[state] / unitMass)
                                : 0d;
                        }
                    }
                }
            }
            totalProbabilityOfFailure = Math.Min(1d, totalProbabilityOfFailure);
            bool hasClaimed = _layout.ClaimedStateCount > 0;
            double probabilityOfNonFailure = _nfMode == null && !hasClaimed ? 0d : Math.Max(0d, 1d - totalProbabilityOfFailure);

            // The claimed non-failure states' conditional complement shares (§7.9.5):
            // q = w / (1 − P_g) against the state's own cascade unit, exact under independent
            // groups and the documented convention otherwise. Type-independent — the weights
            // are the polarity products.
            double claimedShareTotal = 0d;
            if (hasClaimed)
            {
                var claimedConditional = _scratchClaimedConditional!;
                for (int j = 0; j < _fModes.Count; j++)
                {
                    if (_layout.IsFailureState[j])
                    {
                        claimedConditional[j] = 0d;
                        continue;
                    }
                    int owningUnit = _layout.ClaimedStateUnit[j];
                    double divisor = owningUnit >= 0 ? 1d - unitProbabilities[owningUnit] : 1d;
                    double share = divisor > 0d ? responseProbabilities[j] / divisor : 0d;
                    claimedConditional[j] = Tools.Clamp(share, 0d, 1d);
                    claimedShareTotal += claimedConditional[j];
                }
                if (claimedShareTotal > 1d)
                {
                    // Duplicate-claim wiring can over-claim the complement (the Q2 ruling keeps
                    // it legal); the shares renormalize and the mass-balance witness reports
                    // the double count honestly.
                    for (int j = 0; j < _fModes.Count; j++)
                    {
                        claimedConditional[j] /= claimedShareTotal;
                    }
                    claimedShareTotal = 1d;
                }
            }

            // The consequence kernels, per type: the non-failure branches from the non-failure
            // mode's OWN sample at this type (its own coupling draw — v1.0 behavior; the
            // per-mode excess uses each mode's PAIRED sample inside
            // SampledFailureMode.ComputeRisk), then the pathway/branch entries, the recorded
            // points, and the per-type consequence extents.
            ComponentRiskOutput primary = null!;
            for (int k = 0; k < componentTypes; k++)
            {
                var typeOutput = _scratchTypeOutputs[k];
                typeOutput.Reset();
                if (k == 0) primary = typeOutput;

                double[] nonFailWeights = _unitWeight;
                double[] nonFailValues = _zeroValue;
                double nonFailureConsequences = 0d;
                if (_nfMode != null)
                {
                    _nfMode.EvaluateConsequenceBranches(hazardLevel, k, flags, out nonFailWeights, out nonFailValues);
                    for (int j = 0; j < nonFailWeights.Length; j++)
                    {
                        nonFailureConsequences += nonFailWeights[j] * nonFailValues[j];
                    }
                }

                var failEntryValues = typeOutput.FailureConsequences;
                var failEntryProbabilities = typeOutput.ResponseProbabilities;

                // A risk point adopts the excess lists when recording, so those stay freshly
                // allocated; non-recording evaluations reuse the per-type scratch.
                List<double> excessEntryProbabilities;
                List<double> excessEntryValues;
                if (recordOutput)
                {
                    excessEntryProbabilities = new List<double>();
                    excessEntryValues = new List<double>();
                }
                else
                {
                    excessEntryProbabilities = _scratchExcessProbabilities[k];
                    excessEntryValues = _scratchExcessValues[k];
                    excessEntryProbabilities.Clear();
                    excessEntryValues.Clear();
                }

                double expectedFailureConsequences = 0d;
                double expectedExcessConsequences = 0d;
                double minN = k == 0 ? realization.MinN : realization.AdditionalMinN[k - 1];
                double maxN = k == 0 ? realization.MaxN : realization.AdditionalMaxN[k - 1];

                // With claimed non-failure states the complement pair baseline becomes the
                // conditional mixture (§7.9.5): the remainder share of the background branches
                // plus each claimed state's q-scaled branches. The mixture drives the
                // non-failure scalar, the joint excess pairs, and the complement recording —
                // one distribution, three consumers. Without claimed states the baseline stays
                // the raw background branches, bit-identical to the pre-6.7 engine.
                double[] pairWeights = nonFailWeights;
                double[] pairValues = nonFailValues;
                double remainderShare = 1d;
                if (hasClaimed)
                {
                    remainderShare = Math.Max(0d, 1d - claimedShareTotal);
                    pairWeights = _scratchPairWeights![k];
                    pairValues = _scratchPairValues![k];
                    int cursor = 0;
                    if (_nfMode != null)
                    {
                        for (int b = 0; b < nonFailWeights.Length; b++)
                        {
                            pairWeights[cursor] = remainderShare * nonFailWeights[b];
                            pairValues[cursor++] = nonFailValues[b];
                        }
                    }
                    for (int j = 0; j < _fModes.Count; j++)
                    {
                        if (_layout.IsFailureState[j]) continue;
                        double share = _scratchClaimedConditional![j];
                        _fModes[j].EvaluateConsequenceBranches(hazardLevel, k, flags, out double[] claimedWeights, out double[] claimedValues);
                        for (int b = 0; b < claimedWeights.Length; b++)
                        {
                            pairWeights[cursor] = share * claimedWeights[b];
                            pairValues[cursor++] = claimedValues[b];
                            minN = Math.Min(minN, claimedValues[b]);
                            maxN = Math.Max(maxN, claimedValues[b]);
                        }
                    }
                    nonFailureConsequences = 0d;
                    for (int b = 0; b < cursor; b++)
                    {
                        nonFailureConsequences += pairWeights[b] * pairValues[b];
                    }
                }

                bool accumulateContribution = recordOutput && _fModes.Count > 0;
                if (accumulateContribution)
                {
                    Array.Clear(_scratchContributionProbability, 0, _fModes.Count);
                    Array.Clear(_scratchContributionFailure, 0, _fModes.Count);
                    Array.Clear(_scratchContributionExcess, 0, _fModes.Count);
                }

                if (_fModes.Count > 0 && unitCount > 0)
                {
                    var typeModeOutputs = k == 0 ? modeOutputs : FillTypeColumn(k);
                    if (_failureModeMethod == FailureModeMethod.JointFailures)
                    {
                        ComputeJointPathwayEntries(unitProbabilities, typeModeOutputs, pathwayProbabilities!, pathwayIndicators!,
                            pairWeights, pairValues,
                            failEntryProbabilities, failEntryValues, excessEntryProbabilities, excessEntryValues,
                            ref expectedFailureConsequences, ref expectedExcessConsequences, ref minN, ref maxN,
                            accumulateContribution ? _scratchContributionProbability : null,
                            accumulateContribution ? _scratchContributionFailure : null,
                            accumulateContribution ? _scratchContributionExcess : null);
                    }
                    else
                    {
                        for (int j = 0; j < _fModes.Count; j++)
                        {
                            // Claimed non-failure states never contribute failure entries —
                            // they ride the complement decomposition below (§7.9.2).
                            if (!_layout.IsFailureState[j]) continue;

                            AppendModeEntries(typeModeOutputs[j], responseProbabilities[j], adjustedProbabilities![j],
                                failEntryProbabilities, failEntryValues, excessEntryProbabilities, excessEntryValues, ref minN, ref maxN);

                            expectedFailureConsequences += adjustedProbabilities[j] * typeModeOutputs[j].MeanFailureConsequences;
                            expectedExcessConsequences += adjustedProbabilities[j] * typeModeOutputs[j].MeanExcessConsequences;

                            // The per-mode methods ARE the exclusive decomposition (one state
                            // per event — within a state group the conditional distribution
                            // keeps the members exclusive): the state's attributed contribution
                            // is its adjusted probability and the adjusted-scaled means — the
                            // same products the expected-value chains above consume
                            // (% contribution, Phase 6.6).
                            if (accumulateContribution)
                            {
                                _scratchContributionProbability[j] = adjustedProbabilities[j];
                                _scratchContributionFailure[j] = adjustedProbabilities[j] * typeModeOutputs[j].MeanFailureConsequences;
                                _scratchContributionExcess[j] = adjustedProbabilities[j] * typeModeOutputs[j].MeanExcessConsequences;
                            }
                        }
                    }
                }

                if (accumulateContribution)
                {
                    for (int j = 0; j < _fModes.Count; j++)
                    {
                        realization.FailureModes[j].AddContributionSample(k, probability,
                            _scratchContributionProbability[j], _scratchContributionFailure[j], _scratchContributionExcess[j]);
                    }
                }

                double effectiveNonFailure = _nfMode == null && !hasClaimed ? 0d : nonFailureConsequences;
                double meanFailureConsequences = totalProbabilityOfFailure == 0d ? 0d : expectedFailureConsequences / totalProbabilityOfFailure;
                double meanExcessConsequences = totalProbabilityOfFailure == 0d ? 0d : expectedExcessConsequences / totalProbabilityOfFailure;

                // The excess output list against the mean non-failure consequence (the documented
                // ComponentRiskOutput interim; the recorded curves carry the exact pairs).
                for (int e = 0; e < failEntryValues.Count; e++)
                {
                    typeOutput.ExcessConsequences.Add(Math.Max(0d, failEntryValues[e] - effectiveNonFailure));
                }

                // Record the component-level risk points on this type's curves.
                if (recordOutput)
                {
                    var target = k == 0 ? realization.Curves : realization.AdditionalCurves[k - 1];
                    if (_fModes.Count > 0)
                    {
                        target.Fail.AddRiskPoint(recordedHazard, probability,
                            new List<double>(failEntryProbabilities), new List<double>(failEntryValues), hazardExceedance);
                        target.Excess.AddRiskPoint(recordedHazard, probability, excessEntryProbabilities, excessEntryValues);
                    }

                    var totalProbabilities = new List<double>(failEntryProbabilities.Count + nonFailWeights.Length);
                    var totalValues = new List<double>(failEntryValues.Count + nonFailValues.Length);
                    totalProbabilities.AddRange(failEntryProbabilities);
                    totalValues.AddRange(failEntryValues);

                    if (_nfMode != null || hasClaimed)
                    {
                        // The complement recording consumes the pair baseline directly: the raw
                        // background branches without claimed states (bit-identical pre-6.7
                        // entries), the conditional mixture with them (§7.9.5).
                        var backgroundProbabilities = new List<double>(pairWeights.Length);
                        var backgroundValues = new List<double>(pairValues.Length);
                        var nonFailProbabilities = new List<double>(pairWeights.Length);
                        var nonFailPointValues = new List<double>(pairValues.Length);
                        for (int j = 0; j < pairWeights.Length; j++)
                        {
                            backgroundProbabilities.Add(pairWeights[j]);
                            backgroundValues.Add(pairValues[j]);
                            nonFailProbabilities.Add(probabilityOfNonFailure * pairWeights[j]);
                            nonFailPointValues.Add(pairValues[j]);
                            totalProbabilities.Add(probabilityOfNonFailure * pairWeights[j]);
                            totalValues.Add(pairValues[j]);
                        }

                        // The claimed non-failure states' own mode-scope curves record their
                        // conditional complement entries into the NonFail stream (§7.9.2 — a
                        // Non-Fail-final state is not a failure, so Fail/Excess stay empty).
                        if (hasClaimed)
                        {
                            for (int j = 0; j < _fModes.Count; j++)
                            {
                                if (_layout.IsFailureState[j]) continue;
                                double share = _scratchClaimedConditional![j];
                                _fModes[j].EvaluateConsequenceBranches(hazardLevel, k, flags, out double[] claimedWeights, out double[] claimedValues);
                                var claimedProbabilities = new List<double>(claimedWeights.Length);
                                var claimedPointValues = new List<double>(claimedWeights.Length);
                                for (int b = 0; b < claimedWeights.Length; b++)
                                {
                                    claimedProbabilities.Add(probabilityOfNonFailure * share * claimedWeights[b]);
                                    claimedPointValues.Add(claimedValues[b]);
                                }
                                var claimedTarget = k == 0 ? realization.FailureModes[j].Curves : realization.FailureModes[j].AdditionalCurves[k - 1];
                                claimedTarget.NonFail.AddRiskPoint(recordedHazard, probability, claimedProbabilities, claimedPointValues);
                            }
                        }
                        target.Background.AddRiskPoint(recordedHazard, probability, backgroundProbabilities, backgroundValues);
                        target.NonFail.AddRiskPoint(recordedHazard, probability, nonFailProbabilities, nonFailPointValues);
                    }
                    target.Total.AddRiskPoint(recordedHazard, probability, totalProbabilities, totalValues);
                }

                // Per-type consequence extents for the percentile post-processing grids (v1.0
                // behavior on the primary).
                minN = Math.Min(minN, effectiveNonFailure);
                maxN = Math.Max(maxN, Math.Max(meanFailureConsequences, effectiveNonFailure));
                if (k == 0)
                {
                    realization.MinN = minN;
                    realization.MaxN = maxN;
                }
                else
                {
                    realization.AdditionalMinN[k - 1] = minN;
                    realization.AdditionalMaxN[k - 1] = maxN;
                }

                typeOutput.ProbabilityOfFailure = totalProbabilityOfFailure;
                typeOutput.ProbabilityOfNonFailure = probabilityOfNonFailure;
                typeOutput.NonFailureConsequences = effectiveNonFailure;
                typeOutput.MeanFailureConsequences = meanFailureConsequences;
                typeOutput.MeanExcessConsequences = meanExcessConsequences;
                if (typeOutputs != null) typeOutputs[k] = typeOutput;
            }

            // Hazard extents are type-independent.
            realization.MinH = Math.Min(realization.MinH, recordedHazard);
            realization.MaxH = Math.Max(realization.MaxH, recordedHazard);

            return primary;
        }

        /// <summary>
        /// Maps a profile-axis hazard level back onto the raw driving-hazard axis through this
        /// realization's sampled profile transform chain, walked in reverse with
        /// <c>InverseFunction</c> (the sensitivity engine's profile-axis-native interpretation,
        /// Phase 6.6). The identity when no profile is selected. Out-of-range queries clamp per
        /// the sampled functions' own inverse behavior.
        /// </summary>
        /// <param name="profileLevel">The hazard level on the profile axis.</param>
        /// <returns>The raw driving-hazard level.</returns>
        internal double InverseProfileHazard(double profileLevel)
        {
            var profile = _profileTransforms;
            if (profile == null) return profileLevel;
            double level = profileLevel;
            for (int i = profile.Length - 1; i >= 0; i--)
            {
                level = profile[i].InverseFunction(level);
            }
            return level;
        }

        /// <summary>
        /// Fills and returns the reused column view of one consequence type over the per-mode
        /// outputs.
        /// </summary>
        /// <param name="typeIndex">The consequence-type position.</param>
        /// <returns>The per-mode outputs at the given type (the reused column buffer).</returns>
        private ComponentRiskOutput[] FillTypeColumn(int typeIndex)
        {
            var column = _scratchTypeColumns[typeIndex];
            for (int j = 0; j < column.Length; j++)
            {
                column[j] = _scratchModeTypeOutputs[j][typeIndex];
            }
            return column;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// The shared single-unit-weight array for the no-non-failure case.
        /// </summary>
        private static readonly double[] _unitWeight = { 1d };

        /// <summary>
        /// The shared single-zero-value array for the no-non-failure case.
        /// </summary>
        private static readonly double[] _zeroValue = { 0d };

        /// <summary>
        /// A combination unit's failure mass at a hazard level: the exact sum of its exclusive
        /// member states' polarity-product weights (disjoint leaves; a singleton unit
        /// reproduces its state's weight bit-identically).
        /// </summary>
        /// <param name="unit">The combination unit.</param>
        /// <param name="hazardLevel">The hazard level.</param>
        /// <returns>The unit's failure mass.</returns>
        private double UnitMassAt(int unit, double hazardLevel)
        {
            var members = _layout.CombinationUnitStates[unit];
            double mass = 0d;
            for (int m = 0; m < members.Length; m++)
            {
                mass += _fModes[members[m]].SRP(hazardLevel);
            }
            return mass;
        }

        /// <summary>
        /// The common-cause adjustment factor per the captured dependency (v1.0 mapping). The
        /// perfectly-positive branch passes the captured correlation matrix even though the
        /// positive joint-probability kernel never reads it: the Numerics overload rejects a
        /// null matrix before dispatching on the dependency, so the v1.0 matrix-free call form
        /// faulted the run (Phase 5 correction; the matrix is always materialized by the
        /// component's sampler setup).
        /// </summary>
        /// <param name="responseProbabilities">The combination units' failure masses.</param>
        /// <returns>The scaling factor in [0, 1].</returns>
        private double CommonCauseFactor(List<double> responseProbabilities)
        {
            if (_failureModeDependency == DependencyType.Independent)
            {
                return Probability.CommonCauseAdjustment(responseProbabilities);
            }
            if (_failureModeDependency == DependencyType.PerfectlyPositive)
            {
                return Probability.CommonCauseAdjustment(responseProbabilities, _correlationMatrix, Probability.DependencyType.PerfectlyPositive);
            }
            return Probability.CommonCauseAdjustment(responseProbabilities, _correlationMatrix, Probability.DependencyType.CorrelationMatrix);
        }

        /// <summary>
        /// Appends one mode's per-branch entries at an adjusted mode probability (the competing,
        /// common-cause, and mutually-exclusive paths): failure entries per branch, excess
        /// entries from the mode's paired pair distribution scaled to the adjusted probability,
        /// and the extent tracking.
        /// </summary>
        /// <param name="modeOutput">The mode's risk output at this hazard level (one consequence type).</param>
        /// <param name="rawProbability">The mode's unadjusted response probability.</param>
        /// <param name="adjustedProbability">The mode's combination-adjusted probability.</param>
        /// <param name="failEntryProbabilities">The accumulating failure entry probabilities.</param>
        /// <param name="failEntryValues">The accumulating failure entry values.</param>
        /// <param name="excessEntryProbabilities">The accumulating excess entry probabilities.</param>
        /// <param name="excessEntryValues">The accumulating excess entry values.</param>
        /// <param name="minN">The type's running minimum consequence extent.</param>
        /// <param name="maxN">The type's running maximum consequence extent.</param>
        private static void AppendModeEntries(ComponentRiskOutput modeOutput, double rawProbability, double adjustedProbability,
            List<double> failEntryProbabilities, List<double> failEntryValues,
            List<double> excessEntryProbabilities, List<double> excessEntryValues, ref double minN, ref double maxN)
        {
            // Branch entry probabilities are the mode's (rawProbability·weight) entries rescaled
            // to the adjusted probability. A zero raw probability contributes nothing.
            double scale = rawProbability > 0d ? adjustedProbability / rawProbability : 0d;
            for (int i = 0; i < modeOutput.ResponseProbabilities.Count; i++)
            {
                double entryProbability = modeOutput.ResponseProbabilities[i] * scale;
                double entryValue = modeOutput.FailureConsequences[i];
                double entryExcess = modeOutput.ExcessConsequences[i];
                failEntryProbabilities.Add(entryProbability);
                failEntryValues.Add(entryValue);
                excessEntryProbabilities.Add(entryProbability);
                excessEntryValues.Add(entryExcess);
                minN = Math.Min(minN, entryExcess);
                maxN = Math.Max(maxN, entryValue);
            }
        }

        /// <summary>
        /// Decomposes the combination units' failure masses into the exclusive joint-failure
        /// pathway probabilities and indicators per the captured dependency — the
        /// type-independent half of the joint kernel, computed once per evaluation. The
        /// dependency (including the Gaussian copula) couples the units' binary failure events;
        /// within a unit the exclusive states distribute conditionally (arch doc §7.9.6).
        /// </summary>
        /// <param name="responseProbabilities">The combination units' failure masses.</param>
        /// <param name="pathwayProbabilities">Receives the exclusive pathway probabilities.</param>
        /// <param name="pathwayIndicators">Receives the pathway on/off indicators.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the combination caches or the dependent-mode correlation matrix are
        /// missing — an engine wiring defect, not a data condition.
        /// </exception>
        private void ComputePathwayDecomposition(List<double> responseProbabilities,
            out List<double> pathwayProbabilities, out List<int[]> pathwayIndicators)
        {
            int[]? binomialCombinations = _binomialCombinations;
            int[,]? indicatorCombinations = _indicators;
            if (binomialCombinations == null || indicatorCombinations == null)
            {
                throw new InvalidOperationException("The failure-mode combination caches are missing. The component was not sampled through SetupSamplers().");
            }

            switch (_failureModeDependency)
            {
                case DependencyType.Independent:
                    Probability.IndependentExclusive(responseProbabilities, binomialCombinations, indicatorCombinations,
                        out pathwayProbabilities, out pathwayIndicators);
                    break;
                case DependencyType.PerfectlyPositive:
                    Probability.PositivelyDependentExclusive(responseProbabilities, binomialCombinations, indicatorCombinations,
                        out pathwayProbabilities, out pathwayIndicators);
                    break;
                default:
                    if (_correlationMatrix == null)
                    {
                        throw new InvalidOperationException("The failure-mode correlation matrix is missing for the dependent joint combination. Call Validate() and correct the reported errors before sampling.");
                    }
                    Probability.ExclusivePCM(responseProbabilities, binomialCombinations, indicatorCombinations, _correlationMatrix,
                        out pathwayProbabilities, out pathwayIndicators);
                    break;
            }
        }

        /// <summary>
        /// The joint-failures consequence kernel for one consequence type: per pathway, the
        /// cross product over the participating combination units' entries — a unit's entry set
        /// concatenates its exclusive member states' exposure branches, each at conditional
        /// weight (state branch mass / unit mass), so within a unit the exclusive states stay
        /// disjoint while across units the weights multiply; the combined consequence follows
        /// the joint-consequence rule, crossed with the component's non-failure branches for
        /// the exact excess pairs. A singleton unit reproduces the pre-6.7 per-mode arithmetic
        /// bit-identically. The pathway decomposition is supplied by the caller — it is
        /// type-independent and shared.
        /// </summary>
        /// <param name="unitProbabilities">The combination units' failure masses.</param>
        /// <param name="modeOutputs">The per-state risk outputs at this consequence type (branch entries).</param>
        /// <param name="pathwayProbabilities">The exclusive pathway probabilities.</param>
        /// <param name="pathwayIndicators">The pathway on/off indicators (unit space).</param>
        /// <param name="nonFailWeights">The type's non-failure branch weights.</param>
        /// <param name="nonFailValues">The type's non-failure branch values.</param>
        /// <param name="failEntryProbabilities">The accumulating failure entry probabilities.</param>
        /// <param name="failEntryValues">The accumulating failure entry values.</param>
        /// <param name="excessEntryProbabilities">The accumulating excess entry probabilities.</param>
        /// <param name="excessEntryValues">The accumulating excess entry values.</param>
        /// <param name="expectedFailureConsequences">Accumulates Σ entry probability × consequence.</param>
        /// <param name="expectedExcessConsequences">Accumulates Σ excess entry probability × excess.</param>
        /// <param name="minN">The type's running minimum consequence extent.</param>
        /// <param name="maxN">The type's running maximum consequence extent.</param>
        /// <param name="contributionProbability">The optional per-state attributed-probability sink (% contribution, Phase 6.6); null skips attribution.</param>
        /// <param name="contributionFailure">The optional per-state attributed failure-value sink, parallel to the probability sink.</param>
        /// <param name="contributionExcess">The optional per-state attributed excess-value sink, parallel to the probability sink.</param>
        /// <remarks>
        /// The attribution (user-ratified 2026-07-24, generalized to units at Phase 6.7):
        /// within each exclusive pathway tuple the entry probability splits equally among the
        /// participating units (the Shapley value of the union game) and lands on each unit's
        /// picked state — summed over tuples, a unit's share distributes across its members by
        /// their conditional mass, so the Σ-identities hold at every scope. The tuple's
        /// combined failure and excess values split proportionally to the picked states' branch
        /// failure consequences (equal split when they sum to zero). The attribution runs in
        /// separate accumulation chains — the expected-value chains and recorded entries above
        /// are bit-untouched.
        /// </remarks>
        private void ComputeJointPathwayEntries(List<double> unitProbabilities, ComponentRiskOutput[] modeOutputs,
            List<double> pathwayProbabilities, List<int[]> pathwayIndicators,
            double[] nonFailWeights, double[] nonFailValues,
            List<double> failEntryProbabilities, List<double> failEntryValues,
            List<double> excessEntryProbabilities, List<double> excessEntryValues,
            ref double expectedFailureConsequences, ref double expectedExcessConsequences, ref double minN, ref double maxN,
            double[]? contributionProbability = null, double[]? contributionFailure = null, double[]? contributionExcess = null)
        {
            var participating = _scratchParticipating;
            var branchPick = _scratchBranchPick;
            var tupleValues = _scratchTupleValues;
            var pickedStates = _scratchPickedStates;
            for (int j = 0; j < pathwayProbabilities.Count; j++)
            {
                double pathwayProbability = pathwayProbabilities[j];
                if (pathwayProbability <= 0d) continue;

                participating.Clear();
                var indicators = pathwayIndicators[j];
                for (int m = 0; m < indicators.Length; m++)
                {
                    if (indicators[m] == 1) participating.Add(m);
                }
                if (participating.Count == 0) continue;

                // The odometer over the participating units' concatenated (state, branch)
                // entry sets.
                Array.Clear(branchPick, 0, participating.Count);
                while (true)
                {
                    double tupleWeight = 1d;
                    double combined = 0d;
                    double tupleValueSum = 0d;
                    for (int p = 0; p < participating.Count; p++)
                    {
                        int unit = participating[p];
                        var members = _layout.CombinationUnitStates[unit];
                        int pick = branchPick[p];
                        int state = members[0];
                        for (int m = 0; m < members.Length; m++)
                        {
                            state = members[m];
                            int count = modeOutputs[state].ResponseProbabilities.Count;
                            if (pick < count) break;
                            pick -= count;
                        }
                        pickedStates[p] = state;
                        var modeOutput = modeOutputs[state];
                        double raw = unitProbabilities[unit];
                        double weight = raw > 0d
                            ? modeOutput.ResponseProbabilities[pick] / raw
                            : (branchPick[p] == 0 ? 1d : 0d);
                        tupleWeight *= weight;
                        double value = modeOutput.FailureConsequences[pick];
                        if (contributionProbability != null)
                        {
                            tupleValues[p] = value;
                            tupleValueSum += value;
                        }
                        combined = p == 0
                            ? value
                            : _jointConsequences switch
                            {
                                JointConsequenceType.Additive => combined + value,
                                JointConsequenceType.Average => combined + value,
                                JointConsequenceType.Maximum => Math.Max(combined, value),
                                // Minimum — and the defensive arm for an undefined member.
                                _ => Math.Min(combined, value),
                            };
                    }
                    if (_jointConsequences == JointConsequenceType.Average)
                    {
                        combined /= participating.Count;
                    }

                    if (tupleWeight > 0d)
                    {
                        double entryProbability = pathwayProbability * tupleWeight;
                        failEntryProbabilities.Add(entryProbability);
                        failEntryValues.Add(combined);
                        expectedFailureConsequences += entryProbability * combined;
                        maxN = Math.Max(maxN, combined);

                        // Exact excess pairs against the non-failure branches. The attribution's
                        // tuple-excess total accumulates in its own chain so the expected-value
                        // chain stays bit-identical.
                        double tupleExcess = 0d;
                        for (int q = 0; q < nonFailWeights.Length; q++)
                        {
                            double excess = Math.Max(0d, combined - nonFailValues[q]);
                            double excessProbability = entryProbability * nonFailWeights[q];
                            excessEntryProbabilities.Add(excessProbability);
                            excessEntryValues.Add(excess);
                            expectedExcessConsequences += excessProbability * excess;
                            minN = Math.Min(minN, excess);
                            if (contributionProbability != null)
                            {
                                tupleExcess += excessProbability * excess;
                            }
                        }

                        if (contributionProbability != null)
                        {
                            double equalShare = 1d / participating.Count;
                            double tupleFailure = entryProbability * combined;
                            for (int p = 0; p < participating.Count; p++)
                            {
                                int state = pickedStates[p];
                                double share = tupleValueSum > 0d ? tupleValues[p] / tupleValueSum : equalShare;
                                contributionProbability[state] += entryProbability * equalShare;
                                contributionFailure![state] += tupleFailure * share;
                                contributionExcess![state] += tupleExcess * share;
                            }
                        }
                    }

                    // Advance the odometer over each unit's total entry count.
                    int digit = 0;
                    while (digit < participating.Count)
                    {
                        branchPick[digit]++;
                        int unit = participating[digit];
                        var members = _layout.CombinationUnitStates[unit];
                        int totalEntries = 0;
                        for (int m = 0; m < members.Length; m++)
                        {
                            totalEntries += modeOutputs[members[m]].FailureConsequences.Count;
                        }
                        if (branchPick[digit] < totalEntries) break;
                        branchPick[digit] = 0;
                        digit++;
                    }
                    if (digit == participating.Count) break;
                }
            }
        }

        #endregion
    }
}
