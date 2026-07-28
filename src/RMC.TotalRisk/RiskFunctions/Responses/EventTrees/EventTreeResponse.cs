using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.RiskFunctions.Responses.EventTrees
{
    /// <summary>
    /// A conditional-failure response defined by an authored event tree. Explicit sibling
    /// probabilities retain their legacy conditional meaning: sums above one are normalized
    /// proportionally, a remainder receives the residual, and terminal path products form the
    /// mutually exclusive end-state probabilities.
    /// </summary>
    /// <remarks>
    /// The current Phase 10A implementation supports scalar, uncertain-tabular, ordinary-response,
    /// and internal/external independent-clone link occurrences. Linked occurrences participate in
    /// recursive sampling, two-mode serialization, projected hashing, and cycle diagnostics.
    /// Event-tree probability-source recursion, copy/paste fragments, legacy recursive-XML
    /// conversion, expanded graph ports, and cached-plan optimization remain explicitly deferred.
    /// </remarks>
    public sealed class EventTreeResponse : ResponseFunctionBase, IBranchingResponseFunction
    {
        /// <summary>Initializes an empty event-tree response over default hazards 0 and 1.</summary>
        public EventTreeResponse()
            : this(new[] { 0d, 1d }, new EventTree())
        {
        }

        /// <summary>Initializes an event-tree response.</summary>
        /// <param name="hazardLevels">The finite strictly ascending hazard axis.</param>
        /// <param name="eventTree">The controlled authored tree.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public EventTreeResponse(IEnumerable<double> hazardLevels, EventTree eventTree)
        {
            EventTree = eventTree ?? throw new ArgumentNullException(nameof(eventTree));
            EventTree.AttachOwner(this);
            SetHazardLevels(hazardLevels);
        }

        /// <summary>Restores an event-tree response from its v1.1 explicit graph serialization.</summary>
        /// <param name="xElement">The serialized response.</param>
        /// <param name="resolver">The optional resolver for by-reference probability sources.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when required tree content is absent.</exception>
        public EventTreeResponse(XElement xElement, IRiskFunctionResolver? resolver = null)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            ReadIdentityAttributes(xElement);
            SpecifiedHazard = SerializationUtilities.ReadString(xElement, nameof(SpecifiedHazard));
            HazardUnit = SerializationUtilities.ReadString(xElement, nameof(HazardUnit));

            var levels = xElement.Element(nameof(HazardLevels))?.Elements("Level")
                .Select(level => SerializationUtilities.ReadDouble(level, "Value"))
                ?? Enumerable.Empty<double>();
            _hazardLevels.AddRange(levels);

            XElement treeElement = xElement.Element(nameof(EventTree))
                ?? throw new InvalidOperationException("The serialized event-tree response has no EventTree element.");
            EventTree = new EventTree(treeElement, resolver, Name);
            EventTree.AttachOwner(this);
        }

        /// <summary>The stable id of the aggregate implicit non-failure branch.</summary>
        private static readonly Guid ImplicitBranchId = new Guid("f7bf9271-d0b5-5b8a-9b2a-2707517de6e6");

        /// <summary>Number of median-LHS realizations used for uncertainty summaries.</summary>
        private const int SummaryRealizations = 1000;

        /// <summary>Stable seed base used only for uncertainty-summary sampling on a clone.</summary>
        private const int SummarySeedBase = 18031986;

        /// <summary>The owned hazard levels.</summary>
        private readonly List<double> _hazardLevels = new List<double>();

        /// <summary>The cached read-only hazard view.</summary>
        private ReadOnlyCollection<double>? _readOnlyHazards;

        /// <summary>Occurrence-path sampling bindings created by the latest setup.</summary>
        private readonly Dictionary<string, SamplingBinding> _samplingBindings =
            new Dictionary<string, SamplingBinding>(StringComparer.Ordinal);

        /// <summary>The canonical identity under which the current sampler was set up.</summary>
        private byte[]? _samplerIdentity;

        /// <summary>The controlled authored event tree.</summary>
        public EventTree EventTree { get; }

        /// <summary>The finite strictly ascending hazard axis.</summary>
        public IReadOnlyList<double> HazardLevels => _readOnlyHazards ??= _hazardLevels.AsReadOnly();

        /// <summary>Replaces the event-tree hazard axis atomically.</summary>
        /// <param name="hazardLevels">The replacement values.</param>
        /// <exception cref="ArgumentNullException">Thrown when the sequence is null.</exception>
        public void SetHazardLevels(IEnumerable<double> hazardLevels)
        {
            if (hazardLevels == null) throw new ArgumentNullException(nameof(hazardLevels));
            double[] replacement = hazardLevels.ToArray();
            _hazardLevels.Clear();
            _hazardLevels.AddRange(replacement);
            _samplerIdentity = null;
            RaisePropertyChange(nameof(HazardLevels));
        }

        /// <inheritdoc/>
        public override ResponseFunctionType FunctionType => ResponseFunctionType.EventTree;

        /// <inheritdoc/>
        public override bool IsDeterministic => EventTreeOccurrencePlan.Compile(this).CanonicalPreOrder
            .Where(node => node.SourceNode is ChanceNode)
            .All(node => ((ChanceNode)node.SourceNode).ProbabilitySource.IsDeterministic);

        /// <inheritdoc/>
        /// <remarks>
        /// Counts each expanded independent-link occurrence separately. A linked uncertain table
        /// therefore owns a distinct local column, and each referenced response occurrence reports
        /// its full child dimension count.
        /// </remarks>
        public override int SamplingDimensions => EventTreeOccurrencePlan.Compile(this).SamplingDimensions;

        /// <inheritdoc/>
        /// <remarks>
        /// Local table dimensions are columns of this function's sampler. Repeated references to
        /// the same live response use an isolated self-contained clone after the first occurrence,
        /// preventing one occurrence's setup from overwriting another's sampler state.
        /// </remarks>
        public override void SetupSampler(int sampleSize, int seed, SamplingScheme scheme)
        {
            ThrowIfUnusable();
            base.SetupSampler(sampleSize, seed, scheme);
            _samplingBindings.Clear();

            EventTreeOccurrencePlan plan = EventTreeOccurrencePlan.Compile(this);
            var usedResponseInstances = new HashSet<IResponseFunction>(ReferenceEqualityComparer.Instance);
            int dimension = 0;
            int responseOccurrence = 0;
            foreach (EventTreeOccurrenceNode occurrence in plan.CanonicalPreOrder)
            {
                if (occurrence.SourceNode is not ChanceNode chance) continue;
                ProbabilitySource source = chance.ProbabilitySource;
                if (source.Kind == ProbabilitySourceKind.UncertainTabular)
                {
                    _samplingBindings.Add(occurrence.CanonicalPath,
                        new SamplingBinding(dimension, null));
                    dimension++;
                }
                else if (source.Kind == ProbabilitySourceKind.ResponseFunctionReference && source.ResponseFunction != null)
                {
                    IResponseFunction sampledFunction = usedResponseInstances.Add(source.ResponseFunction)
                        ? source.ResponseFunction
                        : CloneResponseFunction(source.ResponseFunction);
                    byte[] sourceHash = Convert.FromHexString(source.CanonicalToken());
                    sampledFunction.SetupSampler(sampleSize,
                        SeedHelpers.HashCombine(seed, sourceHash, responseOccurrence++), scheme);
                    int childDimensions = sampledFunction.SamplingDimensions;
                    for (int realization = 0; realization < sampleSize; realization++)
                    {
                        for (int childDimension = 0; childDimension < childDimensions; childDimension++)
                        {
                            _percentiles![realization, dimension + childDimension] =
                                ((RiskFunctionBase)sampledFunction).SampledPercentile(realization, childDimension);
                        }
                    }
                    _samplingBindings.Add(occurrence.CanonicalPath,
                        new SamplingBinding(-1, sampledFunction));
                    dimension += childDimensions;
                }
            }
            _samplerIdentity = CanonicalHash();
        }

        /// <inheritdoc/>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var messages = new List<string>();
            if (string.IsNullOrEmpty(SpecifiedHazard))
                messages.Add("Error: The event-tree response does not have a specified hazard type.");
            if (string.IsNullOrEmpty(HazardUnit))
                messages.Add("Error: The event-tree response does not have a specified hazard unit.");

            if (_hazardLevels.Count == 0)
            {
                messages.Add("Error: The event-tree response requires at least one hazard level.");
            }
            else
            {
                for (int i = 0; i < _hazardLevels.Count; i++)
                {
                    if (!double.IsFinite(_hazardLevels[i]))
                    {
                        messages.Add("Error: Event-tree hazard levels must be finite.");
                        break;
                    }
                    if (i > 0 && _hazardLevels[i] <= _hazardLevels[i - 1])
                    {
                        messages.Add("Error: Event-tree hazard levels must be strictly ascending.");
                        break;
                    }
                }
            }

            messages.AddRange(EventTree.Validate(_hazardLevels));
            if (EventTree.Nodes.Any(node => node.Id == ImplicitBranchId))
                messages.Add("Error: An event-tree node uses the reserved implicit-branch id.");

            EventTreeOccurrencePlan? plan = null;
            try
            {
                plan = EventTreeOccurrencePlan.Compile(this);
            }
            catch (InvalidOperationException ex)
            {
                AddUnique(messages, $"Error: {ex.Message}");
            }

            if (plan != null)
            {
                for (int i = 0; i < plan.Warnings.Count; i++) AddUnique(messages, plan.Warnings[i]);
                foreach (EventTreeOccurrenceNode occurrence in plan.CanonicalPreOrder)
                {
                    if (occurrence.SourceNode is not ChanceNode chance) continue;
                    foreach (string message in chance.ProbabilitySource.Validate(
                        occurrence.SourceFunction.HazardLevels, occurrence.DisplayName))
                        AddUnique(messages, message);
                }

                if (messages.All(message => !message.StartsWith("Error:", StringComparison.Ordinal)))
                {
                    foreach (EventTreeOccurrenceNode parent in plan.CanonicalPreOrder)
                    {
                        if (parent.Children.Count == 0) continue;
                        int remainderCount = parent.Children.Count(child => child.SourceNode is RemainderNode);
                        if (remainderCount > 1)
                        {
                            AddUnique(messages,
                                $"Error: Expanded event-tree occurrence '{parent.DisplayName}' has more than one remainder branch.");
                            continue;
                        }

                        for (int h = 0; h < _hazardLevels.Count; h++)
                        {
                            double sum = 0d;
                            double compensation = 0d;
                            foreach (EventTreeOccurrenceNode child in parent.Children)
                            {
                                if (child.SourceNode is not ChanceNode) continue;
                                AddCompensated(ref sum, ref compensation,
                                    EvaluateSourceMean(child, _hazardLevels[h], h));
                            }
                            if (sum > 1d)
                            {
                                AddUnique(messages,
                                    $"Warning: Explicit branches of event-tree occurrence '{parent.DisplayName}' sum to {sum.ToString("G17", CultureInfo.InvariantCulture)} at hazard {_hazardLevels[h].ToString("G17", CultureInfo.InvariantCulture)} and will be normalized proportionally.");
                                break;
                            }
                        }
                    }
                }
            }

            return (messages.All(message => !message.StartsWith("Error:", StringComparison.Ordinal)), messages);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ResponseBranchDescriptor> GetBranches()
        {
            EventTreeOccurrencePlan plan = EventTreeOccurrencePlan.Compile(this);
            return BuildBranchBindings(plan).Select(binding => binding.Descriptor).ToArray();
        }

        /// <inheritdoc/>
        public ResponseBranchSample SampleBranches()
        {
            ThrowIfUnusable();
            return EvaluateBranches(SampleMode.Mean, 0d, -1);
        }

        /// <inheritdoc/>
        public ResponseBranchSample SampleBranches(double percentile)
        {
            if (percentile <= 0d || percentile >= 1d)
                throw new ArgumentOutOfRangeException(nameof(percentile), "The percentile must be between 0 and 1.");
            ThrowIfUnusable();
            return EvaluateBranches(SampleMode.Percentile, percentile, -1);
        }

        /// <inheritdoc/>
        public ResponseBranchSample SampleBranches(int realizationIndex)
        {
            ThrowIfUnusable();
            EnsureSamplerCurrent();
            if (realizationIndex < 0 || realizationIndex >= SampleSize)
                throw new ArgumentOutOfRangeException(nameof(realizationIndex));
            return EvaluateBranches(SampleMode.Realization, 0d, realizationIndex);
        }

        /// <inheritdoc/>
        public override OrderedPairedData SampleResponseFunction()
        {
            return AggregateFailureCurve(SampleBranches());
        }

        /// <inheritdoc/>
        public override OrderedPairedData SampleResponseFunction(double percentile)
        {
            return AggregateFailureCurve(SampleBranches(percentile));
        }

        /// <inheritdoc/>
        public override OrderedPairedData SampleResponseFunction(int realizationIndex)
        {
            return AggregateFailureCurve(SampleBranches(realizationIndex));
        }

        /// <inheritdoc/>
        public override IUnivariateDistribution SampleFunction()
        {
            return new EmpiricalDistribution(SampleResponseFunction());
        }

        /// <inheritdoc/>
        public override IUnivariateDistribution SampleFunction(double percentile)
        {
            return new EmpiricalDistribution(SampleResponseFunction(percentile));
        }

        /// <inheritdoc/>
        public override IUnivariateDistribution SampleFunction(int realizationIndex)
        {
            return new EmpiricalDistribution(SampleResponseFunction(realizationIndex));
        }

        /// <inheritdoc/>
        public override bool IsMonotonic()
        {
            if (!Validate().IsValid) return false;
            if (!CurveIsMonotonic(SampleResponseFunction())) return false;
            if (IsDeterministic) return true;
            return CurveIsMonotonic(SampleResponseFunction(0.00001d))
                && CurveIsMonotonic(SampleResponseFunction(0.5d))
                && CurveIsMonotonic(SampleResponseFunction(1d - 0.00001d));
        }

        /// <inheritdoc/>
        public override double MinHazard()
        {
            if (_hazardLevels.Count == 0) throw new InvalidOperationException("The event-tree response has no hazard levels.");
            return _hazardLevels[0];
        }

        /// <inheritdoc/>
        public override double MaxHazard()
        {
            if (_hazardLevels.Count == 0) throw new InvalidOperationException("The event-tree response has no hazard levels.");
            return _hazardLevels[_hazardLevels.Count - 1];
        }

        /// <inheritdoc/>
        public override double MinProbability()
        {
            return SampleResponseFunction()[0].Y;
        }

        /// <inheritdoc/>
        public override double MaxProbability()
        {
            OrderedPairedData curve = SampleResponseFunction();
            return curve[curve.Count - 1].Y;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Deterministic trees are summarized exactly. Uncertain trees use a deterministic
        /// 1,000-realization median-LHS sampler on a self-contained clone, preserving the live
        /// function's sampler state.
        /// </remarks>
        public override UncertaintyAnalysisResults? ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9)
        {
            if (confidenceIntervalWidth <= 0d || confidenceIntervalWidth >= 1d)
                throw new ArgumentOutOfRangeException(nameof(confidenceIntervalWidth), "The confidence interval width must be between 0 and 1.");
            if (!Validate().IsValid) return null;

            var results = new UncertaintyAnalysisResults
            {
                ModeCurve = new double[_hazardLevels.Count],
                MeanCurve = new double[_hazardLevels.Count],
                ConfidenceIntervals = new double[_hazardLevels.Count, 2],
            };
            if (IsDeterministic)
            {
                OrderedPairedData curve = SampleResponseFunction();
                for (int i = 0; i < curve.Count; i++)
                {
                    results.ModeCurve[i] = curve[i].Y;
                    results.MeanCurve[i] = curve[i].Y;
                    results.ConfidenceIntervals[i, 0] = curve[i].Y;
                    results.ConfidenceIntervals[i, 1] = curve[i].Y;
                }
                return results;
            }

            var clone = new EventTreeResponse(ToXElement(RiskSerializationMode.SelfContained));
            int seed = ToPositiveSeed(SeedHelpers.HashCombine(SummarySeedBase, CanonicalHash(), 0));
            clone.SetupSampler(SummaryRealizations, seed, SamplingScheme.LatinHypercubeMedian);
            var values = new double[_hazardLevels.Count, SummaryRealizations];
            for (int realization = 0; realization < SummaryRealizations; realization++)
            {
                OrderedPairedData curve = clone.SampleResponseFunction(realization);
                for (int h = 0; h < curve.Count; h++) values[h, realization] = curve[h].Y;
            }

            double tail = (1d - confidenceIntervalWidth) / 2d;
            var row = new double[SummaryRealizations];
            for (int h = 0; h < _hazardLevels.Count; h++)
                FunctionHelpers.SummarizeEnsembleRow(values, h, row, tail, results);
            return results;
        }

        /// <inheritdoc/>
        public override XElement ToXElement()
        {
            return ToXElement(RiskSerializationMode.SelfContained);
        }

        /// <summary>Serializes the response and propagates the mode to probability-source references.</summary>
        /// <param name="mode">The serialization mode.</param>
        /// <returns>The serialized response.</returns>
        public XElement ToXElement(RiskSerializationMode mode)
        {
            EventTreeOccurrencePlan.Compile(this);
            var element = new XElement(nameof(EventTreeResponse));
            WriteIdentityAttributes(element);
            element.SetAttributeValue(nameof(SpecifiedHazard), SpecifiedHazard);
            element.SetAttributeValue(nameof(HazardUnit), HazardUnit);
            var hazards = new XElement(nameof(HazardLevels));
            foreach (double value in _hazardLevels)
            {
                var level = new XElement("Level");
                level.SetAttributeValue("Value", SerializationUtilities.FormatDouble(value));
                hazards.Add(level);
            }
            element.Add(hazards);
            element.Add(EventTree.ToXElement(mode));
            return element;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Hashes a projected tree identity rather than persistent node/edge ids, display order,
        /// or nested reference markers. Equivalent self-contained and by-reference forms therefore
        /// share one seed identity.
        /// </remarks>
        public override byte[] CanonicalHash()
        {
            EventTreeOccurrencePlan plan = EventTreeOccurrencePlan.Compile(this);
            var identity = new XElement(nameof(EventTreeResponse));
            var hazards = new XElement(nameof(HazardLevels));
            foreach (double value in _hazardLevels)
            {
                var level = new XElement("Level");
                level.SetAttributeValue("Value", SerializationUtilities.FormatDouble(value));
                hazards.Add(level);
            }
            identity.Add(hazards);
            identity.Add(new XElement(plan.Identity));
            return CanonicalContentHasher.Hash(identity, CanonicalizationRules.ModelRules);
        }

        /// <summary>The sampling mode used by the shared evaluator.</summary>
        private enum SampleMode
        {
            /// <summary>Mean source values.</summary>
            Mean,

            /// <summary>One co-monotonic percentile.</summary>
            Percentile,

            /// <summary>One pre-allocated realization.</summary>
            Realization,
        }

        /// <summary>Evaluates every terminal and implicit branch over the tree hazard axis.</summary>
        /// <param name="mode">The source sampling mode.</param>
        /// <param name="percentile">The percentile when applicable.</param>
        /// <param name="realizationIndex">The realization index when applicable.</param>
        /// <returns>The exhaustive branch sample.</returns>
        private ResponseBranchSample EvaluateBranches(SampleMode mode, double percentile, int realizationIndex)
        {
            EventTreeOccurrencePlan plan = EventTreeOccurrencePlan.Compile(this);
            IReadOnlyList<BranchBinding> bindings = BuildBranchBindings(plan);
            ResponseBranchDescriptor[] descriptors = bindings.Select(binding => binding.Descriptor).ToArray();
            var probabilities = new double[descriptors.Length][];
            for (int branch = 0; branch < probabilities.Length; branch++) probabilities[branch] = new double[_hazardLevels.Count];

            int implicitIndex = Enumerable.Range(0, descriptors.Length)
                .Single(i => descriptors[i].Id == ImplicitBranchId);
            var leafIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Occurrence != null)
                    leafIndex.Add(bindings[i].Occurrence!.CanonicalPath, i);
            }

            for (int h = 0; h < _hazardLevels.Count; h++)
            {
                var stack = new Stack<(EventTreeOccurrenceNode Node, double PathProbability)>();
                stack.Push((plan.Root, 1d));
                double implicitCompensation = 0d;
                while (stack.Count > 0)
                {
                    var item = stack.Pop();
                    if (item.Node.Children.Count == 0)
                    {
                        if (item.Node.SourceNode is not InitiatingNode)
                            probabilities[leafIndex[item.Node.CanonicalPath]][h] = item.PathProbability;
                        continue;
                    }

                    IReadOnlyList<EventTreeOccurrenceNode> children = item.Node.Children;
                    var raw = new double[children.Count];
                    double explicitSum = 0d;
                    double sumCompensation = 0d;
                    int remainderIndex = -1;
                    for (int childIndex = 0; childIndex < children.Count; childIndex++)
                    {
                        if (children[childIndex].SourceNode is RemainderNode)
                        {
                            remainderIndex = childIndex;
                            continue;
                        }
                        raw[childIndex] = EvaluateSource(children[childIndex], _hazardLevels[h],
                            h, mode, percentile, realizationIndex);
                        AddCompensated(ref explicitSum, ref sumCompensation, raw[childIndex]);
                    }

                    double scale = explicitSum > 1d ? 1d / explicitSum : 1d;
                    double residual = explicitSum < 1d ? 1d - explicitSum : 0d;
                    if (remainderIndex < 0 && residual > 0d)
                    {
                        double contribution = item.PathProbability * residual;
                        double current = probabilities[implicitIndex][h];
                        AddCompensated(ref current, ref implicitCompensation, contribution);
                        probabilities[implicitIndex][h] = current;
                    }

                    for (int childIndex = children.Count - 1; childIndex >= 0; childIndex--)
                    {
                        double conditional = childIndex == remainderIndex ? residual : raw[childIndex] * scale;
                        stack.Push((children[childIndex], item.PathProbability * conditional));
                    }
                }
            }

            return new ResponseBranchSample(_hazardLevels, descriptors, probabilities);
        }

        /// <summary>Evaluates one chance-node source in the selected sampling mode.</summary>
        /// <param name="occurrence">The expanded chance-node occurrence.</param>
        /// <param name="hazard">The caller's current hazard value.</param>
        /// <param name="hazardIndex">The hazard index.</param>
        /// <param name="mode">The sampling mode.</param>
        /// <param name="percentile">The shared percentile.</param>
        /// <param name="realizationIndex">The realization index.</param>
        /// <returns>The raw conditional probability.</returns>
        private double EvaluateSource(EventTreeOccurrenceNode occurrence, double hazard,
            int hazardIndex, SampleMode mode, double percentile, int realizationIndex)
        {
            var chance = (ChanceNode)occurrence.SourceNode;
            ProbabilitySource source = chance.ProbabilitySource;
            int sourceHazardIndex = occurrence.SourceFunction._hazardLevels.IndexOf(hazard);
            bool aligned = sourceHazardIndex >= 0;
            double value;
            if (mode == SampleMode.Mean)
            {
                value = aligned
                    ? source.EvaluateMean(hazard, sourceHazardIndex)
                    : source.EvaluateMeanAtHazard(hazard);
            }
            else if (mode == SampleMode.Percentile)
            {
                value = aligned
                    ? source.EvaluatePercentile(hazard, sourceHazardIndex, percentile)
                    : source.EvaluatePercentileAtHazard(hazard, percentile);
            }
            else if (mode == SampleMode.Realization)
            {
                if (source.Kind == ProbabilitySourceKind.ResponseFunctionReference)
                {
                    IResponseFunction sampledFunction = _samplingBindings[occurrence.CanonicalPath].ResponseFunction
                        ?? throw new InvalidOperationException("The referenced response occurrence has no sampler binding.");
                    value = sampledFunction.SampleFunction(realizationIndex).CDF(hazard);
                }
                else
                {
                    double localPercentile = source.Kind == ProbabilitySourceKind.UncertainTabular
                        ? Percentile(realizationIndex, _samplingBindings[occurrence.CanonicalPath].LocalDimension)
                        : 0d;
                    value = aligned
                        ? source.EvaluateRealization(hazard, sourceHazardIndex, realizationIndex, localPercentile)
                        : source.EvaluateRealizationAtHazard(hazard, realizationIndex, localPercentile);
                }
            }
            else
            {
                throw new InvalidOperationException($"Unsupported event-tree sample mode '{mode}'.");
            }

            if (!double.IsFinite(value) || value < 0d || value > 1d)
                throw new InvalidOperationException($"Chance occurrence '{occurrence.DisplayName}' evaluated outside [0, 1]. Call Validate() and correct the source.");
            return value;
        }

        /// <summary>Evaluates one occurrence's mean source for validation.</summary>
        private double EvaluateSourceMean(EventTreeOccurrenceNode occurrence, double hazard, int hazardIndex)
        {
            return EvaluateSource(occurrence, hazard, hazardIndex, SampleMode.Mean, 0d, -1);
        }

        /// <summary>Converts exhaustive branch probabilities to aggregate failure probability.</summary>
        /// <param name="sample">The exhaustive branch sample.</param>
        /// <returns>The aggregate hazard/failure-probability curve.</returns>
        private OrderedPairedData AggregateFailureCurve(ResponseBranchSample sample)
        {
            EventTreeOccurrencePlan plan = EventTreeOccurrencePlan.Compile(this);
            IReadOnlyList<BranchBinding> bindings = BuildBranchBindings(plan);
            var indexByPath = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Occurrence != null)
                    indexByPath.Add(bindings[i].Occurrence!.CanonicalPath, i);
            }

            var failure = new double[sample.Hazards.Count];
            for (int h = 0; h < failure.Length; h++)
            {
                double compensation = 0d;
                foreach (EventTreeOccurrenceNode leaf in plan.Leaves)
                {
                    if (!leaf.IsFailure) continue;
                    int branch = indexByPath[leaf.CanonicalPath];
                    AddCompensated(ref failure[h], ref compensation, sample.Probabilities[branch][h]);
                }
                failure[h] = ClampRoundoff(failure[h]);
            }
            return new OrderedPairedData(sample.Hazards.ToArray(), failure,
                true, SortOrder.Ascending, false, SortOrder.None);
        }

        /// <summary>Builds stable direct and linked terminal descriptors for one occurrence plan.</summary>
        private IReadOnlyList<BranchBinding> BuildBranchBindings(EventTreeOccurrencePlan plan)
        {
            var result = new List<BranchBinding>(plan.Leaves.Count + 1)
            {
                new BranchBinding(null,
                    new ResponseBranchDescriptor(ImplicitBranchId, "Unmodeled", false, 2)),
            };
            var usedIds = new HashSet<Guid> { ImplicitBranchId };
            foreach (EventTreeOccurrenceNode leaf in plan.Leaves.Where(leaf => !leaf.IsLinkedOccurrence)
                .OrderBy(leaf => leaf.SourceNode.OutputPort))
            {
                usedIds.Add(leaf.SourceNode.Id);
                result.Add(new BranchBinding(leaf,
                    new ResponseBranchDescriptor(leaf.SourceNode.Id, leaf.DisplayName,
                        leaf.IsFailure, leaf.SourceNode.OutputPort)));
            }

            var linked = new List<(EventTreeOccurrenceNode Leaf, Guid Id)>();
            foreach (EventTreeOccurrenceNode leaf in plan.Leaves.Where(leaf => leaf.IsLinkedOccurrence))
            {
                int salt = 0;
                Guid id;
                do
                {
                    id = CreateLinkedBranchId(leaf.PersistencePath, salt++);
                }
                while (!usedIds.Add(id));
                linked.Add((leaf, id));
            }

            int nextPort = Math.Max(3, EventTree.Nodes.Select(node => node.OutputPort).DefaultIfEmpty(2).Max() + 1);
            foreach (var item in linked.OrderBy(item => item.Id))
            {
                result.Add(new BranchBinding(item.Leaf,
                    new ResponseBranchDescriptor(item.Id, item.Leaf.DisplayName,
                        item.Leaf.IsFailure, nextPort++)));
            }
            return result.OrderBy(binding => binding.Descriptor.OutputPort).ToArray();
        }

        /// <summary>Creates a stable persisted-occurrence branch id without placing ids on the hash surface.</summary>
        private static Guid CreateLinkedBranchId(string persistencePath, int salt)
        {
            var identity = new XElement("LinkedEventTreeBranch");
            identity.SetAttributeValue("PersistencePath", persistencePath);
            identity.SetAttributeValue("Salt", salt.ToString(CultureInfo.InvariantCulture));
            byte[] hash = CanonicalContentHasher.Hash(identity, CanonicalizationRules.ModelRules);
            var bytes = new byte[16];
            Array.Copy(hash, bytes, bytes.Length);
            return new Guid(bytes);
        }
        /// <summary>Creates an isolated self-contained response occurrence for repeated live references.</summary>
        private static IResponseFunction CloneResponseFunction(IResponseFunction source)
        {
            return RiskFunctionFactory.CreateResponseFunction(source.ToXElement())
                ?? throw new InvalidOperationException(
                    $"Referenced response function '{source.Name}' cannot be cloned for an independent occurrence.");
        }

        /// <summary>Adds a diagnostic once while preserving first-discovery order.</summary>
        private static void AddUnique(ICollection<string> messages, string message)
        {
            if (!messages.Contains(message)) messages.Add(message);
        }

        /// <summary>One realization-sampling binding keyed by canonical occurrence path.</summary>
        private sealed class SamplingBinding
        {
            internal SamplingBinding(int localDimension, IResponseFunction? responseFunction)
            {
                LocalDimension = localDimension;
                ResponseFunction = responseFunction;
            }

            internal int LocalDimension { get; }

            internal IResponseFunction? ResponseFunction { get; }
        }

        /// <summary>Associates one expanded terminal occurrence with its public branch descriptor.</summary>
        private sealed class BranchBinding
        {
            internal BranchBinding(EventTreeOccurrenceNode? occurrence, ResponseBranchDescriptor descriptor)
            {
                Occurrence = occurrence;
                Descriptor = descriptor;
            }

            internal EventTreeOccurrenceNode? Occurrence { get; }

            internal ResponseBranchDescriptor Descriptor { get; }
        }

        /// <summary>Throws when validation reports any error.</summary>
        private void ThrowIfUnusable()
        {
            if (!Validate().IsValid)
                throw new InvalidOperationException("The event-tree response is invalid. Call Validate() and correct the reported errors before sampling.");
        }

        /// <summary>Ensures realization sampling uses the configuration that was set up.</summary>
        private void EnsureSamplerCurrent()
        {
            if (_samplerIdentity == null || !_samplerIdentity.AsSpan().SequenceEqual(CanonicalHash()))
                throw new InvalidOperationException("SetupSampler() must be called after the latest event-tree compute edit and before sampling by realization index.");
        }

        /// <summary>Tests a response curve for non-decreasing failure probability.</summary>
        /// <param name="curve">The sampled curve.</param>
        /// <returns>True when no ordinate decreases.</returns>
        private static bool CurveIsMonotonic(OrderedPairedData curve)
        {
            for (int i = 1; i < curve.Count; i++)
                if (curve[i].Y < curve[i - 1].Y) return false;
            return true;
        }

        /// <summary>Adds one value using Kahan compensated summation.</summary>
        /// <param name="sum">The running sum.</param>
        /// <param name="compensation">The running compensation.</param>
        /// <param name="value">The addend.</param>
        private static void AddCompensated(ref double sum, ref double compensation, double value)
        {
            double adjusted = value - compensation;
            double next = sum + adjusted;
            compensation = (next - sum) - adjusted;
            sum = next;
        }

        /// <summary>Clamps only floating-point roundoff immediately outside the unit interval.</summary>
        /// <param name="value">The computed aggregate probability.</param>
        /// <returns>The unit-interval value.</returns>
        private static double ClampRoundoff(double value)
        {
            if (value < 0d && value >= -ResponseBranchSample.ProbabilitySumTolerance) return 0d;
            if (value > 1d && value <= 1d + ResponseBranchSample.ProbabilitySumTolerance) return 1d;
            if (value < 0d || value > 1d)
                throw new InvalidOperationException("The event-tree aggregate probability fell outside [0, 1].");
            return value;
        }
    }
}
