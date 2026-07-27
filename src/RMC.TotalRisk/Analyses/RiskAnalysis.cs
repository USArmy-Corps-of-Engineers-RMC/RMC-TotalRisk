using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Numerics;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using Numerics.Mathematics;
using Numerics.Mathematics.Integration;
using Numerics.Mathematics.LinearAlgebra;
using Numerics.Mathematics.RootFinding;
using Numerics.Mathematics.SpecialFunctions;
using Numerics.Sampling;
using Numerics.Utilities;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Analyses
{
    /// <summary>
    /// The Monte Carlo risk analysis: content-seeded knowledge-uncertainty sampling over the
    /// owned system components, adaptive Gaussâ€“Kronrod integration of the risk integrand, exact
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
    /// <c>AdaptiveGaussKronrod</c> (G10K21) used as an adaptive sampler â€” the returned integral
    /// is discarded and the recorded risk points are the product, with the refinement objective
    /// selected by <see cref="RiskAnalysisOptions.RiskIntegrand"/>; curves are built exactly from
    /// the recorded (mass, consequence) pairs; and seeding is content-based
    /// (architecture doc Â§5.5.4): per-component seeds derive from the analysis seed, the
    /// component's canonical hash, and its occurrence index â€” never from canvas order, so
    /// renaming, moving, or reordering components can never change results, and results are
    /// bit-identical at any thread count (every parallel write is index-owned; ensemble
    /// reductions run in a sequential post-pass, and percentile means are summed sequentially
    /// rather than with parallel reductions â€” a deliberate determinism-over-throughput choice).
    /// </para>
    /// <para>
    /// <b>Ownership and persistence (architecture doc Â§8):</b> components and results arrive
    /// through the constructor (the BestFit analysis shape) and <see cref="ToXElement"/> writes
    /// the analysis metadata, the estimated flag, and the options only. Results are JSON
    /// containers; the consuming layer persists them separately.
    /// </para>
    /// <para>
    /// <b>Multi-component system risk (Phase 4b):</b> the additive method assumes strictly
    /// independent components (ratified v0.13 â€” a supplied dependence is a validation error) and
    /// builds the true system loss exceedance curves by zero-inflated lattice convolution
    /// (<see cref="SystemConvolution"/>) â€” the exact enumeration of all component
    /// failure/non-failure combinations, where v1.0 combined two moments and produced no system
    /// curve at all; the reported stream probabilities keep the v1.0 system-state semantics
    /// (failure union, its complement). The joint method integrates the correlated hazard
    /// hypercube with VEGAS, enumerating real within-component pathway/branch entries across the
    /// component combinations instead of collapsing each component to its conditional mean (the
    /// documented v1.0 system-tail defect), accumulating recorded points across five recording
    /// passes with the masses self-normalized so the exhaustive budget is exactly one, and
    /// exposing the VEGAS power-transform tail focus (Î³) with an automatic heuristic driven by a
    /// deterministic per-component failure-probability quadrature probe.
    /// </para>
    /// <para>
    /// <b>Reliability mode (Phase 4c):</b> <see cref="RiskAnalysisMode.Reliability"/> computes
    /// annualized failure probability only: consequence functions become optional (the relaxed
    /// mode-aware validation chain), the adaptive refinement objective is forced to
    /// <see cref="RiskIntegrand.TotalProbabilityOfFailure"/> (the configured objective applies to
    /// risk mode), and the annualized failure probability is read as the Fail stream's total
    /// probability at every level â€” failure mode, component, and system (the same containers
    /// serve both modes; a consequence-free model's curves are degenerate at zero consequence by
    /// construction).
    /// </para>
    /// <para>
    /// <b>Declared consequence-type axis (Phase 6.5, user-ratified):</b> the analysis declares
    /// its ordered consequence types â€” the primary through
    /// <see cref="SpecifiedConsequence"/>/<see cref="ConsequenceUnit"/> and every additional
    /// position through <see cref="AdditionalConsequenceTypes"/> â€” and validation strictly
    /// matches each component's failure and non-failure paths against the declaration: counts
    /// and order always, labels and units whenever both sides are non-blank (blank is a
    /// wildcard). The declaration is label metadata: it can never enter a canonical hash, so it
    /// never gates or rewires compute for a valid model â€” validity may depend on metadata
    /// consistency while results identity depends only on compute-relevant content.
    /// </para>
    /// <para>
    /// <b>Stage gates:</b> multi-stage response composition remains a validation error until the
    /// event-tree phase, with the message naming the stage.
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
        /// and previously computed results supplied by the consuming layer â€” the BestFit
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

            var typesElement = xElement.Element(nameof(AdditionalConsequenceTypes));
            if (typesElement != null)
            {
                foreach (var child in typesElement.Elements(nameof(ConsequenceTypeDescriptor)))
                {
                    _additionalConsequenceTypes.Add(new ConsequenceTypeDescriptor(child));
                }
            }

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
        /// [1e-16, 1 âˆ’ 1e-16] in hazard non-exceedance probability (v1.0 constant).
        /// </summary>
        private const double ProbabilityFloor = 1e-16;

        /// <summary>
        /// The number of stratified hazard bins seeding the adaptive integrator (v1.0 constant).
        /// </summary>
        private const int HazardBinCount = 50;

        /// <summary>
        /// The number of independent VEGAS recording passes the joint path accumulates loss
        /// exceedance points across (v1.0 recorded a single pass â€” far too sparse for a tail
        /// ordinate in D dimensions; ratified v0.13). The recorded masses are self-normalized by
        /// the realized weight sum, so a pass count truncated by the evaluation cap stays
        /// consistent.
        /// </summary>
        private const int VegasRecordingPasses = 5;

        /// <summary>
        /// The joint path's advisory guardrail on the product of the components' worst-case
        /// recorded entry widths (the system combination cross product).
        /// </summary>
        private const long JointEntryWarningLimit = 4096;

        /// <summary>
        /// The joint path's invalidating guardrail on the product of the components' worst-case
        /// recorded entry widths.
        /// </summary>
        private const long JointEntryErrorLimit = 65_536;

        /// <summary>
        /// The salt distinguishing the joint path's VEGAS driving stream from the component
        /// sampler streams in the content-based seed derivation ("VEGAS" in ASCII).
        /// </summary>
        private static readonly byte[] JointStreamSalt = { 0x56, 0x45, 0x47, 0x41, 0x53 };

        /// <summary>
        /// The owned components, in declared order.
        /// </summary>
        private readonly List<SystemComponent> _components;

        /// <summary>
        /// The correlated component-hazard latent structure for the joint method (v1.0
        /// off-diagonal constants; identity under independence). Run-scoped runtime state â€”
        /// rebuilt by every run, never serialized, never hashed.
        /// </summary>
        private MultivariateNormal? _jointMultivariateNormal;

        /// <summary>
        /// The lower Cholesky factor of the joint hazard covariance, extracted once per run so
        /// the VEGAS integrand applies the latent transform in place instead of allocating
        /// through <see cref="MultivariateNormal.InverseCDF(double[])"/> on every evaluation
        /// (Phase 6.5; the in-place loop replicates the Numerics matrixâ€“vector accumulation
        /// order exactly, so the recorded stream is bit-identical). Run-scoped runtime state.
        /// </summary>
        private double[,]? _jointCholeskyLower;

        /// <summary>
        /// The run's content-derived base seed for the VEGAS driving stream: the analysis seed
        /// folded with every component's canonical hash and occurrence index in declared order.
        /// Run-scoped runtime state.
        /// </summary>
        private int _jointSeedBase;

        /// <summary>
        /// The automatic tail-focus target probability harvested by the run's deterministic
        /// failure-probability quadrature probe (the smallest component annualized failure
        /// probability times the exceedance level, clamped to [1e-12, 1e-2]). Run-scoped runtime
        /// state.
        /// </summary>
        private double _jointTailTargetProbability = 1e-2;

        /// <summary>
        /// The additive convolution order: component indices sorted by canonical hash, so the
        /// sequential pairwise convolution associates identically however the components are
        /// declared â€” reordering components can never move the system curves by
        /// association-rounding. Run-scoped runtime state.
        /// </summary>
        private int[]? _additiveConvolutionOrder;

        /// <summary>
        /// The declared per-type consequence thresholds for the additional consequence types
        /// (Phase 6.6), captured once per run from the descriptors â€” entry k applies to
        /// additional type k at every risk-measure site. Run-scoped runtime state.
        /// </summary>
        private double[]? _runAdditionalThresholds;

        /// <summary>
        /// The effective sampler seed map the last run resolved (Phase 6.6, §5.5.8) — the
        /// baseline a perturbation study pins onto its perturbed runs via
        /// <see cref="PinnedSamplerSeeds"/>. Populated by every <see cref="RunAsync"/>; null
        /// before the first run. Runtime-only — never serialized, never hashed.
        /// </summary>
        public SamplerSeedMap? CapturedSamplerSeeds { get; private set; }

        /// <summary>
        /// An optional pinned sampler seed map (Phase 6.6, §5.5.8 — the seed-stable
        /// perturbation mode): when set, the next run replaces every content-derived sampler
        /// seed — per walk ordinal, including the coupling positions and the joint VEGAS seed
        /// base — with the captured value, so a small numeric perturbation cannot re-roll the
        /// Monte Carlo streams and result deltas are pure parameter effects. The map must fit
        /// the model's walk shape (validated loudly). Runtime-only — never serialized, never
        /// hashed, never part of any identity surface; clear it to restore content-based
        /// seeding.
        /// </summary>
        /// <remarks>
        /// Workflow: run the baseline → read <see cref="CapturedSamplerSeeds"/> → perturb the
        /// parameter under study → assign the captured map here → run → difference the
        /// results. Documented residual: if the perturbation flips the canonical-hash order of
        /// components, the additive convolution re-associates at the last bit.
        /// </remarks>
        public SamplerSeedMap? PinnedSamplerSeeds { get; set; }

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

        /// <summary>Backing field for <see cref="AdditionalConsequenceTypes"/>.</summary>
        private readonly ObservableCollection<ConsequenceTypeDescriptor> _additionalConsequenceTypes =
            new ObservableCollection<ConsequenceTypeDescriptor>();

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
        /// The analysis display name. Metadata â€” serialized, stripped from the canonical hash.
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
        /// The analysis description. Metadata â€” serialized, stripped from the canonical hash.
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
        /// The declared consequence types beyond the primary, in order: entry k âˆ’ 1 declares
        /// consequence-type position k of the analysis's axis (position 0 is the
        /// <see cref="SpecifiedConsequence"/>/<see cref="ConsequenceUnit"/> pair). Empty declares
        /// the legacy single-type axis. Validation strictly matches every component's failure and
        /// non-failure paths against the declared axis in risk mode; reliability mode carries no
        /// consequences and ignores it. Metadata â€” serialized, never hashed.
        /// </summary>
        public ObservableCollection<ConsequenceTypeDescriptor> AdditionalConsequenceTypes => _additionalConsequenceTypes;

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
        /// mutually-exclusive probabilities normalized, exhaustive mass-balance drift) â€” the
        /// headless replacement for the v1.0 messenger surface.
        /// </summary>
        public IReadOnlyList<string> ComputationWarnings => _computationWarnings;

        #endregion

        #region IAnalysis Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Errors: no components; the additive method with a component-hazard dependence (the
        /// ratified v0.13 strict-independence redefinition â€” dependence belongs to the joint
        /// method); the joint method above twenty components (the VEGAS dimension limit), with a
        /// missing, mis-shaped, or non-positive-definite correlation matrix under the
        /// correlation-matrix dependency, or with a combination cross product beyond the
        /// guardrail; any failure or non-failure path that does not carry the declared
        /// consequence-type axis (count and order always; labels and units when both sides are
        /// non-blank â€” risk mode only); any projected failure mode with more than one response
        /// stage (until the event-tree phase); invalid options; and every component's own errors,
        /// aggregated with the component name. Component validation runs mode-aware: reliability
        /// relaxes exactly the consequence-content requirements (Phase 4c). Advisory: components
        /// whose driving hazards disagree on non-blank axis labels warn â€” one analysis models one
        /// hazard axis.
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
                if (_options.SystemRiskMethod == SystemRiskType.AdditiveRiskMethod)
                {
                    if (_options.ComponentHazardDependency != DependencyType.Independent)
                    {
                        messages.Add("Error: The additive system risk method assumes strictly independent components; select the joint method to model cross-component hazard dependence.");
                    }
                }
                else
                {
                    if (_options.ComponentHazardDependency == DependencyType.CorrelationMatrix &&
                        !IsHazardCorrelationMatrixValid())
                    {
                        messages.Add($"Error: The component hazard correlation matrix must be a positive-definite {_components.Count}Ã—{_components.Count} matrix (one row per component).");
                    }

                    // The system-level combination guardrail: the joint integrand crosses the
                    // components' recorded entry lists, so the product of their worst-case widths
                    // bounds the work per evaluation.
                    long entryProduct = 1;
                    for (int i = 0; i < _components.Count && entryProduct <= JointEntryErrorLimit; i++)
                    {
                        entryProduct *= _components[i].EstimateRecordedFailureEntries();
                    }
                    if (entryProduct > JointEntryErrorLimit)
                    {
                        messages.Add($"Error: The joint system combination cross product exceeds {JointEntryErrorLimit} entries per evaluation (the product of the components' pathway/branch widths); reduce the mixture branch counts or failure mode counts.");
                    }
                    else if (entryProduct > JointEntryWarningLimit)
                    {
                        messages.Add($"Warning: The joint system combination cross product ({entryProduct}) exceeds {JointEntryWarningLimit} entries per evaluation; the enumeration grows compute cost accordingly.");
                    }
                }
            }

            for (int i = 0; i < _components.Count; i++)
            {
                var component = _components[i];
                foreach (string message in component.Validate(_options.Mode).ValidationMessages)
                {
                    messages.Add($"{message} [{component.Name}]");
                }
            }

            ValidateConsequenceTypeAxis(messages);
            ValidateHazardAxisConsistency(messages);
            messages.AddRange(EstimateResourceRequirements().Messages(ResourceSeverity.Warning));

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <summary>
        /// Estimates what this run will cost before it starts: the memory it will hold and the
        /// work that dominates it, line by line.
        /// </summary>
        /// <returns>The estimate.</returns>
        /// <remarks>
        /// <para>
        /// Peak LIVE memory, not total allocation — a long ensemble run churns far more than it
        /// holds at any instant. <see cref="Validate"/> folds the warning and error lines into its
        /// messages, so a configuration that cannot run says which option to change rather than
        /// failing later with an allocation exception.
        /// </para>
        /// <para>
        /// Estimates only. The recorded-point figure follows the adaptive integrator, whose
        /// evaluation count is bounded only by <see cref="RiskAnalysisOptions.MaxEvaluations"/>,
        /// and the combination figures assume the inclusion-exclusion bracket does not converge —
        /// the pessimistic end of a range whose usual case is far cheaper.
        /// </para>
        /// </remarks>
        public ResourceEstimate EstimateResourceRequirements()
        {
            const long bytesPerDouble = 8;
            const long bytesPerInt = 4;
            var items = new List<ResourceEstimateItem>();

            int componentCount = _components.Count;
            int typeCount = 1 + _additionalConsequenceTypes.Count;
            int realizations = _options.EstimateMeanRiskOnly ? 1 : _options.Realizations;
            int concurrency = Math.Max(1, Math.Min(Environment.ProcessorCount, realizations));
            bool joint = componentCount > 1 && _options.SystemRiskMethod == SystemRiskType.JointRiskMethod;

            // The joint system's exclusive combination enumeration, per evaluation in flight.
            if (joint)
            {
                if (componentCount > VegasMaxDimensions)
                {
                    items.Add(new ResourceEstimateItem("Joint system dimension", 0, componentCount, false, ResourceSeverity.Error,
                        $"Error: The joint system risk method integrates over one dimension per component and supports at most {VegasMaxDimensions}; the analysis has {componentCount}. Use the additive system risk method, which convolves the components' loss distributions and carries no dimension limit, or group the components."));
                }

                long cap = Math.Max(1, _options.MaxSystemCombinations);
                long enumerated = componentCount >= 62 ? cap : Math.Min(cap, (1L << componentCount));
                long bytes = enumerated * (bytesPerDouble + componentCount * bytesPerInt + 24) * concurrency;

                // An upper bound only. The inclusion-exclusion bracket normally closes far short
                // of the cap, and whether it does depends on the failure probabilities the run
                // encounters, which is not knowable here — the run reports actual truncation.
                items.Add(new ResourceEstimateItem("Joint system combination enumeration", bytes, enumerated, false,
                    ResourceSeverity.Informational,
                    $"The joint system enumerates at most {enumerated:N0} exclusive component combinations per evaluation, holding about {FormatBytes(bytes)}."));
            }

            // Per-component structures: the joint method's 2^U indicator matrix, the correlation
            // matrix, and the dependent competing-risk pre-processing.
            double genzEvaluations = 0d;
            for (int i = 0; i < componentCount; i++)
            {
                var component = _components[i];
                int units = component.CombinationUnitCountForEstimate();
                if (units <= 0) continue;

                if (component.FailureModeMethod == FailureModeMethod.JointFailures)
                {
                    if (units >= 31)
                    {
                        items.Add(new ResourceEstimateItem($"Combination matrix — {component.Name}", 0, 0, false, ResourceSeverity.Error,
                            $"Error: System component '{component.Name}' combines {units} failure paths under the joint failure-mode method, which enumerates every subset of them and cannot exceed 30. Use the Common Cause, Mutually Exclusive, or Competing method, which combine marginally and carry no such limit, or group the failure paths."));
                    }
                    else
                    {
                        long matrixBytes = (long)units * ((1L << units) - 1) * bytesPerInt;
                        var severity = matrixBytes > 512L * 1024 * 1024 ? ResourceSeverity.Error
                            : matrixBytes > 64L * 1024 * 1024 ? ResourceSeverity.Warning
                            : ResourceSeverity.Informational;
                        items.Add(new ResourceEstimateItem($"Combination matrix — {component.Name}", matrixBytes, 0, true, severity,
                            severity == ResourceSeverity.Informational
                                ? $"System component '{component.Name}' holds a {FormatBytes(matrixBytes)} combination matrix over {units} failure paths."
                                : $"{(severity == ResourceSeverity.Error ? "Error" : "Warning")}: System component '{component.Name}' combines {units} failure paths under the joint failure-mode method, whose combination matrix needs {FormatBytes(matrixBytes)}. Use the Common Cause, Mutually Exclusive, or Competing method, which combine marginally and allocate nothing here, or group the failure paths."));
                    }
                }

                if (component.FailureModeDependency != DependencyType.Independent)
                {
                    items.Add(new ResourceEstimateItem($"Correlation matrix — {component.Name}",
                        (long)units * units * bytesPerDouble, 0, true, ResourceSeverity.Informational,
                        $"System component '{component.Name}' holds a {units}×{units} correlation matrix."));
                }

                // The dependent competing branches evaluate a multivariate-normal rectangle
                // integral per unit per hazard level, once per realization unless the component is
                // deterministic and the run can share one pre-processing.
                bool dependentCompeting = component.FailureModeMethod == FailureModeMethod.CompetingFailures
                    && units > 1
                    && component.FailureModeDependency != DependencyType.Independent
                    && component.FailureModeDependency != DependencyType.PerfectlyPositive;
                if (dependentCompeting)
                {
                    double passes = component.IsDeterministic ? 1d : realizations;
                    double evaluations = passes * units * (CompetingIncidenceBins + 1d);
                    genzEvaluations += evaluations;
                    var severity = evaluations > 5e6 ? ResourceSeverity.Warning : ResourceSeverity.Informational;
                    items.Add(new ResourceEstimateItem($"Competing-risk pre-processing — {component.Name}", 0, evaluations, false, severity,
                        severity == ResourceSeverity.Informational
                            ? $"System component '{component.Name}' evaluates {evaluations:N0} multivariate-normal rectangle integrals building its incidence functions."
                            : $"Warning: System component '{component.Name}' evaluates {evaluations:N0} multivariate-normal rectangle integrals building its incidence functions, once per realization because it carries knowledge uncertainty. Reduce Realizations, use the Independent or Perfectly Positive failure-mode dependency, or remove the uncertainty from its hazard and fragilities so the run can build them once."));
                }
            }

            // Recorded risk points per realization in flight, and the stored ensemble.
            double evaluationFloor = (double)realizations * componentCount * HazardBinCount * GaussKronrodNodes
                * (1L << (_options.EnsembleMinDepth + 1));
            long streams = 5 + 2L * MaxFailureModeCount();
            long pointBytes = (long)(HazardBinCount * GaussKronrodNodes) * streams * typeCount * 96 * concurrency;
            items.Add(new ResourceEstimateItem("Recorded risk points", pointBytes, evaluationFloor, false, ResourceSeverity.Informational,
                $"Realizations in flight hold about {FormatBytes(pointBytes)} of recorded risk points."));

            long ensembleBytes = (long)realizations * componentCount * typeCount * streams
                * _options.LECOutputLength * bytesPerDouble * 2;
            var ensembleSeverity = ensembleBytes > 8L * 1024 * 1024 * 1024 ? ResourceSeverity.Error
                : ensembleBytes > 2L * 1024 * 1024 * 1024 ? ResourceSeverity.Warning
                : ResourceSeverity.Informational;
            items.Add(new ResourceEstimateItem("Stored ensemble", ensembleBytes, 0, true, ensembleSeverity,
                ensembleSeverity == ResourceSeverity.Informational
                    ? $"The stored ensemble holds about {FormatBytes(ensembleBytes)}."
                    : $"{(ensembleSeverity == ResourceSeverity.Error ? "Error" : "Warning")}: The stored ensemble needs about {FormatBytes(ensembleBytes)} for {realizations:N0} realizations at an output length of {_options.LECOutputLength}. Reduce Realizations or LECOutputLength, or set EstimateMeanRiskOnly."));

            if (componentCount > 1 && _options.SystemRiskMethod == SystemRiskType.AdditiveRiskMethod)
            {
                long latticeBytes = (long)_options.SystemConvolutionPoints * (componentCount + 1) * bytesPerDouble * concurrency;
                items.Add(new ResourceEstimateItem("System convolution lattice", latticeBytes, 0, false, ResourceSeverity.Informational,
                    $"The additive system convolution holds about {FormatBytes(latticeBytes)} of lattice."));
            }

            return new ResourceEstimate(items, concurrency, evaluationFloor, genzEvaluations);
        }

        /// <summary>
        /// The Gauss–Kronrod node count per accepted interval (G10K21).
        /// </summary>
        private const int GaussKronrodNodes = 21;

        /// <summary>
        /// The mean dropped mass above which a truncated combination enumeration is reported as an
        /// error rather than a warning.
        /// </summary>
        private const double TruncatedCombinationResidualLimit = 1e-3;

        /// <summary>
        /// The largest dimension the VEGAS integrator accepts, and therefore the largest component
        /// count the joint system risk method can carry. The additive method has no equivalent
        /// limit.
        /// </summary>
        /// <remarks>
        /// VEGAS stratifies (calls/2)^(1/D) strata per axis, which reaches one around fifteen
        /// components; beyond that the joint method is adaptive importance sampling over the
        /// correlated hazards, which stays correct but resolves the tail less sharply per
        /// evaluation.
        /// </remarks>
        private const int VegasMaxDimensions = 50;

        /// <summary>
        /// The stratified hazard levels the competing-risks incidence pre-processing spans.
        /// </summary>
        private const int CompetingIncidenceBins = 200;

        /// <summary>
        /// The largest projected failure-mode count across the analysis's components, at least one.
        /// </summary>
        /// <returns>The failure-mode count.</returns>
        private long MaxFailureModeCount()
        {
            long most = 1;
            for (int i = 0; i < _components.Count; i++)
            {
                int count = _components[i].FailureModes.Count;
                if (count > most) most = count;
            }
            return most;
        }

        /// <summary>
        /// Formats a byte count for a caller-facing message.
        /// </summary>
        /// <param name="bytes">The byte count.</param>
        /// <returns>The formatted size.</returns>
        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024d * 1024d):F1} GB";
            if (bytes >= 1024L * 1024) return $"{bytes / (1024d * 1024d):F0} MB";
            if (bytes >= 1024) return $"{bytes / 1024d:F0} KB";
            return $"{bytes} bytes";
        }

        /// <summary>
        /// Strictly matches every component's failure and non-failure paths against the declared
        /// consequence-type axis (Phase 6.5, user-ratified): each path must carry exactly one
        /// consequence function per declared type, in declared order, and a non-blank declared
        /// label or unit must agree (ordinal, case-insensitive) with a non-blank function label
        /// at the same position â€” blank on either side is a wildcard. Risk mode only: a
        /// reliability model carries no consequences, so the axis is inert there. Null function
        /// entries are skipped here â€” they are already component-level errors.
        /// </summary>
        /// <param name="messages">The message sink.</param>
        private void ValidateConsequenceTypeAxis(List<string> messages)
        {
            if (_options.Mode != RiskAnalysisMode.Risk) return;

            int declaredCount = 1 + _additionalConsequenceTypes.Count;
            var labels = new string[declaredCount];
            var units = new string[declaredCount];
            labels[0] = _specifiedConsequence;
            units[0] = _consequenceUnit;
            for (int k = 1; k < declaredCount; k++)
            {
                labels[k] = _additionalConsequenceTypes[k - 1].SpecifiedConsequence;
                units[k] = _additionalConsequenceTypes[k - 1].ConsequenceUnit;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                var component = _components[i];
                var modes = component.FailureModes;
                for (int m = 0; m < modes.Count; m++)
                {
                    string pathName = modes[m].IsNonFailureMode ? "the non-failure path" : "a failure path";
                    var consequences = modes[m].ConsequenceFunctions;
                    if (consequences.Count != declaredCount)
                    {
                        messages.Add($"Error: On {pathName} of system component '{component.Name}', {consequences.Count} consequence function(s) are carried but the analysis declares {declaredCount} consequence type(s); every failure and non-failure path must carry the declared consequence-type axis.");
                        continue;
                    }
                    for (int k = 0; k < declaredCount; k++)
                    {
                        var function = consequences[k];
                        if (function is null) continue;
                        if (LabelsMismatch(labels[k], function.SpecifiedConsequence))
                        {
                            messages.Add($"Error: The consequence function at position {k} of {pathName} of system component '{component.Name}' is typed '{function.SpecifiedConsequence}' but the analysis declares '{labels[k]}' at that position.");
                        }
                        if (LabelsMismatch(units[k], function.ConsequenceUnit))
                        {
                            messages.Add($"Error: The consequence function at position {k} of {pathName} of system component '{component.Name}' has unit '{function.ConsequenceUnit}' but the analysis declares '{units[k]}' at that position.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Determines whether two axis labels disagree: both must be non-blank and differ under
        /// an ordinal case-insensitive comparison (the graph alignment comparison semantics) â€”
        /// blank on either side is a wildcard.
        /// </summary>
        /// <param name="declared">The declared axis label.</param>
        /// <param name="actual">The function's label.</param>
        /// <returns>True when both are non-blank and differ.</returns>
        private static bool LabelsMismatch(string declared, string actual)
        {
            return !string.IsNullOrEmpty(declared) && !string.IsNullOrEmpty(actual) &&
                !string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Warns when the components' driving hazards disagree on non-blank axis labels: one
        /// analysis models one hazard axis (the hazard-type companion of the declared
        /// consequence-type axis), so a label mismatch usually means a mis-assembled system.
        /// Advisory only â€” labels are unhashed display metadata.
        /// </summary>
        /// <param name="messages">The message sink.</param>
        private void ValidateHazardAxisConsistency(List<string> messages)
        {
            if (_components.Count < 2) return;

            string label = string.Empty, unit = string.Empty, labelOwner = string.Empty, unitOwner = string.Empty;
            for (int i = 0; i < _components.Count; i++)
            {
                var hazard = _components[i].HazardFunction;
                if (hazard == null) continue;
                if (!string.IsNullOrEmpty(hazard.SpecifiedHazard))
                {
                    if (string.IsNullOrEmpty(label))
                    {
                        label = hazard.SpecifiedHazard;
                        labelOwner = _components[i].Name;
                    }
                    else if (!string.Equals(label, hazard.SpecifiedHazard, StringComparison.OrdinalIgnoreCase))
                    {
                        messages.Add($"Warning: System component '{_components[i].Name}' drives on hazard '{hazard.SpecifiedHazard}' but component '{labelOwner}' drives on '{label}'; the components of one analysis should share the driving hazard axis.");
                    }
                }
                if (!string.IsNullOrEmpty(hazard.HazardUnit))
                {
                    if (string.IsNullOrEmpty(unit))
                    {
                        unit = hazard.HazardUnit;
                        unitOwner = _components[i].Name;
                    }
                    else if (!string.Equals(unit, hazard.HazardUnit, StringComparison.OrdinalIgnoreCase))
                    {
                        messages.Add($"Warning: System component '{_components[i].Name}' hazard unit '{hazard.HazardUnit}' differs from component '{unitOwner}' unit '{unit}'; the components of one analysis should share the driving hazard axis.");
                    }
                }
            }
        }

        /// <summary>
        /// Determines whether the options' hazard correlation matrix is usable for the joint
        /// method: present, one row per component, and positive definite (Cholesky) â€” the same
        /// check the component applies to its failure-mode matrix.
        /// </summary>
        /// <returns>True when the matrix is usable.</returns>
        private bool IsHazardCorrelationMatrixValid()
        {
            var matrix = _options.HazardCorrelationMatrix;
            if (matrix == null || matrix.GetLength(0) != _components.Count || matrix.GetLength(1) != _components.Count)
            {
                return false;
            }
            try
            {
                return new CholeskyDecomposition(new Matrix(matrix)).IsPositiveDefinite;
            }
            catch (Exception)
            {
                // A decomposition failure means the matrix is not usable â€” exactly what this
                // check reports; the validation message carries the remedy.
                return false;
            }
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">Thrown when validation reports errors.</exception>
        /// <remarks>
        /// The run sequence (architecture doc Â§7.3): the cancelable starting event; the
        /// validation gate (which throws â€” an invalid analysis is a caller error, not a run
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
            if (PinnedSamplerSeeds != null && PinnedSamplerSeeds.ComponentCount != _components.Count)
            {
                throw new InvalidOperationException(
                    "The pinned sampler seed map was captured from a different component count. The seed-stable perturbation mode fits the model shape it was captured from — re-capture from a baseline run of the current structure.");
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

                    // The content-based seed walk (architecture doc Â§5.5.4): occurrence indices
                    // disambiguate identical-content components, and each component's functions
                    // are seeded from (analysis seed, component hash, occurrence index).
                    SystemComponent.AssignOccurrenceIndices(_components);
                    var pinned = PinnedSamplerSeeds;
                    var capturedSeeds = new List<int[]>(_components.Count);
                    var contentHashes = new byte[_components.Count][];
                    for (int i = 0; i < _components.Count; i++)
                    {
                        contentHashes[i] = _components[i].CanonicalHash();
                        int componentSeed = SeedHelpers.HashCombine(_options.PRNGSeed, contentHashes[i], _components[i].OccurrenceIndex);
                        var scribe = new SeedScribe(pinned?.ComponentSeeds[i]);
                        capturedSeeds.Add(_components[i].SetupSamplers(_options.Realizations, componentSeed, _options.SamplingScheme, scribe));
                    }

                    // The canonical component order (hashes sorted): the additive convolution
                    // associates in it, and the system seed base folds in it (Â§7.3 erratum) â€”
                    // so declaration order can never move the convolved curves or the VEGAS
                    // stream identity.
                    var order = new int[_components.Count];
                    for (int i = 0; i < order.Length; i++) order[i] = i;
                    Array.Sort(order, (a, b) => ByteArrayComparer.Instance.Compare(contentHashes[a], contentHashes[b]));
                    _additiveConvolutionOrder = order;

                    int systemSeed = _options.PRNGSeed;
                    for (int i = 0; i < order.Length; i++)
                    {
                        systemSeed = SeedHelpers.HashCombine(systemSeed, contentHashes[order[i]], _components[order[i]].OccurrenceIndex);
                    }
                    _jointSeedBase = pinned?.JointSeedBase ?? systemSeed;

                    // Every run captures its effective seed map (§5.5.8) — the baseline a
                    // perturbation study pins onto its perturbed runs. Capturing an applied map
                    // reproduces it, so capture(apply(map)) is the map itself.
                    CapturedSamplerSeeds = new SamplerSeedMap(capturedSeeds, _jointSeedBase);

                    // The declared per-type consequence thresholds (Phase 6.6): entry k applies
                    // to additional consequence type k at every measure site this run.
                    _runAdditionalThresholds = new double[_additionalConsequenceTypes.Count];
                    for (int i = 0; i < _runAdditionalThresholds.Length; i++)
                    {
                        _runAdditionalThresholds[i] = _additionalConsequenceTypes[i].ConsequenceThreshold;
                    }

                    PrepareJointSystem(token);

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
        /// options child â€” nothing else (architecture doc Â§8). Components and results travel
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
            var typesElement = new XElement(nameof(AdditionalConsequenceTypes));
            for (int i = 0; i < _additionalConsequenceTypes.Count; i++)
            {
                typesElement.Add(_additionalConsequenceTypes[i].ToXElement());
            }
            element.Add(typesElement);
            return element;
        }

        #endregion

        #region Private Helpers â€” Run Paths

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
        /// exposure branches enumerated, never flattened â€” Â§6.4.1), published as the mean
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

            // The scalar-measure percentile summary and convergence diagnostics (Phase 6.6):
            // curves carry bands through the percentile realizations; the scalar catalog gets
            // its intervals here, reduced from the stored per-realization summaries.
            ensemble.Summary = ensemble.ComputeSummary(_options.ConfidenceIntervalWidth);
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
            if (flags.HasExcessiveTruncatedMass)
                _computationWarnings.Add($"Error: The joint system's combination enumeration reached the {_options.MaxSystemCombinations:N0}-combination cap and dropped more than {TruncatedCombinationResidualLimit:P1} of the exclusive probability mass onto the all-components-fail combination, which distorts the system tail. Raise MaxSystemCombinations, or model fewer components under the joint method.");
            else if (flags.HasTruncatedCombinationEnumeration)
                _computationWarnings.Add($"Warning: The joint system's combination enumeration reached the {_options.MaxSystemCombinations:N0}-combination cap on some evaluations; the deepest combinations were not enumerated and their mass was attributed to the all-components-fail combination. Raise MaxSystemCombinations to enumerate further.");
            RaisePropertyChange(nameof(ComputationWarnings));

            // Exhaustive mass-balance drift surfaces here instead of a silent clamp (Â§7.7).
            var mean = _meanRiskResults ?? null;
            if (mean != null)
            {
                CheckMassBalance(mean);
            }
        }

        /// <summary>
        /// Raises a warning when an exhaustive curve's recorded mass drifted more than 1e-6 from
        /// one (the silent v1.0 clamp made leaks invisible), on every consequence type.
        /// Reliability mode skips the check â€” a consequence-free model's total stream is
        /// degenerate at zero consequence by design, so its recorded mass measures the failure
        /// probability, not a leak.
        /// </summary>
        /// <param name="realization">The realization to inspect.</param>
        private void CheckMassBalance(SystemRealization realization)
        {
            if (_options.Mode == RiskAnalysisMode.Reliability) return;
            if (realization.Curves.Total.LECConsequences.Length > 0 && Math.Abs(realization.Curves.Total.MassBalance - 1d) > 1e-6)
            {
                _computationWarnings.Add($"Warning: The total risk curve's recorded probability mass was {realization.Curves.Total.MassBalance:G6} instead of 1.");
            }
            for (int k = 0; k < realization.AdditionalCurves.Count; k++)
            {
                var total = realization.AdditionalCurves[k].Total;
                if (total.LECConsequences.Length > 0 && Math.Abs(total.MassBalance - 1d) > 1e-6)
                {
                    _computationWarnings.Add($"Warning: The total risk curve's recorded probability mass for consequence type {k + 1} was {total.MassBalance:G6} instead of 1.");
                }
            }
        }

        #endregion

        #region Sensitivity Analysis

        /// <summary>
        /// Computes the sensitivity of one stored scalar risk measure to every knowledge input
        /// (Phase 6.6): the per-realization measure values already persisted in
        /// <see cref="RiskResults"/> are correlated against the per-function percentile draws,
        /// re-derived bit-exactly from the content seeds — no re-simulation, no integration.
        /// </summary>
        /// <param name="outputMeasure">The scalar measure to explain.</param>
        /// <param name="riskType">The risk-type stream the measure is read from.</param>
        /// <param name="measure">The association measure to report.</param>
        /// <param name="componentIndex">
        /// The output scope: −1 for the overall system (inputs = every component's knowledge
        /// columns), or a component position (inputs = that component's columns only — other
        /// components' draws are independent of its results by construction).
        /// </param>
        /// <param name="failureModeIndex">
        /// Narrows the output to one failure mode's summaries (Excess and Fail streams only);
        /// −1 for the component or system scope. Requires a component scope.
        /// </param>
        /// <param name="consequenceType">The consequence-type position (0 is the primary).</param>
        /// <returns>
        /// The labeled associations in the sampler walk order, or null when no full-uncertainty
        /// results are stored, the scope's outputs are unavailable, fewer than three valid
        /// realization pairs remain after NaN filtering, or the scope has no knowledge inputs.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an out-of-range scope argument.</exception>
        /// <exception cref="ArgumentException">Thrown for a failure-mode scope with a stream other than Excess or Fail.</exception>
        /// <remarks>
        /// The inputs pair with the stored outputs through the content seeds, so the model must
        /// be unchanged since the run (the same guarantee every diagnostic over stored results
        /// carries). The call re-runs the component sampler setup at the ensemble size; a
        /// subsequent <see cref="RunAsync"/> re-seeds itself at run start.
        /// </remarks>
        public SensitivityResults? MeasureSensitivity(RiskMeasure outputMeasure, RiskType riskType, SensitivityMeasure measure,
            int componentIndex = -1, int failureModeIndex = -1, int consequenceType = 0)
        {
            ValidateSensitivityScope(componentIndex, failureModeIndex, riskType, consequenceType);
            var results = RiskResults;
            if (!IsEstimated || results == null || results.Count < 2) return null;

            int count = results.Count;
            var outputs = new double[count];
            for (int i = 0; i < count; i++)
            {
                var summary = results[i];
                var scope = summary == null ? null : SelectScope(summary, componentIndex, failureModeIndex, riskType, consequenceType);
                outputs[i] = scope == null ? double.NaN : ExtractMeasure(scope, outputMeasure);
            }

            var inputs = BuildSensitivityInputs(componentIndex, count);
            if (inputs.Count == 0) return null;
            string scopeLabel = componentIndex < 0
                ? "System"
                : failureModeIndex < 0 ? _components[componentIndex].Name : $"{_components[componentIndex].Name} mode {failureModeIndex + 1}";
            return Correlate(inputs, outputs, measure, riskType, $"{outputMeasure} — {riskType} — {scopeLabel}");
        }

        /// <summary>
        /// Computes <see cref="MeasureSensitivity"/> for the full scalar-measure catalog in one
        /// pass, reusing one input-matrix derivation across all ten measures.
        /// </summary>
        /// <param name="riskType">The risk-type stream the measures are read from.</param>
        /// <param name="measure">The association measure to report.</param>
        /// <param name="componentIndex">The output scope (see <see cref="MeasureSensitivity"/>).</param>
        /// <param name="failureModeIndex">The failure-mode narrowing (see <see cref="MeasureSensitivity"/>).</param>
        /// <param name="consequenceType">The consequence-type position (0 is the primary).</param>
        /// <returns>One result per measure that produced valid associations; empty when none did.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an out-of-range scope argument.</exception>
        /// <exception cref="ArgumentException">Thrown for a failure-mode scope with a stream other than Excess or Fail.</exception>
        public IReadOnlyList<SensitivityResults> MeasureSensitivityMatrix(RiskType riskType, SensitivityMeasure measure,
            int componentIndex = -1, int failureModeIndex = -1, int consequenceType = 0)
        {
            ValidateSensitivityScope(componentIndex, failureModeIndex, riskType, consequenceType);
            var matrix = new List<SensitivityResults>();
            var results = RiskResults;
            if (!IsEstimated || results == null || results.Count < 2) return matrix;

            int count = results.Count;
            var inputs = BuildSensitivityInputs(componentIndex, count);
            if (inputs.Count == 0) return matrix;

            var outputs = new double[count];
            foreach (RiskMeasure outputMeasure in Enum.GetValues<RiskMeasure>())
            {
                for (int i = 0; i < count; i++)
                {
                    var summary = results[i];
                    var scope = summary == null ? null : SelectScope(summary, componentIndex, failureModeIndex, riskType, consequenceType);
                    outputs[i] = scope == null ? double.NaN : ExtractMeasure(scope, outputMeasure);
                }
                string scopeLabel = componentIndex < 0
                    ? "System"
                    : failureModeIndex < 0 ? _components[componentIndex].Name : $"{_components[componentIndex].Name} mode {failureModeIndex + 1}";
                var result = Correlate(inputs, outputs, measure, riskType, $"{outputMeasure} — {riskType} — {scopeLabel}");
                if (result != null) matrix.Add(result);
            }
            return matrix;
        }

        /// <summary>
        /// Computes the sensitivity of the risk at one hazard level to every knowledge input
        /// (the tornado diagnostic, Phase 6.6): a dedicated content-seeded design (default 100
        /// realizations) evaluates the failure-mode combination decomposition at the level —
        /// one evaluation per realization, no integration — and correlates the weighted
        /// expected consequence for the risk type against the percentile draws.
        /// </summary>
        /// <param name="componentIndex">The component whose risk is decomposed.</param>
        /// <param name="hazardLevel">
        /// The hazard level — interpreted on the component's selected profile hazard axis when
        /// one is set (each realization inverts its own sampled profile chain back to the
        /// driving hazard; user-ratified), the raw driving axis otherwise.
        /// </param>
        /// <param name="measure">The association measure to report.</param>
        /// <param name="riskType">The risk type whose expected consequence is the output.</param>
        /// <param name="realizations">The dedicated design size (a fresh stratified design — not the ensemble's rows).</param>
        /// <param name="consequenceType">The consequence-type position (0 is the primary).</param>
        /// <returns>
        /// The labeled associations in the sampler walk order, or null when the analysis is
        /// invalid (the v1.0 contract), the component is deterministic, or no knowledge inputs
        /// exist.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an out-of-range component index, consequence type, or a design below three realizations.</exception>
        /// <remarks>
        /// Content-seeded — the design derives from (analysis seed, component hash, occurrence
        /// index), never a wall clock — and re-runs the component's sampler setup at the design
        /// size (a subsequent <see cref="RunAsync"/> re-seeds itself). The legacy hazard bin
        /// weight is preserved: the sampled hazard's probability mass over ±(range/200) around
        /// the level, with the tail masses at the domain ends. When a profile element is
        /// selected, every transform on its chain must declare an ordered output axis — the
        /// per-realization inversion reads the sampled curves' <c>InverseFunction</c>, which
        /// requires monotone outputs (rating curves are; an unordered declaration faults the
        /// query loudly rather than inverting ambiguously).
        /// </remarks>
        public SensitivityResults? HazardLevelSensitivity(int componentIndex, double hazardLevel, SensitivityMeasure measure,
            RiskType riskType, int realizations = 100, int consequenceType = 0)
        {
            if (componentIndex < 0 || componentIndex >= _components.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(componentIndex), "The component index is out of range.");
            }
            if (realizations < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(realizations), "The sensitivity design needs at least three realizations.");
            }
            if (consequenceType < 0 || consequenceType > _additionalConsequenceTypes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(consequenceType), "The consequence-type position is not declared.");
            }
            if (!Validate().IsValid) return null;
            var component = _components[componentIndex];
            if (component.IsDeterministic) return null;

            SystemComponent.AssignOccurrenceIndices(_components);
            int seed = SeedHelpers.HashCombine(_options.PRNGSeed, component.CanonicalHash(), component.OccurrenceIndex);
            component.SetupSamplers(realizations, seed, _options.SamplingScheme);
            var inputs = new List<SensitivityInput>();
            component.CollectSensitivityInputs(inputs);
            if (inputs.Count == 0) return null;

            var outputs = new double[realizations];
            Parallel.For(0, realizations, index =>
            {
                var flags = new RiskComputeFlags();
                var sampled = component.Sample(index);
                var scratch = new ComponentRealization(sampled.FailureModeCount);
                scratch.EnsureAdditionalCurves(sampled.ConsequenceTypeCount - 1);
                double raw = sampled.InverseProfileHazard(hazardLevel);

                // The legacy hazard bin weight on this realization's sampled hazard.
                double minHazard = sampled.Hazard.InverseCDF(ProbabilityFloor);
                double maxHazard = sampled.Hazard.InverseCDF(1d - ProbabilityFloor);
                double dx = (maxHazard - minHazard) / 200d;
                double weight;
                if (raw <= minHazard)
                {
                    weight = sampled.Hazard.CDF(minHazard + dx);
                }
                else if (raw >= maxHazard)
                {
                    weight = 1d - sampled.Hazard.CDF(maxHazard - dx);
                }
                else
                {
                    weight = sampled.Hazard.CDF(raw + dx) - sampled.Hazard.CDF(raw - dx);
                }

                var typeOutputs = new ComponentRiskOutput[sampled.ConsequenceTypeCount];
                sampled.ComputeRisk(0.5d, raw, flags, scratch, recordOutput: false, typeOutputs);
                var output = typeOutputs[consequenceType];
                double expectedFailure = output.ProbabilityOfFailure * output.MeanFailureConsequences;
                double expectedExcess = output.ProbabilityOfFailure * output.MeanExcessConsequences;
                double nonFailure = output.NonFailureConsequences;
                double nonFailureProbability = output.ProbabilityOfNonFailure;
                outputs[index] = riskType switch
                {
                    RiskType.Excess => weight * expectedExcess,
                    RiskType.Background => weight * nonFailure,
                    RiskType.Total => weight * (expectedFailure + nonFailureProbability * nonFailure),
                    RiskType.Fail => weight * expectedFailure,
                    _ => weight * nonFailureProbability * nonFailure,
                };
            });

            return Correlate(inputs, outputs, measure, riskType,
                $"Risk at {hazardLevel.ToString("G6", CultureInfo.InvariantCulture)} — {riskType} — {component.Name}");
        }

        /// <summary>
        /// Validates a measure-sensitivity scope: index bounds and the failure-mode stream
        /// restriction (mode summaries carry Excess and Fail only).
        /// </summary>
        /// <param name="componentIndex">The component scope (−1 = system).</param>
        /// <param name="failureModeIndex">The mode narrowing (−1 = none).</param>
        /// <param name="riskType">The requested stream.</param>
        /// <param name="consequenceType">The consequence-type position.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for out-of-range indices.</exception>
        /// <exception cref="ArgumentException">Thrown for a mode scope with a stream other than Excess or Fail.</exception>
        private void ValidateSensitivityScope(int componentIndex, int failureModeIndex, RiskType riskType, int consequenceType)
        {
            if (componentIndex < -1 || componentIndex >= _components.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(componentIndex), "The component index is out of range.");
            }
            if (failureModeIndex >= 0 && componentIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failureModeIndex), "A failure-mode scope requires a component scope.");
            }
            if (failureModeIndex >= 0 && riskType != RiskType.Excess && riskType != RiskType.Fail)
            {
                throw new ArgumentException("Failure-mode summaries carry the Excess and Fail streams only.", nameof(riskType));
            }
            if (consequenceType < 0 || consequenceType > _additionalConsequenceTypes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(consequenceType), "The consequence-type position is not declared.");
            }
        }

        /// <summary>
        /// Re-derives the knowledge-input columns for a sensitivity scope at the given design
        /// size: the content-seed walk (occurrence indices, component seeds, sampler setup) and
        /// the labeled column collection — identical streams to a run at that size.
        /// </summary>
        /// <param name="componentIndex">The scope (−1 = every component, in analysis order).</param>
        /// <param name="sampleSize">The design size.</param>
        /// <returns>The labeled columns in walk order.</returns>
        private List<SensitivityInput> BuildSensitivityInputs(int componentIndex, int sampleSize)
        {
            SystemComponent.AssignOccurrenceIndices(_components);
            var inputs = new List<SensitivityInput>();
            for (int i = 0; i < _components.Count; i++)
            {
                if (componentIndex >= 0 && i != componentIndex) continue;
                int seed = SeedHelpers.HashCombine(_options.PRNGSeed, _components[i].CanonicalHash(), _components[i].OccurrenceIndex);
                _components[i].SetupSamplers(sampleSize, seed, _options.SamplingScheme);
                _components[i].CollectSensitivityInputs(inputs);
            }
            return inputs;
        }

        /// <summary>
        /// Resolves one realization summary's scope stream, or null when the summary does not
        /// carry it (shape drift across a loaded ensemble).
        /// </summary>
        /// <param name="summary">The realization summary.</param>
        /// <param name="componentIndex">The component scope (−1 = system).</param>
        /// <param name="failureModeIndex">The mode narrowing (−1 = none).</param>
        /// <param name="riskType">The stream.</param>
        /// <param name="consequenceType">The consequence-type position.</param>
        /// <returns>The stream summary, or null.</returns>
        private static SummaryRiskResults? SelectScope(SystemRiskResults summary, int componentIndex, int failureModeIndex,
            RiskType riskType, int consequenceType)
        {
            if (componentIndex < 0)
            {
                if (consequenceType == 0) return SystemStream(summary, riskType);
                int k = consequenceType - 1;
                return k < summary.AdditionalConsequences.Count ? TypeStream(summary.AdditionalConsequences[k], riskType) : null;
            }
            if (componentIndex >= summary.ComponentResults.Count) return null;
            var component = summary.ComponentResults[componentIndex];
            if (failureModeIndex < 0)
            {
                if (consequenceType == 0) return ComponentStream(component, riskType);
                int k = consequenceType - 1;
                return k < component.AdditionalConsequences.Count ? TypeStream(component.AdditionalConsequences[k], riskType) : null;
            }
            if (failureModeIndex >= component.FailureModeResults.Count) return null;
            var mode = component.FailureModeResults[failureModeIndex];
            if (consequenceType == 0)
            {
                return riskType == RiskType.Excess ? mode.Excess : mode.Fail;
            }
            int typeIndex = consequenceType - 1;
            if (typeIndex >= mode.AdditionalConsequences.Count) return null;
            return TypeStream(mode.AdditionalConsequences[typeIndex], riskType);
        }

        /// <summary>Selects a system-scope stream summary.</summary>
        /// <param name="summary">The system summary.</param>
        /// <param name="riskType">The stream.</param>
        /// <returns>The stream summary.</returns>
        private static SummaryRiskResults SystemStream(SystemRiskResults summary, RiskType riskType)
        {
            return riskType switch
            {
                RiskType.Excess => summary.Excess,
                RiskType.Background => summary.Background,
                RiskType.Total => summary.Total,
                RiskType.Fail => summary.Fail,
                _ => summary.NonFail,
            };
        }

        /// <summary>Selects a component-scope stream summary.</summary>
        /// <param name="component">The component summary.</param>
        /// <param name="riskType">The stream.</param>
        /// <returns>The stream summary.</returns>
        private static SummaryRiskResults ComponentStream(ComponentResults component, RiskType riskType)
        {
            return riskType switch
            {
                RiskType.Excess => component.Excess,
                RiskType.Background => component.Background,
                RiskType.Total => component.Total,
                RiskType.Fail => component.Fail,
                _ => component.NonFail,
            };
        }

        /// <summary>Selects a consequence-type stream summary.</summary>
        /// <param name="results">The type's summary set.</param>
        /// <param name="riskType">The stream.</param>
        /// <returns>The stream summary.</returns>
        private static SummaryRiskResults TypeStream(ConsequenceResults results, RiskType riskType)
        {
            return riskType switch
            {
                RiskType.Excess => results.Excess,
                RiskType.Background => results.Background,
                RiskType.Total => results.Total,
                RiskType.Fail => results.Fail,
                _ => results.NonFail,
            };
        }

        /// <summary>Extracts one scalar measure from a stream summary.</summary>
        /// <param name="summary">The stream summary.</param>
        /// <param name="measure">The measure.</param>
        /// <returns>The value (possibly NaN).</returns>
        private static double ExtractMeasure(SummaryRiskResults summary, RiskMeasure measure)
        {
            return measure switch
            {
                RiskMeasure.TotalProbability => summary.TotalProbability,
                RiskMeasure.ConditionalMean => summary.ConditionalMean,
                RiskMeasure.Mean => summary.Mean,
                RiskMeasure.StandardDeviation => summary.StandardDeviation,
                RiskMeasure.Skewness => summary.Skewness,
                RiskMeasure.Kurtosis => summary.Kurtosis,
                RiskMeasure.ConsequenceThresholdProbability => summary.ConsequenceThresholdProbability,
                RiskMeasure.HazardThresholdProbability => summary.HazardThresholdProbability,
                RiskMeasure.ValueAtRisk => summary.ValueAtRisk,
                _ => summary.ConditionalValueAtRisk,
            };
        }

        /// <summary>
        /// Correlates every input column against an output vector: pairwise NaN filtering on
        /// the output, the selected association per column (a non-finite correlation coerces to
        /// zero — the v1.0 convention), entries in walk order.
        /// </summary>
        /// <param name="inputs">The labeled input columns.</param>
        /// <param name="outputs">The per-realization output values (NaN = unavailable).</param>
        /// <param name="measure">The association measure.</param>
        /// <param name="riskType">The output's stream (carried on the result).</param>
        /// <param name="outputLabel">The output's display label.</param>
        /// <returns>The result, or null when fewer than three valid pairs remain.</returns>
        private static SensitivityResults? Correlate(List<SensitivityInput> inputs, double[] outputs,
            SensitivityMeasure measure, RiskType riskType, string outputLabel)
        {
            var validIndices = new List<int>(outputs.Length);
            for (int i = 0; i < outputs.Length; i++)
            {
                if (!double.IsNaN(outputs[i])) validIndices.Add(i);
            }
            if (validIndices.Count < 3) return null;

            int count = validIndices.Count;
            var outputVector = new double[count];
            for (int i = 0; i < count; i++)
            {
                outputVector[i] = outputs[validIndices[i]];
            }

            var inputVector = new double[count];
            var entries = new List<SensitivityEntry>(inputs.Count);
            for (int c = 0; c < inputs.Count; c++)
            {
                var read = inputs[c].Read;
                for (int i = 0; i < count; i++)
                {
                    inputVector[i] = read(validIndices[i]);
                }
                double value = measure switch
                {
                    SensitivityMeasure.PearsonCorrelation => Correlation.Pearson(inputVector, outputVector),
                    SensitivityMeasure.SpearmanCorrelation => Correlation.Spearman(inputVector, outputVector),
                    _ => Math.Pow(Correlation.Pearson(inputVector, outputVector), 2d),
                };
                if (!Tools.IsFinite(value)) value = 0d;
                entries.Add(new SensitivityEntry(inputs[c].Label, value));
            }
            return new SensitivityResults(outputLabel, riskType, measure, count, entries);
        }

        #endregion

        #region Private Helpers — Realization Compute

        /// <summary>
        /// Computes one full realization. On the one-dimensional and additive paths each
        /// component gets its own adaptive Gauss–Kronrod pass over its hazard probability domain
        /// and the exact curves, profiles, and risk measures are built per component; a single
        /// component's curves are the system curves (v1.0 behavior), and multiple additive
        /// components aggregate by zero-inflated lattice convolution. The joint path instead
        /// integrates the correlated hazard hypercube with VEGAS, recording component and system
        /// points together.
        /// </summary>
        /// <param name="realizationIndex">The realization index, or −1 for the mean pass.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="token">The run cancellation token.</param>
        /// <returns>The computed realization.</returns>
        private SystemRealization ComputeRealization(int realizationIndex, RiskComputeFlags flags, CancellationToken token)
        {
            var componentRealizations = new List<ComponentRealization>(_components.Count);
            var sampledComponents = new SampledComponent[_components.Count];
            int additionalTypes = 0;
            for (int i = 0; i < _components.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                sampledComponents[i] = _components[i].Sample(realizationIndex);
                sampledComponents[i].RecordAdjustedModeCurves = _options.OutputAdjustedFailureModeCurves;
                additionalTypes = Math.Max(additionalTypes, sampledComponents[i].ConsequenceTypeCount - 1);
                componentRealizations.Add(new ComponentRealization(sampledComponents[i].FailureModeCount)
                {
                    Name = _components[i].Name,
                });
            }
            var realization = new SystemRealization(componentRealizations);
            realization.EnsureAdditionalCurves(additionalTypes);
            StampConsequenceLabels(realization);
            StampModeLabels(realization);
            for (int i = 0; i < componentRealizations.Count; i++)
            {
                componentRealizations[i].SetMeasureOptions(_options.RiskMeasures);
                if (_options.OutputAdjustedFailureModeCurves)
                {
                    var modes = componentRealizations[i].FailureModes;
                    for (int j = 0; j < modes.Count; j++)
                    {
                        modes[j].EnableAdjustedCurves(additionalTypes);
                        modes[j].SetMeasureOptions(_options.RiskMeasures);
                    }
                }
            }

            if (_components.Count > 1 && _options.SystemRiskMethod == SystemRiskType.JointRiskMethod)
            {
                IntegrateJointSystem(sampledComponents, componentRealizations, realization, flags, realizationIndex, token);
                realization.DumpMemory();
                return realization;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                IntegrateComponent(sampledComponents[i], componentRealizations[i], realization, flags, realizationIndex);
                componentRealizations[i].ProcessHazardProbabilities();
                componentRealizations[i].FinalizeContributions(trapezoidMasses: true);
                componentRealizations[i].CreateCurves(_options.LECOutputLength);
                if ((_options.RiskMeasures & RiskMeasureOptions.RiskProfiles) != 0)
                {
                    componentRealizations[i].CreateProfiles(includeFailureModes: realizationIndex < 0);
                }
                componentRealizations[i].ComputeRiskMeasures(_options.ConsequenceThreshold, _options.Alpha, _components[i].HazardThreshold, _runAdditionalThresholds);

                realization.MinN = Math.Min(realization.MinN, componentRealizations[i].MinN);
                realization.MaxN = Math.Max(realization.MaxN, componentRealizations[i].MaxN);
                for (int k = 0; k < componentRealizations[i].AdditionalCurves.Count; k++)
                {
                    realization.AdditionalMinN[k] = Math.Min(realization.AdditionalMinN[k], componentRealizations[i].AdditionalMinN[k]);
                    realization.AdditionalMaxN[k] = Math.Max(realization.AdditionalMaxN[k], componentRealizations[i].AdditionalMaxN[k]);
                }
                realization.MinH[i] = componentRealizations[i].MinH;
                realization.MaxH[i] = componentRealizations[i].MaxH;
            }

            if (_components.Count == 1)
            {
                // Single-component system results are the component results (v1.0 behavior at
                // the legacy clone site), cloned per consequence type. The component's system
                // contribution is its own totals — a 100% share (% contribution, Phase 6.6).
                realization.Curves = componentRealizations[0].Curves.Clone();
                for (int k = 0; k < componentRealizations[0].AdditionalCurves.Count; k++)
                {
                    realization.AdditionalCurves[k] = componentRealizations[0].AdditionalCurves[k].Clone();
                }
                AssignOwnSystemContribution(componentRealizations[0]);
            }
            else
            {
                AggregateAdditiveSystem(realization, componentRealizations, token);
            }

            realization.DumpMemory();
            return realization;
        }

        /// <summary>
        /// Aggregates the additive system realization (strictly independent components, ratified
        /// v0.13): each risk-type stream's exact recorded pairs are zero-inflated and convolved
        /// on the shared consequence lattice â€” the exact enumeration of all component
        /// failure/non-failure combinations â€” and the defective stream probabilities are then
        /// restored to the v1.0 system-state semantics: the failure union for Fail and Excess,
        /// its complement for NonFail (the convolution's own recorded mass measures "any positive
        /// consequence", which quantizes zero-valued events into the atom). The convolved system
        /// mean equals the sum of the component means by construction â€” the v1.0 additive answer,
        /// now with the full curve v1.0 never produced. The failure union folds the component
        /// probabilities in the canonical-hash component order â€” the same association the
        /// convolution uses â€” so declaration order cannot move the union even at the last bit
        /// (a Phase 6 reproducibility-pin finding; declaration order previously reassociated the
        /// union product by one or two units in the last place).
        /// </summary>
        /// <param name="realization">The system realization to fill.</param>
        /// <param name="componentRealizations">The finished per-component realizations.</param>
        /// <param name="token">The run cancellation token.</param>
        private void AggregateAdditiveSystem(SystemRealization realization,
            IReadOnlyList<ComponentRealization> componentRealizations, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ConvolveSystemStreams(realization.Curves, componentRealizations, c => c.Curves);
            for (int k = 0; k < realization.AdditionalCurves.Count; k++)
            {
                int typeIndex = k;
                ConvolveSystemStreams(realization.AdditionalCurves[k], componentRealizations, c => c.AdditionalCurves[typeIndex]);
            }

            // The failure union is type-independent â€” every type's defective streams carry the
            // same system-state probabilities.
            var order = _additiveConvolutionOrder!;
            var failureProbabilities = new double[componentRealizations.Count];
            for (int i = 0; i < componentRealizations.Count; i++)
            {
                failureProbabilities[i] = componentRealizations[order[i]].Curves.Fail.TotalProbability;
            }
            double failureUnion = Probability.IndependentUnion(failureProbabilities);
            double nonFailureComplement = Math.Max(0d, 1d - failureUnion);
            realization.Curves.Fail.TotalProbability = failureUnion;
            realization.Curves.Excess.TotalProbability = failureUnion;
            realization.Curves.NonFail.TotalProbability = nonFailureComplement;
            for (int k = 0; k < realization.AdditionalCurves.Count; k++)
            {
                realization.AdditionalCurves[k].Fail.TotalProbability = failureUnion;
                realization.AdditionalCurves[k].Excess.TotalProbability = failureUnion;
                realization.AdditionalCurves[k].NonFail.TotalProbability = nonFailureComplement;
            }

            realization.Curves.ComputeRiskMeasures(_options.ConsequenceThreshold, _options.Alpha);
            for (int k = 0; k < realization.AdditionalCurves.Count; k++)
            {
                realization.AdditionalCurves[k].ComputeRiskMeasures(AdditionalThresholdAt(k), _options.Alpha);
            }

            // The system support tops out at the sum of the component maxima â€” widen the
            // percentile grid extent to each type's convolved Total curve.
            if (realization.Curves.Total.LECConsequences.Length > 0)
            {
                realization.MaxN = Math.Max(realization.MaxN, realization.Curves.Total.LECConsequences[0]);
            }
            for (int k = 0; k < realization.AdditionalCurves.Count; k++)
            {
                if (realization.AdditionalCurves[k].Total.LECConsequences.Length > 0)
                {
                    realization.AdditionalMaxN[k] = Math.Max(realization.AdditionalMaxN[k], realization.AdditionalCurves[k].Total.LECConsequences[0]);
                }
            }

            // The per-component system contribution (% contribution, Phase 6.6): the failure
            // union of strictly independent components splits exactly by the Shapley value
            // φ_i = p_i · E[1/(1 + K_i)], with K_i the Poisson–binomial count of the OTHER
            // components failing — the equal split of every exclusive failure combination,
            // computed in closed form by the standard subset-distribution recursion instead of
            // a 2^D enumeration. Σφ ≡ the independent union to floating-point association.
            // The mean contributions are the component means themselves — means add exactly
            // under the convolution. The recursion folds in the canonical-hash component order
            // so declaration order cannot move the attribution even at the last bit (the 4b
            // reorder contract).
            var countDistribution = new double[componentRealizations.Count];
            var nextDistribution = new double[componentRealizations.Count];
            for (int i = 0; i < componentRealizations.Count; i++)
            {
                var component = componentRealizations[i];
                Array.Clear(countDistribution, 0, countDistribution.Length);
                countDistribution[0] = 1d;
                int occupied = 1;
                for (int j = 0; j < order.Length; j++)
                {
                    if (order[j] == i) continue;
                    double p = componentRealizations[order[j]].Curves.Fail.TotalProbability;
                    Array.Clear(nextDistribution, 0, occupied + 1);
                    for (int c = 0; c < occupied; c++)
                    {
                        nextDistribution[c] += countDistribution[c] * (1d - p);
                        nextDistribution[c + 1] += countDistribution[c] * p;
                    }
                    occupied++;
                    Array.Copy(nextDistribution, countDistribution, occupied);
                }
                double expectedReciprocal = 0d;
                for (int c = 0; c < occupied; c++)
                {
                    expectedReciprocal += countDistribution[c] / (c + 1);
                }
                double shapleyShare = component.Curves.Fail.TotalProbability * expectedReciprocal;

                component.SystemContribution = new RiskContribution
                {
                    FailureProbability = shapleyShare,
                    FailureMean = component.Curves.Fail.Mean,
                    ExcessMean = component.Curves.Excess.Mean,
                };
                for (int k = 0; k < component.AdditionalCurves.Count; k++)
                {
                    while (component.AdditionalSystemContributions.Count <= k)
                    {
                        component.AdditionalSystemContributions.Add(null);
                    }
                    component.AdditionalSystemContributions[k] = new RiskContribution
                    {
                        FailureProbability = shapleyShare,
                        FailureMean = component.AdditionalCurves[k].Fail.Mean,
                        ExcessMean = component.AdditionalCurves[k].Excess.Mean,
                    };
                }
            }
        }

        /// <summary>
        /// Assigns a single-component system's contribution: the component's own totals — a
        /// 100% share of the system it constitutes (% contribution, Phase 6.6).
        /// </summary>
        /// <param name="component">The finished component realization.</param>
        private static void AssignOwnSystemContribution(ComponentRealization component)
        {
            component.SystemContribution = new RiskContribution
            {
                FailureProbability = component.Curves.Fail.TotalProbability,
                FailureMean = component.Curves.Fail.Mean,
                ExcessMean = component.Curves.Excess.Mean,
            };
            for (int k = 0; k < component.AdditionalCurves.Count; k++)
            {
                while (component.AdditionalSystemContributions.Count <= k)
                {
                    component.AdditionalSystemContributions.Add(null);
                }
                component.AdditionalSystemContributions[k] = new RiskContribution
                {
                    FailureProbability = component.Curves.Fail.TotalProbability,
                    FailureMean = component.AdditionalCurves[k].Fail.Mean,
                    ExcessMean = component.AdditionalCurves[k].Excess.Mean,
                };
            }
        }

        /// <summary>
        /// Convolves all five risk-type streams of one consequence type across the components
        /// onto the system curve set.
        /// </summary>
        /// <param name="target">The system curve set to fill.</param>
        /// <param name="componentRealizations">The finished per-component realizations.</param>
        /// <param name="scope">Selects the consequence type's curve set from a component realization.</param>
        private void ConvolveSystemStreams(Curves target, IReadOnlyList<ComponentRealization> componentRealizations,
            Func<ComponentRealization, Curves> scope)
        {
            ConvolveSystemStream(target.Excess, componentRealizations, scope, c => c.Excess);
            ConvolveSystemStream(target.Background, componentRealizations, scope, c => c.Background);
            ConvolveSystemStream(target.Total, componentRealizations, scope, c => c.Total);
            ConvolveSystemStream(target.Fail, componentRealizations, scope, c => c.Fail);
            ConvolveSystemStream(target.NonFail, componentRealizations, scope, c => c.NonFail);
        }

        /// <summary>
        /// Convolves one risk-type stream across the components onto the system curve: the
        /// components' exact recorded pairs feed the shared lattice, the joint zero atom (lattice
        /// node zero) is kept only on an exhaustive stream (on a defective stream it is the
        /// no-event mass), and the exact curve construction runs on the lattice pairs. A stream
        /// with no positive-consequence mass anywhere stays empty.
        /// </summary>
        /// <param name="target">The system stream to fill.</param>
        /// <param name="componentRealizations">The finished per-component realizations.</param>
        /// <param name="scope">Selects the consequence type's curve set from a component realization.</param>
        /// <param name="stream">Selects the stream from a component's curve set.</param>
        /// <remarks>
        /// The components enter the sequential convolution in canonical-hash order (computed at
        /// run start), so the floating-point association is identical however the components are
        /// declared â€” component reordering stays bit-inert on the system curves.
        /// </remarks>
        private void ConvolveSystemStream(Curve target, IReadOnlyList<ComponentRealization> componentRealizations,
            Func<ComponentRealization, Curves> scope, Func<Curves, Curve> stream)
        {
            var order = _additiveConvolutionOrder!;
            var componentPairs = new List<IReadOnlyList<(double Mass, double Consequence)>>(componentRealizations.Count);
            for (int i = 0; i < componentRealizations.Count; i++)
            {
                componentPairs.Add(stream(scope(componentRealizations[order[i]])).CollectRecordedPairs());
            }

            var (pmf, step) = SystemConvolution.Convolve(componentPairs, _options.SystemConvolutionPoints);
            if (step <= 0d) return;

            var pairs = new List<(double Mass, double Consequence)>(pmf.Length);
            for (int k = target.IsExhaustive ? 0 : 1; k < pmf.Length; k++)
            {
                if (pmf[k] > 0d)
                {
                    pairs.Add((pmf[k], k * step));
                }
            }
            if (pairs.Count == 0) return;
            target.CreateCurve(pairs, _options.LECOutputLength);
        }

        /// <summary>
        /// Runs the adaptive Gaussâ€“Kronrod pass for one component: the integrator is an
        /// importance sampler whose recorded evaluations populate the risk points; its returned
        /// value is discarded and its evaluation count and true error estimate are kept as
        /// diagnostics.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="componentRealization">The component's realization sink.</param>
        /// <param name="realization">The system realization (diagnostics).</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="realizationIndex">
        /// The realization index, or âˆ’1 for the mean pass. Ensemble realizations (index â‰¥ 0)
        /// integrate at the relaxed <see cref="RiskAnalysisOptions.EnsembleTolerance"/> /
        /// <see cref="RiskAnalysisOptions.EnsembleMinDepth"/> discipline â€” the v1.0 philosophy:
        /// ensemble statistics average integration noise, so the ~4,200-evaluation forced floor
        /// of the full discipline is spent only where a single answer is published (the mean
        /// pass, mean-only runs, and the deterministic probes).
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the integration reports failure â€” an integrand exception was absorbed by
        /// the integrator (<c>ReportFailure</c> is false), so the recorded risk points are
        /// truncated and no result may be published. Surfacing the failure here keeps a faulted
        /// evaluation from silently reading as zero risk (the Phase 5 correction; the VEGAS
        /// call sites carry the same guard).
        /// </exception>
        private void IntegrateComponent(SampledComponent sampled, ComponentRealization componentRealization,
            SystemRealization realization, RiskComputeFlags flags, int realizationIndex)
        {
            // One stratification build serves the balanced-objective scales and the integrator
            // seeding alike (the probes run before the integrator touches the list).
            var bins = BuildStratificationBins(sampled, flags);
            var objective = BuildObjective(sampled, componentRealization, flags, bins);
            bool ensemble = realizationIndex >= 0;
            var integrator = new AdaptiveGaussKronrod(objective, ProbabilityFloor, 1d - ProbabilityFloor)
            {
                ReportFailure = false,
                MaxFunctionEvaluations = _options.MaxEvaluations,
                MaxDepth = _options.MaxDepth,
                RelativeTolerance = ensemble ? _options.EnsembleTolerance : _options.Tolerance,
                MinDepth = ensemble ? _options.EnsembleMinDepth : 2,
            };
            integrator.Integrate(bins);
            if (integrator.Status == IntegrationStatus.Failure)
            {
                throw new InvalidOperationException($"The risk integration failed for system component '{sampled.Name}': an integrand evaluation threw and the recorded curves are incomplete. The analysis cannot publish results for this run.");
            }

            realization.FunctionEvaluations += integrator.FunctionEvaluations;
            realization.StandardError += integrator.StandardError / _components.Count;
        }

        /// <summary>
        /// The effective adaptive-refinement objective: reliability mode always refines on the
        /// failure probability (its natural pairing â€” a consequence-free model's consequence
        /// objectives are identically zero, which would defeat the adaptivity); risk mode uses
        /// the configured objective.
        /// </summary>
        private RiskIntegrand EffectiveIntegrand => _options.Mode == RiskAnalysisMode.Reliability
            ? RiskIntegrand.TotalProbabilityOfFailure
            : _options.RiskIntegrand;

        /// <summary>
        /// Builds the integrand for the selected refinement objective (architecture doc Â§7.7).
        /// Every evaluation computes and records the full component risk regardless of the
        /// objective â€” the objective changes only where the adaptive refinement concentrates.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="componentRealization">The component's realization sink.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="bins">The component's stratification bins (the balanced objective's scale probes sweep their edges).</param>
        /// <returns>The integrand over hazard non-exceedance probability.</returns>
        private Func<double, double> BuildObjective(SampledComponent sampled, ComponentRealization componentRealization,
            RiskComputeFlags flags, List<StratificationBin> bins)
        {
            if (EffectiveIntegrand == RiskIntegrand.Balanced)
            {
                var (meanScale, secondScale, tailScale) = ObjectiveScales(sampled, flags, bins);
                return p =>
                {
                    var output = Evaluate(sampled, componentRealization, flags, p, recordOutput: true);
                    return ObjectiveValue(output, p, RiskIntegrand.MeanTotalRisk) / meanScale
                        + ObjectiveValue(output, p, RiskIntegrand.SecondMoment) / secondScale
                        + ObjectiveValue(output, p, RiskIntegrand.TailConditionalRisk) / tailScale;
                };
            }

            var integrand = EffectiveIntegrand;
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
        /// The refinement-objective value at one evaluation (architecture doc Â§7.7 table).
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
        /// Estimates the normalization scales for the balanced objective's three members from
        /// ONE non-recording pre-pass over the stratification-bin edges â€” each evaluation feeds
        /// all three accumulations, so the pass costs a third of the per-member probes it
        /// replaces while producing the identical per-member sums (Phase 6.5; the pre-6.5 shape
        /// ran three separate sweeps and rebuilt the bins each time). A vanishing scale falls
        /// back to one so the balanced sum stays finite.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="bins">The component's stratification bins.</param>
        /// <returns>The positive normalization scales (mean, second moment, tail).</returns>
        private (double Mean, double Second, double Tail) ObjectiveScales(SampledComponent sampled, RiskComputeFlags flags,
            List<StratificationBin> bins)
        {
            var scratch = new ComponentRealization(sampled.FailureModeCount);
            double meanScale = 0d;
            double secondScale = 0d;
            double tailScale = 0d;
            for (int i = 0; i < bins.Count; i++)
            {
                var output = Evaluate(sampled, scratch, flags, bins[i].LowerBound, recordOutput: false);
                meanScale += Math.Abs(ObjectiveValue(output, bins[i].LowerBound, RiskIntegrand.MeanTotalRisk));
                secondScale += Math.Abs(ObjectiveValue(output, bins[i].LowerBound, RiskIntegrand.SecondMoment));
                tailScale += Math.Abs(ObjectiveValue(output, bins[i].LowerBound, RiskIntegrand.TailConditionalRisk));
            }
            var last = Evaluate(sampled, scratch, flags, bins[bins.Count - 1].UpperBound, recordOutput: false);
            meanScale += Math.Abs(ObjectiveValue(last, bins[bins.Count - 1].UpperBound, RiskIntegrand.MeanTotalRisk));
            secondScale += Math.Abs(ObjectiveValue(last, bins[bins.Count - 1].UpperBound, RiskIntegrand.SecondMoment));
            tailScale += Math.Abs(ObjectiveValue(last, bins[bins.Count - 1].UpperBound, RiskIntegrand.TailConditionalRisk));
            return (meanScale > 0d ? meanScale : 1d, secondScale > 0d ? secondScale : 1d, tailScale > 0d ? tailScale : 1d);
        }

        /// <summary>
        /// Builds the stratified hazard bins seeding the adaptive integrator (50 hazard-space
        /// bins mapped to probability â€” v1.0 constants), injecting the discontinuity of a
        /// discontinuous objective as a bin boundary so no quadrature panel spans the jump
        /// (Â§7.7): the tail objective switches at p = Î±, and the threshold objective crosses
        /// where the consequence passes the threshold (located by Brent's method on this
        /// realization's chain when a sign change brackets it).
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="flags">The realization's computational-warning flags (the threshold probe evaluates the chain).</param>
        /// <returns>The stratification bins in probability space.</returns>
        private List<StratificationBin> BuildStratificationBins(SampledComponent sampled, RiskComputeFlags flags)
        {
            var bins = BuildHazardBins(sampled);

            if (EffectiveIntegrand == RiskIntegrand.TailConditionalRisk)
            {
                InjectBoundary(bins, _options.Alpha);
            }
            else if (EffectiveIntegrand == RiskIntegrand.ThresholdExceedanceProbability)
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
        /// Builds the plain 50-bin hazard stratification in probability space (v1.0 constants) â€”
        /// shared by the risk integral's seeding and the tail-focus quadrature probe.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <returns>The stratification bins in probability space.</returns>
        private static List<StratificationBin> BuildHazardBins(SampledComponent sampled)
        {
            var bins = Stratify.XValues(new StratificationOptions(
                sampled.Hazard.InverseCDF(ProbabilityFloor), sampled.Hazard.InverseCDF(1d - ProbabilityFloor), HazardBinCount), true);
            return Stratify.XToProbability(bins, sampled.Hazard.CDF, false);
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

        #region Private Helpers â€” Joint System Integration

        /// <summary>
        /// Prepares the run-scoped joint-method state: the correlated-hazard latent structure,
        /// the 2^D component combination caches (all-zero row first â€” the v1.0 engine-level
        /// layout), and, under the automatic tail-focus mode, the deterministic per-component
        /// failure-probability quadrature probe that sets the VEGAS power-transform target. A
        /// no-op outside the multi-component joint method.
        /// </summary>
        /// <param name="token">The run cancellation token.</param>
        /// <remarks>
        /// The ratified v0.13 heuristic harvested the target from the VEGAS warm-up itself; at
        /// implementation two facts forced this probe instead (recorded in the phase log): the
        /// automatic configuration resets the VEGAS bin count, which reallocates the importance
        /// grid â€” so Î³ must be set before the warm-up, not after it â€” and a Î³ = 1 Monte Carlo
        /// warm-up cannot observe the rare failure probabilities the target needs (that is the
        /// very problem the transform solves). The adaptive Gaussâ€“Kronrod probe of
        /// AFP_i = âˆ« P_F,i(p) dp on the mean sample is deterministic, costs about a thousand
        /// evaluations per component once per run, and measures the failure probabilities to
        /// quadrature accuracy.
        /// </remarks>
        private void PrepareJointSystem(CancellationToken token)
        {
            _jointMultivariateNormal = null;
            _jointCholeskyLower = null;
            _jointTailTargetProbability = 1e-2;
            if (_components.Count < 2 || _options.SystemRiskMethod != SystemRiskType.JointRiskMethod)
            {
                return;
            }

            int d = _components.Count;
            _jointMultivariateNormal = BuildHazardMultivariateNormal(d, out var covariance);

            // Extract the lower Cholesky factor once â€” the same construction the multivariate
            // normal performs internally on the same covariance, so the factor bits are
            // identical and the integrand's in-place latent transform reproduces
            // MultivariateNormal.InverseCDF exactly.
            var lower = new CholeskyDecomposition(new Matrix(covariance)).L;
            _jointCholeskyLower = new double[d, d];
            for (int i = 0; i < d; i++)
            {
                for (int j = 0; j < d; j++)
                {
                    _jointCholeskyLower[i, j] = lower[i, j];
                }
            }

            if (_options.VegasTailFocusMode == VegasTailFocusMode.Automatic)
            {
                double minimumFailureProbability = double.MaxValue;
                for (int i = 0; i < d; i++)
                {
                    token.ThrowIfCancellationRequested();
                    minimumFailureProbability = Math.Min(minimumFailureProbability, ProbeAnnualFailureProbability(_components[i]));
                }
                _jointTailTargetProbability = Tools.Clamp(minimumFailureProbability * _options.Alpha, 1e-12, 1e-2);
            }
        }

        /// <summary>
        /// Builds the correlated component-hazard latent structure per the dependency option,
        /// with the exact v1.0 off-diagonal constants: identity (independent), <c>1 âˆ’ âˆšÎµmach</c>
        /// (perfectly positive), <c>âˆ’1/(D âˆ’ 1) + âˆšÎµmach</c> (perfectly negative), or the
        /// analysis-validated user matrix.
        /// </summary>
        /// <param name="dimension">The component count D.</param>
        /// <param name="covariance">Receives the covariance the normal was built on (the Cholesky-extraction input).</param>
        /// <returns>The multivariate normal over the component hazard probabilities.</returns>
        private MultivariateNormal BuildHazardMultivariateNormal(int dimension, out double[,] covariance)
        {
            var mean = new double[dimension];
            covariance = new double[dimension, dimension];
            if (_options.ComponentHazardDependency == DependencyType.CorrelationMatrix)
            {
                var matrix = _options.HazardCorrelationMatrix!;
                for (int i = 0; i < dimension; i++)
                {
                    for (int j = 0; j < dimension; j++)
                    {
                        covariance[i, j] = matrix[i, j];
                    }
                }
                return new MultivariateNormal(mean, covariance);
            }

            DependencyMatrix.FillEquicorrelated(covariance, dimension,
                DependencyMatrix.AutomaticOffDiagonal(_options.ComponentHazardDependency, dimension));
            return new MultivariateNormal(mean, covariance);
        }

        /// <summary>
        /// The deterministic annual-failure-probability probe: integrates one component's
        /// combined failure probability over its hazard probability domain on the mean sample
        /// with adaptive Gaussâ€“Kronrod â€” the one call site where the integral's returned value is
        /// the product.
        /// </summary>
        /// <param name="component">The component to probe (its samplers are already set up).</param>
        /// <returns>The component's annualized failure probability on the mean sample.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the probe integration reports failure â€” a swallowed integrand exception
        /// would otherwise feed a truncated probability into the tail-focus target (the same
        /// guard as <see cref="IntegrateComponent"/>).
        /// </exception>
        private double ProbeAnnualFailureProbability(SystemComponent component)
        {
            var sampled = component.Sample(-1);
            var scratch = new ComponentRealization(sampled.FailureModeCount);
            var flags = new RiskComputeFlags();
            var integrator = new AdaptiveGaussKronrod(
                p => sampled.ComputeRisk(p, sampled.Hazard.InverseCDF(p), flags, scratch).ProbabilityOfFailure,
                ProbabilityFloor, 1d - ProbabilityFloor)
            {
                ReportFailure = false,
                MaxFunctionEvaluations = _options.MaxEvaluations,
                MaxDepth = _options.MaxDepth,
                RelativeTolerance = _options.Tolerance,
                MinDepth = 2,
            };
            integrator.Integrate(BuildHazardBins(sampled));
            if (integrator.Status == IntegrationStatus.Failure)
            {
                throw new InvalidOperationException($"The failure-probability probe failed for system component '{sampled.Name}': an integrand evaluation threw, so the tail-focus target cannot be derived.");
            }
            return Tools.Clamp(integrator.Result, 0d, 1d);
        }

        /// <summary>
        /// Applies the VEGAS power-transform tail focus per the configured mode â€” always before
        /// the warm-up, because raising the bin count reallocates the importance grid (setting Î³
        /// after the warm-up would discard it). Automatic uses the probe-derived target through
        /// <c>ConfigureForRareEvents</c>; manual applies the user's Î³ with the same bin-count and
        /// grid-damping adjustments when Î³ exceeds one; none leaves Î³ = 1 â€” sampling identical to
        /// v1.0.
        /// </summary>
        /// <param name="integrator">The VEGAS integrator to configure.</param>
        private void ConfigureTailFocus(Vegas integrator)
        {
            switch (_options.VegasTailFocusMode)
            {
                case VegasTailFocusMode.Manual:
                    integrator.TailFocusParameter = _options.VegasTailFocusParameter;
                    if (_options.VegasTailFocusParameter > 1d)
                    {
                        integrator.NumberOfBins = Math.Max(100, integrator.NumberOfBins);
                        integrator.Alpha = 1.8;
                    }
                    break;
                case VegasTailFocusMode.Automatic:
                    integrator.ConfigureForRareEvents(_jointTailTargetProbability);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// The joint system-risk pass: VEGAS integrates over the D-dimensional hypercube of
        /// correlated hazard probabilities, and each evaluation computes every component's risk,
        /// enumerates the exclusive component failure/non-failure combinations, and â€” the v1.1
        /// correction of the documented v1.0 system-tail defect â€” crosses the failing components'
        /// recorded pathway/branch entries (conditional weights from the per-pathway lists)
        /// instead of collapsing each component to its conditional mean. Non-failing components
        /// contribute their branch-weighted mean non-failure consequence (the documented
        /// <see cref="ComponentRiskOutput"/> interim). The VEGAS weight is the recorded
        /// probability mass; recorded points accumulate across the recording passes and are
        /// self-normalized by the realized weight sum so the exhaustive budget is exactly one.
        /// </summary>
        /// <param name="sampledComponents">The sampled components for this realization.</param>
        /// <param name="componentRealizations">The per-component realization sinks.</param>
        /// <param name="realization">The system realization.</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <param name="realizationIndex">The realization index, or âˆ’1 for the mean pass.</param>
        /// <param name="token">The run cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when the VEGAS integration fails.</exception>
        /// <remarks>
        /// The legacy engine's <c>tPF</c> accumulator (incremented twice per combination when
        /// recording) is not carried forward â€” the failure union is the Fail stream's recorded
        /// mass, and no scalar duplicates it. The exclusive-probability enumeration inherits the
        /// legacy convergence shortcut, which can truncate the deepest combinations of a
        /// high-dimensional system; the mass it drops is bounded by the enumeration tolerance
        /// and surfaces honestly through the mass-balance witness.
        /// </remarks>
        private void IntegrateJointSystem(SampledComponent[] sampledComponents,
            List<ComponentRealization> componentRealizations, SystemRealization realization,
            RiskComputeFlags flags, int realizationIndex, CancellationToken token)
        {
            int d = _components.Count;
            var lower = _jointCholeskyLower
                ?? throw new InvalidOperationException("The joint-method state was not prepared. The run sequence must call PrepareJointSystem before computing realizations.");
            int typeCount = 1 + realization.AdditionalCurves.Count;

            // The integration extents: the hazard probability hypercube (v1.0 constants).
            var minimums = new double[d];
            var maximums = new double[d];
            for (int i = 0; i < d; i++)
            {
                minimums[i] = ProbabilityFloor;
                maximums[i] = 1d - ProbabilityFloor;
            }

            // Pre-allocated evaluation buffers â€” the integrand allocates nothing it controls.
            // The primary consequence type drives the VEGAS objective; secondary types are
            // computed and recorded during the recording passes only.
            var hazardLevels = new double[d];
            var failureProbabilities = new double[d];
            var outputs = new ComponentRiskOutput[d];
            var outputsByType = new ComponentRiskOutput[d][];
            for (int i = 0; i < d; i++)
            {
                outputsByType[i] = new ComponentRiskOutput[typeCount];
            }
            var nonFailureValuesByType = new double[typeCount][];
            for (int k = 0; k < typeCount; k++)
            {
                nonFailureValuesByType[k] = new double[d];
            }
            var participating = new List<int>(d);
            var branchPick = new int[d];
            var failPoints = new RiskPoint?[typeCount];
            var excessPoints = new RiskPoint?[typeCount];
            var totalPoints = new RiskPoint?[typeCount];
            var nonFailPoints = new RiskPoint?[typeCount];
            var zBuffer = new double[d];
            var latentBuffer = new double[d];

            // The per-component system-contribution accumulators (% contribution, Phase 6.6),
            // one row per consequence type, filled through the recording passes in weight terms
            // and scaled by the self-normalization factor with the recorded masses.
            var systemContributionProbability = new double[typeCount][];
            var systemContributionFailure = new double[typeCount][];
            var systemContributionExcess = new double[typeCount][];
            for (int k = 0; k < typeCount; k++)
            {
                systemContributionProbability[k] = new double[d];
                systemContributionFailure[k] = new double[d];
                systemContributionExcess[k] = new double[d];
            }
            var tupleFailureValues = new double[d];
            var tupleExcessValues = new double[d];

            bool recording = false;
            double recordedWeightSum = 0d;

            // Reusable exclusive-combination outputs: the pooled overload clears and refills these,
            // reusing indicator rows in place, so the integrand allocates no outputs after its
            // first evaluation. Locals of this call, so the parallel loop shares nothing.
            var exclusiveProbabilities = new List<double>();
            var exclusiveIndicators = new List<int[]>();
            long cappedEvaluations = 0;
            double cappedResidual = 0d;

            double Integrand(double[] point, double weight)
            {
                if (token.IsCancellationRequested) return 0d;

                bool recordSecondary = recording && typeCount > 1;

                // Correlated hazard probabilities through the latent normal: the in-place
                // Cholesky transform replicating MultivariateNormal.InverseCDF exactly â€” the
                // full-row accumulation order of the Numerics matrixâ€“vector product, with the
                // zero mean folded in â€” without its per-evaluation allocations.
                for (int j = 0; j < d; j++)
                {
                    zBuffer[j] = Normal.StandardZ(point[j]);
                }
                for (int i = 0; i < d; i++)
                {
                    double sum = 0.0d;
                    for (int j = 0; j < d; j++)
                    {
                        sum += lower[i, j] * zBuffer[j];
                    }
                    latentBuffer[i] = sum + 0d;
                }
                for (int i = 0; i < d; i++)
                {
                    double probability = Normal.StandardCDF(latentBuffer[i]);
                    probability = Tools.Clamp(probability, ProbabilityFloor, 1d - ProbabilityFloor);
                    hazardLevels[i] = sampledComponents[i].Hazard.InverseCDF(probability);

                    // The VEGAS weight is the recorded probability-mass coordinate (v1.0
                    // semantics â€” the component sinks record mass = weight directly).
                    if (recordSecondary)
                    {
                        sampledComponents[i].ComputeRisk(weight, hazardLevels[i], flags, componentRealizations[i], recording, outputsByType[i]);
                        outputs[i] = outputsByType[i][0];
                    }
                    else
                    {
                        outputs[i] = sampledComponents[i].ComputeRisk(weight, hazardLevels[i], flags, componentRealizations[i], recording);
                        outputsByType[i][0] = outputs[i];
                    }
                    failureProbabilities[i] = outputs[i].ProbabilityOfFailure;
                    nonFailureValuesByType[0][i] = outputs[i].NonFailureConsequences;
                    for (int k = 1; k < (recordSecondary ? typeCount : 1); k++)
                    {
                        nonFailureValuesByType[k][i] = outputsByType[i][k].NonFailureConsequences;
                    }
                }

                // The background consequence combines every component's non-failure scalar.
                double background = CombineAll(nonFailureValuesByType[0], _options.JointConsequences);

                if (recording)
                {
                    recordedWeightSum += weight;
                    for (int k = 0; k < typeCount; k++)
                    {
                        var target = k == 0 ? realization.Curves : realization.AdditionalCurves[k - 1];
                        double typeBackground = k == 0 ? background : CombineAll(nonFailureValuesByType[k], _options.JointConsequences);
                        target.Background.AddRiskPoint(weight, 1d, typeBackground);
                        failPoints[k] = new RiskPoint { HazardProbability = weight, HazardProbabilityMass = weight };
                        excessPoints[k] = new RiskPoint { HazardProbability = weight, HazardProbabilityMass = weight };
                        totalPoints[k] = new RiskPoint { HazardProbability = weight, HazardProbabilityMass = weight };
                        nonFailPoints[k] = new RiskPoint { HazardProbability = weight, HazardProbabilityMass = weight };
                        target.Fail.RiskPoints.Add(failPoints[k]!);
                        target.Excess.RiskPoints.Add(excessPoints[k]!);
                        target.Total.RiskPoints.Add(totalPoints[k]!);
                        target.NonFail.RiskPoints.Add(nonFailPoints[k]!);
                    }
                }

                // The exclusive component failure/non-failure combinations. Conditional on the
                // hazard levels, component failures are independent â€” dependence enters only
                // through the correlated hazards (the v1.0 model). Combinations are generated on
                // demand, so nothing here is bounded by a 2^D matrix.
                var enumeration = Probability.IndependentExclusiveLazy(failureProbabilities,
                    exclusiveProbabilities, exclusiveIndicators, includeNoEventRow: true,
                    maxEmittedCombinations: _options.MaxSystemCombinations);
                if (enumeration == Probability.ExclusiveEnumerationStatus.Capped)
                {
                    double emitted = 0d;
                    for (int c = 0; c < exclusiveProbabilities.Count - 1; c++) emitted += exclusiveProbabilities[c];
                    cappedEvaluations++;
                    cappedResidual += Math.Max(0d, 1d - emitted);
                }

                double expectedFailure = 0d;
                double expectedNonFailure = 0d;
                double secondaryDiscard = 0d;
                for (int c = 0; c < exclusiveIndicators.Count; c++)
                {
                    double combinationProbability = exclusiveProbabilities[c];
                    if (combinationProbability <= 0d) continue;
                    var combination = exclusiveIndicators[c];

                    participating.Clear();
                    bool entriesAvailable = true;
                    for (int i = 0; i < d; i++)
                    {
                        if (combination[i] == 1)
                        {
                            participating.Add(i);
                            if (outputs[i].FailureConsequences.Count == 0) entriesAvailable = false;
                        }
                    }

                    double complementNonFailure = CombineComplement(nonFailureValuesByType[0], combination, _options.JointConsequences);
                    expectedNonFailure += combinationProbability * complementNonFailure;

                    if (participating.Count == 0)
                    {
                        // The no-failure combination: non-failure and total only (v1.0 layout).
                        if (recording)
                        {
                            for (int k = 0; k < typeCount; k++)
                            {
                                double typeComplement = k == 0
                                    ? complementNonFailure
                                    : CombineComplement(nonFailureValuesByType[k], combination, _options.JointConsequences);
                                nonFailPoints[k]!.Add(combinationProbability, typeComplement);
                                totalPoints[k]!.Add(combinationProbability, typeComplement);
                                WidenSystemExtents(realization, k, typeComplement, typeComplement);
                            }
                        }
                        continue;
                    }
                    if (!entriesAvailable) continue;

                    // The odometer over the failing components' recorded entries, per consequence
                    // type: the primary drives the VEGAS objective; secondary types record only.
                    AccumulateJointCombinationEntries(0, combinationProbability, complementNonFailure,
                        participating, branchPick, outputsByType, failureProbabilities, recording,
                        failPoints[0], excessPoints[0], totalPoints[0], realization, ref expectedFailure,
                        weight,
                        recording ? systemContributionProbability[0] : null,
                        recording ? systemContributionFailure[0] : null,
                        recording ? systemContributionExcess[0] : null,
                        tupleFailureValues, tupleExcessValues);
                    if (recordSecondary)
                    {
                        for (int k = 1; k < typeCount; k++)
                        {
                            double typeComplement = CombineComplement(nonFailureValuesByType[k], combination, _options.JointConsequences);
                            AccumulateJointCombinationEntries(k, combinationProbability, typeComplement,
                                participating, branchPick, outputsByType, failureProbabilities, recording,
                                failPoints[k], excessPoints[k], totalPoints[k], realization, ref secondaryDiscard,
                                weight, systemContributionProbability[k], systemContributionFailure[k], systemContributionExcess[k],
                                tupleFailureValues, tupleExcessValues);
                        }
                    }

                    // The non-failure stream records one entry per combination from the
                    // complement's scalars, excluding the all-fail combination whose complement
                    // is empty (v1.0 layout).
                    if (recording && participating.Count < d)
                    {
                        for (int k = 0; k < typeCount; k++)
                        {
                            double typeComplement = k == 0
                                ? complementNonFailure
                                : CombineComplement(nonFailureValuesByType[k], combination, _options.JointConsequences);
                            nonFailPoints[k]!.Add(combinationProbability, typeComplement);
                        }
                    }
                }

                return expectedFailure + expectedNonFailure;
            }

            int vegasSeed = SeedHelpers.ToPositiveSeed(SeedHelpers.HashCombine(_jointSeedBase, JointStreamSalt, realizationIndex));
            var integrator = new Vegas(Integrand, d, minimums, maximums)
            {
                Random = new MersenneTwister(vegasSeed),
                UseSobolSequence = false,
                ReportFailure = false,
                CheckConvergence = false,
                RelativeTolerance = 1e-3,
                MaxFunctionEvaluations = _options.MaxEvaluations,
                FunctionCalls = _options.WarmupEvaluations,
                IndependentEvaluations = _options.WarmupCycles,
                Initialize = 0,
            };
            ConfigureTailFocus(integrator);

            // The warm-up builds the importance grid; nothing is recorded.
            integrator.Integrate();
            realization.ChiSquared = integrator.ChiSquared;
            token.ThrowIfCancellationRequested();
            if (integrator.Status == IntegrationStatus.Failure)
            {
                throw new InvalidOperationException("The VEGAS warm-up failed; the joint system risk integration cannot proceed.");
            }

            // The recording passes inherit the grid but not its answers; masses self-normalize.
            recording = true;
            integrator.Initialize = 1;
            integrator.FunctionCalls = _options.FinalEvaluations;
            integrator.IndependentEvaluations = VegasRecordingPasses;
            integrator.Integrate();
            token.ThrowIfCancellationRequested();
            if (integrator.Status == IntegrationStatus.Failure)
            {
                throw new InvalidOperationException("The VEGAS recording pass failed; the joint system risk integration cannot proceed.");
            }
            realization.FunctionEvaluations += integrator.FunctionEvaluations;
            realization.StandardError = integrator.StandardError;

            if (cappedEvaluations > 0)
            {
                double meanResidual = cappedResidual / cappedEvaluations;
                flags.HasTruncatedCombinationEnumeration = true;
                if (meanResidual > TruncatedCombinationResidualLimit) flags.HasExcessiveTruncatedMass = true;
            }

            if (recordedWeightSum > 0d)
            {
                double scale = 1d / recordedWeightSum;
                realization.Curves.ScaleRecordedMass(scale);
                for (int k = 0; k < realization.AdditionalCurves.Count; k++)
                {
                    realization.AdditionalCurves[k].ScaleRecordedMass(scale);
                }
                for (int i = 0; i < componentRealizations.Count; i++)
                {
                    componentRealizations[i].ScaleRecordedMass(scale);
                }

                // Finalize the % contribution attributions under the same self-normalization
                // (Phase 6.6): the failure-mode accumulators carry weights directly, and the
                // per-component system attributions scale with the recorded masses.
                for (int i = 0; i < componentRealizations.Count; i++)
                {
                    componentRealizations[i].FinalizeContributions(trapezoidMasses: false, scale);
                    componentRealizations[i].SystemContribution = new RiskContribution
                    {
                        FailureProbability = systemContributionProbability[0][i] * scale,
                        FailureMean = systemContributionFailure[0][i] * scale,
                        ExcessMean = systemContributionExcess[0][i] * scale,
                    };
                    for (int k = 1; k < typeCount; k++)
                    {
                        while (componentRealizations[i].AdditionalSystemContributions.Count < k)
                        {
                            componentRealizations[i].AdditionalSystemContributions.Add(null);
                        }
                        componentRealizations[i].AdditionalSystemContributions[k - 1] = new RiskContribution
                        {
                            FailureProbability = systemContributionProbability[k][i] * scale,
                            FailureMean = systemContributionFailure[k][i] * scale,
                            ExcessMean = systemContributionExcess[k][i] * scale,
                        };
                    }
                }
            }

            // Build the component curves from the recorded masses (no mass re-derivation â€” the
            // VEGAS weights are the masses), then the system curves and measures, per
            // consequence type.
            for (int i = 0; i < componentRealizations.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                componentRealizations[i].CreateCurves(_options.LECOutputLength);
                if ((_options.RiskMeasures & RiskMeasureOptions.RiskProfiles) != 0)
                {
                    componentRealizations[i].CreateProfiles(includeFailureModes: realizationIndex < 0);
                }
                componentRealizations[i].ComputeRiskMeasures(_options.ConsequenceThreshold, _options.Alpha, _components[i].HazardThreshold, _runAdditionalThresholds);
                realization.MinN = Math.Min(realization.MinN, componentRealizations[i].MinN);
                realization.MaxN = Math.Max(realization.MaxN, componentRealizations[i].MaxN);
                for (int k = 0; k < componentRealizations[i].AdditionalCurves.Count; k++)
                {
                    realization.AdditionalMinN[k] = Math.Min(realization.AdditionalMinN[k], componentRealizations[i].AdditionalMinN[k]);
                    realization.AdditionalMaxN[k] = Math.Max(realization.AdditionalMaxN[k], componentRealizations[i].AdditionalMaxN[k]);
                }
                realization.MinH[i] = componentRealizations[i].MinH;
                realization.MaxH[i] = componentRealizations[i].MaxH;
            }
            realization.Curves.CreateCurves(_options.LECOutputLength);
            realization.Curves.ComputeRiskMeasures(_options.ConsequenceThreshold, _options.Alpha);
            for (int k = 0; k < realization.AdditionalCurves.Count; k++)
            {
                realization.AdditionalCurves[k].CreateCurves(_options.LECOutputLength);
                realization.AdditionalCurves[k].ComputeRiskMeasures(AdditionalThresholdAt(k), _options.Alpha);
            }
        }

        /// <summary>
        /// Runs the joint-combination odometer for one consequence type: the cross product over
        /// the failing components' recorded entries at that type (conditional branch weights
        /// multiply; consequences combine per the joint rule), accumulating the expected failure
        /// consequence in the caller's sequential chain and recording entries when requested.
        /// </summary>
        /// <param name="typeIndex">The consequence-type position (0 is the primary).</param>
        /// <param name="combinationProbability">The exclusive combination probability.</param>
        /// <param name="complementNonFailure">The combined non-failure consequence of the non-failing components at this type.</param>
        /// <param name="participating">The failing component indices.</param>
        /// <param name="branchPick">The shared odometer buffer (cleared here).</param>
        /// <param name="outputsByType">The per-component, per-type risk outputs.</param>
        /// <param name="failureProbabilities">The per-component failure probabilities.</param>
        /// <param name="recording">True to record entries on the supplied risk points.</param>
        /// <param name="failPoint">This type's Fail risk point (recording only).</param>
        /// <param name="excessPoint">This type's Excess risk point (recording only).</param>
        /// <param name="totalPoint">This type's Total risk point (recording only).</param>
        /// <param name="realization">The system realization (extent tracking).</param>
        /// <param name="expectedFailure">The caller's running expected-failure accumulator (kept sequential for bit-identity).</param>
        /// <param name="weight">The evaluation's VEGAS weight — the recorded mass the contribution shares carry.</param>
        /// <param name="contributionProbability">The optional per-component attributed-probability sink (% contribution, Phase 6.6); null skips attribution.</param>
        /// <param name="contributionFailure">The optional per-component attributed failure-value sink.</param>
        /// <param name="contributionExcess">The optional per-component attributed excess-value sink.</param>
        /// <param name="tupleFailureValues">The caller's per-participant failure-value scratch (attribution weights).</param>
        /// <param name="tupleExcessValues">The caller's per-participant excess-value scratch (attribution weights).</param>
        /// <remarks>
        /// The attribution (user-ratified 2026-07-24): within each exclusive component
        /// combination tuple, the probability splits equally (the Shapley value of the union
        /// game) and the combined failure and excess values split proportionally to the
        /// participants' own entry values (equal split when a value sum is zero). Attribution
        /// runs in separate accumulation chains — the expected-failure chain and the recorded
        /// entries are bit-untouched.
        /// </remarks>
        private void AccumulateJointCombinationEntries(int typeIndex, double combinationProbability, double complementNonFailure,
            List<int> participating, int[] branchPick, ComponentRiskOutput[][] outputsByType, double[] failureProbabilities,
            bool recording, RiskPoint? failPoint, RiskPoint? excessPoint, RiskPoint? totalPoint,
            SystemRealization realization, ref double expectedFailure,
            double weight = 0d, double[]? contributionProbability = null, double[]? contributionFailure = null,
            double[]? contributionExcess = null, double[]? tupleFailureValues = null, double[]? tupleExcessValues = null)
        {
            Array.Clear(branchPick, 0, participating.Count);
            while (true)
            {
                double tupleWeight = 1d;
                double combinedFailure = 0d;
                double combinedExcess = 0d;
                double tupleFailureSum = 0d;
                double tupleExcessSum = 0d;
                for (int p = 0; p < participating.Count; p++)
                {
                    var output = outputsByType[participating[p]][typeIndex];
                    double raw = failureProbabilities[participating[p]];
                    double entryWeight = raw > 0d
                        ? output.ResponseProbabilities[branchPick[p]] / raw
                        : (branchPick[p] == 0 ? 1d : 0d);
                    tupleWeight *= entryWeight;
                    double failureValue = output.FailureConsequences[branchPick[p]];
                    double excessValue = output.ExcessConsequences[branchPick[p]];
                    if (contributionProbability != null)
                    {
                        tupleFailureValues![p] = failureValue;
                        tupleExcessValues![p] = excessValue;
                        tupleFailureSum += failureValue;
                        tupleExcessSum += excessValue;
                    }
                    if (p == 0)
                    {
                        combinedFailure = failureValue;
                        combinedExcess = excessValue;
                    }
                    else
                    {
                        switch (_options.JointConsequences)
                        {
                            case JointConsequenceType.Additive:
                            case JointConsequenceType.Average:
                                combinedFailure += failureValue;
                                combinedExcess += excessValue;
                                break;
                            case JointConsequenceType.Maximum:
                                combinedFailure = Math.Max(combinedFailure, failureValue);
                                combinedExcess = Math.Max(combinedExcess, excessValue);
                                break;
                            case JointConsequenceType.Minimum:
                            default:
                                combinedFailure = Math.Min(combinedFailure, failureValue);
                                combinedExcess = Math.Min(combinedExcess, excessValue);
                                break;
                        }
                    }
                }
                if (_options.JointConsequences == JointConsequenceType.Average)
                {
                    combinedFailure /= participating.Count;
                    combinedExcess /= participating.Count;
                }

                if (tupleWeight > 0d)
                {
                    double entryProbability = combinationProbability * tupleWeight;
                    expectedFailure += entryProbability * combinedFailure;
                    if (recording)
                    {
                        failPoint!.Add(entryProbability, combinedFailure);
                        excessPoint!.Add(entryProbability, combinedExcess);
                        totalPoint!.Add(entryProbability, combinedFailure + complementNonFailure);
                        WidenSystemExtents(realization, typeIndex, complementNonFailure, Math.Max(combinedFailure, complementNonFailure));
                    }

                    if (contributionProbability != null)
                    {
                        double weightedEntry = weight * entryProbability;
                        double equalShare = 1d / participating.Count;
                        for (int p = 0; p < participating.Count; p++)
                        {
                            int componentIndex = participating[p];
                            double failureShare = tupleFailureSum > 0d ? tupleFailureValues![p] / tupleFailureSum : equalShare;
                            double excessShare = tupleExcessSum > 0d ? tupleExcessValues![p] / tupleExcessSum : equalShare;
                            contributionProbability[componentIndex] += weightedEntry * equalShare;
                            contributionFailure![componentIndex] += weightedEntry * combinedFailure * failureShare;
                            contributionExcess![componentIndex] += weightedEntry * combinedExcess * excessShare;
                        }
                    }
                }

                // Advance the odometer.
                int digit = 0;
                while (digit < participating.Count)
                {
                    branchPick[digit]++;
                    if (branchPick[digit] < outputsByType[participating[digit]][typeIndex].FailureConsequences.Count) break;
                    branchPick[digit] = 0;
                    digit++;
                }
                if (digit == participating.Count) break;
            }
        }

        /// <summary>
        /// Widens one consequence type's system extent slots.
        /// </summary>
        /// <param name="realization">The system realization.</param>
        /// <param name="typeIndex">The consequence-type position (0 is the primary).</param>
        /// <param name="minCandidate">The candidate minimum consequence.</param>
        /// <param name="maxCandidate">The candidate maximum consequence.</param>
        private static void WidenSystemExtents(SystemRealization realization, int typeIndex, double minCandidate, double maxCandidate)
        {
            if (typeIndex == 0)
            {
                realization.MinN = Math.Min(realization.MinN, minCandidate);
                realization.MaxN = Math.Max(realization.MaxN, maxCandidate);
            }
            else
            {
                realization.AdditionalMinN[typeIndex - 1] = Math.Min(realization.AdditionalMinN[typeIndex - 1], minCandidate);
                realization.AdditionalMaxN[typeIndex - 1] = Math.Max(realization.AdditionalMaxN[typeIndex - 1], maxCandidate);
            }
        }

        /// <summary>
        /// Combines every component's value under the joint-consequence rule.
        /// </summary>
        /// <param name="values">The per-component values.</param>
        /// <param name="rule">The combination rule.</param>
        /// <returns>The combined value.</returns>
        private static double CombineAll(double[] values, JointConsequenceType rule)
        {
            double combined = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                switch (rule)
                {
                    case JointConsequenceType.Additive:
                    case JointConsequenceType.Average:
                        combined += values[i];
                        break;
                    case JointConsequenceType.Maximum:
                        combined = Math.Max(combined, values[i]);
                        break;
                    case JointConsequenceType.Minimum:
                    default:
                        combined = Math.Min(combined, values[i]);
                        break;
                }
            }
            return rule == JointConsequenceType.Average ? combined / values.Length : combined;
        }

        /// <summary>
        /// Combines the non-participating (indicator zero) components' values under the
        /// joint-consequence rule; an empty complement yields zero (the v1.0 sentinel guard).
        /// </summary>
        /// <param name="values">The per-component values.</param>
        /// <param name="indicators">The combination's failure indicators.</param>
        /// <param name="rule">The combination rule.</param>
        /// <returns>The combined complement value.</returns>
        /// <remarks>
        /// Not delegated to the <c>Tools.Sum</c>/<c>Tools.Mean</c> indicator overloads: they take
        /// <c>IList</c>, which would make every element access in this per-evaluation kernel an
        /// interface dispatch, and Maximum/Minimum have no indicator overload.
        /// </remarks>
        private static double CombineComplement(double[] values, int[] indicators, JointConsequenceType rule)
        {
            double combined = 0d;
            int count = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (indicators[i] != 0) continue;
                if (count == 0)
                {
                    combined = values[i];
                }
                else
                {
                    switch (rule)
                    {
                        case JointConsequenceType.Additive:
                        case JointConsequenceType.Average:
                            combined += values[i];
                            break;
                        case JointConsequenceType.Maximum:
                            combined = Math.Max(combined, values[i]);
                            break;
                        case JointConsequenceType.Minimum:
                        default:
                            combined = Math.Min(combined, values[i]);
                            break;
                    }
                }
                count++;
            }
            if (count == 0) return 0d;
            return rule == JointConsequenceType.Average ? combined / count : combined;
        }

        #endregion

        #region Private Helpers â€” Percentile Post-Processing

        /// <summary>
        /// Builds the mean, median, and confidence-bound curve sets from the realization
        /// ensemble: loss exceedance percentiles on a shared consequence grid (the Total
        /// percentile curve is read from each realization's Total curve â€” the v1.0
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
            int additionalTypes = realizations[0].AdditionalCurves.Count;
            double tail = (1d - _options.ConfidenceIntervalWidth) / 2d;

            // Sequential extent reduction across the ensemble, per consequence type.
            double minN = double.MaxValue;
            double maxN = double.MinValue;
            var additionalMinN = new double[additionalTypes];
            var additionalMaxN = new double[additionalTypes];
            for (int k = 0; k < additionalTypes; k++)
            {
                additionalMinN[k] = double.MaxValue;
                additionalMaxN[k] = double.MinValue;
            }
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
                for (int k = 0; k < additionalTypes; k++)
                {
                    additionalMinN[k] = Math.Min(additionalMinN[k], realizations[i].AdditionalMinN[k]);
                    additionalMaxN[k] = Math.Max(additionalMaxN[k], realizations[i].AdditionalMaxN[k]);
                }
                for (int d = 0; d < componentCount; d++)
                {
                    minH[d] = Math.Min(minH[d], realizations[i].MinH[d]);
                    maxH[d] = Math.Max(maxH[d], realizations[i].MaxH[d]);
                }
            }
            if (!(maxN > minN) || double.IsInfinity(minN) || double.IsInfinity(maxN)) return;

            var lower = CreatePercentileRealization("Lower", componentCount, additionalTypes, realizations[0]);
            var upper = CreatePercentileRealization("Upper", componentCount, additionalTypes, realizations[0]);
            var median = CreatePercentileRealization("Median", componentCount, additionalTypes, realizations[0]);
            var mean = CreatePercentileRealization("Mean", componentCount, additionalTypes, realizations[0]);
            var targets = new[] { lower, upper, median, mean };

            // The shared consequence grid, descending (v1.0 orientation), per consequence type â€”
            // types live on their own magnitude scales.
            double gridMin = minN < 1d ? 0d : minN;
            var consequenceGrid = BuildDescendingGrid(gridMin, maxN, _options.LECOutputLength);

            // System and component LEC percentile curves for the five risk types, primary type.
            AssembleLecPercentiles(realizations, r => r.Curves, c => targets[c].Curves, consequenceGrid, tail, token);
            for (int d = 0; d < componentCount; d++)
            {
                int componentIndex = d;
                AssembleLecPercentiles(realizations,
                    r => r.Components[componentIndex].Curves,
                    c => targets[c].Components[componentIndex].Curves,
                    consequenceGrid, tail, token);

                int modeCount = realizations[0].Components[componentIndex].FailureModes.Count;
                for (int m = 0; m < modeCount; m++)
                {
                    int modeIndex = m;
                    AssembleLecPercentiles(realizations,
                        r => r.Components[componentIndex].FailureModes[modeIndex].Curves,
                        c => targets[c].Components[componentIndex].FailureModes[modeIndex].Curves,
                        consequenceGrid, tail, token);
                }

                // Hazard-profile percentiles on the component's hazard grid, per consequence
                // type (the hazard grid is type-independent).
                if (maxH[d] > minH[d])
                {
                    var hazardGrid = BuildDescendingGrid(minH[d], maxH[d], _options.LECOutputLength);
                    AssembleProfilePercentiles(realizations, componentIndex, c => c.Curves, hazardGrid, tail, targets,
                        primaryType: true, _options.LECOutputLength, token);
                    for (int k = 0; k < additionalTypes; k++)
                    {
                        int typeIndex = k;
                        AssembleProfilePercentiles(realizations, componentIndex, c => c.AdditionalCurves[typeIndex], hazardGrid, tail, targets,
                            primaryType: false, _options.LECOutputLength, token);
                    }
                }
            }

            // The additional consequence types, each on its own grid.
            for (int k = 0; k < additionalTypes; k++)
            {
                int typeIndex = k;
                if (!(additionalMaxN[k] > additionalMinN[k]) || double.IsInfinity(additionalMinN[k]) || double.IsInfinity(additionalMaxN[k]))
                {
                    continue;
                }
                double typeGridMin = additionalMinN[k] < 1d ? 0d : additionalMinN[k];
                var typeGrid = BuildDescendingGrid(typeGridMin, additionalMaxN[k], _options.LECOutputLength);

                AssembleLecPercentiles(realizations, r => r.AdditionalCurves[typeIndex], c => targets[c].AdditionalCurves[typeIndex], typeGrid, tail, token);
                for (int d = 0; d < componentCount; d++)
                {
                    int componentIndex = d;
                    AssembleLecPercentiles(realizations,
                        r => r.Components[componentIndex].AdditionalCurves[typeIndex],
                        c => targets[c].Components[componentIndex].AdditionalCurves[typeIndex],
                        typeGrid, tail, token);

                    int modeCount = realizations[0].Components[componentIndex].FailureModes.Count;
                    for (int m = 0; m < modeCount; m++)
                    {
                        int modeIndex = m;
                        AssembleLecPercentiles(realizations,
                            r => r.Components[componentIndex].FailureModes[modeIndex].AdditionalCurves[typeIndex],
                            c => targets[c].Components[componentIndex].FailureModes[modeIndex].AdditionalCurves[typeIndex],
                            typeGrid, tail, token);
                    }
                }
            }

            LowerRiskResults = lower;
            UpperRiskResults = upper;
            MedianRiskResults = median;
            MeanRiskResults = mean;
        }

        /// <summary>
        /// Creates an empty percentile realization shaped like the ensemble's realizations,
        /// including the additional consequence-type slots.
        /// </summary>
        /// <param name="name">The realization label.</param>
        /// <param name="componentCount">The component count.</param>
        /// <param name="additionalTypes">The number of additional consequence types.</param>
        /// <param name="template">A realization supplying the per-component failure-mode counts.</param>
        /// <returns>The shaped realization.</returns>
        private SystemRealization CreatePercentileRealization(string name, int componentCount, int additionalTypes, SystemRealization template)
        {
            var components = new List<ComponentRealization>(componentCount);
            for (int d = 0; d < componentCount; d++)
            {
                var componentRealization = new ComponentRealization(template.Components[d].FailureModes.Count)
                {
                    Name = template.Components[d].Name,
                };
                for (int m = 0; m < componentRealization.FailureModes.Count; m++)
                {
                    componentRealization.FailureModes[m].Name = template.Components[d].FailureModes[m].Name;
                    componentRealization.FailureModes[m].PathLabel = template.Components[d].FailureModes[m].PathLabel;
                }
                components.Add(componentRealization);
            }
            var realization = new SystemRealization(components) { Name = name };
            realization.EnsureAdditionalCurves(additionalTypes);
            StampConsequenceLabels(realization);
            return realization;
        }

        /// <summary>
        /// Stamps the declared consequence-type labels onto a realization â€” one entry per
        /// computed type including the primary (display metadata; positions past the declaration
        /// stamp blank).
        /// </summary>
        /// <param name="realization">The realization to stamp.</param>
        private void StampConsequenceLabels(SystemRealization realization)
        {
            realization.ConsequenceLabels.Clear();
            realization.ConsequenceUnits.Clear();
            int typeCount = 1 + realization.AdditionalCurves.Count;
            for (int k = 0; k < typeCount; k++)
            {
                if (k == 0)
                {
                    realization.ConsequenceLabels.Add(_specifiedConsequence);
                    realization.ConsequenceUnits.Add(_consequenceUnit);
                }
                else if (k - 1 < _additionalConsequenceTypes.Count)
                {
                    realization.ConsequenceLabels.Add(_additionalConsequenceTypes[k - 1].SpecifiedConsequence);
                    realization.ConsequenceUnits.Add(_additionalConsequenceTypes[k - 1].ConsequenceUnit);
                }
                else
                {
                    realization.ConsequenceLabels.Add(string.Empty);
                    realization.ConsequenceUnits.Add(string.Empty);
                }
            }
        }

        /// <summary>
        /// Stamps each component's end-state labels onto a realization (Phase 6.7 Q3,
        /// user-ratified): the mode name via the terminal-first label chain and the branch path
        /// descriptor, read from the frozen projection snapshot so labels can never drift from
        /// the sampled structure.
        /// </summary>
        /// <param name="realization">The realization to stamp.</param>
        private void StampModeLabels(SystemRealization realization)
        {
            for (int i = 0; i < _components.Count; i++)
            {
                var modes = _components[i].SampledProjection;
                if (modes == null) continue;
                var slots = realization.Components[i].FailureModes;
                int m = 0;
                for (int j = 0; j < modes.Count && m < slots.Count; j++)
                {
                    if (modes[j].IsNonFailureMode) continue;
                    slots[m].Name = SystemComponent.ModeLabel(modes[j], m);
                    slots[m].PathLabel = SystemComponent.ModePathLabel(modes[j]);
                    m++;
                }
            }
        }

        /// <summary>
        /// The declared consequence threshold for additional consequence type k (Phase 6.6), or
        /// NaN when none was declared — the run-scoped capture the measure sites read.
        /// </summary>
        /// <param name="typeIndex">The additional-type position (0 = the first additional type).</param>
        /// <returns>The declared threshold, or NaN.</returns>
        private double AdditionalThresholdAt(int typeIndex)
        {
            var thresholds = _runAdditionalThresholds;
            return thresholds != null && typeIndex < thresholds.Length ? thresholds[typeIndex] : double.NaN;
        }

        /// <summary>
        /// Builds a descending linear grid over [minimum, maximum] with the given ordinate count.
        /// </summary>
        /// <param name="minimum">The grid minimum.</param>
        /// <param name="maximum">The grid maximum.</param>
        /// <param name="count">The ordinate count (at least two).</param>
        /// <returns>The descending grid.</returns>
        /// <remarks>
        /// Not <c>Tools.Sequence</c>: that form accumulates its step and derives its length from
        /// it, where the percentile assembly needs exactly <paramref name="count"/> ordinates.
        /// </remarks>
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
        /// (log-log â€” v1.0 behavior), sorted for the percentile levels, and summed sequentially
        /// for the mean.
        /// </summary>
        /// <param name="realizations">The realization ensemble.</param>
        /// <param name="source">Selects the source curve set from a realization.</param>
        /// <param name="target">Selects the target curve set by percentile slot (0 lower, 1 upper, 2 median, 3 mean).</param>
        /// <param name="consequenceGrid">The shared descending consequence grid.</param>
        /// <param name="tail">The percentile tail level, (1 âˆ’ width)/2.</param>
        /// <param name="token">The run cancellation token.</param>
        private static void AssembleLecPercentiles(SystemRealization[] realizations,
            Func<SystemRealization, Curves> source, Func<int, Curves> target,
            double[] consequenceGrid, double tail, CancellationToken token)
        {
            AssemblePercentileCurve(realizations, r => { var c = source(r).Excess; return (c.LECConsequences, c.LECProbabilities); }, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).Excess.LECConsequences = x; target(slot).Excess.LECProbabilities = y; });
            AssemblePercentileCurve(realizations, r => { var c = source(r).Background; return (c.LECConsequences, c.LECProbabilities); }, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).Background.LECConsequences = x; target(slot).Background.LECProbabilities = y; });
            AssemblePercentileCurve(realizations, r => { var c = source(r).Total; return (c.LECConsequences, c.LECProbabilities); }, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).Total.LECConsequences = x; target(slot).Total.LECProbabilities = y; });
            AssemblePercentileCurve(realizations, r => { var c = source(r).Fail; return (c.LECConsequences, c.LECProbabilities); }, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).Fail.LECConsequences = x; target(slot).Fail.LECProbabilities = y; });
            AssemblePercentileCurve(realizations, r => { var c = source(r).NonFail; return (c.LECConsequences, c.LECProbabilities); }, consequenceGrid, tail, token,
                (slot, x, y) => { target(slot).NonFail.LECConsequences = x; target(slot).NonFail.LECProbabilities = y; });
        }

        /// <summary>
        /// Assembles one percentile curve family: each realization merge-walks the shared
        /// descending grid once with the monotone-cursor log-log interpolator
        /// (<see cref="Curve.InterpolateLogLogDescending"/> â€” bit-identical to the per-query
        /// Numerics interpolation it replaces, O(n + m) per realization instead of a binary
        /// search per ordinate, in parallel over realizations); then per grid ordinate
        /// (parallel, index-owned) the values are compacted in realization order, summed
        /// sequentially for the mean, and sorted for the lower/upper/median levels.
        /// Realizations whose source curve is empty are skipped, and an all-empty family leaves
        /// the targets empty.
        /// </summary>
        /// <param name="realizations">The realization ensemble.</param>
        /// <param name="curve">Selects the source curve's serialized arrays from a realization (X descending).</param>
        /// <param name="grid">The descending X grid.</param>
        /// <param name="tail">The percentile tail level.</param>
        /// <param name="token">The run cancellation token.</param>
        /// <param name="assign">Assigns the assembled arrays per percentile slot (0 lower, 1 upper, 2 median, 3 mean).</param>
        private static void AssemblePercentileCurve(SystemRealization[] realizations,
            Func<SystemRealization, (double[] Xs, double[] Ys)> curve, double[] grid, double tail, CancellationToken token,
            Action<int, double[], double[]> assign)
        {
            int realizationCount = realizations.Length;
            bool anySource = false;
            for (int i = 0; i < realizationCount; i++)
            {
                if (curve(realizations[i]).Xs.Length > 1)
                {
                    anySource = true;
                    break;
                }
            }
            if (!anySource) return;

            // Transposed fill: values[ordinate][realization], NaN marking skipped realizations.
            var values = new double[grid.Length][];
            for (int g = 0; g < grid.Length; g++)
            {
                values[g] = new double[realizationCount];
            }
            Parallel.For(0, realizationCount, new ParallelOptions { CancellationToken = token }, r =>
            {
                var (xs, ys) = curve(realizations[r]);
                if (xs.Length < 2)
                {
                    for (int g = 0; g < grid.Length; g++)
                    {
                        values[g][r] = double.NaN;
                    }
                    return;
                }
                int cursor = 1;
                for (int g = 0; g < grid.Length; g++)
                {
                    values[g][r] = Curve.InterpolateLogLogDescending(xs, ys, grid[g], ref cursor);
                }
            });

            var lowerValues = new double[grid.Length];
            var upperValues = new double[grid.Length];
            var medianValues = new double[grid.Length];
            var meanValues = new double[grid.Length];

            Parallel.For(0, grid.Length, new ParallelOptions { CancellationToken = token }, g =>
            {
                var row = values[g];
                var window = new double[realizationCount];
                double sum = 0d;
                int used = 0;
                for (int r = 0; r < realizationCount; r++)
                {
                    double value = row[r];
                    if (double.IsNaN(value)) continue;
                    window[used] = value;
                    sum += value;
                    used++;
                }
                if (used == 0) return;
                Array.Sort(window, 0, used);
                var trimmed = new double[used];
                Array.Copy(window, trimmed, used);
                lowerValues[g] = Statistics.Percentile(trimmed, tail, true);
                upperValues[g] = Statistics.Percentile(trimmed, 1d - tail, true);
                medianValues[g] = Statistics.Percentile(trimmed, 0.5d, true);
                meanValues[g] = sum / used;
            });

            assign(0, (double[])grid.Clone(), lowerValues);
            assign(1, (double[])grid.Clone(), upperValues);
            assign(2, (double[])grid.Clone(), medianValues);
            assign(3, (double[])grid.Clone(), meanValues);
        }

        /// <summary>
        /// Assembles the risk-profile percentiles for one component and one consequence type
        /// onto the four percentile realizations: the hazard-frequency, conditional-consequence,
        /// and cumulative-expected-consequence profiles on all five risk-type streams (v1.0
        /// banded all five â€” the Total-only interim was a parity gap, restored Phase 6.6), plus
        /// â€” for the primary consequence type â€” the Fail stream's cumulative failure probability
        /// on the hazard grid and the system response profile on its own log-spaced
        /// exceedance-probability grid.
        /// </summary>
        /// <param name="realizations">The realization ensemble.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="scope">Selects the consequence type's curve set from a component realization.</param>
        /// <param name="hazardGrid">The component's descending hazard grid.</param>
        /// <param name="tail">The percentile tail level.</param>
        /// <param name="targets">The percentile realizations (0 lower, 1 upper, 2 median, 3 mean).</param>
        /// <param name="primaryType">True when the scope selects the primary consequence type (enables the failure-stream profiles).</param>
        /// <param name="outputLength">The banded profile resolution (the LEC output length).</param>
        /// <param name="token">The run cancellation token.</param>
        private static void AssembleProfilePercentiles(SystemRealization[] realizations, int componentIndex,
            Func<ComponentRealization, Curves> scope, double[] hazardGrid, double tail, SystemRealization[] targets,
            bool primaryType, int outputLength, CancellationToken token)
        {
            Span<RiskType> streams = stackalloc RiskType[]
            {
                RiskType.Excess, RiskType.Background, RiskType.Total, RiskType.Fail, RiskType.NonFail,
            };
            foreach (RiskType stream in streams)
            {
                RiskType riskType = stream;
                AssemblePercentileCurve(realizations,
                    r => { var c = scope(r.Components[componentIndex]).GetCurve(riskType); return (c.HazardFrequencyHazards, c.HazardFrequencyProbabilities); }, hazardGrid, tail, token,
                    (slot, x, y) =>
                    {
                        var target = scope(targets[slot].Components[componentIndex]).GetCurve(riskType);
                        target.HazardFrequencyHazards = x;
                        target.HazardFrequencyProbabilities = y;
                    });
                AssemblePercentileCurve(realizations,
                    r => { var c = scope(r.Components[componentIndex]).GetCurve(riskType); return (c.HazardVsCenHazards, c.HazardVsCenConsequences); }, hazardGrid, tail, token,
                    (slot, x, y) =>
                    {
                        var target = scope(targets[slot].Components[componentIndex]).GetCurve(riskType);
                        target.HazardVsCenHazards = x;
                        target.HazardVsCenConsequences = y;
                    });
                AssemblePercentileCurve(realizations,
                    r => { var c = scope(r.Components[componentIndex]).GetCurve(riskType); return (c.HazardFrequencyHazards, c.CumulativeExpectedConsequences); }, hazardGrid, tail, token,
                    (slot, x, y) =>
                    {
                        // Y-only: the banded X axis is the hazard grid the frequency assembly
                        // already stamped on the target's HazardFrequencyHazards.
                        scope(targets[slot].Components[componentIndex]).GetCurve(riskType).CumulativeExpectedConsequences = y;
                    });
            }

            if (!primaryType) return;

            // The failure-stream profiles (primary type only): the cumulative failure
            // probability rides the hazard grid; the system response profile bands on its own
            // log-spaced exceedance grid spanning the engine's recorded probability domain.
            AssemblePercentileCurve(realizations,
                r => { var c = scope(r.Components[componentIndex]).Fail; return (c.HazardFrequencyHazards, c.CumulativeFailureProbabilities); }, hazardGrid, tail, token,
                (slot, x, y) =>
                {
                    scope(targets[slot].Components[componentIndex]).Fail.CumulativeFailureProbabilities = y;
                });
            var exceedanceGrid = BuildLogDescendingGrid(ProbabilityFloor, 1d - ProbabilityFloor, outputLength);
            AssemblePercentileCurve(realizations,
                r => { var c = scope(r.Components[componentIndex]).Fail; return (c.SystemResponseExceedanceProbabilities, c.SystemResponseProbabilities); }, exceedanceGrid, tail, token,
                (slot, x, y) =>
                {
                    var target = scope(targets[slot].Components[componentIndex]).Fail;
                    target.SystemResponseExceedanceProbabilities = x;
                    target.SystemResponseProbabilities = y;
                });
        }

        /// <summary>
        /// Builds a descending log-spaced grid over [minimum, maximum] with the given ordinate
        /// count â€” the system response profile's exceedance-probability ladder.
        /// </summary>
        /// <param name="minimum">The positive grid minimum.</param>
        /// <param name="maximum">The grid maximum (greater than the minimum).</param>
        /// <param name="count">The ordinate count (at least two).</param>
        /// <returns>The descending grid.</returns>
        private static double[] BuildLogDescendingGrid(double minimum, double maximum, int count)
        {
            var grid = new double[count];
            double logMaximum = Math.Log(maximum);
            double logStep = Math.Log(maximum / minimum) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                grid[i] = Math.Exp(logMaximum - i * logStep);
            }
            return grid;
        }

        #endregion
    }
}
