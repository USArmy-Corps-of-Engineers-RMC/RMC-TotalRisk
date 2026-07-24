using System;
using System.Collections.Generic;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;
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
    /// normalization (with its probability-above-one warning). The optional profile-axis remap
    /// (<c>ProfileHazardFunction</c>) is deferred with open question Q-T — recorded hazard levels
    /// are the raw driving hazard.
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
        /// Samples a system component for one realization.
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
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (projectedModes == null) throw new ArgumentNullException(nameof(projectedModes));
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

            _failureModes = new List<SampledFailureMode>(projectedModes.Count);
            _fModes = new List<SampledFailureMode>(projectedModes.Count);
            int consequenceTypeCount = 1;
            for (int i = 0; i < projectedModes.Count; i++)
            {
                var sampled = new SampledFailureMode(projectedModes[i], projectedModes[i].IsNonFailureMode ? null : nonFailureMode, realizationIndex);
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

            // Weak-link competing failures: pre-process the cumulative incidence functions over
            // 200 stratified hazard levels (v1.0 constants). A single mode short-circuits to its
            // own response probability, so the pre-processing is skipped then.
            if (_failureModeMethod == FailureModeMethod.CompetingFailures && _fModes.Count > 1)
            {
                double minHazard = Hazard.InverseCDF(ProbabilityFloor);
                double maxHazard = Hazard.InverseCDF(1d - ProbabilityFloor);
                HazardBins = Stratify.XValues(new StratificationOptions(minHazard, maxHazard, 200), false);

                var distributions = new EmpiricalDistribution[_fModes.Count];
                var responseValues = new List<double>[_fModes.Count];
                var hazardValues = new List<double>[_fModes.Count];
                for (int j = 0; j < _fModes.Count; j++)
                {
                    hazardValues[j] = new List<double>(HazardBins.Count + 1);
                    responseValues[j] = new List<double>(HazardBins.Count + 1);
                }

                double level = HazardBins[0].LowerBound;
                for (int j = 0; j < _fModes.Count; j++)
                {
                    hazardValues[j].Add(level);
                    responseValues[j].Add(_fModes[j].SRP(level));
                }
                for (int i = 0; i < HazardBins.Count; i++)
                {
                    level = HazardBins[i].UpperBound;
                    for (int j = 0; j < _fModes.Count; j++)
                    {
                        hazardValues[j].Add(level);
                        responseValues[j].Add(_fModes[j].SRP(level));
                    }
                }
                for (int j = 0; j < _fModes.Count; j++)
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

            // The profile-axis remap (ProfileHazardFunction) is deferred with Q-T: recorded
            // hazard levels are the raw driving hazard.
            double recordedHazard = hazardLevel;

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
                    _fModes[j].ComputeRisk(probability, hazardLevel, _nfMode, flags, realization.FailureModes[j], recordOutput, modeTypeOutputs[j]);
                    modeOutputs[j] = modeTypeOutputs[j][0];
                }
                else
                {
                    modeOutputs[j] = _fModes[j].ComputeRisk(probability, hazardLevel, _nfMode, flags, realization.FailureModes[j], recordOutput);
                }
                responseProbabilities.Add(modeOutputs[j].ProbabilityOfFailure);
            }

            // The combination structure is type-independent — compute it once and share it with
            // every consequence kernel: the joint pathway decomposition, or the per-mode
            // adjusted probabilities.
            List<double>? pathwayProbabilities = null;
            List<int[]>? pathwayIndicators = null;
            double[]? adjustedProbabilities = null;
            double totalProbabilityOfFailure = 0d;
            if (_fModes.Count > 0)
            {
                if (_failureModeMethod == FailureModeMethod.JointFailures)
                {
                    ComputePathwayDecomposition(responseProbabilities, out pathwayProbabilities, out pathwayIndicators);
                    for (int j = 0; j < pathwayProbabilities.Count; j++)
                    {
                        totalProbabilityOfFailure += pathwayProbabilities[j];
                    }
                }
                else
                {
                    adjustedProbabilities = _scratchAdjustedProbabilities;
                    double commonCauseFactor = _failureModeMethod == FailureModeMethod.CommonCauseFailures
                        ? CommonCauseFactor(responseProbabilities)
                        : 0d;
                    double normalization = 1d;
                    if (_failureModeMethod == FailureModeMethod.MutuallyExclusive)
                    {
                        normalization = Probability.MutuallyExclusiveAdjustment(responseProbabilities);
                        if (normalization < 1d) flags.HasProbabilityGreaterThanOne = true;
                    }
                    for (int j = 0; j < _fModes.Count; j++)
                    {
                        double adjusted;
                        if (_failureModeMethod == FailureModeMethod.CompetingFailures)
                        {
                            adjusted = _fModes.Count == 1 ? responseProbabilities[j] : _cumulativeIncidenceFunctions![j].CDF(hazardLevel);
                        }
                        else if (_failureModeMethod == FailureModeMethod.CommonCauseFailures)
                        {
                            adjusted = responseProbabilities[j] * commonCauseFactor;
                        }
                        else
                        {
                            adjusted = responseProbabilities[j] * normalization;
                        }
                        adjustedProbabilities[j] = adjusted;
                        totalProbabilityOfFailure += adjusted;
                    }
                }
            }
            totalProbabilityOfFailure = Math.Min(1d, totalProbabilityOfFailure);
            double probabilityOfNonFailure = _nfMode == null ? 0d : Math.Max(0d, 1d - totalProbabilityOfFailure);

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

                if (_fModes.Count > 0)
                {
                    var typeModeOutputs = k == 0 ? modeOutputs : FillTypeColumn(k);
                    if (_failureModeMethod == FailureModeMethod.JointFailures)
                    {
                        ComputeJointPathwayEntries(responseProbabilities, typeModeOutputs, pathwayProbabilities!, pathwayIndicators!,
                            nonFailWeights, nonFailValues,
                            failEntryProbabilities, failEntryValues, excessEntryProbabilities, excessEntryValues,
                            ref expectedFailureConsequences, ref expectedExcessConsequences, ref minN, ref maxN);
                    }
                    else
                    {
                        for (int j = 0; j < _fModes.Count; j++)
                        {
                            AppendModeEntries(typeModeOutputs[j], responseProbabilities[j], adjustedProbabilities![j],
                                failEntryProbabilities, failEntryValues, excessEntryProbabilities, excessEntryValues, ref minN, ref maxN);

                            expectedFailureConsequences += adjustedProbabilities[j] * typeModeOutputs[j].MeanFailureConsequences;
                            expectedExcessConsequences += adjustedProbabilities[j] * typeModeOutputs[j].MeanExcessConsequences;
                        }
                    }
                }

                double effectiveNonFailure = _nfMode == null ? 0d : nonFailureConsequences;
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
                            new List<double>(failEntryProbabilities), new List<double>(failEntryValues));
                        target.Excess.AddRiskPoint(recordedHazard, probability, excessEntryProbabilities, excessEntryValues);
                    }

                    var totalProbabilities = new List<double>(failEntryProbabilities.Count + nonFailWeights.Length);
                    var totalValues = new List<double>(failEntryValues.Count + nonFailValues.Length);
                    totalProbabilities.AddRange(failEntryProbabilities);
                    totalValues.AddRange(failEntryValues);

                    if (_nfMode != null)
                    {
                        var backgroundProbabilities = new List<double>(nonFailWeights.Length);
                        var backgroundValues = new List<double>(nonFailValues.Length);
                        var nonFailProbabilities = new List<double>(nonFailWeights.Length);
                        var nonFailPointValues = new List<double>(nonFailValues.Length);
                        for (int j = 0; j < nonFailWeights.Length; j++)
                        {
                            backgroundProbabilities.Add(nonFailWeights[j]);
                            backgroundValues.Add(nonFailValues[j]);
                            nonFailProbabilities.Add(probabilityOfNonFailure * nonFailWeights[j]);
                            nonFailPointValues.Add(nonFailValues[j]);
                            totalProbabilities.Add(probabilityOfNonFailure * nonFailWeights[j]);
                            totalValues.Add(nonFailValues[j]);
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
        /// The common-cause adjustment factor per the captured dependency (v1.0 mapping). The
        /// perfectly-positive branch passes the captured correlation matrix even though the
        /// positive joint-probability kernel never reads it: the Numerics overload rejects a
        /// null matrix before dispatching on the dependency, so the v1.0 matrix-free call form
        /// faulted the run (Phase 5 correction; the matrix is always materialized by the
        /// component's sampler setup).
        /// </summary>
        /// <param name="responseProbabilities">The per-mode response probabilities.</param>
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
        /// Decomposes the per-mode response probabilities into the exclusive joint-failure
        /// pathway probabilities and indicators per the captured dependency — the
        /// type-independent half of the joint kernel, computed once per evaluation.
        /// </summary>
        /// <param name="responseProbabilities">The per-mode response probabilities.</param>
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
        /// cross product over the failing modes' exposure branches (weights multiply; the
        /// combined consequence follows the joint-consequence rule), crossed with the
        /// component's non-failure branches for the exact excess pairs. The pathway
        /// decomposition is supplied by the caller — it is type-independent and shared.
        /// </summary>
        /// <param name="responseProbabilities">The per-mode response probabilities.</param>
        /// <param name="modeOutputs">The per-mode risk outputs at this consequence type (branch entries).</param>
        /// <param name="pathwayProbabilities">The exclusive pathway probabilities.</param>
        /// <param name="pathwayIndicators">The pathway on/off indicators.</param>
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
        private void ComputeJointPathwayEntries(List<double> responseProbabilities, ComponentRiskOutput[] modeOutputs,
            List<double> pathwayProbabilities, List<int[]> pathwayIndicators,
            double[] nonFailWeights, double[] nonFailValues,
            List<double> failEntryProbabilities, List<double> failEntryValues,
            List<double> excessEntryProbabilities, List<double> excessEntryValues,
            ref double expectedFailureConsequences, ref double expectedExcessConsequences, ref double minN, ref double maxN)
        {
            var participating = _scratchParticipating;
            var branchPick = _scratchBranchPick;
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

                // The odometer over the failing modes' branch sets.
                Array.Clear(branchPick, 0, participating.Count);
                while (true)
                {
                    double tupleWeight = 1d;
                    double combined = 0d;
                    for (int p = 0; p < participating.Count; p++)
                    {
                        var modeOutput = modeOutputs[participating[p]];
                        double raw = responseProbabilities[participating[p]];
                        double weight = raw > 0d
                            ? modeOutput.ResponseProbabilities[branchPick[p]] / raw
                            : (branchPick[p] == 0 ? 1d : 0d);
                        tupleWeight *= weight;
                        double value = modeOutput.FailureConsequences[branchPick[p]];
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

                        // Exact excess pairs against the non-failure branches.
                        for (int q = 0; q < nonFailWeights.Length; q++)
                        {
                            double excess = Math.Max(0d, combined - nonFailValues[q]);
                            double excessProbability = entryProbability * nonFailWeights[q];
                            excessEntryProbabilities.Add(excessProbability);
                            excessEntryValues.Add(excess);
                            expectedExcessConsequences += excessProbability * excess;
                            minN = Math.Min(minN, excess);
                        }
                    }

                    // Advance the odometer.
                    int digit = 0;
                    while (digit < participating.Count)
                    {
                        branchPick[digit]++;
                        if (branchPick[digit] < modeOutputs[participating[digit]].FailureConsequences.Count) break;
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
