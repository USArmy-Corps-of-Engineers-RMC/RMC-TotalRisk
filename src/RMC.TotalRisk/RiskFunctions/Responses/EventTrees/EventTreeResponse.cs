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
    /// This first Phase 10A implementation slice supports scalar, aligned uncertain-tabular, and
    /// ordinary response-function probability sources. Event-tree-to-event-tree source recursion,
    /// link nodes, copy/paste fragments, legacy recursive-XML conversion, expanded graph ports,
    /// and the allocation-free compiled-plan optimization remain explicitly deferred.
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

        /// <summary>Sampler dimension assigned to each local uncertain table.</summary>
        private readonly Dictionary<Guid, int> _localDimensionByNode = new Dictionary<Guid, int>();

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
        public override bool IsDeterministic => EventTree.Nodes.OfType<ChanceNode>()
            .All(node => node.ProbabilitySource.IsDeterministic);

        /// <inheritdoc/>
        /// <remarks>
        /// Counts one dimension for every aligned table plus dimensions recursively reported by
        /// each distinct referenced response occurrence.
        /// </remarks>
        public override int SamplingDimensions => EventTree.Nodes.OfType<ChanceNode>()
            .Sum(node => node.ProbabilitySource.SamplingDimensions);

        /// <inheritdoc/>
        /// <remarks>
        /// Local table dimensions are columns of this function's sampler. Referenced responses are
        /// set up recursively with metadata-inert content-and-occurrence seeds.
        /// </remarks>
        public override void SetupSampler(int sampleSize, int seed, SamplingScheme scheme)
        {
            ThrowIfUnusable();
            base.SetupSampler(sampleSize, seed, scheme);
            _localDimensionByNode.Clear();

            int dimension = 0;
            int responseOccurrence = 0;
            foreach (ChanceNode chance in EventTree.CanonicalPreOrder().OfType<ChanceNode>())
            {
                ProbabilitySource source = chance.ProbabilitySource;
                if (source.Kind == ProbabilitySourceKind.UncertainTabular)
                {
                    _localDimensionByNode.Add(chance.Id, dimension);
                    dimension++;
                }
                else if (source.Kind == ProbabilitySourceKind.ResponseFunctionReference && source.ResponseFunction != null)
                {
                    byte[] sourceHash = Convert.FromHexString(source.CanonicalToken());
                    source.ResponseFunction.SetupSampler(sampleSize,
                        SeedHelpers.HashCombine(seed, sourceHash, responseOccurrence++), scheme);
                    int childDimensions = source.ResponseFunction.SamplingDimensions;
                    for (int realization = 0; realization < sampleSize; realization++)
                    {
                        for (int childDimension = 0; childDimension < childDimensions; childDimension++)
                        {
                            _percentiles![realization, dimension + childDimension] =
                                ((RiskFunctionBase)source.ResponseFunction).SampledPercentile(realization, childDimension);
                        }
                    }
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

            var responseSources = EventTree.Nodes.OfType<ChanceNode>()
                .Select(node => node.ProbabilitySource.ResponseFunction)
                .Where(function => function != null)
                .ToArray();
            for (int i = 0; i < responseSources.Length; i++)
            {
                for (int j = i + 1; j < responseSources.Length; j++)
                {
                    if (ReferenceEquals(responseSources[i], responseSources[j]))
                    {
                        messages.Add($"Error: Referenced response function '{responseSources[i]!.Name}' is used by more than one chance node; independent repeated response occurrences are deferred beyond the first Phase 10A implementation slice.");
                        i = responseSources.Length;
                        break;
                    }
                }
            }

            if (messages.All(message => !message.StartsWith("Error:", StringComparison.Ordinal)))
            {
                foreach (EventNodeBase parent in EventTree.DepthFirstPreOrder())
                {
                    if (parent.Children.Count == 0) continue;
                    for (int h = 0; h < _hazardLevels.Count; h++)
                    {
                        double sum = 0d;
                        double compensation = 0d;
                        foreach (ChanceNode chance in CanonicalChildren(parent).OfType<ChanceNode>())
                            AddCompensated(ref sum, ref compensation,
                                chance.ProbabilitySource.EvaluateMean(_hazardLevels[h], h));
                        if (sum > 1d)
                        {
                            messages.Add($"Warning: Explicit branches of event-tree node '{parent.Name}' sum to {sum.ToString("G17", CultureInfo.InvariantCulture)} at hazard {_hazardLevels[h].ToString("G17", CultureInfo.InvariantCulture)} and will be normalized proportionally.");
                            break;
                        }
                    }
                }
            }

            return (messages.All(message => !message.StartsWith("Error:", StringComparison.Ordinal)), messages);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ResponseBranchDescriptor> GetBranches()
        {
            var leaves = EventTree.GetLeaves().OrderBy(node => node.OutputPort).ToArray();
            var descriptors = new List<ResponseBranchDescriptor>(leaves.Length + 1);
            for (int i = 0; i < leaves.Length; i++)
                descriptors.Add(new ResponseBranchDescriptor(leaves[i].Id, leaves[i].Name, leaves[i].IsFailure, leaves[i].OutputPort));
            descriptors.Insert(0, new ResponseBranchDescriptor(ImplicitBranchId, "Unmodeled", false, 2));
            return descriptors;
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
            var identity = new XElement(nameof(EventTreeResponse));
            var hazards = new XElement(nameof(HazardLevels));
            foreach (double value in _hazardLevels)
            {
                var level = new XElement("Level");
                level.SetAttributeValue("Value", SerializationUtilities.FormatDouble(value));
                hazards.Add(level);
            }
            identity.Add(hazards);
            identity.Add(EventTree.ToIdentityXElement());
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
            IReadOnlyList<ResponseBranchDescriptor> descriptors = GetBranches();
            var probabilities = new double[descriptors.Count][];
            for (int branch = 0; branch < probabilities.Length; branch++) probabilities[branch] = new double[_hazardLevels.Count];
            var leafIndex = new Dictionary<Guid, int>();
            int implicitIndex = Enumerable.Range(0, descriptors.Count).Single(i => descriptors[i].Id == ImplicitBranchId);
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (i != implicitIndex) leafIndex.Add(descriptors[i].Id, i);
            }

            for (int h = 0; h < _hazardLevels.Count; h++)
            {
                var stack = new Stack<(EventNodeBase Node, double PathProbability)>();
                stack.Push((EventTree.Root, 1d));
                double implicitCompensation = 0d;
                while (stack.Count > 0)
                {
                    var item = stack.Pop();
                    if (item.Node.IsTerminal)
                    {
                        if (item.Node is not InitiatingNode)
                            probabilities[leafIndex[item.Node.Id]][h] = item.PathProbability;
                        continue;
                    }

                    IReadOnlyList<EventNodeBase> children = CanonicalChildren(item.Node);
                    var raw = new double[children.Count];
                    double explicitSum = 0d;
                    double sumCompensation = 0d;
                    int remainderIndex = -1;
                    for (int childIndex = 0; childIndex < children.Count; childIndex++)
                    {
                        if (children[childIndex] is RemainderNode)
                        {
                            remainderIndex = childIndex;
                            continue;
                        }
                        var chance = (ChanceNode)children[childIndex];
                        raw[childIndex] = EvaluateSource(chance, h, mode, percentile, realizationIndex);
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
        /// <param name="chance">The chance node.</param>
        /// <param name="hazardIndex">The hazard index.</param>
        /// <param name="mode">The sampling mode.</param>
        /// <param name="percentile">The shared percentile.</param>
        /// <param name="realizationIndex">The realization index.</param>
        /// <returns>The raw conditional probability.</returns>
        private double EvaluateSource(ChanceNode chance, int hazardIndex, SampleMode mode, double percentile, int realizationIndex)
        {
            ProbabilitySource source = chance.ProbabilitySource;
            double hazard = _hazardLevels[hazardIndex];
            double value = mode switch
            {
                SampleMode.Mean => source.EvaluateMean(hazard, hazardIndex),
                SampleMode.Percentile => source.EvaluatePercentile(hazard, hazardIndex, percentile),
                SampleMode.Realization => source.EvaluateRealization(hazard, hazardIndex, realizationIndex,
                    source.Kind == ProbabilitySourceKind.UncertainTabular
                        ? Percentile(realizationIndex, _localDimensionByNode[chance.Id])
                        : 0d),
                _ => throw new InvalidOperationException($"Unsupported event-tree sample mode '{mode}'."),
            };
            if (!double.IsFinite(value) || value < 0d || value > 1d)
                throw new InvalidOperationException($"Chance node '{chance.Name}' evaluated outside [0, 1]. Call Validate() and correct the source.");
            return value;
        }

        /// <summary>Converts exhaustive branch probabilities to aggregate failure probability.</summary>
        /// <param name="sample">The exhaustive branch sample.</param>
        /// <returns>The aggregate hazard/failure-probability curve.</returns>
        private OrderedPairedData AggregateFailureCurve(ResponseBranchSample sample)
        {
            var failure = new double[sample.Hazards.Count];
            var indexById = sample.Branches.Select((branch, index) => (branch.Id, index)).ToDictionary(item => item.Id, item => item.index);
            var failureLeaves = CanonicalLeaves().Where(node => node.IsFailure).ToArray();
            for (int h = 0; h < failure.Length; h++)
            {
                double compensation = 0d;
                foreach (EventNodeBase leaf in failureLeaves)
                {
                    int branch = indexById[leaf.Id];
                    AddCompensated(ref failure[h], ref compensation, sample.Probabilities[branch][h]);
                }
                failure[h] = ClampRoundoff(failure[h]);
            }
            return new OrderedPairedData(sample.Hazards.ToArray(), failure,
                true, SortOrder.Ascending, false, SortOrder.None);
        }

        /// <summary>Gets terminal nodes in metadata-inert canonical order.</summary>
        /// <returns>The authored leaves.</returns>
        private IReadOnlyList<EventNodeBase> CanonicalLeaves()
        {
            return EventTree.CanonicalPreOrder()
                .Where(node => node.IsTerminal && node is not InitiatingNode)
                .ToArray();
        }

        /// <summary>Gets canonical children without using display order as compute identity.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The canonical child sequence.</returns>
        private IReadOnlyList<EventNodeBase> CanonicalChildren(EventNodeBase parent)
        {
            return parent.Children
                .Select((child, order) => new
                {
                    Child = child,
                    Order = order,
                    Token = CanonicalContentHasher.ToTokenHex(EventTree.SubtreeCanonicalHash(child.Id)),
                })
                .OrderBy(item => item.Token, StringComparer.Ordinal)
                .ThenBy(item => item.Order)
                .Select(item => item.Child)
                .ToArray();
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
