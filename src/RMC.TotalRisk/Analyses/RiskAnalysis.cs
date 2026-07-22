using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Mathematics;
using Numerics.Mathematics.Integration;
using Numerics.Mathematics.RootFinding;
using Numerics.Sampling;
using Numerics.Utilities;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Analyses
{
    /// <summary>
    /// The Monte Carlo risk analysis: content-seeded knowledge-uncertainty sampling over the
    /// owned system components, adaptive Gauss–Kronrod integration of the risk integrand, exact
    /// loss-exceedance-curve construction with the full risk-measure catalog, and percentile
    /// post-processing of the ensemble.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from the v1.0 engine with the ratified v0.13 corrections
    /// (<c>docs/technical-reference/risk-integration.md</c>,
    /// <c>docs/technical-reference/loss-exceedance-curves.md</c>): the 1D integrator is
    /// <c>AdaptiveGaussKronrod</c> (G10K21) used as an adaptive sampler — the returned integral
    /// is discarded and the recorded risk points are the product, with the refinement objective
    /// selected by <see cref="RiskAnalysisOptions.RiskIntegrand"/>; curves are built exactly from
    /// the recorded (mass, consequence) pairs; and seeding is content-based
    /// (architecture doc §5.5.4): per-component seeds derive from the analysis seed, the
    /// component's canonical hash, and its occurrence index — never from canvas order, so
    /// renaming, moving, or reordering components can never change results, and results are
    /// bit-identical at any thread count (every parallel write is index-owned; ensemble
    /// reductions run in a sequential post-pass, and percentile means are summed sequentially
    /// rather than with parallel reductions — a deliberate determinism-over-throughput choice).
    /// </para>
    /// <para>
    /// <b>Ownership and persistence (architecture doc §8):</b> components and results arrive
    /// through the constructor (the BestFit analysis shape) and <see cref="ToXElement"/> writes
    /// the analysis metadata, the estimated flag, and the options only. Results are JSON
    /// containers; the consuming layer persists them separately.
    /// </para>
    /// <para>
    /// <b>Stage gates:</b> this engine stage computes the one-dimensional single-component path.
    /// Multi-component system aggregation arrives with the system-risk stage (Phase 4b),
    /// reliability mode with Phase 4c, and multi-stage response composition with the event-tree
    /// phase — each is a validation error until its stage lands, with the message naming the
    /// stage.
    /// </para>
    /// </remarks>
    public class RiskAnalysis : AnalysisBase
    {
        #region Construction

        /// <summary>
        /// Initializes a risk analysis over the given components with default options.
        /// </summary>
        /// <param name="components">The system components the analysis owns, in declared order.</param>
        /// <exception cref="ArgumentNullException">Thrown when the component list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the component list contains a null entry.</exception>
        public RiskAnalysis(IList<SystemComponent> components)
        {
            if (components == null) throw new ArgumentNullException(nameof(components));
            _components = new List<SystemComponent>(components.Count);
            foreach (var component in components)
            {
                if (component == null) throw new ArgumentException("The component list contains a null entry.", nameof(components));
                _components.Add(component);
            }
            _options = new RiskAnalysisOptions();
            _options.PropertyChanged += OptionsPropertyChanged;
        }

        /// <summary>
        /// Initializes a risk analysis from its serialized configuration, with the components
        /// and previously computed results supplied by the consuming layer — the BestFit
        /// constructor shape (configuration from XML; model and results through arguments).
        /// </summary>
        /// <param name="components">The system components the analysis owns, in declared order.</param>
        /// <param name="xElement">The serialized configuration produced by <see cref="ToXElement"/>.</param>
        /// <param name="riskResults">The previously computed summary ensemble, or null.</param>
        /// <param name="meanResults">The previously computed mean realization, or null.</param>
        /// <param name="medianResults">The previously computed median realization, or null.</param>
        /// <param name="lowerResults">The previously computed lower confidence realization, or null.</param>
        /// <param name="upperResults">The previously computed upper confidence realization, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when the component list or element is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the component list contains a null entry.</exception>
        public RiskAnalysis(IList<SystemComponent> components, XElement xElement,
            EnsembleResults? riskResults = null, SystemRealization? meanResults = null,
            SystemRealization? medianResults = null, SystemRealization? lowerResults = null,
            SystemRealization? upperResults = null)
            : this(components)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            Name = SerializationUtilities.ReadString(xElement, nameof(Name), "Risk Analysis");
            Description = SerializationUtilities.ReadString(xElement, nameof(Description));
            SpecifiedConsequence = SerializationUtilities.ReadString(xElement, nameof(SpecifiedConsequence));
            ConsequenceUnit = SerializationUtilities.ReadString(xElement, nameof(ConsequenceUnit));

            var optionsElement = xElement.Element(nameof(RiskAnalysisOptions));
            if (optionsElement != null)
            {
                Options = new RiskAnalysisOptions(optionsElement);
            }

            _riskResults = riskResults;
            _meanRiskResults = meanResults;
            _medianRiskResults = medianResults;
            _lowerRiskResults = lowerResults;
            _upperRiskResults = upperResults;
            _isEstimated = SerializationUtilities.ReadBoolean(xElement, nameof(IsEstimated)) && riskResults != null;
        }

        #endregion

        #region Members

        /// <summary>
        /// The engine's probability floor: the integration domain is
        /// [1e-16, 1 − 1e-16] in hazard non-exceedance probability (v1.0 constant).
        /// </summary>
        private const double ProbabilityFloor = 1e-16;

        /// <summary>
        /// The number of stratified hazard bins seeding the adaptive integrator (v1.0 constant).
        /// </summary>
        private const int HazardBinCount = 50;

        /// <summary>
        /// The owned components, in declared order.
        /// </summary>
        private readonly List<SystemComponent> _components;

        /// <summary>Backing field for <see cref="Options"/>.</summary>
        private RiskAnalysisOptions _options;

        /// <summary>Backing field for <see cref="Name"/>.</summary>
        private string _name = "Risk Analysis";

        /// <summary>Backing field for <see cref="Description"/>.</summary>
        private string _description = string.Empty;

        /// <summary>Backing field for <see cref="SpecifiedConsequence"/>.</summary>
        private string _specifiedConsequence = string.Empty;

        /// <summary>Backing field for <see cref="ConsequenceUnit"/>.</summary>
        private string _consequenceUnit = string.Empty;

        /// <summary>Backing field for <see cref="RiskResults"/>.</summary>
        private EnsembleResults? _riskResults;

        /// <summary>Backing field for <see cref="MeanRiskResults"/>.</summary>
        private SystemRealization? _meanRiskResults;

        /// <summary>Backing field for <see cref="MedianRiskResults"/>.</summary>
        private SystemRealization? _medianRiskResults;

        /// <summary>Backing field for <see cref="LowerRiskResults"/>.</summary>
        private SystemRealization? _lowerRiskResults;

        /// <summary>Backing field for <see cref="UpperRiskResults"/>.</summary>
        private SystemRealization? _upperRiskResults;

        /// <summary>Backing field for <see cref="ComputationWarnings"/>.</summary>
        private readonly List<string> _computationWarnings = new List<string>();

        /// <summary>
        /// The analysis display name. Metadata — serialized, stripped from the canonical hash.
        /// </summary>
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    RaisePropertyChange(nameof(Name));
                }
            }
        }

        /// <summary>
        /// The analysis description. Metadata — serialized, stripped from the canonical hash.
        /// </summary>
        public string Description
        {
            get { return _description; }
            set
            {
                if (_description != value)
                {
                    _description = value;
                    RaisePropertyChange(nameof(Description));
                }
            }
        }

        /// <summary>
        /// The consequence type label of the analysis results (e.g., "Life Loss"). Metadata.
        /// </summary>
        public string SpecifiedConsequence
        {
            get { return _specifiedConsequence; }
            set
            {
                if (_specifiedConsequence != value)
                {
                    _specifiedConsequence = value;
                    RaisePropertyChange(nameof(SpecifiedConsequence));
                }
            }
        }

        /// <summary>
        /// The consequence unit label of the analysis results (e.g., "lives"). Metadata.
        /// </summary>
        public string ConsequenceUnit
        {
            get { return _consequenceUnit; }
            set
            {
                if (_consequenceUnit != value)
                {
                    _consequenceUnit = value;
                    RaisePropertyChange(nameof(ConsequenceUnit));
                }
            }
        }

        /// <summary>
        /// The run options. Assigning replaces the subscription; any option change invalidates
        /// the current results. Assigning null coerces to fresh defaults.
        /// </summary>
        public RiskAnalysisOptions Options
        {
            get { return _options; }
            set
            {
                if (ReferenceEquals(_options, value)) return;
                if (_options != null) _options.PropertyChanged -= OptionsPropertyChanged;
                _options = value ?? new RiskAnalysisOptions();
                _options.PropertyChanged += OptionsPropertyChanged;
                IsEstimated = false;
                RaisePropertyChange(nameof(Options));
            }
        }

        /// <summary>
        /// The owned system components, in declared order.
        /// </summary>
        public IReadOnlyList<SystemComponent> Components => _components;

        /// <summary>
        /// The per-realization summary ensemble from the last run (one entry per realization; a
        /// mean-only run publishes a single-entry ensemble). Null until estimated.
        /// </summary>
        public EnsembleResults? RiskResults
        {
            get { return _riskResults; }
            private set
            {
                _riskResults = value;
                RaisePropertyChange(nameof(RiskResults));
            }
        }

        /// <summary>
        /// The mean results: the mean-only realization, or the ensemble mean curves from a full
        /// run. Null until estimated.
        /// </summary>
        public SystemRealization? MeanRiskResults
        {
            get { return _meanRiskResults; }
            private set
            {
                _meanRiskResults = value;
                RaisePropertyChange(nameof(MeanRiskResults));
            }
        }

        /// <summary>
        /// The ensemble median curves from a full run. Null until estimated or for mean-only runs.
        /// </summary>
        public SystemRealization? MedianRiskResults
        {
            get { return _medianRiskResults; }
            private set
            {
                _medianRiskResults = value;
                RaisePropertyChange(nameof(MedianRiskResults));
            }
        }

        /// <summary>
        /// The lower confidence-bound curves from a full run at the configured interval width.
        /// Null until estimated or for mean-only runs.
        /// </summary>
        public SystemRealization? LowerRiskResults
        {
            get { return _lowerRiskResults; }
            private set
            {
                _lowerRiskResults = value;
                RaisePropertyChange(nameof(LowerRiskResults));
            }
        }

        /// <summary>
        /// The upper confidence-bound curves from a full run at the configured interval width.
        /// Null until estimated or for mean-only runs.
        /// </summary>
        public SystemRealization? UpperRiskResults
        {
            get { return _upperRiskResults; }
            private set
            {
                _upperRiskResults = value;
                RaisePropertyChange(nameof(UpperRiskResults));
            }
        }

        /// <summary>
        /// The computational warnings raised by the last run (negative consequences clamped,
        /// mutually-exclusive probabilities normalized, exhaustive mass-balance drift) — the
        /// headless replacement for the v1.0 messenger surface.
        /// </summary>
        public IReadOnlyList<string> ComputationWarnings => _computationWarnings;

        #endregion

        #region IAnalysis Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Errors: no components; more than one component (until the system-risk stage, Phase
        /// 4b); reliability mode (until Phase 4c); any projected failure mode with more than one
        /// response stage (until the event-tree phase); invalid options; and every component's
        /// own errors, aggregated with the component name.
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var messages = new List<string>();

            messages.AddRange(_options.Validate().ValidationMessages);

            if (_components.Count == 0)
            {
                messages.Add("Error: The analysis has no system components.");
            }
            if (_components.Count > 1)
            {
                messages.Add($"Error: The analysis contains {_components.Count} system components; multi-component system risk aggregation is not available until the system-risk phase (Phase 4b) — a single system component is supported.");
            }
            if (_options.Mode == RiskAnalysisMode.Reliability)
            {
                messages.Add("Error: Reliability mode is not available until Phase 4c.");
            }

            for (int i = 0; i < _components.Count; i++)
            {
                var component = _components[i];
                foreach (string message in component.Validate().ValidationMessages)
                {
                    messages.Add($"{message} [{component.Name}]");
                }
                var modes = component.FailureModes;
                for (int m = 0; m < modes.Count; m++)
                {
                    if (modes[m].ResponseStages.Count > 1)
                    {
                        messages.Add($"Error: A failure mode of system component '{component.Name}' has {modes[m].ResponseStages.Count} response stages; multi-stage response composition is not yet supported by the risk engine — it lands with the event-tree phase.");
                    }
                }
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">Thrown when validation reports errors.</exception>
        /// <remarks>
        /// The run sequence (architecture doc §7.3): the cancelable starting event; the
        /// validation gate (which throws — an invalid analysis is a caller error, not a run
        /// outcome); a fresh cancellation source linked with the caller's token; occurrence-index
        /// assignment and the content-based per-component seed walk; then the mean-only pass or
        /// the parallel full-uncertainty ensemble with percentile post-processing. Runtime
        /// faults and cancellation surface through <see cref="IAnalysis.AnalysisCompleted"/>
        /// rather than propagating (the BestFit convention).
        /// </remarks>
        public override async Task RunAsync(SafeProgressReporter? progressReporter = null, CancellationToken cancellationToken = default)
        {
            var startingArgs = new CancelEventArgs();
            OnAnalysisStarting(startingArgs);
            if (startingArgs.Cancel)
            {
                OnAnalysisCompleted(new AnalysisRunCompletedEventArgs(wasCanceled: true, succeeded: false, error: null));
                return;
            }

            var (isValid, validationMessages) = Validate();
            if (!isValid)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, validationMessages));
            }

            var token = ResetCancellationToken(cancellationToken);
            IsEstimated = false;
            RiskResults = null;
            MeanRiskResults = null;
            MedianRiskResults = null;
            LowerRiskResults = null;
            UpperRiskResults = null;
            _computationWarnings.Clear();

            try
            {
                await Task.Run(() =>
                {
                    if (_options.UseDefaults)
                    {
                        _options.SetIntegrationDefaults(_components.Count);
                    }

                    // The content-based seed walk (architecture doc §5.5.4): occurrence indices
                    // disambiguate identical-content components, and each component's functions
                    // are seeded from (analysis seed, component hash, occurrence index).
                    SystemComponent.AssignOccurrenceIndices(_components);
                    for (int i = 0; i < _components.Count; i++)
                    {
                        int componentSeed = SeedHelpers.HashCombine(_options.PRNGSeed, _components[i].CanonicalHash(), _components[i].OccurrenceIndex);
                        _components[i].SetupSamplers(_options.Realizations, componentSeed, _options.SamplingScheme);
                    }

                    if (_options.EstimateMeanRiskOnly)
                    {
                        RunMeanOnly(progressReporter, token);
                    }
                    else
                    {
                        RunFullUncertainty(progressReporter, token);
                    }
                }, token).ConfigureAwait(false);

                IsEstimated = true;
                OnAnalysisCompleted(new AnalysisRunCompletedEventArgs(wasCanceled: false, succeeded: true, error: null));
            }
            catch (OperationCanceledException)
            {
                OnAnalysisCompleted(new AnalysisRunCompletedEventArgs(wasCanceled: true, succeeded: false, error: null));
            }
            catch (Exception ex)
            {
                OnAnalysisCompleted(new AnalysisRunCompletedEventArgs(wasCanceled: false, succeeded: false, error: ex));
            }
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes the analysis configuration: the metadata, the estimated flag, and the
        /// options child — nothing else (architecture doc §8). Components and results travel
        /// through the constructor; the consuming layer persists them separately.
        /// </summary>
        /// <returns>The serialized configuration.</returns>
        public XElement ToXElement()
        {
            var element = new XElement(nameof(RiskAnalysis));
            element.SetAttributeValue(nameof(Name), _name);
            element.SetAttributeValue(nameof(Description), _description);
            element.SetAttributeValue(nameof(SpecifiedConsequence), _specifiedConsequence);
            element.SetAttributeValue(nameof(ConsequenceUnit), _consequenceUnit);
            element.SetAttributeValue(nameof(IsEstimated), _isEstimated);
            element.Add(_options.ToXElement());
            return element;
        }

        #endregion

        #region Private Helpers — Run Paths

        /// <summary>
        /// Invalidates the results when any option changes.
        /// </summary>
        /// <param name="sender">The options instance.</param>
        /// <param name="e">The change arguments.</param>
        private void OptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            IsEstimated = false;
        }

        /// <summary>
        /// The mean-only pass: one realization on the expected input functions (mixture
        /// exposure branches enumerated, never flattened — §6.4.1), published as the mean
        /// results and a single-entry summary ensemble.
        /// </summary>
        /// <param name="progressReporter">The optional progress sink.</param>
        /// <param name="token">The run cancellation token.</param>
        private void RunMeanOnly(SafeProgressReporter? progressReporter, CancellationToken token)
        {
            var flags = new RiskComputeFlags();
            var realization = ComputeRealization(-1, flags, token);
            realization.Name = "Mean";
            CollectWarnings(flags);

            MeanRiskResults = realization;
            var ensemble = new EnsembleResults(1);
            ensemble[0] = new SystemRiskResults(realization);
            RiskResults = ensemble;
            progressReporter?.ReportProgress(100d);
        }

        /// <summary>
        /// The full-uncertainty pass: the parallel realization ensemble with index-owned writes,
        /// sequential post-pass reductions (bit-identical at any thread count), percentile
        /// post-processing, and the compact summary ensemble.
        /// </summary>
        /// <param name="progressReporter">The optional progress sink.</param>
        /// <param name="token">The run cancellation token.</param>
        private void RunFullUncertainty(SafeProgressReporter? progressReporter, CancellationToken token)
        {
            int realizationCount = _options.Realizations;
            var realizations = new SystemRealization[realizationCount];
            var summaries = new SystemRiskResults[realizationCount];
            var flagsPerRealization = new RiskComputeFlags[realizationCount];
            long completed = 0;

            Parallel.For(0, realizationCount, new ParallelOptions { CancellationToken = token }, index =>
            {
                var flags = new RiskComputeFlags();
                var realization = ComputeRealization(index, flags, token);
                realizations[index] = realization;
                summaries[index] = new SystemRiskResults(realization);
                flagsPerRealization[index] = flags;
                long done = Interlocked.Increment(ref completed);
                progressReporter?.ReportProgress(done * 100d / realizationCount);
            });

            // Sequential post-pass reductions: warning flags and ensemble extents. In-loop
            // shared updates would be racy (the v1.0 defect) and break thread-count bit-identity.
            var mergedFlags = new RiskComputeFlags();
            for (int i = 0; i < realizationCount; i++)
            {
                mergedFlags.MergeWith(flagsPerRealization[i]);
            }
            CollectWarnings(mergedFlags);

            PostProcessUncertainty(realizations, token);

            var ensemble = new EnsembleResults(realizationCount);
            for (int i = 0; i < realizationCount; i++)
            {
                ensemble[i] = summaries[i];
            }
            RiskResults = ensemble;
        }

        /// <summary>
        /// Translates the merged computational flags into the run's warning surface.
        /// </summary>
        /// <param name="flags">The merged flags.</param>
        private void CollectWarnings(RiskComputeFlags flags)
        {
            if (flags.HasNegativeFailureConsequence)
                _computationWarnings.Add("Warning: Negative failure consequences were computed and set to zero.");
            if (flags.HasNegativeNonFailureConsequence)
                _computationWarnings.Add("Warning: Negative non-failure consequences were computed and set to zero.");
            if (flags.HasNegativeExcessConsequence)
                _computationWarnings.Add("Warning: Negative excess consequences were computed and set to zero.");
            if (flags.HasProbabilityGreaterThanOne)
                _computationWarnings.Add("Warning: Mutually exclusive failure mode probabilities summed above one and were normalized.");
            RaisePropertyChange(nameof(ComputationWarnings));

            // Exhaustive mass-balance drift surfaces here instead of a silent clamp (§7.7).
            var mean = _meanRiskResults ?? null;
            if (mean != null)
            {
                CheckMassBalance(mean);
            }
        }

        /// <summary>
        /// Raises a warning when an exhaustive curve's recorded mass drifted more than 1e-6 from
        /// one (the silent v1.0 clamp made leaks invisible).
        /// </summary>
        /// <param name="realization">The realization to inspect.</param>
        private void CheckMassBalance(SystemRealization realization)
        {
            if (realization.Curves.Total.LECConsequences.Length > 0 && Math.Abs(realization.Curves.Total.MassBalance - 1d) > 1e-6)
            {
                _computationWarnings.Add($"Warning: The total risk curve's recorded probability mass was {realization.Curves.Total.MassBalance:G6} instead of 1.");
            }
        }

        #endregion

        #region Private Helpers — Realization Compute

        /// <summary>
        /// Computes one full realization: per component, the adaptive Gauss–Kronrod pass over
        /// the hazard probability domain records the risk points, then the exact curves,
        /// profiles, and risk measures are built. The single-component system curves are the
        /// component curves (v1.0 behavior); multi-component aggregation lands in Phase 4b.
        /// </summary>
        /// <param name="realizationIndex">The realization index, or −1 for the mean pass.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="token">The run cancellation token.</param>
        /// <returns>The computed realization.</returns>
        private SystemRealization ComputeRealization(int realizationIndex, RiskComputeFlags flags, CancellationToken token)
        {
            var componentRealizations = new List<ComponentRealization>(_components.Count);
            var sampledComponents = new SampledComponent[_components.Count];
            for (int i = 0; i < _components.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                sampledComponents[i] = _components[i].Sample(realizationIndex);
                componentRealizations.Add(new ComponentRealization(sampledComponents[i].FailureModeCount)
                {
                    Name = _components[i].Name,
                });
            }
            var realization = new SystemRealization(componentRealizations);

            for (int i = 0; i < _components.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                IntegrateComponent(sampledComponents[i], componentRealizations[i], realization, flags);
                componentRealizations[i].ProcessHazardProbabilities();
                componentRealizations[i].CreateCurves(_options.LECOutputLength);
                componentRealizations[i].CreateProfiles();
                componentRealizations[i].ComputeRiskMeasures(_options.ConsequenceThreshold, _options.Alpha, _components[i].HazardThreshold);

                realization.MinN = Math.Min(realization.MinN, componentRealizations[i].MinN);
                realization.MaxN = Math.Max(realization.MaxN, componentRealizations[i].MaxN);
                realization.MinH[i] = componentRealizations[i].MinH;
                realization.MaxH[i] = componentRealizations[i].MaxH;
            }

            // Single-component system results are the component results (v1.0 behavior at the
            // legacy clone site); Phase 4b replaces this with the aggregation methods.
            realization.Curves = componentRealizations[0].Curves.Clone();

            realization.DumpMemory();
            return realization;
        }

        /// <summary>
        /// Runs the adaptive Gauss–Kronrod pass for one component: the integrator is an
        /// importance sampler whose recorded evaluations populate the risk points; its returned
        /// value is discarded and its evaluation count and true error estimate are kept as
        /// diagnostics.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="componentRealization">The component's realization sink.</param>
        /// <param name="realization">The system realization (diagnostics).</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        private void IntegrateComponent(SampledComponent sampled, ComponentRealization componentRealization,
            SystemRealization realization, RiskComputeFlags flags)
        {
            var objective = BuildObjective(sampled, componentRealization, flags);
            var integrator = new AdaptiveGaussKronrod(objective, ProbabilityFloor, 1d - ProbabilityFloor)
            {
                ReportFailure = false,
                MaxFunctionEvaluations = _options.MaxEvaluations,
                MaxDepth = _options.MaxDepth,
                RelativeTolerance = _options.Tolerance,
                MinDepth = 2,
            };
            integrator.Integrate(BuildStratificationBins(sampled, flags));

            realization.FunctionEvaluations += integrator.FunctionEvaluations;
            realization.StandardError += integrator.StandardError / _components.Count;
        }

        /// <summary>
        /// Builds the integrand for the selected refinement objective (architecture doc §7.7).
        /// Every evaluation computes and records the full component risk regardless of the
        /// objective — the objective changes only where the adaptive refinement concentrates.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="componentRealization">The component's realization sink.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <returns>The integrand over hazard non-exceedance probability.</returns>
        private Func<double, double> BuildObjective(SampledComponent sampled, ComponentRealization componentRealization,
            RiskComputeFlags flags)
        {
            if (_options.RiskIntegrand == RiskIntegrand.Balanced)
            {
                double meanScale = ObjectiveScale(sampled, flags, RiskIntegrand.MeanTotalRisk);
                double secondScale = ObjectiveScale(sampled, flags, RiskIntegrand.SecondMoment);
                double tailScale = ObjectiveScale(sampled, flags, RiskIntegrand.TailConditionalRisk);
                return p =>
                {
                    var output = Evaluate(sampled, componentRealization, flags, p, recordOutput: true);
                    return ObjectiveValue(output, p, RiskIntegrand.MeanTotalRisk) / meanScale
                        + ObjectiveValue(output, p, RiskIntegrand.SecondMoment) / secondScale
                        + ObjectiveValue(output, p, RiskIntegrand.TailConditionalRisk) / tailScale;
                };
            }

            var integrand = _options.RiskIntegrand;
            return p =>
            {
                var output = Evaluate(sampled, componentRealization, flags, p, recordOutput: true);
                return ObjectiveValue(output, p, integrand);
            };
        }

        /// <summary>
        /// Evaluates the component risk at one hazard probability.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="componentRealization">The component's realization sink.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="probability">The hazard non-exceedance probability.</param>
        /// <param name="recordOutput">True to record risk points.</param>
        /// <returns>The component risk output.</returns>
        private static ComponentRiskOutput Evaluate(SampledComponent sampled, ComponentRealization componentRealization,
            RiskComputeFlags flags, double probability, bool recordOutput)
        {
            double hazardLevel = sampled.Hazard.InverseCDF(probability);
            return sampled.ComputeRisk(probability, hazardLevel, flags, componentRealization, recordOutput);
        }

        /// <summary>
        /// The refinement-objective value at one evaluation (architecture doc §7.7 table).
        /// </summary>
        /// <param name="output">The component risk output at the evaluation point.</param>
        /// <param name="probability">The hazard non-exceedance probability.</param>
        /// <param name="integrand">The objective member (never <see cref="RiskIntegrand.Balanced"/> here).</param>
        /// <returns>The objective value.</returns>
        private double ObjectiveValue(ComponentRiskOutput output, double probability, RiskIntegrand integrand)
        {
            switch (integrand)
            {
                case RiskIntegrand.MeanIncrementalRisk:
                    return output.ProbabilityOfFailure * output.MeanExcessConsequences;
                case RiskIntegrand.TotalProbabilityOfFailure:
                    return output.ProbabilityOfFailure;
                case RiskIntegrand.TailConditionalRisk:
                    return probability <= _options.Alpha
                        ? output.ProbabilityOfFailure * output.MeanFailureConsequences
                        : 0d;
                case RiskIntegrand.ThresholdExceedanceProbability:
                {
                    double exceedance = 0d;
                    for (int i = 0; i < output.ResponseProbabilities.Count; i++)
                    {
                        if (output.FailureConsequences[i] > _options.ConsequenceThreshold) exceedance += output.ResponseProbabilities[i];
                    }
                    if (output.NonFailureConsequences > _options.ConsequenceThreshold) exceedance += output.ProbabilityOfNonFailure;
                    return exceedance;
                }
                case RiskIntegrand.SecondMoment:
                {
                    double secondMoment = 0d;
                    for (int i = 0; i < output.ResponseProbabilities.Count; i++)
                    {
                        secondMoment += output.ResponseProbabilities[i] * output.FailureConsequences[i] * output.FailureConsequences[i];
                    }
                    secondMoment += output.ProbabilityOfNonFailure * output.NonFailureConsequences * output.NonFailureConsequences;
                    return secondMoment;
                }
                default:
                    return output.ProbabilityOfFailure * output.MeanFailureConsequences
                        + output.ProbabilityOfNonFailure * output.NonFailureConsequences;
            }
        }

        /// <summary>
        /// Estimates a normalization scale for one of the balanced objective's members from a
        /// non-recording pre-pass over the stratification-bin edges; a vanishing scale falls
        /// back to one so the balanced sum stays finite.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="integrand">The objective member to scale.</param>
        /// <returns>The positive normalization scale.</returns>
        private double ObjectiveScale(SampledComponent sampled, RiskComputeFlags flags, RiskIntegrand integrand)
        {
            var scratch = new ComponentRealization(sampled.FailureModeCount);
            var bins = BuildStratificationBins(sampled, flags);
            double scale = 0d;
            for (int i = 0; i < bins.Count; i++)
            {
                var output = Evaluate(sampled, scratch, flags, bins[i].LowerBound, recordOutput: false);
                scale += Math.Abs(ObjectiveValue(output, bins[i].LowerBound, integrand));
            }
            var last = Evaluate(sampled, scratch, flags, bins[bins.Count - 1].UpperBound, recordOutput: false);
            scale += Math.Abs(ObjectiveValue(last, bins[bins.Count - 1].UpperBound, integrand));
            return scale > 0d ? scale : 1d;
        }

        /// <summary>
        /// Builds the stratified hazard bins seeding the adaptive integrator (50 hazard-space
        /// bins mapped to probability — v1.0 constants), injecting the discontinuity of a
        /// discontinuous objective as a bin boundary so no quadrature panel spans the jump
        /// (§7.7): the tail objective switches at p = α, and the threshold objective crosses
        /// where the consequence passes the threshold (located by Brent's method on this
        /// realization's chain when a sign change brackets it).
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="flags">The realization's computational-warning flags (the threshold probe evaluates the chain).</param>
        /// <returns>The stratification bins in probability space.</returns>
        private List<StratificationBin> BuildStratificationBins(SampledComponent sampled, RiskComputeFlags flags)
        {
            var bins = Stratify.XValues(new StratificationOptions(
                sampled.Hazard.InverseCDF(ProbabilityFloor), sampled.Hazard.InverseCDF(1d - ProbabilityFloor), HazardBinCount), true);
            bins = Stratify.XToProbability(bins, sampled.Hazard.CDF, false);

            if (_options.RiskIntegrand == RiskIntegrand.TailConditionalRisk)
            {
                InjectBoundary(bins, _options.Alpha);
            }
            else if (_options.RiskIntegrand == RiskIntegrand.ThresholdExceedanceProbability)
            {
                var scratch = new ComponentRealization(sampled.FailureModeCount);
                double Crossing(double p)
                {
                    var output = Evaluate(sampled, scratch, flags, p, recordOutput: false);
                    return output.MeanFailureConsequences - _options.ConsequenceThreshold;
                }
                double lower = bins[0].LowerBound;
                double upper = bins[bins.Count - 1].UpperBound;
                double atLower = Crossing(lower);
                double atUpper = Crossing(upper);
                if (atLower * atUpper < 0d)
                {
                    double root = Brent.Solve(Crossing, lower, upper);
                    InjectBoundary(bins, root);
                }
            }
            return bins;
        }

        /// <summary>
        /// Splits the bin containing the boundary probability into two bins meeting at it; a
        /// boundary outside the bin range (or already on an edge) is a no-op.
        /// </summary>
        /// <param name="bins">The probability-space bins, in order.</param>
        /// <param name="boundary">The boundary probability to inject.</param>
        private static void InjectBoundary(List<StratificationBin> bins, double boundary)
        {
            for (int i = 0; i < bins.Count; i++)
            {
                if (boundary > bins[i].LowerBound && boundary < bins[i].UpperBound)
                {
                    var lower = new StratificationBin(bins[i].LowerBound, boundary);
                    var upper = new StratificationBin(boundary, bins[i].UpperBound);
                    bins.RemoveAt(i);
                    bins.Insert(i, upper);
                    bins.Insert(i, lower);
                    return;
                }
            }
        }

        #endregion

        #region Private Helpers — Percentile Post-Processing

        /// <summary>
        /// Builds the mean, median, and confidence-bound curve sets from the realization
        /// ensemble: loss exceedance percentiles on a shared consequence grid (the Total
        /// percentile curve is read from each realization's Total curve — the v1.0
        /// failure-plus-non-failure reconstruction is deleted), and hazard-profile percentiles
        /// on per-component hazard grids. Percentile assembly parallelizes over grid ordinates
        /// with index-owned writes; means are summed sequentially per ordinate.
        /// </summary>
        /// <param name="realizations">The realization ensemble.</param>
        /// <param name="token">The run cancellation token.</param>
        private void PostProcessUncertainty(SystemRealization[] realizations, CancellationToken token)
        {
            int realizationCount = realizations.Length;
            if (realizationCount == 0) return;
            int componentCount = _components.Count;
            double tail = (1d - _options.ConfidenceIntervalWidth) / 2d;

            // Sequential extent reduction across the ensemble.
            double minN = double.MaxValue;
            double maxN = double.MinValue;
            var minH = new double[componentCount];
            var maxH = new double[componentCount];
            for (int d = 0; d < componentCount; d++)
            {
                minH[d] = double.MaxValue;
                maxH[d] = double.MinValue;
            }
            for (int i = 0; i < realizationCount; i++)
            {
                minN = Math.Min(minN, realizations[i].MinN);
                maxN = Math.Max(maxN, realizations[i].MaxN);
                for (int d = 0; d < componentCount; d++)
                {
                    minH[d] = Math.Min(minH[d], realizations[i].MinH[d]);
                    maxH[d] = Math.Max(maxH[d], realizations[i].MaxH[d]);
                }
            }
            if (!(maxN > minN) || double.IsInfinity(minN) || double.IsInfinity(maxN)) return;

            var lower = CreatePercentileRealization("Lower", componentCount, realizations[0]);
            var upper = CreatePercentileRealization("Upper", componentCount, realizations[0]);
            var median = CreatePercentileRealization("Median", componentCount, realizations[0]);
            var mean = CreatePercentileRealization("Mean", componentCount, realizations[0]);

            // The shared consequence grid, descending (v1.0 orientation).
            double gridMin = minN < 1d ? 0d : minN;
            var consequenceGrid = BuildDescendingGrid(gridMin, maxN, _options.LECOutputLength);

            // System and component LEC percentile curves for the five risk types.
            AssembleLecPercentiles(realizations, r => r.Curves, c => new[] { lower, upper, median, mean }[c].Curves, consequenceGrid, tail, token);
            for (int d = 0; d < componentCount; d++)
            {
                int componentIndex = d;
                AssembleLecPercentiles(realizations,
                    r => r.Components[componentIndex].Curves,
                    c => new[] { lower, upper, median, mean }[c].Components[componentIndex].Curves,
                    consequenceGrid, tail, token);

                int modeCount = realizations[0].Components[componentIndex].FailureModes.Count;
                for (int m = 0; m < modeCount; m++)
                {
                    int modeIndex = m;
                    AssembleLecPercentiles(realizations,
                        r => r.Components[componentIndex].FailureModes[modeIndex].Curves,
                        c => new[] { lower, upper, median, mean }[c].Components[componentIndex].FailureModes[modeIndex].Curves,
                        consequenceGrid, tail, token);
                }

                // Hazard-profile percentiles on the component's hazard grid.
                if (maxH[d] > minH[d])
                {
                    var hazardGrid = BuildDescendingGrid(minH[d], maxH[d], _options.LECOutputLength);
                    AssembleProfilePercentiles(realizations, componentIndex, hazardGrid, tail,
                        new[] { lower, upper, median, mean }, token);
                }
            }

            LowerRiskResults = lower;
            UpperRiskResults = upper;
            MedianRiskResults = median;
            MeanRiskResults = mean;
        }

        /// <summary>
        /// Creates an empty percentile realization shaped like the ensemble's realizations.
        /// </summary>
        /// <param name="name">The realization label.</param>
        /// <param name="componentCount">The component count.</param>
        /// <param name="template">A realization supplying the per-component failure-mode counts.</param>
        /// <returns>The shaped realization.</returns>
        private SystemRealization CreatePercentileRealization(string name, int componentCount, SystemRealization template)
        {
            var components = new List<ComponentRealization>(componentCount);
            for (int d = 0; d < componentCount; d++)
            {
                components.Add(new ComponentRealization(template.Components[d].FailureModes.Count)
                {
                    Name = template.Components[d].Name,
                });
            }
            return new SystemRealization(components) { Name = name };
        }

        /// <summary>
        /// Builds a descending linear grid over [minimum, maximum] with the given ordinate count.
        /// </summary>
        /// <param name="minimum">The grid minimum.</param>
        /// <param name="maximum">The grid maximum.</param>
        /// <param name="count">The ordinate count (at least two).</param>
        /// <returns>The descending grid.</returns>
        private static double[] BuildDescendingGrid(double minimum, double maximum, int count)
        {
            var grid = new double[count];
            double step = (maximum - minimum) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                grid[i] = maximum - i * step;
            }
            return grid;
        }

        /// <summary>
        /// Assembles the five risk-type LEC percentile curves onto the target curve sets: at
        /// each grid consequence, the realizations' exceedance probabilities are interpolated
        /// (log-log — v1.0 behavior), sorted for the percentile levels, and summed sequentially
        /// for the mean.
        /// </summary>
        /// <param name="realizations">The realization ensemble.</param>
        /// <param name="source">Selects the source curve set from a realization.</param>
        /// <param name="target">Selects the target curve set by percentile slot (0 lower, 1 upper, 2 median, 3 mean).</param>
        /// <param name="consequenceGrid">The shared descending consequence grid.</param>
        /// <param name="tail">The percentile tail level, (1 − width)/2.</param>
        /// <param name="token">The run cancellation token.</param>
        private static void AssembleLecPercentiles(SystemRealization[] realizations,
            Func<SystemRealization, Curves> source, Func<int, Curves> target,
            double[] consequenceGrid, double tail, CancellationToken token)
        {
            AssemblePercentileCurve(realizations, r => source(r).Excess.LEC, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).Excess.LECConsequences = x; target(slot).Excess.LECProbabilities = y; });
            AssemblePercentileCurve(realizations, r => source(r).Background.LEC, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).Background.LECConsequences = x; target(slot).Background.LECProbabilities = y; });
            AssemblePercentileCurve(realizations, r => source(r).Total.LEC, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).Total.LECConsequences = x; target(slot).Total.LECProbabilities = y; });
            AssemblePercentileCurve(realizations, r => source(r).Fail.LEC, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).Fail.LECConsequences = x; target(slot).Fail.LECProbabilities = y; });
            AssemblePercentileCurve(realizations, r => source(r).NonFail.LEC, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).NonFail.LECConsequences = x; target(slot).NonFail.LECProbabilities = y; });
        }

        /// <summary>
        /// Assembles one percentile curve family: per grid ordinate (parallel, index-owned), the
        /// realizations' interpolated values are sorted for the lower/upper/median levels and
        /// summed sequentially for the mean; realizations whose source curve is empty are
        /// skipped, and an all-empty family leaves the targets empty.
        /// </summary>
        /// <param name="realizations">The realization ensemble.</param>
        /// <param name="curve">Selects the source curve view from a realization.</param>
        /// <param name="grid">The descending X grid.</param>
        /// <param name="tail">The percentile tail level.</param>
        /// <param name="token">The run cancellation token.</param>
        /// <param name="assign">Assigns the assembled arrays per percentile slot (0 lower, 1 upper, 2 median, 3 mean).</param>
        private static void AssemblePercentileCurve(SystemRealization[] realizations,
            Func<SystemRealization, OrderedPairedData> curve, double[] grid, double tail, CancellationToken token,
            Action<int, double[], double[]> assign)
        {
            int realizationCount = realizations.Length;
            bool anySource = false;
            for (int i = 0; i < realizationCount; i++)
            {
                if (curve(realizations[i]).Count > 1)
                {
                    anySource = true;
                    break;
                }
            }
            if (!anySource) return;

            var lowerValues = new double[grid.Length];
            var upperValues = new double[grid.Length];
            var medianValues = new double[grid.Length];
            var meanValues = new double[grid.Length];

            Parallel.For(0, grid.Length, new ParallelOptions { CancellationToken = token }, g =>
            {
                var values = new double[realizationCount];
                double sum = 0d;
                int used = 0;
                for (int i = 0; i < realizationCount; i++)
                {
                    var source = curve(realizations[i]);
                    if (source.Count < 2) continue;
                    double value = source.GetYFromX(grid[g], Transform.Logarithmic, Transform.Logarithmic);
                    if (double.IsNaN(value)) continue;
                    values[used] = value;
                    sum += value;
                    used++;
                }
                if (used == 0) return;
                Array.Sort(values, 0, used);
                var window = new double[used];
                Array.Copy(values, window, used);
                lowerValues[g] = Statistics.Percentile(window, tail, true);
                upperValues[g] = Statistics.Percentile(window, 1d - tail, true);
                medianValues[g] = Statistics.Percentile(window, 0.5d, true);
                meanValues[g] = sum / used;
            });

            assign(0, (double[])grid.Clone(), lowerValues);
            assign(1, (double[])grid.Clone(), upperValues);
            assign(2, (double[])grid.Clone(), medianValues);
            assign(3, (double[])grid.Clone(), meanValues);
        }

        /// <summary>
        /// Assembles the hazard-frequency and conditional-consequence profile percentiles for
        /// one component onto the four percentile realizations' Total streams (the reporting
        /// profiles — v1.0 scope).
        /// </summary>
        /// <param name="realizations">The realization ensemble.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="hazardGrid">The component's descending hazard grid.</param>
        /// <param name="tail">The percentile tail level.</param>
        /// <param name="targets">The percentile realizations (0 lower, 1 upper, 2 median, 3 mean).</param>
        /// <param name="token">The run cancellation token.</param>
        private static void AssembleProfilePercentiles(SystemRealization[] realizations, int componentIndex,
            double[] hazardGrid, double tail, SystemRealization[] targets, CancellationToken token)
        {
            AssemblePercentileCurve(realizations,
                r => r.Components[componentIndex].Curves.Total.HazardFrequency, hazardGrid, tail, token,
                (slot, x, y) =>
                {
                    targets[slot].Components[componentIndex].Curves.Total.HazardFrequencyHazards = x;
                    targets[slot].Components[componentIndex].Curves.Total.HazardFrequencyProbabilities = y;
                });
            AssemblePercentileCurve(realizations,
                r => r.Components[componentIndex].Curves.Total.HazardvsCEN, hazardGrid, tail, token,
                (slot, x, y) =>
                {
                    targets[slot].Components[componentIndex].Curves.Total.HazardVsCenHazards = x;
                    targets[slot].Components[componentIndex].Curves.Total.HazardVsCenConsequences = y;
                });
        }

        #endregion
    }
}
