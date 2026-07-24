using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// <b>Multi-component system risk (Phase 4b):</b> the additive method assumes strictly
    /// independent components (ratified v0.13 — a supplied dependence is a validation error) and
    /// builds the true system loss exceedance curves by zero-inflated lattice convolution
    /// (<see cref="SystemConvolution"/>) — the exact enumeration of all component
    /// failure/non-failure combinations, where v1.0 combined two moments and produced no system
    /// curve at all; the reported stream probabilities keep the v1.0 system-state semantics
    /// (failure union, its complement). The joint method integrates the correlated hazard
    /// hypercube with VEGAS, enumerating real within-component pathway/branch entries across the
    /// component combinations instead of collapsing each component to its conditional mean (the
    /// documented v1.0 system-tail defect), accumulating recorded points across five recording
    /// passes with the masses self-normalized so the exhaustive budget is exactly one, and
    /// exposing the VEGAS power-transform tail focus (γ) with an automatic heuristic driven by a
    /// deterministic per-component failure-probability quadrature probe.
    /// </para>
    /// <para>
    /// <b>Reliability mode (Phase 4c):</b> <see cref="RiskAnalysisMode.Reliability"/> computes
    /// annualized failure probability only: consequence functions become optional (the relaxed
    /// mode-aware validation chain), the adaptive refinement objective is forced to
    /// <see cref="RiskIntegrand.TotalProbabilityOfFailure"/> (the configured objective applies to
    /// risk mode), and the annualized failure probability is read as the Fail stream's total
    /// probability at every level — failure mode, component, and system (the same containers
    /// serve both modes; a consequence-free model's curves are degenerate at zero consequence by
    /// construction).
    /// </para>
    /// <para>
    /// <b>Declared consequence-type axis (Phase 6.5, user-ratified):</b> the analysis declares
    /// its ordered consequence types — the primary through
    /// <see cref="SpecifiedConsequence"/>/<see cref="ConsequenceUnit"/> and every additional
    /// position through <see cref="AdditionalConsequenceTypes"/> — and validation strictly
    /// matches each component's failure and non-failure paths against the declaration: counts
    /// and order always, labels and units whenever both sides are non-blank (blank is a
    /// wildcard). The declaration is label metadata: it can never enter a canonical hash, so it
    /// never gates or rewires compute for a valid model — validity may depend on metadata
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
        /// [1e-16, 1 − 1e-16] in hazard non-exceedance probability (v1.0 constant).
        /// </summary>
        private const double ProbabilityFloor = 1e-16;

        /// <summary>
        /// The number of stratified hazard bins seeding the adaptive integrator (v1.0 constant).
        /// </summary>
        private const int HazardBinCount = 50;

        /// <summary>
        /// The number of independent VEGAS recording passes the joint path accumulates loss
        /// exceedance points across (v1.0 recorded a single pass — far too sparse for a tail
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
        /// off-diagonal constants; identity under independence). Run-scoped runtime state —
        /// rebuilt by every run, never serialized, never hashed.
        /// </summary>
        private MultivariateNormal? _jointMultivariateNormal;

        /// <summary>
        /// The component failure/non-failure indicator combinations for the joint method: 2^D
        /// rows over D components with the all-zero (no-failure) combination first — the v1.0
        /// engine-level layout. Run-scoped runtime state.
        /// </summary>
        private int[,]? _jointIndicators;

        /// <summary>
        /// The binomial subset counts over the components (how many combinations fail exactly k
        /// components). Run-scoped runtime state.
        /// </summary>
        private int[]? _jointBinomialCombinations;

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
        /// declared — reordering components can never move the system curves by
        /// association-rounding. Run-scoped runtime state.
        /// </summary>
        private int[]? _additiveConvolutionOrder;

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
        /// The declared consequence types beyond the primary, in order: entry k − 1 declares
        /// consequence-type position k of the analysis's axis (position 0 is the
        /// <see cref="SpecifiedConsequence"/>/<see cref="ConsequenceUnit"/> pair). Empty declares
        /// the legacy single-type axis. Validation strictly matches every component's failure and
        /// non-failure paths against the declared axis in risk mode; reliability mode carries no
        /// consequences and ignores it. Metadata — serialized, never hashed.
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
        /// mutually-exclusive probabilities normalized, exhaustive mass-balance drift) — the
        /// headless replacement for the v1.0 messenger surface.
        /// </summary>
        public IReadOnlyList<string> ComputationWarnings => _computationWarnings;

        #endregion

        #region IAnalysis Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Errors: no components; the additive method with a component-hazard dependence (the
        /// ratified v0.13 strict-independence redefinition — dependence belongs to the joint
        /// method); the joint method above twenty components (the VEGAS dimension limit), with a
        /// missing, mis-shaped, or non-positive-definite correlation matrix under the
        /// correlation-matrix dependency, or with a combination cross product beyond the
        /// guardrail; any failure or non-failure path that does not carry the declared
        /// consequence-type axis (count and order always; labels and units when both sides are
        /// non-blank — risk mode only); any projected failure mode with more than one response
        /// stage (until the event-tree phase); invalid options; and every component's own errors,
        /// aggregated with the component name. Component validation runs mode-aware: reliability
        /// relaxes exactly the consequence-content requirements (Phase 4c). Advisory: components
        /// whose driving hazards disagree on non-blank axis labels warn — one analysis models one
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
                    if (_components.Count > 20)
                    {
                        messages.Add($"Error: The joint system risk method supports at most 20 components (the VEGAS dimension limit); the analysis has {_components.Count}.");
                    }
                    if (_options.ComponentHazardDependency == DependencyType.CorrelationMatrix &&
                        !IsHazardCorrelationMatrixValid())
                    {
                        messages.Add($"Error: The component hazard correlation matrix must be a positive-definite {_components.Count}×{_components.Count} matrix (one row per component).");
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
                var modes = component.FailureModes;
                for (int m = 0; m < modes.Count; m++)
                {
                    if (modes[m].ResponseStages.Count > 1)
                    {
                        messages.Add($"Error: A failure mode of system component '{component.Name}' has {modes[m].ResponseStages.Count} response stages; multi-stage response composition is not yet supported by the risk engine — it lands with the event-tree phase.");
                    }
                }
            }

            ValidateConsequenceTypeAxis(messages);
            ValidateHazardAxisConsistency(messages);

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <summary>
        /// Strictly matches every component's failure and non-failure paths against the declared
        /// consequence-type axis (Phase 6.5, user-ratified): each path must carry exactly one
        /// consequence function per declared type, in declared order, and a non-blank declared
        /// label or unit must agree (ordinal, case-insensitive) with a non-blank function label
        /// at the same position — blank on either side is a wildcard. Risk mode only: a
        /// reliability model carries no consequences, so the axis is inert there. Null function
        /// entries are skipped here — they are already component-level errors.
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
        /// an ordinal case-insensitive comparison (the graph alignment comparison semantics) —
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
        /// Advisory only — labels are unhashed display metadata.
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
        /// method: present, one row per component, and positive definite (Cholesky) — the same
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
                // A decomposition failure means the matrix is not usable — exactly what this
                // check reports; the validation message carries the remedy.
                return false;
            }
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
                    var contentHashes = new byte[_components.Count][];
                    for (int i = 0; i < _components.Count; i++)
                    {
                        contentHashes[i] = _components[i].CanonicalHash();
                        int componentSeed = SeedHelpers.HashCombine(_options.PRNGSeed, contentHashes[i], _components[i].OccurrenceIndex);
                        _components[i].SetupSamplers(_options.Realizations, componentSeed, _options.SamplingScheme);
                    }

                    // The canonical component order (hashes sorted): the additive convolution
                    // associates in it, and the system seed base folds in it (§7.3 erratum) —
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
                    _jointSeedBase = systemSeed;

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
            var typesElement = new XElement(nameof(AdditionalConsequenceTypes));
            for (int i = 0; i < _additionalConsequenceTypes.Count; i++)
            {
                typesElement.Add(_additionalConsequenceTypes[i].ToXElement());
            }
            element.Add(typesElement);
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
        /// one (the silent v1.0 clamp made leaks invisible), on every consequence type.
        /// Reliability mode skips the check — a consequence-free model's total stream is
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
                additionalTypes = Math.Max(additionalTypes, sampledComponents[i].ConsequenceTypeCount - 1);
                componentRealizations.Add(new ComponentRealization(sampledComponents[i].FailureModeCount)
                {
                    Name = _components[i].Name,
                });
            }
            var realization = new SystemRealization(componentRealizations);
            realization.EnsureAdditionalCurves(additionalTypes);
            StampConsequenceLabels(realization);

            if (_components.Count > 1 && _options.SystemRiskMethod == SystemRiskType.JointRiskMethod)
            {
                IntegrateJointSystem(sampledComponents, componentRealizations, realization, flags, realizationIndex, token);
                realization.DumpMemory();
                return realization;
            }

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
                // the legacy clone site), cloned per consequence type.
                realization.Curves = componentRealizations[0].Curves.Clone();
                for (int k = 0; k < componentRealizations[0].AdditionalCurves.Count; k++)
                {
                    realization.AdditionalCurves[k] = componentRealizations[0].AdditionalCurves[k].Clone();
                }
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
        /// on the shared consequence lattice — the exact enumeration of all component
        /// failure/non-failure combinations — and the defective stream probabilities are then
        /// restored to the v1.0 system-state semantics: the failure union for Fail and Excess,
        /// its complement for NonFail (the convolution's own recorded mass measures "any positive
        /// consequence", which quantizes zero-valued events into the atom). The convolved system
        /// mean equals the sum of the component means by construction — the v1.0 additive answer,
        /// now with the full curve v1.0 never produced. The failure union folds the component
        /// probabilities in the canonical-hash component order — the same association the
        /// convolution uses — so declaration order cannot move the union even at the last bit
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

            // The failure union is type-independent — every type's defective streams carry the
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
                realization.AdditionalCurves[k].ComputeRiskMeasures(double.NaN, _options.Alpha);
            }

            // The system support tops out at the sum of the component maxima — widen the
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
        /// declared — component reordering stays bit-inert on the system curves.
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
        /// Runs the adaptive Gauss–Kronrod pass for one component: the integrator is an
        /// importance sampler whose recorded evaluations populate the risk points; its returned
        /// value is discarded and its evaluation count and true error estimate are kept as
        /// diagnostics.
        /// </summary>
        /// <param name="sampled">The sampled component.</param>
        /// <param name="componentRealization">The component's realization sink.</param>
        /// <param name="realization">The system realization (diagnostics).</param>
        /// <param name="flags">The realization's computational-warning flags.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the integration reports failure — an integrand exception was absorbed by
        /// the integrator (<c>ReportFailure</c> is false), so the recorded risk points are
        /// truncated and no result may be published. Surfacing the failure here keeps a faulted
        /// evaluation from silently reading as zero risk (the Phase 5 correction; the VEGAS
        /// call sites carry the same guard).
        /// </exception>
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
            if (integrator.Status == IntegrationStatus.Failure)
            {
                throw new InvalidOperationException($"The risk integration failed for system component '{sampled.Name}': an integrand evaluation threw and the recorded curves are incomplete. The analysis cannot publish results for this run.");
            }

            realization.FunctionEvaluations += integrator.FunctionEvaluations;
            realization.StandardError += integrator.StandardError / _components.Count;
        }

        /// <summary>
        /// The effective adaptive-refinement objective: reliability mode always refines on the
        /// failure probability (its natural pairing — a consequence-free model's consequence
        /// objectives are identically zero, which would defeat the adaptivity); risk mode uses
        /// the configured objective.
        /// </summary>
        private RiskIntegrand EffectiveIntegrand => _options.Mode == RiskAnalysisMode.Reliability
            ? RiskIntegrand.TotalProbabilityOfFailure
            : _options.RiskIntegrand;

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
            if (EffectiveIntegrand == RiskIntegrand.Balanced)
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
        /// Builds the plain 50-bin hazard stratification in probability space (v1.0 constants) —
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

        #region Private Helpers — Joint System Integration

        /// <summary>
        /// Prepares the run-scoped joint-method state: the correlated-hazard latent structure,
        /// the 2^D component combination caches (all-zero row first — the v1.0 engine-level
        /// layout), and, under the automatic tail-focus mode, the deterministic per-component
        /// failure-probability quadrature probe that sets the VEGAS power-transform target. A
        /// no-op outside the multi-component joint method.
        /// </summary>
        /// <param name="token">The run cancellation token.</param>
        /// <remarks>
        /// The ratified v0.13 heuristic harvested the target from the VEGAS warm-up itself; at
        /// implementation two facts forced this probe instead (recorded in the phase log): the
        /// automatic configuration resets the VEGAS bin count, which reallocates the importance
        /// grid — so γ must be set before the warm-up, not after it — and a γ = 1 Monte Carlo
        /// warm-up cannot observe the rare failure probabilities the target needs (that is the
        /// very problem the transform solves). The adaptive Gauss–Kronrod probe of
        /// AFP_i = ∫ P_F,i(p) dp on the mean sample is deterministic, costs about a thousand
        /// evaluations per component once per run, and measures the failure probabilities to
        /// quadrature accuracy.
        /// </remarks>
        private void PrepareJointSystem(CancellationToken token)
        {
            _jointMultivariateNormal = null;
            _jointIndicators = null;
            _jointBinomialCombinations = null;
            _jointTailTargetProbability = 1e-2;
            if (_components.Count < 2 || _options.SystemRiskMethod != SystemRiskType.JointRiskMethod)
            {
                return;
            }

            int d = _components.Count;
            _jointMultivariateNormal = BuildHazardMultivariateNormal(d);
            _jointIndicators = BuildSystemIndicators(d);
            _jointBinomialCombinations = BuildSystemBinomialCombinations(d);

            if (_options.VegasTailFocusMode == VegasTailFocusMode.Automatic)
            {
                double minimumFailureProbability = double.MaxValue;
                for (int i = 0; i < d; i++)
                {
                    token.ThrowIfCancellationRequested();
                    minimumFailureProbability = Math.Min(minimumFailureProbability, ProbeAnnualFailureProbability(_components[i]));
                }
                _jointTailTargetProbability = Math.Min(1e-2, Math.Max(1e-12, minimumFailureProbability * _options.Alpha));
            }
        }

        /// <summary>
        /// Builds the correlated component-hazard latent structure per the dependency option,
        /// with the exact v1.0 off-diagonal constants: identity (independent), <c>1 − √εmach</c>
        /// (perfectly positive), <c>−1/(D − 1) + √εmach</c> (perfectly negative), or the
        /// analysis-validated user matrix.
        /// </summary>
        /// <param name="dimension">The component count D.</param>
        /// <returns>The multivariate normal over the component hazard probabilities.</returns>
        private MultivariateNormal BuildHazardMultivariateNormal(int dimension)
        {
            var mean = new double[dimension];
            var covariance = new double[dimension, dimension];
            double offDiagonal;
            switch (_options.ComponentHazardDependency)
            {
                case DependencyType.PerfectlyPositive:
                    offDiagonal = 1d - Math.Sqrt(Tools.DoubleMachineEpsilon);
                    break;
                case DependencyType.PerfectlyNegative:
                    offDiagonal = -1d / (dimension - 1) + Math.Sqrt(Tools.DoubleMachineEpsilon);
                    break;
                case DependencyType.CorrelationMatrix:
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
                default:
                    offDiagonal = 0d;
                    break;
            }
            for (int i = 0; i < dimension; i++)
            {
                for (int j = 0; j < dimension; j++)
                {
                    covariance[i, j] = i == j ? 1d : offDiagonal;
                }
            }
            return new MultivariateNormal(mean, covariance);
        }

        /// <summary>
        /// Builds the engine-level failure/non-failure indicator combinations: 2^D rows over the
        /// D components, the all-zero (no-failure) combination first and the remaining rows in
        /// subset-size order — the layout <c>Probability.IndependentExclusive</c> enumerates and
        /// the v1.0 engine used.
        /// </summary>
        /// <param name="dimension">The component count D.</param>
        /// <returns>The indicator matrix.</returns>
        private static int[,] BuildSystemIndicators(int dimension)
        {
            var combinations = Factorial.AllCombinations(dimension);
            var indicators = new int[1 << dimension, dimension];
            for (int i = 0; i < combinations.GetLength(0); i++)
            {
                for (int j = 0; j < dimension; j++)
                {
                    indicators[i + 1, j] = combinations[i, j];
                }
            }
            return indicators;
        }

        /// <summary>
        /// Builds the binomial subset counts over the components: how many combinations fail
        /// exactly k of the D components, for k = 1..D.
        /// </summary>
        /// <param name="dimension">The component count D.</param>
        /// <returns>The subset counts.</returns>
        private static int[] BuildSystemBinomialCombinations(int dimension)
        {
            var counts = new int[dimension];
            for (int i = 1; i <= dimension; i++)
            {
                counts[i - 1] = (int)Factorial.BinomialCoefficient(dimension, i);
            }
            return counts;
        }

        /// <summary>
        /// The deterministic annual-failure-probability probe: integrates one component's
        /// combined failure probability over its hazard probability domain on the mean sample
        /// with adaptive Gauss–Kronrod — the one call site where the integral's returned value is
        /// the product.
        /// </summary>
        /// <param name="component">The component to probe (its samplers are already set up).</param>
        /// <returns>The component's annualized failure probability on the mean sample.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the probe integration reports failure — a swallowed integrand exception
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
            return Math.Min(1d, Math.Max(0d, integrator.Result));
        }

        /// <summary>
        /// Applies the VEGAS power-transform tail focus per the configured mode — always before
        /// the warm-up, because raising the bin count reallocates the importance grid (setting γ
        /// after the warm-up would discard it). Automatic uses the probe-derived target through
        /// <c>ConfigureForRareEvents</c>; manual applies the user's γ with the same bin-count and
        /// grid-damping adjustments when γ exceeds one; none leaves γ = 1 — sampling identical to
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
        /// enumerates the exclusive component failure/non-failure combinations, and — the v1.1
        /// correction of the documented v1.0 system-tail defect — crosses the failing components'
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
        /// <param name="realizationIndex">The realization index, or −1 for the mean pass.</param>
        /// <param name="token">The run cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when the VEGAS integration fails.</exception>
        /// <remarks>
        /// The legacy engine's <c>tPF</c> accumulator (incremented twice per combination when
        /// recording) is not carried forward — the failure union is the Fail stream's recorded
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
            var multivariateNormal = _jointMultivariateNormal
                ?? throw new InvalidOperationException("The joint-method state was not prepared. The run sequence must call PrepareJointSystem before computing realizations.");
            var indicators = _jointIndicators!;
            var binomialCombinations = _jointBinomialCombinations!;
            int typeCount = 1 + realization.AdditionalCurves.Count;

            // The integration extents: the hazard probability hypercube (v1.0 constants).
            var minimums = new double[d];
            var maximums = new double[d];
            for (int i = 0; i < d; i++)
            {
                minimums[i] = ProbabilityFloor;
                maximums[i] = 1d - ProbabilityFloor;
            }

            // Pre-allocated evaluation buffers — the integrand allocates nothing it controls.
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

            bool recording = false;
            double recordedWeightSum = 0d;

            double Integrand(double[] point, double weight)
            {
                if (token.IsCancellationRequested) return 0d;

                bool recordSecondary = recording && typeCount > 1;

                // Correlated hazard probabilities through the latent normal (Cholesky).
                var latent = multivariateNormal.InverseCDF(point);
                for (int i = 0; i < d; i++)
                {
                    double probability = Normal.StandardCDF(latent[i]);
                    probability = Math.Max(ProbabilityFloor, Math.Min(1d - ProbabilityFloor, probability));
                    hazardLevels[i] = sampledComponents[i].Hazard.InverseCDF(probability);

                    // The VEGAS weight is the recorded probability-mass coordinate (v1.0
                    // semantics — the component sinks record mass = weight directly).
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
                // hazard levels, component failures are independent — dependence enters only
                // through the correlated hazards (the v1.0 model).
                Probability.IndependentExclusive(failureProbabilities, binomialCombinations, indicators,
                    out var exclusiveProbabilities, out var exclusiveIndicators);

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
                        failPoints[0], excessPoints[0], totalPoints[0], realization, ref expectedFailure);
                    if (recordSecondary)
                    {
                        for (int k = 1; k < typeCount; k++)
                        {
                            double typeComplement = CombineComplement(nonFailureValuesByType[k], combination, _options.JointConsequences);
                            AccumulateJointCombinationEntries(k, combinationProbability, typeComplement,
                                participating, branchPick, outputsByType, failureProbabilities, recording,
                                failPoints[k], excessPoints[k], totalPoints[k], realization, ref secondaryDiscard);
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
            }

            // Build the component curves from the recorded masses (no mass re-derivation — the
            // VEGAS weights are the masses), then the system curves and measures, per
            // consequence type.
            for (int i = 0; i < componentRealizations.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                componentRealizations[i].CreateCurves(_options.LECOutputLength);
                componentRealizations[i].CreateProfiles();
                componentRealizations[i].ComputeRiskMeasures(_options.ConsequenceThreshold, _options.Alpha, _components[i].HazardThreshold);
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
                realization.AdditionalCurves[k].ComputeRiskMeasures(double.NaN, _options.Alpha);
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
        private void AccumulateJointCombinationEntries(int typeIndex, double combinationProbability, double complementNonFailure,
            List<int> participating, int[] branchPick, ComponentRiskOutput[][] outputsByType, double[] failureProbabilities,
            bool recording, RiskPoint? failPoint, RiskPoint? excessPoint, RiskPoint? totalPoint,
            SystemRealization realization, ref double expectedFailure)
        {
            Array.Clear(branchPick, 0, participating.Count);
            while (true)
            {
                double tupleWeight = 1d;
                double combinedFailure = 0d;
                double combinedExcess = 0d;
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

            // The shared consequence grid, descending (v1.0 orientation), per consequence type —
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
                    AssembleProfilePercentiles(realizations, componentIndex, c => c.Curves, hazardGrid, tail, targets, token);
                    for (int k = 0; k < additionalTypes; k++)
                    {
                        int typeIndex = k;
                        AssembleProfilePercentiles(realizations, componentIndex, c => c.AdditionalCurves[typeIndex], hazardGrid, tail, targets, token);
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
                components.Add(new ComponentRealization(template.Components[d].FailureModes.Count)
                {
                    Name = template.Components[d].Name,
                });
            }
            var realization = new SystemRealization(components) { Name = name };
            realization.EnsureAdditionalCurves(additionalTypes);
            StampConsequenceLabels(realization);
            return realization;
        }

        /// <summary>
        /// Stamps the declared consequence-type labels onto a realization — one entry per
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
        /// one component and one consequence type onto the four percentile realizations' Total
        /// streams (the reporting profiles — v1.0 scope).
        /// </summary>
        /// <param name="realizations">The realization ensemble.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="scope">Selects the consequence type's curve set from a component realization.</param>
        /// <param name="hazardGrid">The component's descending hazard grid.</param>
        /// <param name="tail">The percentile tail level.</param>
        /// <param name="targets">The percentile realizations (0 lower, 1 upper, 2 median, 3 mean).</param>
        /// <param name="token">The run cancellation token.</param>
        private static void AssembleProfilePercentiles(SystemRealization[] realizations, int componentIndex,
            Func<ComponentRealization, Curves> scope, double[] hazardGrid, double tail, SystemRealization[] targets,
            CancellationToken token)
        {
            AssemblePercentileCurve(realizations,
                r => scope(r.Components[componentIndex]).Total.HazardFrequency, hazardGrid, tail, token,
                (slot, x, y) =>
                {
                    scope(targets[slot].Components[componentIndex]).Total.HazardFrequencyHazards = x;
                    scope(targets[slot].Components[componentIndex]).Total.HazardFrequencyProbabilities = y;
                });
            AssemblePercentileCurve(realizations,
                r => scope(r.Components[componentIndex]).Total.HazardvsCEN, hazardGrid, tail, token,
                (slot, x, y) =>
                {
                    scope(targets[slot].Components[componentIndex]).Total.HazardVsCenHazards = x;
                    scope(targets[slot].Components[componentIndex]).Total.HazardVsCenConsequences = y;
                });
        }

        #endregion
    }
}
