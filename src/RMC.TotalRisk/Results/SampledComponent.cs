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
            for (int i = 0; i < projectedModes.Count; i++)
            {
                var sampled = new SampledFailureMode(projectedModes[i], projectedModes[i].IsNonFailureMode ? null : nonFailureMode, realizationIndex);
                _failureModes.Add(sampled);
                if (sampled.IsNonFailureMode)
                {
                    _nfMode = sampled;
                }
                else
                {
                    _fModes.Add(sampled);
                }
            }

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

        #endregion

        #region Methods

        /// <summary>
        /// Computes the component's risk at one hazard evaluation point: the per-mode responses,
        /// the failure-mode combination, the branch-enumerated pathway entries, and the recorded
        /// risk points.
        /// </summary>
        /// <param name="probability">The hazard non-exceedance probability at the evaluation point (the recorded probability coordinate; the VEGAS path passes its weight).</param>
        /// <param name="hazardLevel">The hazard level.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="realization">The component's realization sink (with one failure-mode realization per failure mode, in projected order).</param>
        /// <param name="recordOutput">True to record risk-point entries on the realization curves.</param>
        /// <returns>The component's risk output at the evaluation point.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the flags or realization sink is null.</exception>
        public ComponentRiskOutput ComputeRisk(double probability, double hazardLevel, RiskComputeFlags flags,
            ComponentRealization realization, bool recordOutput = false)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (realization == null) throw new ArgumentNullException(nameof(realization));

            var output = new ComponentRiskOutput();

            // The profile-axis remap (ProfileHazardFunction) is deferred with Q-T: recorded
            // hazard levels are the raw driving hazard.
            double recordedHazard = hazardLevel;

            // The non-failure consequence branches from the non-failure mode's OWN sample (its
            // own coupling draw — v1.0 behavior; the per-mode excess uses each mode's PAIRED
            // sample inside SampledFailureMode.ComputeRisk).
            double[] nonFailWeights = _unitWeight;
            double[] nonFailValues = _zeroValue;
            double nonFailureConsequences = 0d;
            if (_nfMode != null)
            {
                _nfMode.EvaluateConsequenceBranches(hazardLevel, flags, out nonFailWeights, out nonFailValues);
                for (int j = 0; j < nonFailWeights.Length; j++)
                {
                    nonFailureConsequences += nonFailWeights[j] * nonFailValues[j];
                }
            }

            // Per-mode risk at this hazard level.
            var modeOutputs = new ComponentRiskOutput[_fModes.Count];
            var responseProbabilities = new List<double>(_fModes.Count);
            for (int j = 0; j < _fModes.Count; j++)
            {
                modeOutputs[j] = _fModes[j].ComputeRisk(probability, hazardLevel, _nfMode, flags, realization.FailureModes[j], recordOutput);
                responseProbabilities.Add(modeOutputs[j].ProbabilityOfFailure);
            }

            // The accumulated pathway entries (these lists double as the output lists and, when
            // recording, the Fail/Total risk-point entries).
            var failEntryValues = output.FailureConsequences;
            var failEntryProbabilities = output.ResponseProbabilities;
            var excessEntryProbabilities = new List<double>();
            var excessEntryValues = new List<double>();

            double totalProbabilityOfFailure = 0d;
            double expectedFailureConsequences = 0d;
            double expectedExcessConsequences = 0d;

            if (_fModes.Count > 0)
            {
                if (_failureModeMethod == FailureModeMethod.JointFailures)
                {
                    ComputeJointPathways(responseProbabilities, modeOutputs, nonFailWeights, nonFailValues,
                        failEntryProbabilities, failEntryValues, excessEntryProbabilities, excessEntryValues,
                        realization, ref totalProbabilityOfFailure, ref expectedFailureConsequences, ref expectedExcessConsequences);
                }
                else
                {
                    // The per-mode methods: an adjusted probability per mode, entries per branch.
                    for (int j = 0; j < _fModes.Count; j++)
                    {
                        double adjusted;
                        if (_failureModeMethod == FailureModeMethod.CompetingFailures)
                        {
                            adjusted = _fModes.Count == 1 ? responseProbabilities[j] : _cumulativeIncidenceFunctions![j].CDF(hazardLevel);
                        }
                        else if (_failureModeMethod == FailureModeMethod.CommonCauseFailures)
                        {
                            adjusted = responseProbabilities[j] * CommonCauseFactor(responseProbabilities);
                        }
                        else
                        {
                            double normalization = Probability.MutuallyExclusiveAdjustment(responseProbabilities);
                            if (normalization < 1d) flags.HasProbabilityGreaterThanOne = true;
                            adjusted = responseProbabilities[j] * normalization;
                        }

                        AppendModeEntries(modeOutputs[j], responseProbabilities[j], adjusted,
                            failEntryProbabilities, failEntryValues, excessEntryProbabilities, excessEntryValues, realization);

                        expectedFailureConsequences += adjusted * modeOutputs[j].MeanFailureConsequences;
                        expectedExcessConsequences += adjusted * modeOutputs[j].MeanExcessConsequences;
                        totalProbabilityOfFailure += adjusted;
                    }
                }
            }

            totalProbabilityOfFailure = Math.Min(1d, totalProbabilityOfFailure);
            double probabilityOfNonFailure = _nfMode == null ? 0d : Math.Max(0d, 1d - totalProbabilityOfFailure);
            nonFailureConsequences = _nfMode == null ? 0d : nonFailureConsequences;
            double meanFailureConsequences = totalProbabilityOfFailure == 0d ? 0d : expectedFailureConsequences / totalProbabilityOfFailure;
            double meanExcessConsequences = totalProbabilityOfFailure == 0d ? 0d : expectedExcessConsequences / totalProbabilityOfFailure;

            // The excess output list against the mean non-failure consequence (the documented
            // ComponentRiskOutput interim; the recorded curves carry the exact pairs).
            for (int k = 0; k < failEntryValues.Count; k++)
            {
                output.ExcessConsequences.Add(Math.Max(0d, failEntryValues[k] - nonFailureConsequences));
            }

            // Record the component-level risk points.
            if (recordOutput)
            {
                if (_fModes.Count > 0)
                {
                    realization.Curves.Fail.AddRiskPoint(recordedHazard, probability,
                        new List<double>(failEntryProbabilities), new List<double>(failEntryValues));
                    realization.Curves.Excess.AddRiskPoint(recordedHazard, probability, excessEntryProbabilities, excessEntryValues);
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
                    realization.Curves.Background.AddRiskPoint(recordedHazard, probability, backgroundProbabilities, backgroundValues);
                    realization.Curves.NonFail.AddRiskPoint(recordedHazard, probability, nonFailProbabilities, nonFailPointValues);
                }
                realization.Curves.Total.AddRiskPoint(recordedHazard, probability, totalProbabilities, totalValues);
            }

            // Extent tracking for the percentile post-processing grids (v1.0 behavior).
            realization.MinN = Math.Min(realization.MinN, nonFailureConsequences);
            realization.MaxN = Math.Max(realization.MaxN, Math.Max(meanFailureConsequences, nonFailureConsequences));
            realization.MinH = Math.Min(realization.MinH, recordedHazard);
            realization.MaxH = Math.Max(realization.MaxH, recordedHazard);

            output.ProbabilityOfFailure = totalProbabilityOfFailure;
            output.ProbabilityOfNonFailure = probabilityOfNonFailure;
            output.NonFailureConsequences = nonFailureConsequences;
            output.MeanFailureConsequences = meanFailureConsequences;
            output.MeanExcessConsequences = meanExcessConsequences;
            return output;
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
        /// The common-cause adjustment factor per the captured dependency (v1.0 mapping).
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
                return Probability.CommonCauseAdjustment(responseProbabilities, dependency: Probability.DependencyType.PerfectlyPositive);
            }
            return Probability.CommonCauseAdjustment(responseProbabilities, _correlationMatrix, Probability.DependencyType.CorrelationMatrix);
        }

        /// <summary>
        /// Appends one mode's per-branch entries at an adjusted mode probability (the competing,
        /// common-cause, and mutually-exclusive paths): failure entries per branch, excess
        /// entries from the mode's paired pair distribution scaled to the adjusted probability,
        /// and the extent tracking.
        /// </summary>
        /// <param name="modeOutput">The mode's risk output at this hazard level.</param>
        /// <param name="rawProbability">The mode's unadjusted response probability.</param>
        /// <param name="adjustedProbability">The mode's combination-adjusted probability.</param>
        /// <param name="failEntryProbabilities">The accumulating failure entry probabilities.</param>
        /// <param name="failEntryValues">The accumulating failure entry values.</param>
        /// <param name="excessEntryProbabilities">The accumulating excess entry probabilities.</param>
        /// <param name="excessEntryValues">The accumulating excess entry values.</param>
        /// <param name="realization">The component realization (extent tracking).</param>
        private static void AppendModeEntries(ComponentRiskOutput modeOutput, double rawProbability, double adjustedProbability,
            List<double> failEntryProbabilities, List<double> failEntryValues,
            List<double> excessEntryProbabilities, List<double> excessEntryValues, ComponentRealization realization)
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
                realization.MinN = Math.Min(realization.MinN, entryExcess);
                realization.MaxN = Math.Max(realization.MaxN, entryValue);
            }
        }

        /// <summary>
        /// The joint-failures pathway kernel: inclusion–exclusion pathway probabilities per the
        /// captured dependency, then per pathway the cross product over the failing modes'
        /// exposure branches (weights multiply; the combined consequence follows the
        /// joint-consequence rule), crossed with the component's non-failure branches for the
        /// exact excess pairs.
        /// </summary>
        /// <param name="responseProbabilities">The per-mode response probabilities.</param>
        /// <param name="modeOutputs">The per-mode risk outputs (branch entries).</param>
        /// <param name="nonFailWeights">The non-failure branch weights.</param>
        /// <param name="nonFailValues">The non-failure branch values.</param>
        /// <param name="failEntryProbabilities">The accumulating failure entry probabilities.</param>
        /// <param name="failEntryValues">The accumulating failure entry values.</param>
        /// <param name="excessEntryProbabilities">The accumulating excess entry probabilities.</param>
        /// <param name="excessEntryValues">The accumulating excess entry values.</param>
        /// <param name="realization">The component realization (extent tracking).</param>
        /// <param name="totalProbabilityOfFailure">Accumulates the pathway union probability (once per pathway).</param>
        /// <param name="expectedFailureConsequences">Accumulates Σ entry probability × consequence.</param>
        /// <param name="expectedExcessConsequences">Accumulates Σ excess entry probability × excess.</param>
        private void ComputeJointPathways(List<double> responseProbabilities, ComponentRiskOutput[] modeOutputs,
            double[] nonFailWeights, double[] nonFailValues,
            List<double> failEntryProbabilities, List<double> failEntryValues,
            List<double> excessEntryProbabilities, List<double> excessEntryValues,
            ComponentRealization realization,
            ref double totalProbabilityOfFailure, ref double expectedFailureConsequences, ref double expectedExcessConsequences)
        {
            // The combination caches exist whenever the component projects failure paths; a miss
            // here is an engine wiring defect, not a data condition.
            int[]? binomialCombinations = _binomialCombinations;
            int[,]? indicatorCombinations = _indicators;
            if (binomialCombinations == null || indicatorCombinations == null)
            {
                throw new InvalidOperationException("The failure-mode combination caches are missing. The component was not sampled through SetupSamplers().");
            }

            List<double> pathwayProbabilities;
            List<int[]> pathwayIndicators;
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

            var participating = new List<int>(_fModes.Count);
            var branchPick = new int[_fModes.Count];
            for (int j = 0; j < pathwayProbabilities.Count; j++)
            {
                double pathwayProbability = pathwayProbabilities[j];
                totalProbabilityOfFailure += pathwayProbability;
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
                        realization.MaxN = Math.Max(realization.MaxN, combined);

                        // Exact excess pairs against the non-failure branches.
                        for (int q = 0; q < nonFailWeights.Length; q++)
                        {
                            double excess = Math.Max(0d, combined - nonFailValues[q]);
                            double excessProbability = entryProbability * nonFailWeights[q];
                            excessEntryProbabilities.Add(excessProbability);
                            excessEntryValues.Add(excess);
                            expectedExcessConsequences += excessProbability * excess;
                            realization.MinN = Math.Min(realization.MinN, excess);
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
