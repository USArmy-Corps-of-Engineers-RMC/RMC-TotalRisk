using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Numerics;
using Numerics.Data;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Responses
{
    /// <summary>
    /// A composite response (fragility) function: combines a weighted list of child response
    /// functions as a mixture distribution (<see cref="CompositeCombinationType.Mixture"/>, the
    /// default) or as a competing-risks weakest-link combination
    /// (<see cref="CompositeCombinationType.CompetingRisks"/>) — the dam-safety practice of
    /// weighting alternative response scenarios, or of combining several simultaneous failure
    /// mechanisms whose weakest link governs.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>CompositeResponse</c>. <b>Mixture</b> builds a
    /// <see cref="Numerics.Distributions.Mixture"/> over the children sampled at the same
    /// realization, so the conditional failure probability at hazard <c>h</c> is
    /// <c>Σ ωᵢ·pᵢ(h)</c>. <b>CompetingRisks</b> builds a
    /// <see cref="Numerics.Distributions.CompetingRisks"/> with
    /// <c>MinimumOfRandomVariables = true</c> — the weakest link, <c>1 − ∏(1 − pᵢ(h))</c> under
    /// independence — using the configured <see cref="Dependency"/>; weights are inert there. The
    /// weakest-link rule is the one substantive divergence from <c>CompositeHazard</c>, which takes
    /// the maximum (most severe loading controls).
    /// </para>
    /// <para>
    /// <b>The mixture is aleatory (ratified Phase 9),</b> exactly as for <c>CompositeHazard</c>:
    /// the combination is one distribution carried through every realization,
    /// <see cref="SamplingDimensions"/> is zero in both modes, and a mixture of deterministic
    /// children is itself deterministic. The 2024 verification report notes that a composite
    /// response is the same mixture as a composite hazard, plotted as a CDF against non-exceedance
    /// probability rather than as exceedance probability — report Tables 44 through 46 verify both.
    /// </para>
    /// <para>
    /// <b>Ordered-pair curve samples are not emitted</b> (v1.0 parity — the legacy overloads threw
    /// too, as <c>ParametricResponse</c> and <c>NonFailResponse</c> still do). The risk engine
    /// consumes the distribution form exclusively, and a re-tabulation on the union of child knots
    /// would agree with the true combined curve only <i>at</i> the knots under the weakest-link
    /// rule, where <c>1 − ∏(1 − pᵢ(h))</c> is nonlinear in the children — silently creating a
    /// second, subtly wrong response surface. Use <see cref="SampleFunction()"/> for the combined
    /// distribution, or <see cref="ComputeUncertaintyResults"/> for a plottable band.
    /// </para>
    /// <para>
    /// <b>Improved over v1.0</b> (each covered by test): the percentile overload is deterministic
    /// and RNG-free — v1.0 built <c>new Random((int)Math.Round(1 + p·100000))</c> and fed children
    /// <c>NextDouble()</c>; children keep their own interpolation transforms; the lossy
    /// all-empirical union-knot collapse is not ported; empty composites throw instead of returning
    /// sentinels; the circular-reference recursion carries a visited set; a
    /// <c>NonFailResponse</c> child is rejected by validation rather than producing a null
    /// distribution inside the combination kernel; and <see cref="SetupSampler"/> rejects a
    /// posterior-indexed child whose realization capacity is below the requested sample size.
    /// </para>
    /// <para>
    /// <b>Serialization and hashing</b> follow <c>CompositeHazard</c> exactly: dual-mode persistence
    /// over the shared function-entry contract, and a projected identity form carrying the
    /// combination mode, the interpolation transforms, the dependence and matrix (each coerced out
    /// where inert), the entry count, and per entry the effective weight and the child's own
    /// canonical hash.
    /// </para>
    /// </remarks>
    public class CompositeResponse : ResponseFunctionBase
    {
        #region Construction

        /// <summary>
        /// Initializes an empty composite with the v1.0 defaults (Mixture mode, independent, no
        /// children).
        /// </summary>
        public CompositeResponse()
        {
            ResponseFunctions = new ObservableCollection<WeightedResponseFunction>();
        }

        /// <summary>
        /// Initializes a composite over the specified weighted children (Mixture mode).
        /// </summary>
        /// <param name="responseFunctions">The weighted child entries.</param>
        /// <exception cref="ArgumentNullException">Thrown when the sequence is null.</exception>
        public CompositeResponse(IEnumerable<WeightedResponseFunction> responseFunctions)
        {
            if (responseFunctions == null) throw new ArgumentNullException(nameof(responseFunctions));
            ResponseFunctions = new ObservableCollection<WeightedResponseFunction>(responseFunctions);
        }

        /// <summary>
        /// Restores a composite response function from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement(RiskSerializationMode)"/>.</param>
        /// <param name="resolver">
        /// The function resolver, required only to read a by-reference form. An unresolvable
        /// reference keeps its weighted entry with a null function — the weight survives the
        /// round-trip — and is reported by <see cref="Validate"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when inline child content cannot be reconstructed (dropping it would lose model
        /// content on the next save), or when a serialized reference id is stale.
        /// </exception>
        public CompositeResponse(XElement xElement, IRiskFunctionResolver? resolver = null)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            ReadIdentityAttributes(xElement);
            SpecifiedHazard = SerializationUtilities.ReadString(xElement, nameof(SpecifiedHazard));
            HazardUnit = SerializationUtilities.ReadString(xElement, nameof(HazardUnit));
            _compositeCombinationType = SerializationUtilities.ReadEnum(xElement, nameof(CompositeCombinationType), CompositeCombinationType.Mixture);
            _dependency = SerializationUtilities.ReadEnum(xElement, nameof(Dependency), DependencyType.Independent);
            _correlationMatrix = SerializationUtilities.ParseMatrix(SerializationUtilities.ReadString(xElement, nameof(CorrelationMatrix)));
            _hazardTransform = SerializationUtilities.ReadEnum(xElement, nameof(HazardTransform), Transform.None);
            _probabilityTransform = SerializationUtilities.ReadEnum(xElement, nameof(ProbabilityTransform), Transform.None);

            ResponseFunctions = new ObservableCollection<WeightedResponseFunction>();
            var container = xElement.Element(nameof(ResponseFunctions));
            if (container != null)
            {
                foreach (var entryElement in container.Elements(nameof(WeightedResponseFunction)))
                {
                    double weight = SerializationUtilities.ReadDouble(entryElement, nameof(WeightedResponseFunction.Weight));
                    IResponseFunction? function = null;
                    var child = entryElement.Elements().FirstOrDefault();
                    if (child != null)
                    {
                        // The resolver threads into the inline factory so an inline nested
                        // composite can resolve its own by-reference children.
                        function = FunctionEntry.Read<IResponseFunction>(
                            child, resolver, c => RiskFunctionFactory.CreateFromXElement(c, resolver),
                            Name, $"The {nameof(CompositeResponse)} '{Name}'", "response function",
                            _unresolvedFunctionReferences);
                    }

                    // The entry is kept even when the function is null (no child serialized, or an
                    // unresolvable reference): dropping it would silently change the weight list —
                    // and therefore the hash and the weight-sum validation — on the next save.
                    ResponseFunctions.Add(new WeightedResponseFunction(function, weight));
                }
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// The number of internal realizations behind <see cref="ComputeUncertaintyResults"/>.
        /// </summary>
        private const int SummaryRealizations = 10_000;

        /// <summary>
        /// The number of interior grid points a child without its own hazard knots contributes to
        /// the uncertainty summary grid.
        /// </summary>
        private const int SummaryGridPoints = 25;

        /// <summary>
        /// The fixed base seed folded with the canonical content hash to derive the
        /// <see cref="ComputeUncertaintyResults"/> sampling seed — content-based, so the summary is
        /// deterministic for identical compute content and independent of names or ids.
        /// </summary>
        private const int SummarySeedBase = 12345;

        /// <summary>
        /// The tolerance on the weight sum in Mixture mode — the same gate
        /// <see cref="Numerics.Distributions.Mixture"/> applies to its own weights.
        /// </summary>
        private const double WeightSumTolerance = 1e-8;

        /// <summary>
        /// Backing field for <see cref="ResponseFunctions"/>. Assigned through the property by every
        /// constructor, so the collection subscription is always attached.
        /// </summary>
        private ObservableCollection<WeightedResponseFunction> _responseFunctions = null!;

        /// <summary>
        /// Backing field for <see cref="CompositeCombinationType"/> — the v1.0 default is Mixture
        /// (<c>IsMixture = true</c>).
        /// </summary>
        private CompositeCombinationType _compositeCombinationType = CompositeCombinationType.Mixture;

        /// <summary>
        /// Backing field for <see cref="Dependency"/>; meaningful only under CompetingRisks.
        /// </summary>
        private DependencyType _dependency = DependencyType.Independent;

        /// <summary>
        /// Backing field for <see cref="CorrelationMatrix"/>; meaningful only under CompetingRisks
        /// with <see cref="DependencyType.CorrelationMatrix"/>.
        /// </summary>
        private double[,]? _correlationMatrix;

        /// <summary>
        /// Backing field for <see cref="HazardTransform"/> — the v1.0 default is None.
        /// </summary>
        private Transform _hazardTransform = Transform.None;

        /// <summary>
        /// Backing field for <see cref="ProbabilityTransform"/> — the v1.0 response default is None
        /// (the hazard composite defaults to NormalZ).
        /// </summary>
        private Transform _probabilityTransform = Transform.None;

        /// <summary>
        /// The distinct entries this composite currently holds a change subscription on — the
        /// shadow of <see cref="ResponseFunctions"/> that
        /// <see cref="ResponseFunctionsCollectionChanged"/> reconciles against.
        /// </summary>
        /// <remarks>
        /// A shadow set rather than per-item bookkeeping off the event arguments, because
        /// <see cref="NotifyCollectionChangedAction.Reset"/> — which <c>Clear()</c> raises —
        /// carries no <c>OldItems</c>.
        /// </remarks>
        private readonly HashSet<WeightedResponseFunction> _subscribedEntries = new HashSet<WeightedResponseFunction>();

        /// <summary>
        /// Descriptions of serialized function references that could not be resolved, reported by
        /// <see cref="Validate"/> so the precise cause is visible instead of a generic message.
        /// </summary>
        private readonly List<string> _unresolvedFunctionReferences = new List<string>();

        /// <summary>
        /// True while <see cref="EntryPropertyChanged"/> is re-raising, breaking the notification
        /// feedback loop a cyclic composite graph would otherwise create.
        /// </summary>
        private bool _raisingEntryChange;

        /// <summary>
        /// The ordered weighted child entries. Children are referenced, not owned. Assigning null
        /// coerces to an empty collection.
        /// </summary>
        /// <remarks>
        /// Observable, and the composite tracks its membership. Declared order is compute-relevant:
        /// it drives the child sampler ordinals and the hashed entry order (ratified Q-I).
        /// </remarks>
        public ObservableCollection<WeightedResponseFunction> ResponseFunctions
        {
            get { return _responseFunctions; }
            set
            {
                if (ReferenceEquals(_responseFunctions, value)) return;

                if (_responseFunctions != null) _responseFunctions.CollectionChanged -= ResponseFunctionsCollectionChanged;
                _responseFunctions = value ?? new ObservableCollection<WeightedResponseFunction>();
                _responseFunctions.CollectionChanged += ResponseFunctionsCollectionChanged;

                ReconcileEntrySubscriptions();
                RaisePropertyChange(nameof(ResponseFunctions));
            }
        }

        /// <summary>
        /// How the children are combined. Compute-relevant: part of the hashed identity form.
        /// </summary>
        public CompositeCombinationType CompositeCombinationType
        {
            get { return _compositeCombinationType; }
            set
            {
                if (_compositeCombinationType != value)
                {
                    _compositeCombinationType = value;
                    RaisePropertyChange(nameof(CompositeCombinationType));
                }
            }
        }

        /// <summary>
        /// The statistical dependence between the children under
        /// <see cref="CompositeCombinationType.CompetingRisks"/>. Inert under Mixture — a mixture is
        /// a single distribution, not a joint event — and coerced out of the canonical hash there.
        /// </summary>
        public DependencyType Dependency
        {
            get { return _dependency; }
            set
            {
                if (_dependency != value)
                {
                    _dependency = value;
                    RaisePropertyChange(nameof(Dependency));
                }
            }
        }

        /// <summary>
        /// The user-specified correlation matrix, used only under
        /// <see cref="CompositeCombinationType.CompetingRisks"/> with
        /// <see cref="DependencyType.CorrelationMatrix"/> (one row and column per child, positive
        /// definite). Serialized and hashed only in that combination.
        /// </summary>
        public double[,]? CorrelationMatrix
        {
            get { return _correlationMatrix; }
            set
            {
                _correlationMatrix = value;
                RaisePropertyChange(nameof(CorrelationMatrix));
            }
        }

        /// <summary>
        /// The interpolation transform applied to the hazard axis of the combined distribution's
        /// empirical CDF. Compute-relevant in both modes.
        /// </summary>
        public Transform HazardTransform
        {
            get { return _hazardTransform; }
            set
            {
                if (_hazardTransform != value)
                {
                    _hazardTransform = value;
                    RaisePropertyChange(nameof(HazardTransform));
                }
            }
        }

        /// <summary>
        /// The interpolation transform applied to the probability axis of the combined
        /// distribution's empirical CDF (v1.0 response default None). Compute-relevant in both
        /// modes.
        /// </summary>
        public Transform ProbabilityTransform
        {
            get { return _probabilityTransform; }
            set
            {
                if (_probabilityTransform != value)
                {
                    _probabilityTransform = value;
                    RaisePropertyChange(nameof(ProbabilityTransform));
                }
            }
        }

        /// <inheritdoc/>
        public override ResponseFunctionType FunctionType => ResponseFunctionType.Composite;

        /// <inheritdoc/>
        /// <remarks>
        /// Deterministic when every non-null child is. There is deliberately no mixture special
        /// case (the divergence from <c>CompositeConsequence</c>): the mixture is aleatory, so no
        /// branch is drawn per realization and a mixture of fixed distributions is itself one fixed
        /// distribution.
        /// </remarks>
        public override bool IsDeterministic
        {
            get
            {
                for (int i = 0; i < _responseFunctions.Count; i++)
                {
                    var function = _responseFunctions[i].ResponseFunction;
                    if (function != null && !function.IsDeterministic) return false;
                }
                return true;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Always zero, in both modes: the combination is aleatory and consumes no knowledge draw
        /// of its own. Children own their dimensions and are set up recursively by
        /// <see cref="SetupSampler"/> (architecture doc §5.8.5).
        /// </remarks>
        public override int SamplingDimensions => 0;

        #endregion

        #region IRiskFunction Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Recurses into every child with a content-derived seed:
        /// <c>SeedHelpers.HashCombine(seed, child.CanonicalHash(), ordinal)</c>. The ordinal gives
        /// identical-content siblings independent draws; the child hash is metadata-inert, so
        /// renaming a child can never change results.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the composite configuration is invalid, or when a posterior-indexed child
        /// cannot serve the requested sample size.
        /// </exception>
        public override void SetupSampler(int sampleSize, int seed, SamplingScheme scheme)
        {
            ThrowIfUnusable(checkCycles: true);
            base.SetupSampler(sampleSize, seed, scheme);

            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var child = _responseFunctions[i].ResponseFunction;
                if (child == null) continue;
                CompositeSupport.ThrowIfPosteriorCapacityTooSmall(child, sampleSize, Name);
                child.SetupSampler(sampleSize, SeedHelpers.HashCombine(seed, child.CanonicalHash(), i), scheme);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Errors (invalidating): missing axis labels; no children (or an unresolved serialized
        /// reference, reported precisely instead); a null child entry; a <c>NonFailResponse</c>
        /// child, which emits no distribution to combine; Mixture weights outside [0, 1] or not
        /// summing to one (±1e-8); a correlation matrix that is missing, wrongly dimensioned, or not
        /// positive definite when the competing-risks dependence requires one; a circular reference;
        /// an invalid child (summary line only). Warnings (advisory): child axis labels that do not
        /// match the composite's (the ratified Phase 3 downgrade), and a single-entry competing-risks
        /// combination, which degenerates to that child.
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var messages = new List<string>();

            if (string.IsNullOrEmpty(SpecifiedHazard))
                messages.Add("Error: The composite response function does not have a specified hazard type.");
            if (string.IsNullOrEmpty(HazardUnit))
                messages.Add("Error: The composite response function does not have a specified hazard unit.");

            foreach (string reference in _unresolvedFunctionReferences)
            {
                messages.Add($"Error: The composite response function '{Name}' references {reference}, which was not found.");
            }

            if (_responseFunctions.Count == 0)
            {
                if (_unresolvedFunctionReferences.Count == 0)
                    messages.Add("Error: No response functions have been defined for the composite.");
                return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
            }

            bool weightsApply = _compositeCombinationType == CompositeCombinationType.Mixture;
            bool anyWeightOutOfRange = false;
            double weightSum = 0d;
            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var entry = _responseFunctions[i];
                if (entry.ResponseFunction == null)
                    messages.Add("Error: A weighted response function has not been defined for the composite function.");
                if (entry.ResponseFunction is NonFailResponse)
                    messages.Add("Error: A non-failure response cannot be a child of a composite response function; it emits no distribution to combine.");
                if (weightsApply && (entry.Weight < 0d || entry.Weight > 1d)) anyWeightOutOfRange = true;
                weightSum += entry.Weight;
            }

            if (weightsApply && anyWeightOutOfRange)
                messages.Add("Error: The response function weight must be between 0 and 1.");
            if (weightsApply && !anyWeightOutOfRange && Math.Abs(weightSum - 1d) > WeightSumTolerance)
                messages.Add("Error: Composite response function weights do not sum to 1.");

            if (!IsCorrelationMatrixValid())
                messages.Add($"Error: The composite response function correlation matrix must be a positive definite {_responseFunctions.Count}x{_responseFunctions.Count} matrix.");

            if (_compositeCombinationType == CompositeCombinationType.CompetingRisks && _responseFunctions.Count == 1)
                messages.Add("Warning: A competing-risks combination over a single response function degenerates to that function.");

            var circularChild = FindCircularChild();
            if (circularChild != null)
                messages.Add($"Error: Circular reference error in the selected composite response function '{circularChild.Name}'.");

            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var function = _responseFunctions[i].ResponseFunction;
                if (function == null) continue;

                // A cyclic child would recurse forever through its own Validate; the circular error
                // above already reports the precise cause.
                if (circularChild == null && !function.Validate().IsValid)
                    messages.Add($"Error: The selected response function '{function.Name}' is invalid.");

                if (function.SpecifiedHazard != SpecifiedHazard)
                    messages.Add($"Warning: The response function '{function.Name}' does not match hazard type '{SpecifiedHazard}' of the composite function.");
                if (function.HazardUnit != HazardUnit)
                    messages.Add($"Warning: The response function '{function.Name}' does not match hazard unit '{HazardUnit}' of the composite function.");
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">
        /// Always — composite response functions do not emit ordered-pair curve samples. See the
        /// class remarks for why a union-knot re-tabulation would be wrong.
        /// </exception>
        public override OrderedPairedData SampleResponseFunction()
        {
            throw new NotImplementedException(CurveSampleMessage);
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">Always — see <see cref="SampleResponseFunction()"/>.</exception>
        public override OrderedPairedData SampleResponseFunction(double percentile)
        {
            throw new NotImplementedException(CurveSampleMessage);
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">Always — see <see cref="SampleResponseFunction()"/>.</exception>
        public override OrderedPairedData SampleResponseFunction(int realizationIndex)
        {
            throw new NotImplementedException(CurveSampleMessage);
        }

        /// <inheritdoc/>
        /// <remarks>The combined mean distribution: every child contributes its own mean distribution.</remarks>
        /// <exception cref="InvalidOperationException">Thrown when the composite configuration is invalid.</exception>
        public override IUnivariateDistribution SampleFunction()
        {
            ThrowIfUnusable(checkCycles: true);
            int count = _responseFunctions.Count;
            var distributions = new IUnivariateDistribution[count];
            for (int i = 0; i < count; i++)
            {
                distributions[i] = _responseFunctions[i].ResponseFunction!.SampleFunction();
            }
            return BuildCombined(distributions);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Deterministic and RNG-free (the v1.0 percentile-reseeded <c>Random</c> is deliberately
        /// gone): every child is sampled co-monotonically at the given knowledge percentile and the
        /// combination is rebuilt over the resulting distributions.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the composite configuration is invalid.</exception>
        public override IUnivariateDistribution SampleFunction(double percentile)
        {
            ThrowIfUnusable(checkCycles: true);
            int count = _responseFunctions.Count;
            var distributions = new IUnivariateDistribution[count];
            for (int i = 0; i < count; i++)
            {
                distributions[i] = _responseFunctions[i].ResponseFunction!.SampleFunction(percentile);
            }
            return BuildCombined(distributions);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The per-realization path: every child samples realization
        /// <paramref name="realizationIndex"/> from its own content-seeded sampler (children are
        /// mutually independent), and the combination is rebuilt over the resulting distributions.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the composite configuration is invalid.</exception>
        public override IUnivariateDistribution SampleFunction(int realizationIndex)
        {
            ThrowIfUnusable(checkCycles: false);
            int count = _responseFunctions.Count;
            var distributions = new IUnivariateDistribution[count];
            for (int i = 0; i < count; i++)
            {
                distributions[i] = _responseFunctions[i].ResponseFunction!.SampleFunction(realizationIndex);
            }
            return BuildCombined(distributions);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// True when every non-null child is monotonic — and that is a theorem, not a heuristic: a
        /// convex combination <c>Σ ωᵢ·pᵢ(h)</c> of non-decreasing <c>pᵢ</c> is non-decreasing, and
        /// <c>1 − ∏(1 − pᵢ(h))</c> is non-decreasing in each <c>pᵢ</c>, so monotone children imply
        /// a monotone combination under <b>both</b> rules. An empty composite is trivially
        /// monotonic (v1.0 parity).
        /// </remarks>
        public override bool IsMonotonic()
        {
            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var function = _responseFunctions[i].ResponseFunction;
                if (function != null && !function.IsMonotonic()) return false;
            }
            return true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The smallest child minimum. Improved over v1.0: an empty composite throws instead of
        /// returning the legacy <see cref="double.MaxValue"/> sentinel.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the composite has no usable children.</exception>
        public override double MinHazard()
        {
            return Extremum(f => f.MinHazard(), smallest: true);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The largest child maximum. Improved over v1.0: an empty composite throws instead of
        /// returning the legacy <see cref="double.MinValue"/> sentinel.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the composite has no usable children.</exception>
        public override double MaxHazard()
        {
            return Extremum(f => f.MaxHazard(), smallest: false);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The child envelope (v1.0 parity). Note this <i>bounds</i> rather than equals the combined
        /// curve's endpoint: under the weakest-link rule the true value at the lower hazard bound is
        /// <c>1 − ∏(1 − pᵢ)</c>, which is at least the largest child probability. Nothing in the
        /// engine consumes these bounds; they exist for the v1.0 API surface.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the composite has no usable children.</exception>
        public override double MinProbability()
        {
            return Extremum(f => f.MinProbability(), smallest: true);
        }

        /// <inheritdoc/>
        /// <remarks>The child envelope — see <see cref="MinProbability"/>.</remarks>
        /// <exception cref="InvalidOperationException">Thrown when the composite has no usable children.</exception>
        public override double MaxProbability()
        {
            return Extremum(f => f.MaxProbability(), smallest: false);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// Deterministic internal Monte Carlo on a clone (so the live instance's engine sampler
        /// state is never disturbed): the clone's sampler runs <see cref="SummaryRealizations"/>
        /// median-LHS realizations seeded by
        /// <see cref="SeedHelpers.HashCombine(int, byte[], int)"/> over
        /// (<see cref="SummarySeedBase"/>, <see cref="CanonicalHash"/>, 0), and every realization's
        /// combined distribution is evaluated across <see cref="UncertaintySummaryHazards"/>.
        /// </para>
        /// <para>
        /// Curves are conditional failure probabilities index-aligned with
        /// <see cref="UncertaintySummaryHazards"/>: mean, median (mode curve), and the (1 ∓ w)/2
        /// percentiles. Deterministic composites evaluate the mean distribution exactly with no
        /// simulation. Returns null when <see cref="Validate"/> reports errors.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the width is outside (0, 1).</exception>
        public override UncertaintyAnalysisResults? ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9)
        {
            if (confidenceIntervalWidth <= 0d || confidenceIntervalWidth >= 1d)
                throw new ArgumentOutOfRangeException(nameof(confidenceIntervalWidth), "The confidence interval width must be between 0 and 1.");
            if (!Validate().IsValid) return null;

            double[] hazards = UncertaintySummaryHazards();
            var results = new UncertaintyAnalysisResults
            {
                ModeCurve = new double[hazards.Length],
                MeanCurve = new double[hazards.Length],
                ConfidenceIntervals = new double[hazards.Length, 2],
            };

            if (IsDeterministic)
            {
                var mean = SampleFunction();
                for (int i = 0; i < hazards.Length; i++)
                {
                    double value = mean.CDF(hazards[i]);
                    results.ModeCurve[i] = value;
                    results.MeanCurve[i] = value;
                    results.ConfidenceIntervals[i, 0] = value;
                    results.ConfidenceIntervals[i, 1] = value;
                }
                return results;
            }

            var clone = (CompositeResponse)RiskFunctionFactory.CreateResponseFunction(ToXElement())!;
            int seed = ToPositiveSeed(SeedHelpers.HashCombine(SummarySeedBase, CanonicalHash(), 0));
            clone.SetupSampler(SummaryRealizations, seed, SamplingScheme.LatinHypercubeMedian);

            var values = new double[hazards.Length, SummaryRealizations];
            for (int k = 0; k < SummaryRealizations; k++)
            {
                var distribution = clone.SampleFunction(k);
                for (int i = 0; i < hazards.Length; i++)
                {
                    values[i, k] = distribution.CDF(hazards[i]);
                }
            }

            double tail = (1d - confidenceIntervalWidth) / 2d;
            var row = new double[SummaryRealizations];
            for (int i = 0; i < hazards.Length; i++)
            {
                FunctionHelpers.SummarizeEnsembleRow(values, i, row, tail, results);
            }
            return results;
        }

        /// <summary>
        /// The hazard grid that <see cref="ComputeUncertaintyResults"/> summarizes over — callers
        /// pair the returned probability curves with these hazards by index.
        /// </summary>
        /// <returns>The sorted distinct union of the child summary hazards.</returns>
        /// <remarks>
        /// Tabular children contribute their table ordinates (the legacy union-of-knots grid), and
        /// any other child kind contributes an evenly spaced grid across its own hazard bounds;
        /// nested composites recurse (cycle-safe).
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the composite has no usable children.</exception>
        public double[] UncertaintySummaryHazards()
        {
            var hazards = new SortedSet<double>();
            CollectSummaryHazards(hazards, new HashSet<CompositeResponse>());
            if (hazards.Count == 0) throw new InvalidOperationException(NoChildrenMessage);
            return hazards.ToArray();
        }

        /// <summary>
        /// Determines whether the correlation matrix satisfies the competing-risks
        /// correlation-matrix dependence: one row and column per child entry, and positive definite
        /// (Cholesky). Always true in every other mode.
        /// </summary>
        /// <returns>True when the matrix is usable (or not required).</returns>
        public bool IsCorrelationMatrixValid()
        {
            return CompositeSupport.IsValid(_compositeCombinationType, _dependency, _correlationMatrix, _responseFunctions.Count);
        }

        /// <summary>
        /// Determines whether the given composite appears anywhere in this composite's child graph
        /// — the circular-reference guard.
        /// </summary>
        /// <param name="composite">The composite to search for.</param>
        /// <returns>True when the composite is referenced at any nesting depth.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the composite is null.</exception>
        /// <remarks>
        /// Improved over the v1.0 recursion: a visited set guards against cycles that do not involve
        /// the target, which would otherwise recurse forever.
        /// </remarks>
        public bool ContainsComposite(CompositeResponse composite)
        {
            if (composite == null) throw new ArgumentNullException(nameof(composite));
            return ContainsComposite(composite, new HashSet<CompositeResponse>());
        }

        #endregion

        #region Serialization

        /// <inheritdoc/>
        /// <remarks>
        /// The self-contained form (child content inline) — the storeless default. Not this type's
        /// hash surface: see <see cref="CanonicalHash"/>.
        /// </remarks>
        public override XElement ToXElement()
        {
            return ToXElement(RiskSerializationMode.SelfContained);
        }

        /// <summary>
        /// Serializes the composite in the requested mode: child content inline
        /// (<see cref="RiskSerializationMode.SelfContained"/>), or weight-bearing entries whose
        /// children are <c>FunctionReference</c> markers
        /// (<see cref="RiskSerializationMode.ByReference"/>).
        /// </summary>
        /// <param name="mode">The serialization mode; the mode propagates to nested composites.</param>
        /// <returns>The serialized form.</returns>
        public XElement ToXElement(RiskSerializationMode mode)
        {
            var element = new XElement(nameof(CompositeResponse));
            WriteIdentityAttributes(element);
            element.SetAttributeValue(nameof(SpecifiedHazard), SpecifiedHazard);
            element.SetAttributeValue(nameof(HazardUnit), HazardUnit);
            element.SetAttributeValue(nameof(CompositeCombinationType), _compositeCombinationType.ToString());
            element.SetAttributeValue(nameof(Dependency), _dependency.ToString());
            element.SetAttributeValue(nameof(CorrelationMatrix),
                CompositeSupport.IsMatrixMode(_compositeCombinationType, _dependency)
                    ? SerializationUtilities.FormatMatrix(_correlationMatrix)
                    : string.Empty);
            element.SetAttributeValue(nameof(HazardTransform), _hazardTransform.ToString());
            element.SetAttributeValue(nameof(ProbabilityTransform), _probabilityTransform.ToString());

            var container = new XElement(nameof(ResponseFunctions));
            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var entry = _responseFunctions[i];
                var entryElement = new XElement(nameof(WeightedResponseFunction));
                entryElement.SetAttributeValue(nameof(WeightedResponseFunction.Weight), SerializationUtilities.FormatDouble(entry.Weight));
                if (entry.ResponseFunction != null)
                {
                    // Nested composites need no special case: under ByReference the nested composite
                    // itself becomes a marker, and under SelfContained its parameterless
                    // ToXElement() recurses fully inline.
                    entryElement.Add(FunctionEntry.Write(entry.ResponseFunction, mode));
                }
                container.Add(entryElement);
            }
            element.Add(container);
            return element;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Hashes the projected identity form, never the persisted form (the ratified
        /// <c>SystemComponent</c> identity-form exception): the combination mode, the interpolation
        /// transforms, the dependence and correlation matrix, the entry count, and per entry the
        /// effective weight and the child's own canonical hash. Three coercions keep inert edits
        /// from re-rolling seeds — weights project as one under CompetingRisks, the dependence
        /// projects as Independent under Mixture, and the correlation matrix projects as empty
        /// outside the one mode that reads it.
        /// </remarks>
        public override byte[] CanonicalHash()
        {
            var identity = new XElement(nameof(CompositeResponse));
            identity.SetAttributeValue(nameof(CompositeCombinationType), _compositeCombinationType.ToString());
            identity.SetAttributeValue(nameof(Dependency), EffectiveDependency().ToString());
            identity.SetAttributeValue(nameof(CorrelationMatrix),
                CompositeSupport.IsMatrixMode(_compositeCombinationType, _dependency)
                    ? SerializationUtilities.FormatMatrix(_correlationMatrix)
                    : string.Empty);
            identity.SetAttributeValue(nameof(HazardTransform), _hazardTransform.ToString());
            identity.SetAttributeValue(nameof(ProbabilityTransform), _probabilityTransform.ToString());
            identity.SetAttributeValue("Count", _responseFunctions.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var entry = _responseFunctions[i];
                var entryElement = new XElement(nameof(WeightedResponseFunction));
                entryElement.SetAttributeValue(nameof(WeightedResponseFunction.Weight), SerializationUtilities.FormatDouble(EffectiveWeight(i)));
                entryElement.SetAttributeValue("FunctionHash", entry.ResponseFunction == null
                    ? string.Empty
                    : CanonicalContentHasher.ToTokenHex(entry.ResponseFunction.CanonicalHash()));
                identity.Add(entryElement);
            }
            return CanonicalContentHasher.Hash(identity, CanonicalizationRules.ModelRules);
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// The message reported when a compute surface is reached with no usable children.
        /// </summary>
        private const string NoChildrenMessage =
            "The composite response function has no child functions. Call Validate() and correct the reported errors before sampling.";

        /// <summary>
        /// The message explaining why ordered-pair curve samples are not emitted.
        /// </summary>
        private const string CurveSampleMessage =
            "Composite response functions do not emit ordered-pair curve samples; the combined failure " +
            "probability is nonlinear in the children. Use SampleFunction() for the combined distribution, " +
            "or ComputeUncertaintyResults() for a plottable band.";

        /// <summary>
        /// Builds the combined distribution over the sampled child distributions — the single place
        /// the combination rule lives, shared by all three sampling overloads.
        /// </summary>
        /// <param name="distributions">The child distributions, one per entry, in declared order.</param>
        /// <returns>The combined distribution, with its empirical CDF built.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a child sampled to a distribution the Numerics combination kernels cannot
        /// accept.
        /// </exception>
        private IUnivariateDistribution BuildCombined(IUnivariateDistribution[] distributions)
        {
            CompositeSupport.ThrowIfNotNumericsDistributions(distributions, nameof(CompositeResponse));

            if (_compositeCombinationType == CompositeCombinationType.Mixture)
            {
                var weights = new double[distributions.Length];
                for (int i = 0; i < weights.Length; i++) weights[i] = EffectiveWeight(i);
                var mixture = new Mixture(weights, distributions)
                {
                    XTransform = _hazardTransform,
                    ProbabilityTransform = _probabilityTransform,
                };
                mixture.CreateEmpiricalCDF();
                return mixture;
            }

            var competing = new CompetingRisks(distributions)
            {
                // The minimum rule: the weakest link governs. (A hazard composite takes the
                // maximum instead — the most severe loading controls.)
                MinimumOfRandomVariables = true,
                Dependency = CompositeSupport.MapDependency(_dependency),
                XTransform = _hazardTransform,
                ProbabilityTransform = _probabilityTransform,
            };
            if (CompositeSupport.IsMatrixMode(_compositeCombinationType, _dependency) && _correlationMatrix != null)
            {
                competing.CorrelationMatrix = _correlationMatrix;
            }
            competing.CreateEmpiricalCDF();
            return competing;
        }

        /// <summary>
        /// The smallest or largest value of a per-child bound across the configured children.
        /// </summary>
        /// <param name="selector">The child bound to read.</param>
        /// <param name="smallest">True for the minimum, false for the maximum.</param>
        /// <returns>The extremum across non-null children.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the composite has no usable children.</exception>
        private double Extremum(Func<IResponseFunction, double> selector, bool smallest)
        {
            double extremum = smallest ? double.MaxValue : double.MinValue;
            bool any = false;
            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var function = _responseFunctions[i].ResponseFunction;
                if (function == null) continue;
                any = true;
                double value = selector(function);
                if (smallest ? value < extremum : value > extremum) extremum = value;
            }
            if (!any) throw new InvalidOperationException(NoChildrenMessage);
            return extremum;
        }

        /// <summary>
        /// The weight the compute paths use for an entry: one under CompetingRisks (weights are
        /// inert there), the entry weight otherwise.
        /// </summary>
        /// <param name="index">The entry index.</param>
        /// <returns>The effective weight.</returns>
        private double EffectiveWeight(int index)
        {
            return _compositeCombinationType == CompositeCombinationType.CompetingRisks ? 1d : _responseFunctions[index].Weight;
        }

        /// <summary>
        /// The dependence the hash projects: the configured value under CompetingRisks, and
        /// Independent under Mixture, which ignores dependence entirely.
        /// </summary>
        /// <returns>The effective dependence.</returns>
        private DependencyType EffectiveDependency()
        {
            return _compositeCombinationType == CompositeCombinationType.CompetingRisks ? _dependency : DependencyType.Independent;
        }

        /// <summary>
        /// The sample-time usability gate: at least one entry, every entry configured and not a
        /// non-failure sentinel, Mixture weights in [0, 1] summing to one, and a usable correlation
        /// matrix where the dependence requires one.
        /// </summary>
        /// <param name="checkCycles">True to also reject circular references.</param>
        /// <exception cref="InvalidOperationException">Thrown when the configuration is invalid.</exception>
        private void ThrowIfUnusable(bool checkCycles)
        {
            bool usable = _responseFunctions.Count > 0;
            if (usable)
            {
                double weightSum = 0d;
                bool weightsApply = _compositeCombinationType == CompositeCombinationType.Mixture;
                for (int i = 0; i < _responseFunctions.Count; i++)
                {
                    var entry = _responseFunctions[i];
                    if (entry.ResponseFunction == null || entry.ResponseFunction is NonFailResponse
                        || (weightsApply && (entry.Weight < 0d || entry.Weight > 1d)))
                    {
                        usable = false;
                        break;
                    }
                    weightSum += entry.Weight;
                }
                if (usable && weightsApply && Math.Abs(weightSum - 1d) > WeightSumTolerance) usable = false;
            }
            if (usable && !IsCorrelationMatrixValid()) usable = false;
            if (usable && checkCycles && FindCircularChild() != null) usable = false;

            if (!usable)
                throw new InvalidOperationException("The composite response configuration is invalid. Call Validate() and correct the reported errors before sampling.");
        }

        /// <summary>
        /// Finds the first child that closes a cycle back to this composite, or null when the child
        /// graph is acyclic.
        /// </summary>
        /// <returns>The offending child function, or null.</returns>
        private IResponseFunction? FindCircularChild()
        {
            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var function = _responseFunctions[i].ResponseFunction;
                if (function is not CompositeResponse nested) continue;
                if (ReferenceEquals(nested, this) || nested.ContainsComposite(this)) return function;
            }
            return null;
        }

        /// <summary>
        /// The cycle-safe recursion behind <see cref="ContainsComposite(CompositeResponse)"/>.
        /// </summary>
        /// <param name="target">The composite being searched for.</param>
        /// <param name="visited">The composites already searched (guards cycles not involving the target).</param>
        /// <returns>True when the target is referenced at any nesting depth.</returns>
        private bool ContainsComposite(CompositeResponse target, HashSet<CompositeResponse> visited)
        {
            if (!visited.Add(this)) return false;
            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var function = _responseFunctions[i].ResponseFunction;
                if (ReferenceEquals(function, target)) return true;
                if (function is CompositeResponse nested && nested.ContainsComposite(target, visited)) return true;
            }
            return false;
        }

        /// <summary>
        /// Accumulates the summary hazard grid from the children: tabular knots, an evenly spaced
        /// grid across any other child's own bounds, and nested composites recursively (cycle-safe).
        /// </summary>
        /// <param name="hazards">The accumulating sorted distinct grid.</param>
        /// <param name="visited">The composites already visited.</param>
        private void CollectSummaryHazards(SortedSet<double> hazards, HashSet<CompositeResponse> visited)
        {
            if (!visited.Add(this)) return;
            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                switch (_responseFunctions[i].ResponseFunction)
                {
                    case TabularResponse tabular:
                        for (int j = 0; j < tabular.UncertainOrderedPairedData.Count; j++)
                            hazards.Add(tabular.UncertainOrderedPairedData[j].X);
                        break;
                    case CompositeResponse nested:
                        nested.CollectSummaryHazards(hazards, visited);
                        break;
                    case null:
                        break;
                    default:
                        AddGrid(hazards, _responseFunctions[i].ResponseFunction!);
                        break;
                }
            }
        }

        /// <summary>
        /// Adds an evenly spaced grid across a child's hazard bounds, for a child kind that carries
        /// no knots of its own (a parametric fragility).
        /// </summary>
        /// <param name="hazards">The accumulating grid.</param>
        /// <param name="function">The child function.</param>
        private static void AddGrid(SortedSet<double> hazards, IResponseFunction function)
        {
            double minimum = function.MinHazard();
            double maximum = function.MaxHazard();
            if (!Tools.IsFinite(minimum) || !Tools.IsFinite(maximum) || maximum <= minimum)
            {
                hazards.Add(minimum);
                hazards.Add(maximum);
                return;
            }

            double step = (maximum - minimum) / (SummaryGridPoints - 1);
            for (int i = 0; i < SummaryGridPoints; i++) hazards.Add(minimum + (i * step));
        }

        /// <summary>
        /// Keeps the composite's change subscriptions in step with the entry list, and reports the
        /// membership change as <c>ResponseFunctions</c>.
        /// </summary>
        /// <param name="sender">The entry collection.</param>
        /// <param name="e">The membership change.</param>
        private void ResponseFunctionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ReconcileEntrySubscriptions();
            RaisePropertyChange(nameof(ResponseFunctions));
        }

        /// <summary>
        /// Subscribes to every entry now in the list and unsubscribes from every entry that has left
        /// it, using <see cref="_subscribedEntries"/> as the record of what is attached.
        /// </summary>
        private void ReconcileEntrySubscriptions()
        {
            foreach (var stale in _subscribedEntries.Where(entry => !_responseFunctions.Contains(entry)).ToList())
            {
                stale.PropertyChanged -= EntryPropertyChanged;
                _subscribedEntries.Remove(stale);
            }

            for (int i = 0; i < _responseFunctions.Count; i++)
            {
                var entry = _responseFunctions[i];
                if (entry != null && _subscribedEntries.Add(entry)) entry.PropertyChanged += EntryPropertyChanged;
            }
        }

        /// <summary>
        /// Re-raises an entry-level change as a change of the composite's entry list. Reentrant
        /// notifications are suppressed via <see cref="_raisingEntryChange"/> so a cyclic composite
        /// graph degrades to a reportable validation error instead of unbounded recursion.
        /// </summary>
        /// <param name="sender">The entry.</param>
        /// <param name="e">The originating change arguments.</param>
        private void EntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_raisingEntryChange) return;
            _raisingEntryChange = true;
            try
            {
                RaisePropertyChange(nameof(ResponseFunctions));
            }
            finally
            {
                _raisingEntryChange = false;
            }
        }

        #endregion
    }
}
