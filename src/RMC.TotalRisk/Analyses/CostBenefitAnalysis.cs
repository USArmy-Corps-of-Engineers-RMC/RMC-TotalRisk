using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Numerics;
using Numerics.Utilities;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Analyses
{
    /// <summary>
    /// The cost-benefit study: a collection of risk-reduction alternatives with a designated
    /// baseline, evaluated as mean-only life-cycle trajectories on one study-wide epoch grid,
    /// priced under the study's discounting conventions, and reduced to the per-alternative
    /// economics, consequence-reduction, and trajectory tables.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The study composes shipped machinery only: each distinct (system, plan) pair runs one
    /// life-cycle trajectory query — mean-only, on throwaway clones, with epoch realizations
    /// retained — so every referenced analysis is byte-untouched and two identical runs
    /// publish bit-identical results. The study owns time and money: one horizon and one
    /// discount rate for every alternative, with each plan's years folded into one shared
    /// epoch grid so deteriorating responses re-age at identical boundaries everywhere.
    /// Benefits are signed reductions against the designated baseline on the declared benefit
    /// stream, computed in both accounting conventions; costs are commitments and are never
    /// survival-weighted. Nothing about the study — its declarations or its results — enters
    /// any canonical-hash or seed surface.
    /// </para>
    /// </remarks>
    public sealed class CostBenefitAnalysis : AnalysisBase
    {
        #region Construction

        /// <summary>The display name.</summary>
        private string _name = string.Empty;

        /// <summary>The display description.</summary>
        private string _description = string.Empty;

        /// <summary>The study declarations.</summary>
        private CostBenefitOptions _options;

        /// <summary>The designated baseline alternative.</summary>
        private RiskReductionAlternative? _baseline;

        /// <summary>The published results, or null.</summary>
        private CostBenefitResults? _results;

        /// <summary>
        /// Initializes a cost-benefit study.
        /// </summary>
        /// <param name="options">The study declarations.</param>
        /// <exception cref="ArgumentNullException">Thrown when the options are null.</exception>
        public CostBenefitAnalysis(CostBenefitOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            Alternatives = new ObservableCollection<RiskReductionAlternative>();
            Alternatives.CollectionChanged += (_, _) => InvalidatePublishedState();
        }

        #endregion

        #region Members

        /// <summary>
        /// The display name.
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                _name = value ?? string.Empty;
                RaisePropertyChange(nameof(Name));
            }
        }

        /// <summary>
        /// The display description.
        /// </summary>
        public string Description
        {
            get => _description;
            set
            {
                _description = value ?? string.Empty;
                RaisePropertyChange(nameof(Description));
            }
        }

        /// <summary>
        /// The study declarations. Replace to edit — assigning a new set clears the published
        /// results.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when the assigned options are null.</exception>
        public CostBenefitOptions Options
        {
            get => _options;
            set
            {
                _options = value ?? throw new ArgumentNullException(nameof(value));
                InvalidatePublishedState();
                RaisePropertyChange(nameof(Options));
            }
        }

        /// <summary>
        /// The study's alternatives. Membership edits clear the published results.
        /// </summary>
        public ObservableCollection<RiskReductionAlternative> Alternatives { get; }

        /// <summary>
        /// The designated baseline — the existing condition every delta anchors to. Must
        /// reference a member of <see cref="Alternatives"/> (validated); assigning clears the
        /// published results.
        /// </summary>
        public RiskReductionAlternative? Baseline
        {
            get => _baseline;
            set
            {
                _baseline = value;
                InvalidatePublishedState();
                RaisePropertyChange(nameof(Baseline));
            }
        }

        /// <summary>
        /// The published study results, or null until a successful run.
        /// </summary>
        public CostBenefitResults? Results => _results;

        #endregion

        #region IAnalysis Methods

        /// <inheritdoc/>
        public override IReadOnlyList<ValidationIssue> ValidateIssues()
        {
            var messages = ValidateMessagesCore(_options, SnapshotAlternatives(), _baseline);
            var issues = new List<ValidationIssue>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                issues.Add(ValidationIssue.FromLegacyMessage(messages[i]));
            }
            return issues.AsReadOnly();
        }

        /// <inheritdoc/>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var issues = ValidateIssues();
            var messages = new List<string>(issues.Count);
            bool isValid = true;
            for (int i = 0; i < issues.Count; i++)
            {
                messages.Add(issues[i].ToLegacyMessage());
                if (issues[i].Severity == DiagnosticSeverity.Error) isValid = false;
            }
            return (isValid, messages);
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the study is already running or fails validation.
        /// </exception>
        /// <exception cref="OperationCanceledException">Thrown when the run is canceled.</exception>
        public override async Task RunAsync(SafeProgressReporter? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            var startingArgs = new CancelEventArgs();
            if (!TryBeginRun())
            {
                var concurrentError = new InvalidOperationException("This analysis is already running.");
                OnAnalysisCompleted(new AnalysisRunCompletedEventArgs(wasCanceled: false, succeeded: false, error: concurrentError));
                throw concurrentError;
            }

            AnalysisRunCompletedEventArgs? completion = null;
            try
            {
                OnAnalysisStarting(startingArgs);
                if (startingArgs.Cancel)
                {
                    throw new OperationCanceledException("The analysis was canceled by an AnalysisStarting handler.");
                }

                InvalidatePublishedState();

                // The race-safe snapshot: validation and the whole computation read one
                // consistent view of the study, so a concurrent edit cannot slip between the
                // gate and the work.
                var runOptions = _options;
                var runAlternatives = SnapshotAlternatives();
                var runBaseline = _baseline;
                var messages = ValidateMessagesCore(runOptions, runAlternatives, runBaseline);
                bool isValid = true;
                for (int i = 0; i < messages.Count; i++)
                {
                    if (messages[i].StartsWith("Error: ", StringComparison.Ordinal)) isValid = false;
                }
                if (!isValid)
                {
                    throw new InvalidOperationException(string.Join(Environment.NewLine, messages));
                }

                var token = ResetCancellationToken(cancellationToken);
                CostBenefitResults results = await Task.Run(
                    () => ComputeStudy(runOptions, runAlternatives, runBaseline!, progressReporter, token),
                    CancellationToken.None).ConfigureAwait(false);

                _results = results;
                RaisePropertyChange(nameof(Results));
                IsEstimated = true;
                completion = new AnalysisRunCompletedEventArgs(wasCanceled: false, succeeded: true, error: null);
            }
            catch (OperationCanceledException)
            {
                completion = new AnalysisRunCompletedEventArgs(wasCanceled: true, succeeded: false, error: null);
                InvalidatePublishedState();
                throw;
            }
            catch (Exception ex)
            {
                completion = new AnalysisRunCompletedEventArgs(wasCanceled: false, succeeded: false, error: ex);
                InvalidatePublishedState();
                throw;
            }
            finally
            {
                EndRun();
                OnAnalysisCompleted(completion ?? new AnalysisRunCompletedEventArgs(wasCanceled: false,
                    succeeded: false, error: new InvalidOperationException("The analysis exited without a completion state.")));
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Snapshots the alternatives collection for a consistent read.
        /// </summary>
        /// <returns>The snapshot.</returns>
        private RiskReductionAlternative[] SnapshotAlternatives()
        {
            var snapshot = new RiskReductionAlternative[Alternatives.Count];
            Alternatives.CopyTo(snapshot, 0);
            return snapshot;
        }

        /// <summary>
        /// Clears the published results and the estimated flag.
        /// </summary>
        private void InvalidatePublishedState()
        {
            if (_results != null)
            {
                _results = null;
                RaisePropertyChange(nameof(Results));
            }
            IsEstimated = false;
        }

        /// <summary>
        /// Reads a system's declared consequence-type labels and units.
        /// </summary>
        /// <param name="system">The analysis.</param>
        /// <returns>The labels and units, position 0 the primary type.</returns>
        private static (List<string> Labels, List<string> Units) DeclaredAxis(RiskAnalysis system)
        {
            var labels = new List<string>(1 + system.AdditionalConsequenceTypes.Count) { system.SpecifiedConsequence };
            var units = new List<string>(labels.Capacity) { system.ConsequenceUnit };
            for (int i = 0; i < system.AdditionalConsequenceTypes.Count; i++)
            {
                labels.Add(system.AdditionalConsequenceTypes[i].SpecifiedConsequence);
                units.Add(system.AdditionalConsequenceTypes[i].ConsequenceUnit);
            }
            return (labels, units);
        }

        /// <summary>
        /// Resolves the per-type monetization factors against the declared axis: identity
        /// types (unit equal to the monetary unit) price at one, explicitly priced types at
        /// their factor, and every other slot is NaN. Null when no monetization map is
        /// declared.
        /// </summary>
        /// <param name="options">The study declarations.</param>
        /// <param name="units">The declared consequence-type units.</param>
        /// <returns>The per-type factors, or null.</returns>
        private static double[]? ResolveMonetizationFactors(CostBenefitOptions options, IReadOnlyList<string> units)
        {
            if (options.Monetization == null) return null;
            var factors = new double[units.Count];
            for (int t = 0; t < units.Count; t++)
            {
                factors[t] = string.Equals(units[t], options.Monetization.MonetaryUnit, StringComparison.Ordinal)
                    ? 1d
                    : double.NaN;
            }
            for (int i = 0; i < options.Monetization.Factors.Count; i++)
            {
                MonetizationFactor factor = options.Monetization.Factors[i];
                if (factor.ConsequenceType < units.Count)
                {
                    factors[factor.ConsequenceType] = factor.AmountPerUnit;
                }
            }
            return factors;
        }

        /// <summary>
        /// Selects one stream's horizon aggregate lists from a trajectory.
        /// </summary>
        /// <param name="trajectory">The trajectory.</param>
        /// <param name="stream">The stream (Total, Excess, or Fail).</param>
        /// <returns>The per-type present-value, equivalent-annual, cumulative, absorbing present-value, and absorbing cumulative lists.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an unsupported stream.</exception>
        private static (IReadOnlyList<double> PresentValue, IReadOnlyList<double> EquivalentAnnual,
            IReadOnlyList<double> Cumulative, IReadOnlyList<double> AbsorbingPresentValue,
            IReadOnlyList<double> AbsorbingCumulative) SelectStreamAggregates(
            LifeCycleRiskResults trajectory, RiskType stream)
        {
            return stream switch
            {
                RiskType.Total => (trajectory.PresentValueOfExpectedConsequences,
                    trajectory.EquivalentAnnualConsequences,
                    trajectory.CumulativeExpectedConsequences,
                    trajectory.AbsorbingPresentValueOfExpectedConsequences,
                    trajectory.AbsorbingCumulativeExpectedConsequences),
                RiskType.Excess => (trajectory.ExcessPresentValueOfExpectedConsequences,
                    trajectory.ExcessEquivalentAnnualConsequences,
                    trajectory.ExcessCumulativeExpectedConsequences,
                    trajectory.AbsorbingExcessPresentValueOfExpectedConsequences,
                    trajectory.AbsorbingExcessCumulativeExpectedConsequences),
                RiskType.Fail => (trajectory.FailPresentValueOfExpectedConsequences,
                    trajectory.FailEquivalentAnnualConsequences,
                    trajectory.FailCumulativeExpectedConsequences,
                    trajectory.AbsorbingFailPresentValueOfExpectedConsequences,
                    trajectory.AbsorbingFailCumulativeExpectedConsequences),
                _ => throw new ArgumentOutOfRangeException(nameof(stream),
                    "The benefit stream must be Total, Excess, or Fail."),
            };
        }

        /// <summary>
        /// Derives the study-wide epoch grid: the sorted distinct union of year zero, the
        /// study's evaluation years, and every alternative's plan years, so every trajectory
        /// shares identical epoch boundaries.
        /// </summary>
        /// <param name="options">The study declarations.</param>
        /// <param name="alternatives">The alternatives.</param>
        /// <returns>The ascending grid years.</returns>
        private static int[] DeriveStudyGrid(CostBenefitOptions options,
            IReadOnlyList<RiskReductionAlternative> alternatives)
        {
            var years = new SortedSet<int> { 0 };
            for (int i = 0; i < options.EvaluationYears.Count; i++)
            {
                years.Add(options.EvaluationYears[i]);
            }
            for (int i = 0; i < alternatives.Count; i++)
            {
                LifeCyclePlan? plan = alternatives[i].Plan;
                if (plan == null) continue;
                for (int j = 0; j < plan.EvaluationYears.Count; j++)
                {
                    years.Add(plan.EvaluationYears[j]);
                }
                for (int j = 0; j < plan.Interventions.Count; j++)
                {
                    years.Add(plan.Interventions[j].Year);
                }
            }
            var grid = new int[years.Count];
            years.CopyTo(grid);
            return grid;
        }

        /// <summary>
        /// Prices one cost stream under the study's discounting conventions: dated capital at
        /// (1 + r)^−year, recurring segments as annuity-factor differences over
        /// (startYear, endYear], and the undiscounted cumulative total.
        /// </summary>
        /// <param name="costs">The cost stream.</param>
        /// <param name="periodYears">The study horizon.</param>
        /// <param name="discountRate">The study rate.</param>
        /// <param name="horizonAnnuity">The horizon annuity factor.</param>
        /// <returns>The per-kind present values, the total, the equivalent annual cost, and the cumulative cost.</returns>
        private static (double Capital, double OperationsAndMaintenance, double OperatingChanges,
            double Total, double EquivalentAnnual, double Cumulative) PriceCosts(
            CostStream costs, int periodYears, double discountRate, double horizonAnnuity)
        {
            double capital = 0d;
            double cumulative = 0d;
            for (int i = 0; i < costs.Capital.Count; i++)
            {
                CapitalCostEntry entry = costs.Capital[i];
                capital += entry.Amount * DiscountingSupport.DiscountFactor(entry.Year, discountRate);
                cumulative += entry.Amount;
            }
            double operations = PriceSegments(costs.OperationsAndMaintenance, periodYears, discountRate, ref cumulative);
            double operating = PriceSegments(costs.OperatingChanges, periodYears, discountRate, ref cumulative);
            double total = capital + operations + operating;
            return (capital, operations, operating, total, total / horizonAnnuity, cumulative);
        }

        /// <summary>
        /// Prices one kind's recurring segments as annuity-factor differences and accumulates
        /// their undiscounted totals.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <param name="periodYears">The study horizon (the unbounded end).</param>
        /// <param name="discountRate">The study rate.</param>
        /// <param name="cumulative">The undiscounted running total (updated in place).</param>
        /// <returns>The kind's present value.</returns>
        private static double PriceSegments(IReadOnlyList<RecurringCostSegment> segments,
            int periodYears, double discountRate, ref double cumulative)
        {
            double presentValue = 0d;
            for (int i = 0; i < segments.Count; i++)
            {
                RecurringCostSegment segment = segments[i];
                int endYear = segment.EndYear ?? periodYears;
                presentValue += segment.AnnualAmount
                    * (DiscountingSupport.AnnuityFactor(endYear, discountRate)
                        - DiscountingSupport.AnnuityFactor(segment.StartYear, discountRate));
                cumulative += segment.AnnualAmount * (endYear - segment.StartYear);
            }
            return presentValue;
        }

        /// <summary>
        /// The survival-equivalent annualized failure probability over the horizon —
        /// 1 − (1 − P_T)^(1/T), evaluated in log space.
        /// </summary>
        /// <param name="failureProbabilityByHorizon">P(at least one failure by the horizon).</param>
        /// <param name="periodYears">The horizon in years.</param>
        /// <returns>The equivalent annual probability.</returns>
        private static double SurvivalEquivalentAnnualProbability(double failureProbabilityByHorizon, int periodYears)
        {
            return -Tools.Expm1(Tools.Log1p(-failureProbabilityByHorizon) / periodYears);
        }

        /// <summary>
        /// Runs the study on one consistent snapshot: one trajectory per distinct
        /// (system, plan) pair on the shared grid, baseline first, then the per-alternative
        /// pricing and reductions.
        /// </summary>
        /// <param name="options">The study declarations.</param>
        /// <param name="alternatives">The alternatives snapshot.</param>
        /// <param name="baseline">The designated baseline (a member of the snapshot).</param>
        /// <param name="progressReporter">The progress sink; progress is evaluations done over total.</param>
        /// <param name="token">The cancellation token, honored between evaluations.</param>
        /// <returns>The study results.</returns>
        private static CostBenefitResults ComputeStudy(CostBenefitOptions options,
            IReadOnlyList<RiskReductionAlternative> alternatives, RiskReductionAlternative baseline,
            SafeProgressReporter? progressReporter, CancellationToken token)
        {
            int periodYears = options.PeriodYears;
            double discountRate = options.DiscountRate;
            double horizonAnnuity = DiscountingSupport.AnnuityFactor(periodYears, discountRate);
            int[] grid = DeriveStudyGrid(options, alternatives);

            // One trajectory per distinct (system, plan) pair — the baseline's first, then
            // the remaining alternatives in declaration order.
            var evaluationOrder = new List<(RiskAnalysis System, LifeCyclePlan? Plan)>();
            var seen = new HashSet<(RiskAnalysis, LifeCyclePlan?)> ();
            if (seen.Add((baseline.System, baseline.Plan)))
            {
                evaluationOrder.Add((baseline.System, baseline.Plan));
            }
            for (int i = 0; i < alternatives.Count; i++)
            {
                var key = (alternatives[i].System, alternatives[i].Plan);
                if (seen.Add(key)) evaluationOrder.Add(key);
            }

            var trajectoriesByKey = new Dictionary<(RiskAnalysis, LifeCyclePlan?), LifeCycleRiskResults>();
            for (int i = 0; i < evaluationOrder.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                (RiskAnalysis system, LifeCyclePlan? plan) = evaluationOrder[i];
                var definition = new LifeCycleDefinition(periodYears, discountRate, grid,
                    plan?.Interventions, retainEpochRealizations: true);
                trajectoriesByKey[(system, plan)] = system.MeasureLifeCycleRisk(definition);
                progressReporter?.ReportProgress((i + 1) * 100d / evaluationOrder.Count);
            }

            LifeCycleRiskResults baselineTrajectory = trajectoriesByKey[(baseline.System, baseline.Plan)];
            (List<string> labels, List<string> units) = DeclaredAxis(baseline.System);
            int typeCount = labels.Count;
            double[]? factors = ResolveMonetizationFactors(options, units);
            bool anyMonetized = false;
            if (factors != null)
            {
                for (int t = 0; t < factors.Length; t++)
                {
                    if (!double.IsNaN(factors[t])) anyMonetized = true;
                }
            }

            var baselineBenefitStream = SelectStreamAggregates(baselineTrajectory, options.BenefitRiskType);
            double baselineEquivalentAnnualProbability =
                SurvivalEquivalentAnnualProbability(baselineTrajectory.FailureProbabilityByHorizon, periodYears);
            double baselineYearZeroProbability = baselineTrajectory.Epochs[0].System.FailureProbability;

            var rows = new List<AlternativeEconomics>(alternatives.Count);
            var reductions = new List<ConsequenceReduction>();
            var points = new List<TrajectoryPoint>();
            var trajectories = new List<LifeCycleRiskResults>(alternatives.Count);

            // The baseline is row zero; the remaining alternatives follow in declaration order.
            var rowOrder = new List<RiskReductionAlternative>(alternatives.Count) { baseline };
            for (int i = 0; i < alternatives.Count; i++)
            {
                if (!ReferenceEquals(alternatives[i], baseline)) rowOrder.Add(alternatives[i]);
            }

            for (int i = 0; i < rowOrder.Count; i++)
            {
                RiskReductionAlternative alternative = rowOrder[i];
                LifeCycleRiskResults trajectory = trajectoriesByKey[(alternative.System, alternative.Plan)];
                trajectories.Add(trajectory);

                (double capitalPv, double omPv, double operatingPv, double totalPv, double eac, double cumulativeCost) =
                    PriceCosts(alternative.Costs, periodYears, discountRate, horizonAnnuity);

                // The benefit aggregates on the study's stream, both conventions.
                var streamAggregates = SelectStreamAggregates(trajectory, options.BenefitRiskType);
                double monetized = anyMonetized ? 0d : double.NaN;
                double economic = anyMonetized ? 0d : double.NaN;
                double absorbingMonetized = monetized;
                double absorbingEconomic = economic;
                if (anyMonetized)
                {
                    for (int t = 0; t < typeCount; t++)
                    {
                        double factor = factors![t];
                        if (double.IsNaN(factor)) continue;
                        double reduction = factor
                            * (baselineBenefitStream.PresentValue[t] - streamAggregates.PresentValue[t]);
                        double absorbingReduction = factor
                            * (baselineBenefitStream.AbsorbingPresentValue[t] - streamAggregates.AbsorbingPresentValue[t]);
                        monetized += reduction;
                        absorbingMonetized += absorbingReduction;
                        if (t != options.LifeSafetyConsequenceType)
                        {
                            economic += reduction;
                            absorbingEconomic += absorbingReduction;
                        }
                    }
                }
                double netPresentValue = monetized - totalPv;
                double absorbingNetPresentValue = absorbingMonetized - totalPv;
                double benefitCostRatio = totalPv > 0d ? monetized / totalPv : double.NaN;
                double absorbingBenefitCostRatio = totalPv > 0d ? absorbingMonetized / totalPv : double.NaN;

                double equivalentAnnualProbability =
                    SurvivalEquivalentAnnualProbability(trajectory.FailureProbabilityByHorizon, periodYears);
                double yearZeroProbability = trajectory.Epochs[0].System.FailureProbability;

                // The life-saved axis always reads the Excess stream of the declared type.
                double livesSaved = double.NaN;
                if (options.LifeSafetyConsequenceType >= 0 && options.LifeSafetyConsequenceType < typeCount)
                {
                    livesSaved = baselineTrajectory.ExcessEquivalentAnnualConsequences[options.LifeSafetyConsequenceType]
                        - trajectory.ExcessEquivalentAnnualConsequences[options.LifeSafetyConsequenceType];
                }

                rows.Add(new AlternativeEconomics(alternative.Name, alternative.Description,
                    isBaseline: ReferenceEquals(alternative, baseline),
                    capitalPv, omPv, operatingPv, totalPv, eac, cumulativeCost,
                    monetized, economic, netPresentValue, netPresentValue / horizonAnnuity, benefitCostRatio,
                    absorbingMonetized, absorbingEconomic, absorbingNetPresentValue,
                    absorbingNetPresentValue / horizonAnnuity, absorbingBenefitCostRatio,
                    equivalentAnnualProbability,
                    baselineEquivalentAnnualProbability - equivalentAnnualProbability,
                    yearZeroProbability,
                    baselineYearZeroProbability - yearZeroProbability,
                    livesSaved));

                // The per-type per-stream reduction rows, in stream-major order per type.
                foreach (RiskType stream in new[] { RiskType.Total, RiskType.Excess, RiskType.Fail })
                {
                    var baselineStream = SelectStreamAggregates(baselineTrajectory, stream);
                    var alternativeStream = SelectStreamAggregates(trajectory, stream);
                    for (int t = 0; t < typeCount; t++)
                    {
                        double presentValueReduction = baselineStream.PresentValue[t] - alternativeStream.PresentValue[t];
                        double monetizedReduction = factors != null && !double.IsNaN(factors[t])
                            ? factors[t] * presentValueReduction
                            : double.NaN;
                        reductions.Add(new ConsequenceReduction(alternative.Name, t, stream,
                            labels[t], units[t],
                            baselineStream.PresentValue[t], alternativeStream.PresentValue[t],
                            presentValueReduction,
                            baselineStream.EquivalentAnnual[t] - alternativeStream.EquivalentAnnual[t],
                            baselineStream.Cumulative[t] - alternativeStream.Cumulative[t],
                            baselineStream.AbsorbingPresentValue[t] - alternativeStream.AbsorbingPresentValue[t],
                            baselineStream.AbsorbingCumulative[t] - alternativeStream.AbsorbingCumulative[t],
                            monetizedReduction));
                    }
                }

                // The plot-ready trajectory points, one per epoch.
                for (int k = 0; k < trajectory.Epochs.Count; k++)
                {
                    LifeCycleEpochRisk epoch = trajectory.Epochs[k];
                    points.Add(new TrajectoryPoint(alternative.Name, epoch.StartYear, epoch.SpanYears,
                        epoch.System.FailureProbability, epoch.CumulativeFailureProbability,
                        epoch.System.ExpectedConsequences,
                        epoch.System.ExcessExpectedConsequences,
                        epoch.System.FailExpectedConsequences));
                }
            }

            return new CostBenefitResults(periodYears, discountRate, grid,
                options.BenefitRiskType, options.Accounting, options.AlphaLevels,
                options.Monetization, options.LifeSafetyConsequenceType,
                options.WillingnessToPay, options.WillingnessToPayVintage,
                options.AlarpProximity, options.AlarpBandThresholds,
                options.IndividualRiskLimit, options.EquityExponent, options.DoNoHarm,
                labels, units, rows, reductions, points, trajectories);
        }

        /// <summary>
        /// Executes the study validation rules and gathers their compatibility messages.
        /// Structured conversion is centralized in <see cref="ValidateIssues"/>.
        /// </summary>
        /// <param name="options">The declarations under validation.</param>
        /// <param name="alternatives">The alternatives under validation.</param>
        /// <param name="baseline">The designated baseline, or null.</param>
        /// <returns>The prefixed validation messages.</returns>
        private static List<string> ValidateMessagesCore(CostBenefitOptions options,
            IReadOnlyList<RiskReductionAlternative> alternatives, RiskReductionAlternative? baseline)
        {
            var messages = new List<string>();

            // The option-local rules (levels, weights, band thresholds, child declarations).
            (_, List<string> optionMessages) = options.Validate();
            messages.AddRange(optionMessages);

            if (options.BenefitRiskType != RiskType.Total && options.BenefitRiskType != RiskType.Excess
                && options.BenefitRiskType != RiskType.Fail)
            {
                messages.Add("Error: The benefit stream must be Total, Excess, or Fail.");
            }

            // The baseline designation.
            if (baseline == null)
            {
                messages.Add("Error: The study requires a designated baseline alternative.");
            }
            else
            {
                bool isMember = false;
                for (int i = 0; i < alternatives.Count; i++)
                {
                    if (ReferenceEquals(alternatives[i], baseline)) isMember = true;
                }
                if (!isMember)
                    messages.Add("Error: The designated baseline must be a member of the alternatives collection.");
            }

            // Unique names.
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < alternatives.Count; i++)
            {
                if (!seenNames.Add(alternatives[i].Name))
                    messages.Add($"Error: Two alternatives share the name '{alternatives[i].Name}'.");
            }

            // Horizon bounds on plan and cost years, per alternative.
            int periodYears = options.PeriodYears;
            for (int i = 0; i < alternatives.Count; i++)
            {
                RiskReductionAlternative alternative = alternatives[i];
                LifeCyclePlan? plan = alternative.Plan;
                if (plan != null)
                {
                    bool planYearOut = false;
                    for (int j = 0; j < plan.EvaluationYears.Count; j++)
                    {
                        if (plan.EvaluationYears[j] >= periodYears) planYearOut = true;
                    }
                    for (int j = 0; j < plan.Interventions.Count; j++)
                    {
                        if (plan.Interventions[j].Year >= periodYears) planYearOut = true;
                    }
                    if (planYearOut)
                        messages.Add($"Error: Alternative '{alternative.Name}' declares a plan year at or beyond the horizon.");
                }

                bool costYearOut = false;
                bool segmentEndOut = false;
                for (int j = 0; j < alternative.Costs.Capital.Count; j++)
                {
                    if (alternative.Costs.Capital[j].Year >= periodYears) costYearOut = true;
                }
                CheckSegments(alternative.Costs.OperationsAndMaintenance, periodYears, ref costYearOut, ref segmentEndOut);
                CheckSegments(alternative.Costs.OperatingChanges, periodYears, ref costYearOut, ref segmentEndOut);
                if (costYearOut)
                    messages.Add($"Error: Alternative '{alternative.Name}' declares a cost year at or beyond the horizon.");
                if (segmentEndOut)
                    messages.Add($"Error: Alternative '{alternative.Name}' declares a cost segment ending beyond the horizon.");
            }

            // The cross-alternative comparability rules anchor to the baseline's declaration.
            if (baseline != null)
            {
                (List<string> baselineLabels, List<string> baselineUnits) = DeclaredAxis(baseline.System);
                int typeCount = baselineLabels.Count;

                for (int i = 0; i < alternatives.Count; i++)
                {
                    RiskReductionAlternative alternative = alternatives[i];
                    if (ReferenceEquals(alternative, baseline)) continue;
                    (List<string> labels, List<string> units) = DeclaredAxis(alternative.System);
                    if (labels.Count != typeCount)
                    {
                        messages.Add($"Error: Alternative '{alternative.Name}' declares {labels.Count} consequence types while the baseline declares {typeCount}.");
                        continue;
                    }
                    for (int t = 0; t < typeCount; t++)
                    {
                        // The engine's blank-wildcard comparability rule: a blank side
                        // matches anything; two non-blank sides must agree.
                        if (!string.IsNullOrEmpty(labels[t]) && !string.IsNullOrEmpty(baselineLabels[t])
                            && !string.Equals(labels[t], baselineLabels[t], StringComparison.Ordinal))
                        {
                            messages.Add($"Error: Alternative '{alternative.Name}' consequence type {t} label '{labels[t]}' conflicts with the baseline's '{baselineLabels[t]}'.");
                        }
                        if (!string.IsNullOrEmpty(units[t]) && !string.IsNullOrEmpty(baselineUnits[t])
                            && !string.Equals(units[t], baselineUnits[t], StringComparison.Ordinal))
                        {
                            messages.Add($"Error: Alternative '{alternative.Name}' consequence type {t} unit '{units[t]}' conflicts with the baseline's '{baselineUnits[t]}'.");
                        }
                    }
                }

                // One analysis mode per study — the axes are not comparable across modes.
                RiskAnalysisMode mode = baseline.System.Options.Mode;
                bool mixedModes = false;
                for (int i = 0; i < alternatives.Count; i++)
                {
                    if (alternatives[i].System.Options.Mode != mode) mixedModes = true;
                }
                if (mixedModes)
                {
                    messages.Add("Error: Alternatives mix risk-analysis modes; reliability-mode and consequence-mode alternatives are not comparable.");
                }
                else if (mode == RiskAnalysisMode.Reliability)
                {
                    messages.Add("Warning: The study's alternatives run in reliability mode; consequence-dependent metrics are skipped (NaN).");
                }

                // Monetization against the declared axis.
                if (options.Monetization != null)
                {
                    for (int i = 0; i < options.Monetization.Factors.Count; i++)
                    {
                        MonetizationFactor factor = options.Monetization.Factors[i];
                        if (factor.ConsequenceType >= typeCount)
                        {
                            messages.Add($"Error: A monetization factor prices consequence-type position {factor.ConsequenceType}, which is not declared.");
                        }
                        else if (string.Equals(baselineUnits[factor.ConsequenceType],
                            options.Monetization.MonetaryUnit, StringComparison.Ordinal))
                        {
                            messages.Add($"Error: A monetization factor prices consequence type {factor.ConsequenceType}, whose unit already equals the monetary unit (identity-monetized at factor one).");
                        }
                    }
                }
                if (options.LifeSafetyConsequenceType >= typeCount)
                {
                    messages.Add($"Error: The life-safety consequence-type position {options.LifeSafetyConsequenceType} is not declared.");
                }

                // The monetized-set warnings.
                double[]? factors = ResolveMonetizationFactors(options, baselineUnits);
                bool anyMonetized = false;
                if (factors != null)
                {
                    for (int t = 0; t < factors.Length; t++)
                    {
                        if (!double.IsNaN(factors[t])) anyMonetized = true;
                    }
                }
                if (!anyMonetized)
                {
                    messages.Add("Warning: No consequence types are monetized; net present value, net annual benefit, and benefit-cost ratio will be NaN.");
                }
                if (options.LifeSafetyConsequenceType >= 0 && options.LifeSafetyConsequenceType < typeCount
                    && factors != null && !double.IsNaN(factors[options.LifeSafetyConsequenceType]))
                {
                    messages.Add("Warning: The life-safety consequence type is monetized: monetized lives enter net present value and the benefit-cost ratio while the adjusted cost per statistical life saved stays economic-only, so the net-present-value sign and the disproportionality ratio are not independent evidence when the price differs from the willingness to pay.");
                }

                // Measure-relevant option mismatches across alternatives (the study
                // re-evaluates uniformly at its declared levels).
                for (int i = 0; i < alternatives.Count; i++)
                {
                    RiskReductionAlternative alternative = alternatives[i];
                    double runAlpha = alternative.System.Options.Alpha;
                    bool declared = false;
                    for (int j = 0; j < options.AlphaLevels.Count; j++)
                    {
                        if (options.AlphaLevels[j] == runAlpha) declared = true;
                    }
                    if (!declared)
                    {
                        messages.Add($"Warning: Alternative '{alternative.Name}' runs exceedance level α = {runAlpha} which is not among the study's declared levels; tail measures are re-evaluated uniformly at the study's levels.");
                    }
                    if (ReferenceEquals(alternative, baseline)) continue;
                    double baselineThreshold = baseline.System.Options.ConsequenceThreshold;
                    double alternativeThreshold = alternative.System.Options.ConsequenceThreshold;
                    bool thresholdsAgree = baselineThreshold == alternativeThreshold
                        || (double.IsNaN(baselineThreshold) && double.IsNaN(alternativeThreshold));
                    if (!thresholdsAgree)
                    {
                        messages.Add($"Warning: Alternative '{alternative.Name}' declares a different consequence threshold than the baseline; threshold measures are not comparable across alternatives.");
                    }
                    if (alternative.System.Options.RiskMeasures != baseline.System.Options.RiskMeasures)
                    {
                        messages.Add($"Warning: Alternative '{alternative.Name}' computes a different optional-measure set than the baseline.");
                    }
                }
            }

            // Running analyses refuse a study run.
            var seenSystems = new HashSet<RiskAnalysis>();
            for (int i = 0; i < alternatives.Count; i++)
            {
                RiskReductionAlternative alternative = alternatives[i];
                if (!seenSystems.Add(alternative.System)) continue;
                if (alternative.System.IsRunning)
                {
                    messages.Add($"Error: Alternative '{alternative.Name}' references a running analysis.");
                }
            }

            // The costless-alternative and skipped-block advisories.
            for (int i = 0; i < alternatives.Count; i++)
            {
                RiskReductionAlternative alternative = alternatives[i];
                if (ReferenceEquals(alternative, baseline)) continue;
                if (alternative.Costs.Capital.Count == 0
                    && alternative.Costs.OperationsAndMaintenance.Count == 0
                    && alternative.Costs.OperatingChanges.Count == 0)
                {
                    messages.Add($"Warning: Alternative '{alternative.Name}' declares no costs; its benefit-cost ratio will be NaN.");
                }
            }
            for (int i = 0; i < alternatives.Count; i++)
            {
                RiskReductionAlternative alternative = alternatives[i];
                if (alternative.System.RiskResults == null)
                {
                    messages.Add($"Warning: Alternative '{alternative.Name}' carries no stored full-uncertainty ensemble; the epistemic decision strategies will be skipped.");
                }
                if (alternative.System.LogicTreeEnumeration == null)
                {
                    messages.Add($"Warning: Alternative '{alternative.Name}' was not enumerated by the exact logic tree; the shared-state regret strategies will be skipped.");
                }
            }
            if (double.IsNaN(options.WillingnessToPay))
            {
                messages.Add("Warning: No willingness to pay is declared; the disproportionality and ALARP block is skipped.");
            }
            if (options.LifeSafetyConsequenceType >= 0
                && (double.IsNaN(options.BaselineIndividualRisk) || double.IsNaN(options.AlternativeIndividualRisk)))
            {
                messages.Add("Warning: The individual-risk seats are not declared; the survival-equivalent annualized failure probability proxy is used and echoed.");
            }

            return messages;
        }

        /// <summary>
        /// Checks one kind's segments against the horizon bounds.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <param name="periodYears">The study horizon.</param>
        /// <param name="costYearOut">Set when a start year sits at or beyond the horizon.</param>
        /// <param name="segmentEndOut">Set when a bounded end year runs beyond the horizon.</param>
        private static void CheckSegments(IReadOnlyList<RecurringCostSegment> segments, int periodYears,
            ref bool costYearOut, ref bool segmentEndOut)
        {
            for (int j = 0; j < segments.Count; j++)
            {
                if (segments[j].StartYear >= periodYears) costYearOut = true;
                if (segments[j].EndYear.HasValue && segments[j].EndYear!.Value > periodYears) segmentEndOut = true;
            }
        }

        #endregion
    }
}
