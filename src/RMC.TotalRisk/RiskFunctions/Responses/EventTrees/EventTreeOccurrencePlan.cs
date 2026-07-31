using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Numerics.Data;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.RiskFunctions.Responses.EventTrees
{
    /// <summary>
    /// Immutable expanded event-tree occurrence plan. Authored link nodes are retained in the
    /// projected identity and occurrence paths but resolved to their target subtrees for compute.
    /// </summary>
    internal sealed class EventTreeOccurrencePlan
    {
        /// <summary>Initializes a completed occurrence plan.</summary>
        private EventTreeOccurrencePlan(EventTreeOccurrenceNode root,
            IReadOnlyList<string> warnings, TreePlanDependencies dependencies)
        {
            Root = root;
            Warnings = Array.AsReadOnly(warnings.ToArray());
            Dependencies = dependencies;
            var preOrder = new List<EventTreeOccurrenceNode>();
            var stack = new Stack<EventTreeOccurrenceNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                EventTreeOccurrenceNode node = stack.Pop();
                preOrder.Add(node);
                for (int i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
            }
            CanonicalPreOrder = Array.AsReadOnly(preOrder.ToArray());
            Leaves = Array.AsReadOnly(preOrder
                .Where(node => node.Children.Count == 0 && node.SourceNode is not InitiatingNode)
                .ToArray());
            SamplingDimensions = preOrder.Sum(node => node.ProbabilitySamplingDimensions);
            IsDeterministic = preOrder
                .Where(node => node.SourceNode is ChanceNode)
                .All(node => node.ProbabilityIsDeterministic);
            _identity = new XElement(nameof(EventTree), root.Identity);
            IdentityToken = CanonicalContentHasher.ToTokenHex(
                CanonicalContentHasher.Hash(_identity, CanonicalizationRules.ModelRules));

            var indexes = new Dictionary<EventTreeOccurrenceNode, int>(
                ReferenceEqualityComparer.Instance);
            for (int i = 0; i < preOrder.Count; i++) indexes.Add(preOrder[i], i);
            var instructions = new EventTreeEvaluationInstruction[preOrder.Count];
            int edgeCount = 0;
            for (int i = 0; i < preOrder.Count; i++)
            {
                int[] childIndexes = preOrder[i].Children.Select(child => indexes[child]).ToArray();
                edgeCount += childIndexes.Length;
                instructions[i] = new EventTreeEvaluationInstruction(preOrder[i], childIndexes);
            }
            EvaluationInstructions = Array.AsReadOnly(instructions);
            ExpandedEdgeCount = edgeCount;
        }

        /// <summary>The expanded initiating root or selected subtree root.</summary>
        internal EventTreeOccurrenceNode Root { get; }

        /// <summary>All expanded occurrences in metadata-inert canonical pre-order.</summary>
        internal IReadOnlyList<EventTreeOccurrenceNode> CanonicalPreOrder { get; }

        /// <summary>Expanded terminal occurrences in canonical pre-order.</summary>
        internal IReadOnlyList<EventTreeOccurrenceNode> Leaves { get; }

        /// <summary>Primitive-index evaluation instructions in parent-before-child order.</summary>
        internal IReadOnlyList<EventTreeEvaluationInstruction> EvaluationInstructions { get; }

        /// <summary>The number of expanded parent-child edges.</summary>
        internal int ExpandedEdgeCount { get; }

        /// <summary>The privately owned projected identity.</summary>
        private readonly XElement _identity;

        /// <summary>Returns an owned copy of the projected identity.</summary>
        internal XElement Identity => new XElement(_identity);

        /// <summary>The stable token of <see cref="Identity"/>.</summary>
        internal string IdentityToken { get; }

        /// <summary>The local and recursively referenced sampling-dimension count.</summary>
        internal int SamplingDimensions { get; }

        /// <summary>Whether every expanded local and nested probability source is deterministic.</summary>
        internal bool IsDeterministic { get; }

        /// <summary>Lenient node-name fallback diagnostics discovered during expansion.</summary>
        internal IReadOnlyList<string> Warnings { get; }

        /// <summary>The complete live-content snapshot governing safe plan reuse.</summary>
        internal TreePlanDependencies Dependencies { get; }

        /// <summary>Compiles the full response tree.</summary>
        /// <param name="owner">The owning response.</param>
        /// <returns>The immutable expanded plan.</returns>
        internal static EventTreeOccurrencePlan Compile(EventTreeResponse owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            return Compile(owner, owner.EventTree.Root);
        }

        /// <summary>Compiles one authored subtree in the context of its owning response.</summary>
        /// <param name="owner">The owning response.</param>
        /// <param name="subtreeRoot">The selected authored subtree root.</param>
        /// <returns>The immutable expanded plan.</returns>
        internal static EventTreeOccurrencePlan Compile(EventTreeResponse owner, EventNodeBase subtreeRoot)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (subtreeRoot == null) throw new ArgumentNullException(nameof(subtreeRoot));
            if (!ReferenceEquals(subtreeRoot.Owner, owner.EventTree))
                throw new InvalidOperationException("The selected event-tree subtree does not belong to the response.");

            var compiler = new Compiler();
            var persistence = new List<Guid> { subtreeRoot.Id };
            EventTreeOccurrenceNode root = compiler.Expand(owner, subtreeRoot, false,
                Array.Empty<EventTreeLinkNode>(), persistence);
            AssignCanonicalPaths(root);
            return new EventTreeOccurrencePlan(root, compiler.Warnings.ToArray(),
                compiler.CreateDependencies());
        }

        /// <summary>Assigns stable paths after each sibling set has been sorted by projected identity.</summary>
        private static void AssignCanonicalPaths(EventTreeOccurrenceNode root)
        {
            TreeCanonicalPaths.Assign(root, node => node.Children, node => node.IdentityToken,
                (node, path) => node.AssignCanonicalPath(path));
        }

        /// <summary>Stateful depth-first compiler used for cycle paths and lenient warnings.</summary>
        private sealed class Compiler
        {
            /// <summary>Every event-tree response whose live compute state feeds this plan.</summary>
            private readonly HashSet<EventTreeResponse> _eventTreeFunctions =
                new HashSet<EventTreeResponse>(ReferenceEqualityComparer.Instance);

            /// <summary>Every mutable local uncertain table read by this plan.</summary>
            private readonly HashSet<UncertainOrderedPairedData> _tables =
                new HashSet<UncertainOrderedPairedData>(ReferenceEqualityComparer.Instance);

            /// <summary>Every ordinary live response referenced by this plan.</summary>
            private readonly HashSet<IResponseFunction> _ordinaryResponses =
                new HashSet<IResponseFunction>(ReferenceEqualityComparer.Instance);

            /// <summary>Lenient resolution warnings.</summary>
            internal List<string> Warnings { get; } = new List<string>();

            /// <summary>Captures the complete dependency state after expansion succeeds.</summary>
            internal TreePlanDependencies CreateDependencies()
            {
                return new TreePlanDependencies(_eventTreeFunctions.ToArray(),
                    _tables.ToArray(), _ordinaryResponses.ToArray());
            }

            /// <summary>Expands one authored node, following any link wrapper.</summary>
            internal EventTreeOccurrenceNode Expand(EventTreeResponse function, EventNodeBase node,
                bool linkedAncestor, IReadOnlyList<EventTreeLinkNode> pendingLinks,
                IReadOnlyList<Guid> persistencePath)
            {
                if (TreeCompilationScope.IndexOf(function, node.Id) >= 0)
                    throw TreeCompilationScope.CreateCycleError(function, node, EventTreeFrameDescriber.Instance);
                _eventTreeFunctions.Add(function);

                TreeCompilationScope.Push(function, node, node.Id, EventTreeFrameDescriber.Instance);
                try
                {
                    if (node is EventTreeLinkNode link)
                    {
                        if (link.Children.Count != 0)
                            throw new InvalidOperationException($"Event-tree link '{link.Name}' in function '{DisplayFunction(function)}' cannot own authored children.");
                        if (link.UnresolvedReferences.Count > 0)
                            throw new InvalidOperationException($"Event-tree link '{link.Name}' in function '{DisplayFunction(function)}' references {string.Join(", ", link.UnresolvedReferences)}, which was not found.");

                        EventTreeResponse targetFunction = link.IsExternal
                            ? link.TargetFunction ?? throw new InvalidOperationException(
                                $"External event-tree link '{link.Name}' in function '{DisplayFunction(function)}' has no resolved target function.")
                            : function;
                        EventTree targetTree = targetFunction.EventTree;
                        EventNodeBase? targetNode = link.ResolveNode(targetTree, out bool usedNameFallback);
                        if (targetNode == null)
                        {
                            TreeNodeReference target = link.Target;
                            throw new InvalidOperationException(
                                $"Event-tree link '{link.Name}' in function '{DisplayFunction(function)}' cannot resolve target node " +
                                $"'{target.NodeName ?? target.NodeId.ToString("D")}' in function '{DisplayFunction(targetFunction)}'.");
                        }
                        if (targetNode is InitiatingNode)
                            throw new InvalidOperationException(
                                $"Event-tree link '{link.Name}' in function '{DisplayFunction(function)}' targets initiating node " +
                                $"'{targetNode.Name}' in function '{DisplayFunction(targetFunction)}'; links must target a chance or remainder subtree.");
                        if (usedNameFallback)
                        {
                            Warnings.Add(
                                $"Warning: Event-tree link '{link.Name}' in function '{DisplayFunction(function)}' resolved target node " +
                                $"'{targetNode.Name}' by its name fallback; save the model to repair the persistent node id.");
                        }

                        var wrappers = new List<EventTreeLinkNode>(pendingLinks.Count + 1);
                        wrappers.AddRange(pendingLinks);
                        wrappers.Add(link);
                        var targetPersistence = new List<Guid>(persistencePath.Count + 1);
                        targetPersistence.AddRange(persistencePath);
                        targetPersistence.Add(targetNode.Id);
                        return Expand(targetFunction, targetNode, true, wrappers, targetPersistence);
                    }

                    XElement? probabilityIdentity = null;
                    int probabilitySamplingDimensions = 0;
                    bool probabilityIsDeterministic = true;
                    if (node is ChanceNode chance)
                    {
                        ProbabilitySource source = chance.ProbabilitySource;
                        if (source.Table is not null) _tables.Add(source.Table);
                        if (source.ResponseFunction != null && source.ResponseFunction is not EventTreeResponse)
                            _ordinaryResponses.Add(source.ResponseFunction);
                        byte[]? recursiveResponseHash = null;
                        if (source.ResponseFunction is EventTreeResponse nestedResponse)
                        {
                            EventTreeOccurrencePlan nestedPlan = CompileNestedResponse(nestedResponse);
                            probabilitySamplingDimensions = nestedPlan.SamplingDimensions;
                            probabilityIsDeterministic = nestedPlan.IsDeterministic;
                            recursiveResponseHash = nestedResponse.CanonicalHash(nestedPlan);
                        }
                        else
                        {
                            probabilitySamplingDimensions = source.SamplingDimensions;
                            probabilityIsDeterministic = source.IsDeterministic;
                        }
                        probabilityIdentity = source.ToIdentityXElement(recursiveResponseHash);
                    }

                    var children = new List<EventTreeOccurrenceNode>(node.Children.Count);
                    for (int i = 0; i < node.Children.Count; i++)
                    {
                        EventNodeBase child = node.Children[i];
                        var childPersistence = new List<Guid>(persistencePath.Count + 1);
                        childPersistence.AddRange(persistencePath);
                        childPersistence.Add(child.Id);
                        EventTreeOccurrenceNode occurrence = Expand(function, child,
                            linkedAncestor || pendingLinks.Count > 0,
                            Array.Empty<EventTreeLinkNode>(), childPersistence);
                        occurrence.AssignAuthoredSiblingOrder(i);
                        children.Add(occurrence);
                    }
                    children = children.Select((child, index) => (Child: child, Index: index))
                        .OrderBy(item => item.Child.IdentityToken, StringComparer.Ordinal)
                        .ThenBy(item => item.Index)
                        .Select(item => item.Child)
                        .ToList();

                    bool terminal = children.Count == 0;
                    bool isFailure = terminal && pendingLinks.Count > 0 ? pendingLinks[0].IsFailure : node.IsFailure;
                    XElement identity = BuildUnderlyingIdentity(node, isFailure, terminal, children,
                        probabilityIdentity);
                    if (pendingLinks.Count > 0)
                    {
                        for (int i = pendingLinks.Count - 1; i >= 0; i--)
                        {
                            EventTreeLinkNode wrapper = pendingLinks[i];
                            var linkIdentity = new XElement("Node");
                            linkIdentity.SetAttributeValue("Type", nameof(EventTreeLinkNode));
                            linkIdentity.SetAttributeValue(nameof(EventTreeLinkNode.LinkMode), wrapper.LinkMode.ToString());
                            if (terminal && i == 0) linkIdentity.SetAttributeValue(nameof(EventNodeBase.IsFailure), isFailure);
                            linkIdentity.Add(new XElement("Target", identity));
                            identity = linkIdentity;
                        }
                    }

                    EventNodeBase displayNode = pendingLinks.Count > 0 && terminal
                        ? pendingLinks[0]
                        : node;
                    return new EventTreeOccurrenceNode(function, node, children, identity,
                        linkedAncestor || pendingLinks.Count > 0, isFailure, displayNode,
                        string.Join("/", persistencePath.Select(id => id.ToString("N"))),
                        probabilitySamplingDimensions, probabilityIsDeterministic,
                        probabilityIdentity);
                }
                finally
                {
                    TreeCompilationScope.Pop();
                }
            }

            /// <summary>Compiles one nested response while retaining the caller's active cycle stack.</summary>
            private EventTreeOccurrencePlan CompileNestedResponse(EventTreeResponse function)
            {
                EventNodeBase rootNode = function.EventTree.Root;
                var persistence = new List<Guid> { rootNode.Id };
                EventTreeOccurrenceNode root = Expand(function, rootNode, false,
                    Array.Empty<EventTreeLinkNode>(), persistence);
                AssignCanonicalPaths(root);
                return new EventTreeOccurrencePlan(root, Array.Empty<string>(),
                    TreePlanDependencies.Empty);
            }

            /// <summary>Builds the projected identity of one effective non-link node.</summary>
            private static XElement BuildUnderlyingIdentity(EventNodeBase node, bool isFailure,
                bool terminal, IReadOnlyList<EventTreeOccurrenceNode> children,
                XElement? probabilityIdentity)
            {
                var identity = new XElement("Node");
                identity.SetAttributeValue("Type", node.SerializedName);
                if (terminal) identity.SetAttributeValue(nameof(EventNodeBase.IsFailure), isFailure);
                if (node is ChanceNode) identity.Add(probabilityIdentity);
                for (int i = 0; i < children.Count; i++) identity.Add(children[i].Identity);
                return identity;
            }

            /// <summary>Describes one cycle-path entry without using it as compute identity.</summary>
            private static string Describe(EventTreeResponse function, EventNodeBase node)
            {
                string nodeName = string.IsNullOrEmpty(node.Name) ? node.SerializedName : node.Name;
                return $"'{DisplayFunction(function)}'::'{nodeName}' ({node.SerializedName}, {node.Id:D})";
            }

            /// <summary>Returns a useful function label for diagnostics.</summary>
            private static string DisplayFunction(EventTreeResponse function)
            {
                return string.IsNullOrEmpty(function.Name) ? "<unnamed event tree>" : function.Name;
            }

            /// <summary>The shared event-tree frame renderer for the ambient compilation scope.</summary>
            private sealed class EventTreeFrameDescriber : TreeFrameDescriber
            {
                /// <summary>The shared instance.</summary>
                internal static EventTreeFrameDescriber Instance { get; } = new EventTreeFrameDescriber();

                /// <inheritdoc/>
                internal override string CycleKindLabel => "event-tree";

                /// <inheritdoc/>
                internal override string Describe(object function, object node)
                {
                    return Compiler.Describe((EventTreeResponse)function, (EventNodeBase)node);
                }
            }
        }
    }

    /// <summary>One immutable parent-before-child evaluation instruction.</summary>
    internal sealed class EventTreeEvaluationInstruction
    {
        /// <summary>Initializes one instruction and seals its child-index array.</summary>
        internal EventTreeEvaluationInstruction(EventTreeOccurrenceNode occurrence,
            IReadOnlyList<int> childIndexes)
        {
            Occurrence = occurrence;
            _childIndexes = childIndexes.ToArray();
        }

        /// <summary>The effective occurrence evaluated by this instruction.</summary>
        internal EventTreeOccurrenceNode Occurrence { get; }

        /// <summary>The immutable child-index storage.</summary>
        private readonly int[] _childIndexes;

        /// <summary>The number of outgoing expanded edges.</summary>
        internal int ChildCount => _childIndexes.Length;

        /// <summary>Returns one child instruction index.</summary>
        internal int ChildIndex(int index)
        {
            return _childIndexes[index];
        }
    }

    /// <summary>One effective node occurrence in an expanded event-tree plan.</summary>
    internal sealed class EventTreeOccurrenceNode
    {
        internal EventTreeOccurrenceNode(EventTreeResponse sourceFunction, EventNodeBase sourceNode,
            IReadOnlyList<EventTreeOccurrenceNode> children, XElement identity, bool isLinkedOccurrence,
            bool isFailure, EventNodeBase displayNode, string persistencePath,
            int probabilitySamplingDimensions, bool probabilityIsDeterministic,
            XElement? probabilityIdentity)
        {
            SourceFunction = sourceFunction;
            SourceNode = sourceNode;
            Children = Array.AsReadOnly(children.ToArray());
            _identity = new XElement(identity);
            IsLinkedOccurrence = isLinkedOccurrence;
            IsFailure = isFailure;
            DisplayNode = displayNode;
            PersistencePath = persistencePath;
            IdentityToken = CanonicalContentHasher.ToTokenHex(CanonicalContentHasher.Hash(
                _identity, CanonicalizationRules.ModelRules));
            ProbabilitySamplingDimensions = probabilitySamplingDimensions;
            ProbabilityIsDeterministic = probabilityIsDeterministic;
            ProbabilityIdentityToken = probabilityIdentity == null
                ? string.Empty
                : CanonicalContentHasher.ToTokenHex(
                    CanonicalContentHasher.Hash(probabilityIdentity, CanonicalizationRules.ModelRules));
        }

        /// <summary>The response owning the effective authored source node.</summary>
        internal EventTreeResponse SourceFunction { get; }

        /// <summary>The effective authored chance or remainder node.</summary>
        internal EventNodeBase SourceNode { get; }

        /// <summary>Canonical-order expanded children.</summary>
        internal IReadOnlyList<EventTreeOccurrenceNode> Children { get; }

        /// <summary>The privately owned projected occurrence identity.</summary>
        private readonly XElement _identity;

        /// <summary>Returns an owned copy of the projected occurrence identity.</summary>
        internal XElement Identity => new XElement(_identity);

        /// <summary>The stable token of <see cref="Identity"/>.</summary>
        internal string IdentityToken { get; }

        /// <summary>Whether this occurrence is inside at least one independent link.</summary>
        internal bool IsLinkedOccurrence { get; }

        /// <summary>The effective terminal classification.</summary>
        internal bool IsFailure { get; }

        /// <summary>The authored metadata node that labels this occurrence.</summary>
        private EventNodeBase DisplayNode { get; }

        /// <summary>The occurrence display label.</summary>
        internal string DisplayName => DisplayNode.Name;

        /// <summary>The persistent-id occurrence path used only for stable branch addressing.</summary>
        internal string PersistencePath { get; }

        /// <summary>The exact recursive sampling dimensions contributed by this chance occurrence.</summary>
        internal int ProbabilitySamplingDimensions { get; }

        /// <summary>Whether this chance occurrence's complete probability-source graph is deterministic.</summary>
        internal bool ProbabilityIsDeterministic { get; }

        /// <summary>The stable projected-identity token used for this source occurrence's seed.</summary>
        internal string ProbabilityIdentityToken { get; }

        /// <summary>The source parent's persistent child order used only for topology inspection.</summary>
        internal int AuthoredSiblingOrder { get; private set; }

        /// <summary>The metadata-free occurrence path assigned after canonical sibling sorting.</summary>
        internal string CanonicalPath { get; private set; } = string.Empty;

        /// <summary>Assigns the source presentation order during compilation.</summary>
        internal void AssignAuthoredSiblingOrder(int order)
        {
            AuthoredSiblingOrder = order;
        }

        /// <summary>Assigns the canonical occurrence path during compilation.</summary>
        internal void AssignCanonicalPath(string path)
        {
            CanonicalPath = path;
        }
    }
}
