using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Xml.Linq;
using Numerics;
using Numerics.Distributions;
using Numerics.Mathematics.LinearAlgebra;
using Numerics.Mathematics.SpecialFunctions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Systems.Components
{
    /// <summary>
    /// A system risk component: one hazard driving potential failure modes and their
    /// consequences, with the failure-mode combination options. The component owns its risk
    /// topology as a <see cref="ComponentGraph"/> — the formal DAG of hazard, transform,
    /// response, and consequence elements — and projects the engine-facing
    /// <see cref="FailureMode"/> chains from it deterministically.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>SystemComponent</c> with the option surface preserved (defaults,
    /// the CommonCause/MutuallyExclusive dependency coercion, the multivariate-normal off-diagonal
    /// constants <c>1 − √εmach</c> and <c>−1/(D − 1) + √εmach</c>, and the combination caches)
    /// and one ratified v1.1 restructuring (architecture doc v0.9): v1.0 stored failure modes as
    /// a collection projected by the UI-side <c>RiskDiagram</c> from canvas topology; v1.1 makes
    /// the graph itself the model — <see cref="Graph"/> is the persisted truth, and
    /// <see cref="FailureModes"/> is a fresh, deterministic projection snapshot on every access
    /// (cheap at component scale; capture the list once per run). <see cref="AddFailureMode"/>
    /// keeps near-v1.0 ergonomics by expanding a chain-style mode into wired graph elements.
    /// </para>
    /// <para>
    /// <b>Identity vs persistence:</b> <see cref="ToXElement()"/> persists the element graph
    /// (whose link attributes carry Guids and names), which therefore cannot be the seed-identity
    /// surface. <see cref="CanonicalHash"/> instead hashes a deterministic internal identity form
    /// — the options, the hazard content, and the projected failure modes in path order —
    /// realizing the architecture doc §5.5.3 recipe. Renaming or re-identifying elements, moving
    /// them on a canvas, or editing descriptions can never change the hash or re-roll Monte Carlo
    /// seeds; equal-content components hash identically and are disambiguated by
    /// <see cref="OccurrenceIndex"/> (§5.5.4).
    /// </para>
    /// <para>
    /// Improvements over v1.0, per the improve-on-port rule: <see cref="Clone"/> deep-copies via
    /// the serialization round-trip (v1.0 shared function references between clones); the
    /// correlation matrix serializes G17 row-major and actually round-trips (v1.0's read loop
    /// discarded every parsed value, so its persisted matrices were never restored); the matrix
    /// is serialized only under <see cref="DependencyType.CorrelationMatrix"/> (in the automatic
    /// dependency modes v1.0 lazily overwrote the field with the derived matrix, which would make
    /// the hash depend on whether the multivariate normal had been accessed); and the v1.0
    /// response-function-uniqueness error is dropped as obsolete under inline ownership and
    /// occurrence indexing. <c>ProfileHazardFunction</c> is deferred to the results design
    /// (open question Q-T), and the sampling machinery (<c>SetupSamplers</c>/<c>Sample</c>)
    /// lands with the risk engine.
    /// </para>
    /// </remarks>
    public class SystemComponent : INotifyPropertyChanged
    {
        #region Construction

        /// <summary>
        /// Initializes a system component with the v1.0 defaults: joint failures, maximum joint
        /// consequences, independent failure modes, a zero hazard threshold, and an empty graph.
        /// </summary>
        public SystemComponent()
        {
            _graph = new ComponentGraph();
            SubscribeGraph();
        }

        /// <summary>
        /// Initializes a system component from a hazard function (the v1.0 constructor shape):
        /// creates the graph's hazard root element wrapping the function.
        /// </summary>
        /// <param name="hazardFunction">The hazard function; may be null (assigned later).</param>
        /// <param name="failureModeMethod">How multiple failure modes combine. Default: joint failures.</param>
        /// <param name="jointConsequences">How joint-failure consequences combine. Default: maximum.</param>
        /// <param name="hazardThreshold">The hazard level threshold for assurance estimates. Default: 0.</param>
        public SystemComponent(IHazardFunction? hazardFunction,
            FailureModeMethod failureModeMethod = FailureModeMethod.JointFailures,
            JointConsequenceType jointConsequences = JointConsequenceType.Maximum,
            double hazardThreshold = 0d)
        {
            _graph = new ComponentGraph();
            SubscribeGraph();

            _name = hazardFunction is null ? "System Component" : $"System Component - {hazardFunction.Name}";
            _failureModeMethod = failureModeMethod;
            _jointConsequences = jointConsequences;
            _hazardThreshold = hazardThreshold;
            if (hazardFunction != null)
            {
                var root = new HazardElement(_graph.GetUniqueName(string.IsNullOrEmpty(hazardFunction.Name) ? "Hazard" : hazardFunction.Name))
                {
                    Function = hazardFunction,
                };
                _graph.AddElement(root);
            }
        }

        /// <summary>
        /// Restores a system component from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement()"/>.</param>
        /// <param name="resolver">
        /// The function resolver, required only when the graph was written
        /// <see cref="RiskSerializationMode.ByReference"/>; null for self-contained forms.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the serialized graph is inconsistent (see <see cref="ComponentGraph"/>).</exception>
        public SystemComponent(XElement xElement, IRiskFunctionResolver? resolver = null)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            _name = SerializationUtilities.ReadString(xElement, nameof(Name), "System Component");
            _failureModeMethod = SerializationUtilities.ReadEnum(xElement, nameof(FailureModeMethod), FailureModeMethod.JointFailures);
            _jointConsequences = SerializationUtilities.ReadEnum(xElement, nameof(JointConsequences), JointConsequenceType.Maximum);
            _failureModeDependency = SerializationUtilities.ReadEnum(xElement, nameof(FailureModeDependency), DependencyType.Independent);
            _hazardThreshold = SerializationUtilities.ReadDouble(xElement, nameof(HazardThreshold));
            _correlationMatrix = ParseMatrix(SerializationUtilities.ReadString(xElement, nameof(CorrelationMatrix)));

            var graphElement = xElement.Element(nameof(ComponentGraph));
            _graph = graphElement != null ? new ComponentGraph(graphElement, resolver) : new ComponentGraph();
            SubscribeGraph();
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Name"/>.
        /// </summary>
        private string _name = "System Component";

        /// <summary>
        /// The owned risk topology. Assigned once per constructor.
        /// </summary>
        private ComponentGraph _graph;

        /// <summary>
        /// Backing field for <see cref="FailureModeMethod"/>.
        /// </summary>
        private FailureModeMethod _failureModeMethod = FailureModeMethod.JointFailures;

        /// <summary>
        /// Backing field for <see cref="FailureModeDependency"/>.
        /// </summary>
        private DependencyType _failureModeDependency = DependencyType.Independent;

        /// <summary>
        /// Backing field for <see cref="JointConsequences"/>.
        /// </summary>
        private JointConsequenceType _jointConsequences = JointConsequenceType.Maximum;

        /// <summary>
        /// Backing field for <see cref="CorrelationMatrix"/>. In the automatic dependency modes
        /// the multivariate-normal update writes the derived matrix here (v1.0 behavior); only
        /// the <see cref="DependencyType.CorrelationMatrix"/> mode treats it as user content.
        /// </summary>
        private double[,]? _correlationMatrix;

        /// <summary>
        /// Backing field for <see cref="HazardThreshold"/>.
        /// </summary>
        private double _hazardThreshold;

        /// <summary>
        /// The failure-path count the combination caches were computed for; −1 forces a rebuild.
        /// </summary>
        private int _combosForCount = -1;

        /// <summary>
        /// Cached failure on/off combinations (see <see cref="FailureModeIndicators"/>).
        /// </summary>
        private int[,]? _failureModeCombinations;

        /// <summary>
        /// The failure-path count the binomial cache was computed for; −1 forces a rebuild.
        /// </summary>
        private int _binomialForCount = -1;

        /// <summary>
        /// Cached binomial subset counts (see <see cref="FailureModeBinomialCombinations"/>).
        /// </summary>
        private int[]? _failureModeBinomialCombinations;

        /// <summary>
        /// The failure-path count the multivariate normal was built for; −1 forces a rebuild.
        /// </summary>
        private int _mvnForCount = -1;

        /// <summary>
        /// Marks the multivariate normal stale after a dependency or matrix edit.
        /// </summary>
        private bool _mvnStale = true;

        /// <summary>
        /// The cached multivariate normal for failure-mode dependence.
        /// </summary>
        private MultivariateNormal? _mvn;

        /// <summary>
        /// Whether the correlation matrix passed its last positive-definiteness check.
        /// </summary>
        private bool _matrixValid = true;

        /// <summary>
        /// The component's display name. Identity metadata — serialized, never hashed.
        /// </summary>
        public string Name
        {
            get { return _name; }
            set
            {
                string coerced = value ?? string.Empty;
                if (!string.Equals(_name, coerced, StringComparison.Ordinal))
                {
                    _name = coerced;
                    RaisePropertyChange(nameof(Name));
                }
            }
        }

        /// <summary>
        /// Gets the component's risk topology: the typed, validated DAG of hazard, transform,
        /// response, and consequence elements. The graph is the persisted truth;
        /// <see cref="FailureModes"/> projects from it.
        /// </summary>
        public ComponentGraph Graph
        {
            get { return _graph; }
        }

        /// <summary>
        /// The hazard function — a view over the graph's hazard root element (v1.0 API). Getting
        /// returns the root's wrapped function; setting assigns it, creating the root element
        /// when the graph has none, and applies the v1.0 auto-naming ("System Component - …")
        /// while the component still carries a default name.
        /// </summary>
        public IHazardFunction? HazardFunction
        {
            get { return RootHazardElement?.Function; }
            set
            {
                var root = RootHazardElement;
                if (root == null)
                {
                    root = new HazardElement(_graph.GetUniqueName(value is null || string.IsNullOrEmpty(value.Name) ? "Hazard" : value.Name));
                    _graph.AddElement(root);
                }
                root.Function = value;
                if (value != null && (string.IsNullOrEmpty(_name) || string.Equals(_name, "System Component", StringComparison.Ordinal)))
                {
                    Name = $"System Component - {value.Name}";
                }
                RaisePropertyChange(nameof(HazardFunction));
            }
        }

        /// <summary>
        /// Gets the failure modes projected from the graph topology: one per consequence element
        /// in graph declared order, each the chain of stage transforms, responses, trailing
        /// transforms, and consequence functions along the terminal's path. A fresh snapshot is
        /// built on every access — always consistent with the graph; capture the list once per
        /// run. Structurally unsound graphs (no single root, cycles) project empty; validation
        /// reports why.
        /// </summary>
        public IReadOnlyList<FailureMode> FailureModes
        {
            get { return ProjectFailureModes(); }
        }

        /// <summary>
        /// How multiple failure modes combine. Selecting the common-cause or mutually-exclusive
        /// method coerces <see cref="FailureModeDependency"/> to independent (v1.0 behavior —
        /// those methods have no dependence model). Compute-relevant — hashed.
        /// </summary>
        public FailureModeMethod FailureModeMethod
        {
            get { return _failureModeMethod; }
            set
            {
                if (_failureModeMethod != value)
                {
                    _failureModeMethod = value;
                    RaisePropertyChange(nameof(FailureModeMethod));
                    if (_failureModeMethod == FailureModeMethod.CommonCauseFailures || _failureModeMethod == FailureModeMethod.MutuallyExclusive)
                    {
                        FailureModeDependency = DependencyType.Independent;
                    }
                }
            }
        }

        /// <summary>
        /// The statistical dependence between failure modes, realized as a Gaussian copula over
        /// the response probabilities. Compute-relevant — hashed.
        /// </summary>
        public DependencyType FailureModeDependency
        {
            get { return _failureModeDependency; }
            set
            {
                if (_failureModeDependency != value)
                {
                    _failureModeDependency = value;
                    _mvnStale = true;
                    RaisePropertyChange(nameof(FailureModeDependency));
                }
            }
        }

        /// <summary>
        /// How the consequences of jointly failing modes combine. Compute-relevant — hashed.
        /// </summary>
        public JointConsequenceType JointConsequences
        {
            get { return _jointConsequences; }
            set
            {
                if (_jointConsequences != value)
                {
                    _jointConsequences = value;
                    RaisePropertyChange(nameof(JointConsequences));
                }
            }
        }

        /// <summary>
        /// The failure-mode correlation matrix. User content under
        /// <see cref="DependencyType.CorrelationMatrix"/> (must be positive definite with one row
        /// per failure path — validated); in the automatic modes it holds the derived effective
        /// matrix after the multivariate normal is built (v1.0 behavior). Serialized and hashed
        /// only in the correlation-matrix mode.
        /// </summary>
        public double[,]? CorrelationMatrix
        {
            get { return _correlationMatrix; }
            set
            {
                _correlationMatrix = value;
                _mvnStale = true;
                RaisePropertyChange(nameof(CorrelationMatrix));
            }
        }

        /// <summary>
        /// The hazard level threshold for assurance estimates (the probability of hazard levels
        /// exceeding the threshold). Compute-relevant — hashed.
        /// </summary>
        public double HazardThreshold
        {
            get { return _hazardThreshold; }
            set
            {
                if (_hazardThreshold != value)
                {
                    _hazardThreshold = value;
                    RaisePropertyChange(nameof(HazardThreshold));
                }
            }
        }

        /// <summary>
        /// Determines whether the hazard and every projected failure mode carry no knowledge
        /// uncertainty.
        /// </summary>
        public bool IsDeterministic
        {
            get
            {
                var hazard = HazardFunction;
                if (hazard is not null && !hazard.IsDeterministic) return false;
                var modes = ProjectFailureModes();
                for (int i = 0; i < modes.Count; i++)
                {
                    if (!modes[i].IsDeterministic) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// The failure on/off indicator combinations over the failure paths
        /// (<c>Factorial.AllCombinations</c>), cached per failure-path count; null while the
        /// component has no failure paths. Capture before a realization loop.
        /// </summary>
        public int[,]? FailureModeIndicators
        {
            get
            {
                int count = FailurePathCount();
                if (count != _combosForCount)
                {
                    _failureModeCombinations = count > 0 ? Factorial.AllCombinations(count) : null;
                    _combosForCount = count;
                }
                return _failureModeCombinations;
            }
        }

        /// <summary>
        /// The binomial subset counts over the failure paths (how many combinations fail exactly
        /// k modes), cached per failure-path count; null while the component has no failure
        /// paths. Capture before a realization loop.
        /// </summary>
        public int[]? FailureModeBinomialCombinations
        {
            get
            {
                int count = FailurePathCount();
                if (count != _binomialForCount)
                {
                    if (count > 0)
                    {
                        var subsets = new int[count];
                        for (int i = 1; i <= count; i++)
                        {
                            subsets[i - 1] = (int)Math.Round(Factorial.BinomialCoefficient(count, i));
                        }
                        _failureModeBinomialCombinations = subsets;
                    }
                    else
                    {
                        _failureModeBinomialCombinations = null;
                    }
                    _binomialForCount = count;
                }
                return _failureModeBinomialCombinations;
            }
        }

        /// <summary>
        /// The multivariate normal realizing the failure-mode dependence (the Gaussian copula's
        /// latent distribution), built lazily per the dependency option with the exact v1.0
        /// off-diagonal constants: <c>1 − √εmach</c> (perfectly positive) and
        /// <c>−1/(D − 1) + √εmach</c> (perfectly negative — the most negative exchangeable
        /// equicorrelation that stays positive semi-definite). Null while the component has no
        /// failure paths or the correlation matrix is invalid.
        /// </summary>
        public MultivariateNormal? FailureModeMultivariateNormal
        {
            get
            {
                EnsureDependencyMatrixCurrent();
                return _matrixValid && FailurePathCount() > 0 ? _mvn : null;
            }
        }

        /// <summary>
        /// Ensures the failure-mode dependence machinery is current for the present failure-path
        /// count: rebuilds the multivariate normal when stale and — in the automatic dependency
        /// modes — back-fills <see cref="CorrelationMatrix"/> with the derived matrix. v1.0
        /// rebuilt eagerly on every ctor/setter/mode edit, so its compute paths always read a
        /// fresh matrix; v1.1 builds lazily, so the per-run freeze point
        /// (<see cref="SetupSamplers"/>) calls this before any sampling. Without this call the
        /// perfectly-negative mode's derived matrix never materializes on the compute path (the
        /// sampled component captures the raw <see cref="CorrelationMatrix"/> reference), which
        /// silently zeroed dependent combination kernels before the Phase 5 correction.
        /// </summary>
        internal void EnsureDependencyMatrixCurrent()
        {
            int count = FailurePathCount();
            if (_mvnStale || count != _mvnForCount)
            {
                UpdateMultivariateNormal(count);
            }
        }

        /// <summary>
        /// The component's occurrence index among identical-content components in the current
        /// analysis (architecture doc §5.5.4): the number of other components with the same
        /// canonical hash that precede it in the canonical ordering. Runtime-only — assigned by
        /// <see cref="AssignOccurrenceIndices"/> before each run; never serialized, never hashed.
        /// </summary>
        public int OccurrenceIndex { get; internal set; }

        /// <summary>
        /// The failure-mode projection captured by <see cref="SetupSamplers"/> for the current
        /// run — <see cref="FailureModes"/> builds a fresh projection on every access, so the
        /// engine must sample against one frozen snapshot. Runtime sampler state: never
        /// serialized, never hashed, never cloned.
        /// </summary>
        private IReadOnlyList<FailureMode>? _sampledModes;

        /// <summary>
        /// The snapshot's non-failure mode; null when the component has none.
        /// </summary>
        private FailureMode? _sampledNonFailureMode;

        /// <summary>
        /// Raised when a component property changes. Passive contract — headless callers need
        /// not subscribe.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Methods

        /// <summary>
        /// Expands a chain-style failure mode into wired graph elements (the near-v1.0 authoring
        /// path): stage transforms and responses become elements chained from the hazard root
        /// (created if absent), trailing transforms follow, and a terminal consequence element
        /// carries the mode's consequence functions. An explicit non-default consequence hazard
        /// position becomes a hazard-source binding on the terminal.
        /// </summary>
        /// <param name="failureMode">The chain-style mode to expand. The graph takes ownership of its function instances.</param>
        /// <exception cref="ArgumentNullException">Thrown when the mode is null.</exception>
        /// <remarks>
        /// The expansion is lossless for failure chains — projecting <see cref="FailureModes"/>
        /// afterwards reproduces the mode bit-identically (same canonical hash). A v1.0-style
        /// non-failure mode (transforms carried in <c>ResponseToConsequence</c>) normalizes to
        /// the canonical stage form on projection: identical math, canonicalized shape.
        /// <c>MultipleConsequences</c> re-derives from topology (a linear expansion is
        /// single-terminal).
        /// </remarks>
        public void AddFailureMode(FailureMode failureMode)
        {
            if (failureMode == null) throw new ArgumentNullException(nameof(failureMode));

            var root = RootHazardElement;
            if (root == null)
            {
                root = new HazardElement(_graph.GetUniqueName("Hazard"));
                _graph.AddElement(root);
            }

            IRiskElement upstream = root;
            var stageTransformElements = new List<TransformElement>();
            bool isNonFail = failureMode.IsNonFailureMode;
            for (int s = 0; s < failureMode.ResponseStages.Count; s++)
            {
                var stage = failureMode.ResponseStages[s];
                if (stage is null) continue;
                for (int t = 0; t < stage.Transforms.Count; t++)
                {
                    upstream = AddTransformElement(stage.Transforms[t], upstream, stageTransformElements);
                }
                if (!isNonFail)
                {
                    var responseElement = new ResponseElement(_graph.GetUniqueName(ElementName(stage.Response?.Name, "Response")))
                    {
                        Function = stage.Response,
                        Input = new RiskConnection(upstream),
                    };
                    _graph.AddElement(responseElement);
                    upstream = responseElement;
                }
            }
            for (int t = 0; t < failureMode.ResponseToConsequence.Count; t++)
            {
                upstream = AddTransformElement(failureMode.ResponseToConsequence[t], upstream, null);
            }

            var primary = failureMode.ConsequenceFunctions.Count > 0 ? failureMode.ConsequenceFunctions[0] : null;
            var terminal = new ConsequenceElement(_graph.GetUniqueName(ElementName(primary?.Name, "Consequence")))
            {
                Input = new RiskConnection(upstream),
                Functions = new ObservableCollection<IConsequenceFunction>(failureMode.ConsequenceFunctions),
            };

            if (failureMode.ConsequenceHazardPosition.HasValue &&
                failureMode.ConsequenceHazardPosition.Value != failureMode.TotalStageTransformCount)
            {
                int position = failureMode.ConsequenceHazardPosition.Value;
                IRiskElement? target = position == 0
                    ? root
                    : (position - 1 < stageTransformElements.Count ? stageTransformElements[position - 1] : null);
                if (target != null)
                {
                    terminal.HazardSource = new RiskConnection(target,
                        failureMode.ConsequenceHazardDimension == HazardDimension.Secondary ? 1 : 0);
                }
            }
            _graph.AddElement(terminal);
        }

        /// <summary>
        /// Determines whether the correlation matrix is valid for the current failure-path count
        /// (a matching dimension and positive definiteness via Cholesky decomposition). Always
        /// true outside the <see cref="DependencyType.CorrelationMatrix"/> mode.
        /// </summary>
        /// <returns>True when the matrix is usable.</returns>
        public bool IsCorrelationMatrixValid()
        {
            ValidateCorrelationMatrix(FailurePathCount());
            return _matrixValid;
        }

        /// <summary>
        /// Validates the component and reports any issues found.
        /// </summary>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item>
        /// <description><c>IsValid</c>: <c>true</c> if the component passes all validation checks; otherwise <c>false</c>.</description>
        /// </item>
        /// <item>
        /// <description><c>ValidationMessages</c>: messages describing validation errors ("Error: …", invalidating) and warnings ("Warning: …", advisory).</description>
        /// </item>
        /// </list>
        /// </returns>
        /// <remarks>
        /// Aggregates the graph's structural validation (which covers the hazard root, element
        /// functions, connectivity, path rules, bindings, and consequence alignment), the
        /// correlation-matrix check under the correlation-matrix dependency mode, and the
        /// projected failure modes' advisory label-continuity warnings (their errors are covered
        /// structurally by the graph; warnings are de-duplicated across modes sharing a chain
        /// prefix).
        /// </remarks>
        public (bool IsValid, List<string> ValidationMessages) Validate()
        {
            return Validate(RiskAnalysisMode.Risk);
        }

        /// <summary>
        /// Validates the component for the given analysis mode. Reliability mode (Phase 4c)
        /// threads through to the graph's and failure modes' relaxed consequence checks —
        /// consequence elements stay the structural terminals but need no functions; everything
        /// else is identical to <see cref="Validate()"/>.
        /// </summary>
        /// <param name="mode">The analysis mode the component is being validated for.</param>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item>
        /// <description><c>IsValid</c>: <c>true</c> if the component passes all validation checks; otherwise <c>false</c>.</description>
        /// </item>
        /// <item>
        /// <description><c>ValidationMessages</c>: messages describing validation errors ("Error: …", invalidating) and warnings ("Warning: …", advisory).</description>
        /// </item>
        /// </list>
        /// </returns>
        public (bool IsValid, List<string> ValidationMessages) Validate(RiskAnalysisMode mode)
        {
            var messages = new List<string>();
            messages.AddRange(_graph.Validate(mode).ValidationMessages);

            if (_failureModeDependency == DependencyType.CorrelationMatrix && !IsCorrelationMatrixValid())
            {
                messages.Add($"Error: The failure mode correlation matrix is not positive definite or does not match the failure-path count for system component '{Name}'.");
            }

            var modes = ProjectFailureModes();
            for (int i = 0; i < modes.Count; i++)
            {
                foreach (string message in modes[i].Validate(mode).ValidationMessages)
                {
                    if (message.StartsWith("Warning:", StringComparison.Ordinal) && !messages.Contains(message))
                    {
                        messages.Add(message);
                    }
                }
            }

            // The Q-W branch-explosion guardrail at the component level: joint failure pathways
            // take the cross product of the failing modes' exposure branches within one
            // consequence type (per-type marginal compute — types never cross), so the worst
            // type's product across failure modes is bounded — warn above 64, error above 1024.
            // The per-mode combination methods never cross modes, so the check applies to the
            // joint method only.
            if (_failureModeMethod == FailureModeMethod.JointFailures)
            {
                long pathwayBranches = WorstCasePathwayBranches(modes);
                if (pathwayBranches > 1024)
                {
                    messages.Add($"Error: The joint failure pathways' combined exposure branches exceed 1024 (the cross product of the failure modes' branch counts within the worst consequence type) for system component '{Name}'; reduce the mixture branch counts.");
                }
                else if (pathwayBranches > 64)
                {
                    messages.Add($"Warning: The joint failure pathways' combined exposure branches ({pathwayBranches}) exceed 64 for system component '{Name}'; the branch cross product grows compute cost accordingly.");
                }
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <summary>
        /// Estimates the worst-case number of failure pathway/branch entries this component can
        /// record at one hazard evaluation — the joint system-risk path's per-component
        /// combination width. Entries are recorded per consequence type and types never cross
        /// (per-type marginal compute), so the estimate is the worst type's bound: for the joint
        /// failure-mode method <c>Π (1 + bᵢ) − 1</c> over the failure modes' branch counts at
        /// that type (every failure subset crossed with its branch tuples); for the per-mode
        /// methods <c>Σ bᵢ</c>. The running product is capped so the estimate never overflows.
        /// </summary>
        /// <returns>The worst-case entry count, at least one.</returns>
        internal long EstimateRecordedFailureEntries()
        {
            const long cap = 1L << 40;
            var modes = ProjectFailureModes();
            int typeCount = ConsequenceTypeCount(modes);
            long estimate = 1;
            for (int k = 0; k < typeCount; k++)
            {
                long jointBound = 1;
                long perModeBound = 0;
                for (int i = 0; i < modes.Count; i++)
                {
                    if (modes[i].IsNonFailureMode) continue;
                    long branches = BranchCountAt(modes[i], k);
                    perModeBound += branches;
                    if (jointBound < cap) jointBound *= 1 + branches;
                }
                long typeEstimate = _failureModeMethod == FailureModeMethod.JointFailures ? jointBound - 1 : perModeBound;
                estimate = Math.Max(estimate, typeEstimate);
            }
            return Math.Max(1, Math.Min(cap, estimate));
        }

        /// <summary>
        /// The worst consequence type's joint-pathway branch cross product across the failure
        /// modes (per-type marginal compute — types never cross), capped just past the error
        /// guardrail so the product never overflows.
        /// </summary>
        /// <param name="modes">The projected failure modes.</param>
        /// <returns>The worst type's cross product, at least one.</returns>
        private static long WorstCasePathwayBranches(IReadOnlyList<FailureMode> modes)
        {
            int typeCount = ConsequenceTypeCount(modes);
            long worst = 1;
            for (int k = 0; k < typeCount; k++)
            {
                long pathwayBranches = 1;
                for (int i = 0; i < modes.Count && pathwayBranches <= 1024; i++)
                {
                    if (modes[i].IsNonFailureMode) continue;
                    pathwayBranches *= BranchCountAt(modes[i], k);
                }
                worst = Math.Max(worst, pathwayBranches);
            }
            return worst;
        }

        /// <summary>
        /// The number of consequence-type positions any failure path carries (misaligned counts
        /// are an analysis-level validation error; the guardrails bound whatever is present).
        /// </summary>
        /// <param name="modes">The projected failure modes.</param>
        /// <returns>The maximum consequence count across the failure paths, at least one.</returns>
        private static int ConsequenceTypeCount(IReadOnlyList<FailureMode> modes)
        {
            int typeCount = 1;
            for (int i = 0; i < modes.Count; i++)
            {
                if (!modes[i].IsNonFailureMode)
                {
                    typeCount = Math.Max(typeCount, modes[i].ConsequenceFunctions.Count);
                }
            }
            return typeCount;
        }

        /// <summary>
        /// A failure mode's exposure-branch count at one consequence-type position (one for a
        /// missing or null position — the mode then contributes no branching at that type).
        /// </summary>
        /// <param name="mode">The failure mode.</param>
        /// <param name="position">The consequence-type position.</param>
        /// <returns>The branch count, at least one.</returns>
        private static long BranchCountAt(FailureMode mode, int position)
        {
            var consequence = position < mode.ConsequenceFunctions.Count ? mode.ConsequenceFunctions[position] : null;
            return Math.Max(1, consequence?.CountExposureBranches() ?? 1);
        }

        /// <summary>
        /// Computes the component's canonical SHA-256 content hash — the stable, name-free
        /// identity that content-based Monte Carlo seeding derives from. Hashes the internal
        /// identity form (options + hazard content + projected failure modes in path order), NOT
        /// the persisted element graph, so element names, ids, positions, and link attributes
        /// can never perturb seeds.
        /// </summary>
        /// <returns>The 32-byte SHA-256 hash of the canonicalized identity form.</returns>
        public byte[] CanonicalHash()
        {
            return CanonicalContentHasher.Hash(BuildIdentityXElement(), CanonicalizationRules.ModelRules);
        }

        /// <summary>
        /// Assigns occurrence indices across an analysis's components (architecture doc §5.5.4
        /// reference implementation): sort by (canonical hash via
        /// <see cref="ByteArrayComparer.Instance"/>, declared index), then number each
        /// equal-hash bucket 0..n−1. Recomputed before every run; never persisted.
        /// </summary>
        /// <param name="components">The analysis's components in declared order.</param>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the list contains a null component.</exception>
        public static void AssignOccurrenceIndices(IReadOnlyList<SystemComponent> components)
        {
            if (components == null) throw new ArgumentNullException(nameof(components));

            int count = components.Count;
            var hashes = new byte[count][];
            var order = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (components[i] == null) throw new ArgumentException("The component list contains a null entry.", nameof(components));
                hashes[i] = components[i].CanonicalHash();
                order[i] = i;
            }

            Array.Sort(order, (a, b) =>
            {
                int comparison = ByteArrayComparer.Instance.Compare(hashes[a], hashes[b]);
                return comparison != 0 ? comparison : a.CompareTo(b);
            });

            int occurrence = 0;
            for (int k = 0; k < count; k++)
            {
                occurrence = k > 0 && ByteArrayComparer.Instance.Compare(hashes[order[k - 1]], hashes[order[k]]) == 0
                    ? occurrence + 1
                    : 0;
                components[order[k]].OccurrenceIndex = occurrence;
            }
        }

        /// <summary>
        /// Sets up the component's samplers for a run: captures the failure-mode projection once
        /// (the frozen snapshot every realization samples against), then walks the hazard and
        /// every projected mode in order, seeding each distinct function instance exactly once
        /// with a content-derived seed
        /// (<c>SeedHelpers.HashCombine(componentSeed, function.CanonicalHash(), ordinal)</c>).
        /// </summary>
        /// <param name="sampleSize">The realization count N.</param>
        /// <param name="componentSeed">
        /// The component's content-derived seed —
        /// <c>SeedHelpers.HashCombine(analysisSeed, CanonicalHash(), OccurrenceIndex)</c>
        /// (architecture doc §5.5.4), computed by the analysis after
        /// <see cref="AssignOccurrenceIndices"/>.
        /// </param>
        /// <param name="scheme">The knowledge-uncertainty sampling scheme.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the sample size is not positive.</exception>
        /// <remarks>
        /// Ordinals advance per walk position (every encounter); a function instance referenced
        /// at several positions is seeded at its first canonical position only and draws the
        /// identical realizations everywhere it appears — one shared instance is one knowledge
        /// quantity (the single-owner rule; equal-content distinct instances get different
        /// ordinals and draw independently). Consequence functions are not walked: their shared
        /// knowledge percentiles come from each mode's coupling matrix (Q-N). Forward rule for
        /// composite hazards/responses (Phase 9): the walk seeds cluster roots only, a root's
        /// own <c>SetupSampler</c> owns its subtree, and the dedup set must absorb subtree
        /// members. This is also the per-run freeze point that materializes the effective
        /// failure-mode dependency matrix (<see cref="EnsureDependencyMatrixCurrent"/>) so the
        /// automatic modes' derived matrices are current before the first sample.
        /// </remarks>
        public void SetupSamplers(int sampleSize, int componentSeed, SamplingScheme scheme)
        {
            if (sampleSize <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSize), "The sample size must be positive.");

            // Materialize the effective failure-mode dependency matrix before any sampling: the
            // sampled component captures the raw correlation-matrix reference, and the automatic
            // modes derive theirs here (v1.0 rebuilt eagerly on every edit; v1.1 refreshes at
            // this single-threaded per-run freeze point instead).
            EnsureDependencyMatrixCurrent();

            var modes = ProjectFailureModes();
            _sampledModes = modes;
            _sampledNonFailureMode = null;
            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i].IsNonFailureMode)
                {
                    _sampledNonFailureMode = modes[i];
                    break;
                }
            }

            var seededFunctions = new HashSet<IRiskFunction>(ReferenceEqualityComparer.Instance);
            int ordinal = 0;
            var hazard = HazardFunction;
            if (hazard != null)
            {
                if (seededFunctions.Add(hazard))
                {
                    hazard.SetupSampler(sampleSize, SeedHelpers.HashCombine(componentSeed, hazard.CanonicalHash(), ordinal), scheme);
                }
                ordinal++;
            }
            for (int i = 0; i < modes.Count; i++)
            {
                ordinal = modes[i].SetupSamplers(sampleSize, componentSeed, ordinal, scheme, seededFunctions);
            }
        }

        /// <summary>
        /// Samples the component for one realization against the snapshot captured by
        /// <see cref="SetupSamplers"/>.
        /// </summary>
        /// <param name="realizationIndex">The realization index, or −1 for the mean functions.</param>
        /// <returns>The sampled component.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown before <see cref="SetupSamplers"/> has run, or when the component has no
        /// hazard function.
        /// </exception>
        public SampledComponent Sample(int realizationIndex = -1)
        {
            if (_sampledModes == null)
            {
                throw new InvalidOperationException("SetupSamplers() must be called before sampling.");
            }
            return new SampledComponent(this, _sampledModes, _sampledNonFailureMode, realizationIndex);
        }

        /// <summary>
        /// Creates a deep, isolated copy via the serialization round-trip. An improvement over
        /// v1.0, which shared function references between clones. The occurrence index is
        /// runtime state and is not carried.
        /// </summary>
        /// <returns>The copied component.</returns>
        public SystemComponent Clone()
        {
            return new SystemComponent(ToXElement());
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes the component: the option attributes and the owned element graph. This is
        /// the persistence surface; the canonical hash uses the internal identity form instead
        /// (see <see cref="CanonicalHash"/>). The correlation matrix serializes (G17 row-major,
        /// rows ';'-separated, values ','-separated) only under the correlation-matrix dependency
        /// mode — in the automatic modes the matrix is derived state, and serializing it would
        /// make the persisted form depend on whether the multivariate normal had been built.
        /// </summary>
        /// <returns>The serialized form.</returns>
        public XElement ToXElement()
        {
            return ToXElement(RiskSerializationMode.SelfContained);
        }

        /// <summary>
        /// Serializes the component in the given mode. The mode governs only how the graph's
        /// wrapped functions are written; the component's own options are unaffected, and so is
        /// <see cref="CanonicalHash"/>, which hashes the projected failure modes rather than the
        /// persisted graph. A component therefore seeds identically however it was persisted.
        /// </summary>
        /// <param name="mode">The serialization mode.</param>
        /// <returns>The serialized form.</returns>
        public XElement ToXElement(RiskSerializationMode mode)
        {
            var element = new XElement(nameof(SystemComponent));
            element.SetAttributeValue(nameof(Name), _name);
            element.SetAttributeValue(nameof(FailureModeMethod), _failureModeMethod.ToString());
            element.SetAttributeValue(nameof(JointConsequences), _jointConsequences.ToString());
            element.SetAttributeValue(nameof(FailureModeDependency), _failureModeDependency.ToString());
            element.SetAttributeValue(nameof(HazardThreshold), SerializationUtilities.FormatDouble(_hazardThreshold));
            element.SetAttributeValue(nameof(CorrelationMatrix),
                _failureModeDependency == DependencyType.CorrelationMatrix ? FormatMatrix(_correlationMatrix) : string.Empty);
            element.Add(_graph.ToXElement(mode));
            return element;
        }

        /// <summary>
        /// Enumerates the distinct input functions this component's graph wraps, in declared
        /// element order. The dependency set a consuming layer persists alongside a by-reference
        /// component, and the answer to "what uses this function?" before one is deleted.
        /// </summary>
        /// <returns>The referenced functions, each appearing once.</returns>
        public IEnumerable<IRiskFunction> GetReferencedFunctions()
        {
            return _graph.GetReferencedFunctions();
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// The graph's hazard root element, or null while the graph has none.
        /// </summary>
        private HazardElement? RootHazardElement
        {
            get
            {
                foreach (var hazard in _graph.GetElements<HazardElement>())
                {
                    return hazard;
                }
                return null;
            }
        }

        /// <summary>
        /// Builds the canonical identity form realizing the §5.5.3 recipe: the option attributes,
        /// the hazard content inline, and the projected failure modes in path order. Identity
        /// metadata inside (names, labels) is stripped by the hasher; element ids, positions, and
        /// link attributes never appear at all.
        /// </summary>
        /// <returns>The identity form.</returns>
        private XElement BuildIdentityXElement()
        {
            var element = new XElement(nameof(SystemComponent));
            element.SetAttributeValue(nameof(FailureModeMethod), _failureModeMethod.ToString());
            element.SetAttributeValue(nameof(JointConsequences), _jointConsequences.ToString());
            element.SetAttributeValue(nameof(FailureModeDependency), _failureModeDependency.ToString());
            element.SetAttributeValue(nameof(HazardThreshold), SerializationUtilities.FormatDouble(_hazardThreshold));
            element.SetAttributeValue(nameof(CorrelationMatrix),
                _failureModeDependency == DependencyType.CorrelationMatrix ? FormatMatrix(_correlationMatrix) : string.Empty);

            var hazard = HazardFunction;
            if (hazard != null)
            {
                element.Add(new XElement(nameof(HazardFunction), hazard.ToXElement()));
            }

            var modes = new XElement(nameof(FailureModes));
            var projected = ProjectFailureModes();
            for (int i = 0; i < projected.Count; i++)
            {
                modes.Add(projected[i].ToXElement());
            }
            element.Add(modes);
            return element;
        }

        /// <summary>
        /// Projects the failure modes from the graph topology: one per consequence element in
        /// declared order, walking each terminal's root-first path and classifying transforms
        /// into stages around the responses (a response-free path becomes the canonical
        /// non-failure stage form). Structurally unsound graphs project empty.
        /// </summary>
        /// <returns>The projected modes, parent-wired.</returns>
        private List<FailureMode> ProjectFailureModes()
        {
            var modes = new List<FailureMode>();
            var root = RootHazardElement;
            if (root == null) return modes;
            int hazardCount = 0;
            foreach (var _ in _graph.GetElements<HazardElement>()) hazardCount++;
            if (hazardCount != 1) return modes;
            if (!_graph.TopologicalSort()) return modes;

            foreach (var terminal in _graph.GetElements<ConsequenceElement>())
            {
                var path = _graph.GetUpstreamPath(terminal);
                if (path.Count == 0 || !ReferenceEquals(path[0], root)) continue;
                modes.Add(BuildFailureMode(terminal, path));
            }
            return modes;
        }

        /// <summary>
        /// Builds one failure mode from a terminal's root-first path: transforms accumulate into
        /// the pending chain, each response element closes a stage, transforms after the last
        /// response become the trailing chain, and the terminal's functions become the ordered
        /// consequence list. The terminal's hazard-source binding projects to a chain position
        /// and dimension; the multiple-consequences flag derives from the last response's
        /// fan-out.
        /// </summary>
        /// <param name="terminal">The path's consequence element.</param>
        /// <param name="path">The root-first path ending with the terminal.</param>
        /// <returns>The projected failure mode, parent-wired.</returns>
        private FailureMode BuildFailureMode(ConsequenceElement terminal, IReadOnlyList<IRiskElement> path)
        {
            var stages = new List<ResponseStage>();
            var pending = new List<ITransformFunction>();
            ResponseElement? lastResponseElement = null;
            for (int i = 1; i < path.Count - 1; i++)
            {
                if (path[i] is TransformElement transformElement)
                {
                    pending.Add(transformElement.Function!);
                }
                else if (path[i] is ResponseElement responseElement)
                {
                    stages.Add(new ResponseStage(pending, responseElement.Function));
                    pending = new List<ITransformFunction>();
                    lastResponseElement = responseElement;
                }
            }

            List<ResponseStage> finalStages;
            List<ITransformFunction> trailing;
            if (lastResponseElement == null)
            {
                // The non-failure path: all transforms in the canonical stage form, so any
                // upstream signal position stays addressable by the consequence binding.
                finalStages = new List<ResponseStage> { new ResponseStage(pending, new NonFailResponse()) };
                trailing = new List<ITransformFunction>();
            }
            else
            {
                finalStages = stages;
                trailing = pending;
            }

            var mode = new FailureMode(finalStages, trailing, new List<IConsequenceFunction>(terminal.Functions))
            {
                Parent = this,
            };

            if (terminal.HazardSource != null)
            {
                var target = terminal.HazardSource.Source;
                int cursor = 0;
                int position = -1;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    if (path[i] is HazardElement && ReferenceEquals(path[i], target))
                    {
                        position = 0;
                        break;
                    }
                    if (path[i] is TransformElement)
                    {
                        cursor++;
                        if (ReferenceEquals(path[i], target))
                        {
                            position = cursor;
                            break;
                        }
                    }
                }
                if (position >= 0)
                {
                    mode.ConsequenceHazardPosition = position;
                    mode.ConsequenceHazardDimension = terminal.HazardSource.SourcePort == 1
                        ? HazardDimension.Secondary
                        : HazardDimension.Primary;
                }
                // Off-path bindings leave the default; graph validation reports them.
            }

            if (lastResponseElement != null)
            {
                int fanOut = 0;
                foreach (var _ in _graph.GetDownstreamElements(lastResponseElement)) fanOut++;
                mode.MultipleConsequences = fanOut >= 2;
            }
            return mode;
        }

        /// <summary>
        /// Counts the failure paths: terminals whose root-first path contains a response element
        /// (the v1.0 non-non-fail failure-mode count driving the combination caches and the
        /// multivariate normal's dimension).
        /// </summary>
        /// <returns>The failure-path count.</returns>
        private int FailurePathCount()
        {
            var root = RootHazardElement;
            if (root == null) return 0;

            int count = 0;
            foreach (var terminal in _graph.GetElements<ConsequenceElement>())
            {
                var path = _graph.GetUpstreamPath(terminal);
                if (path.Count == 0 || !ReferenceEquals(path[0], root)) continue;
                for (int i = 1; i < path.Count - 1; i++)
                {
                    if (path[i] is ResponseElement)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Rebuilds the multivariate normal for the failure-mode dependence — the exact v1.0
        /// construction: zero means; unit diagonal; off-diagonals per the dependency option
        /// (identity, <c>1 − √εmach</c>, <c>−1/(D − 1) + √εmach</c>, or the user matrix). In the
        /// automatic modes the derived matrix is written back to the correlation-matrix field
        /// (v1.0 behavior); it never serializes from those modes.
        /// </summary>
        /// <param name="dimension">The failure-path count D.</param>
        private void UpdateMultivariateNormal(int dimension)
        {
            _mvnForCount = dimension;
            _mvnStale = false;
            _mvn = null;
            if (dimension <= 0)
            {
                _matrixValid = true;
                return;
            }

            var mu = new double[dimension];
            var sigma = new double[dimension, dimension];
            switch (_failureModeDependency)
            {
                case DependencyType.Independent:
                    for (int i = 0; i < dimension; i++)
                    {
                        for (int j = 0; j < dimension; j++)
                        {
                            sigma[i, j] = i == j ? 1d : 0d;
                        }
                    }
                    _correlationMatrix = sigma;
                    break;
                case DependencyType.PerfectlyPositive:
                    for (int i = 0; i < dimension; i++)
                    {
                        for (int j = 0; j < dimension; j++)
                        {
                            sigma[i, j] = i == j ? 1d : 1d - Math.Sqrt(Tools.DoubleMachineEpsilon);
                        }
                    }
                    _correlationMatrix = sigma;
                    break;
                case DependencyType.PerfectlyNegative:
                    double minimumRho = -1d / (dimension - 1) + Math.Sqrt(Tools.DoubleMachineEpsilon);
                    for (int i = 0; i < dimension; i++)
                    {
                        for (int j = 0; j < dimension; j++)
                        {
                            sigma[i, j] = i == j ? 1d : minimumRho;
                        }
                    }
                    _correlationMatrix = sigma;
                    break;
                case DependencyType.CorrelationMatrix:
                    if (_correlationMatrix == null || _correlationMatrix.GetLength(0) != dimension || _correlationMatrix.GetLength(1) != dimension)
                    {
                        _matrixValid = false;
                        return;
                    }
                    for (int i = 0; i < dimension; i++)
                    {
                        for (int j = 0; j < dimension; j++)
                        {
                            sigma[i, j] = _correlationMatrix[i, j];
                        }
                    }
                    break;
            }

            ValidateCorrelationMatrix(dimension);
            if (!_matrixValid) return;

            _mvn = new MultivariateNormal(mu, sigma);
        }

        /// <summary>
        /// Validates the correlation matrix for the correlation-matrix dependency mode: the
        /// dimension must match the failure-path count and the matrix must be positive definite
        /// (Cholesky). The automatic modes always pass (their matrices are constructed valid).
        /// </summary>
        /// <param name="dimension">The failure-path count D.</param>
        private void ValidateCorrelationMatrix(int dimension)
        {
            _matrixValid = true;
            if (_failureModeDependency != DependencyType.CorrelationMatrix) return;

            if (_correlationMatrix == null || _correlationMatrix.GetLength(0) != dimension || _correlationMatrix.GetLength(1) != dimension)
            {
                _matrixValid = false;
                return;
            }
            try
            {
                var cholesky = new CholeskyDecomposition(new Matrix(_correlationMatrix));
                if (!cholesky.IsPositiveDefinite) _matrixValid = false;
            }
            catch (Exception)
            {
                // A failed decomposition means the matrix is not positive definite — the exact
                // v1.0 treatment of the numerical failure path.
                _matrixValid = false;
            }
        }

        /// <summary>
        /// Adds a transform element wrapping a function, wired from the current upstream element.
        /// </summary>
        /// <param name="function">The transform function to wrap; null entries are carried (validation reports them).</param>
        /// <param name="upstream">The element the new transform consumes.</param>
        /// <param name="stageRegistry">When non-null, collects the created element for binding-position mapping (stage transforms only).</param>
        /// <returns>The created element (the new upstream).</returns>
        private TransformElement AddTransformElement(ITransformFunction? function, IRiskElement upstream, List<TransformElement>? stageRegistry)
        {
            var element = new TransformElement(_graph.GetUniqueName(ElementName(function?.Name, "Transform")))
            {
                Function = function,
                Input = new RiskConnection(upstream),
            };
            _graph.AddElement(element);
            stageRegistry?.Add(element);
            return element;
        }

        /// <summary>
        /// Chooses an element display name: the wrapped function's name when present, otherwise
        /// the role fallback.
        /// </summary>
        /// <param name="functionName">The wrapped function's name; may be null or empty.</param>
        /// <param name="fallback">The role fallback ("Transform", "Response", "Consequence").</param>
        /// <returns>The chosen base name (uniqueness is applied by the graph).</returns>
        private static string ElementName(string? functionName, string fallback)
        {
            return string.IsNullOrEmpty(functionName) ? fallback : functionName!;
        }

        /// <summary>
        /// Formats a matrix as G17 invariant text: rows ';'-separated, values ','-separated.
        /// </summary>
        /// <param name="matrix">The matrix; null or empty formats as empty text.</param>
        /// <returns>The serialized text.</returns>
        private static string FormatMatrix(double[,]? matrix)
        {
            if (matrix == null || matrix.GetLength(0) == 0) return string.Empty;

            var builder = new StringBuilder();
            for (int i = 0; i < matrix.GetLength(0); i++)
            {
                if (i > 0) builder.Append(';');
                for (int j = 0; j < matrix.GetLength(1); j++)
                {
                    if (j > 0) builder.Append(',');
                    builder.Append(SerializationUtilities.FormatDouble(matrix[i, j]));
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// Parses matrix text produced by <see cref="FormatMatrix"/>. Strict: a ragged shape or
        /// an unparseable value rejects the whole matrix (null) — validation then reports the
        /// missing matrix.
        /// </summary>
        /// <param name="text">The serialized text; may be null or empty.</param>
        /// <returns>The parsed square matrix, or null.</returns>
        private static double[,]? ParseMatrix(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            string[] rows = text!.Split(';');
            int size = rows.Length;
            var matrix = new double[size, size];
            for (int i = 0; i < size; i++)
            {
                string[] values = rows[i].Split(',');
                if (values.Length != size) return null;
                for (int j = 0; j < size; j++)
                {
                    double parsed = SerializationUtilities.ParseDouble(values[j], double.NaN);
                    if (double.IsNaN(parsed)) return null;
                    matrix[i, j] = parsed;
                }
            }
            return matrix;
        }

        /// <summary>
        /// Subscribes to the owned graph's change notification so projection-dependent surfaces
        /// re-raise (the v1.0 collection-changed relay, re-homed onto the graph).
        /// </summary>
        private void SubscribeGraph()
        {
            _graph.PropertyChanged += GraphPropertyChanged;
        }

        /// <summary>
        /// Relays graph membership changes as a failure-mode change (the projection changed).
        /// </summary>
        /// <param name="sender">The owned graph.</param>
        /// <param name="e">The change description.</param>
        private void GraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(ComponentGraph.Elements), StringComparison.Ordinal))
            {
                RaisePropertyChange(nameof(FailureModes));
            }
        }

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        private void RaisePropertyChange(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
