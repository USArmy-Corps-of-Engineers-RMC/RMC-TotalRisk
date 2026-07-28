using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RMC.TotalRisk.Core;
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
        private EventTreeOccurrencePlan(EventTreeOccurrenceNode root, IReadOnlyList<string> warnings)
        {
            Root = root;
            Warnings = warnings;
            var preOrder = new List<EventTreeOccurrenceNode>();
            var stack = new Stack<EventTreeOccurrenceNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                EventTreeOccurrenceNode node = stack.Pop();
                preOrder.Add(node);
                for (int i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
            }
            CanonicalPreOrder = preOrder;
            Leaves = preOrder.Where(node => node.Children.Count == 0 && node.SourceNode is not InitiatingNode).ToArray();
            SamplingDimensions = preOrder.Where(node => node.SourceNode is ChanceNode)
                .Sum(node => ((ChanceNode)node.SourceNode).ProbabilitySource.SamplingDimensions);
            Identity = new XElement(nameof(EventTree), new XElement(root.Identity));
            IdentityToken = CanonicalContentHasher.ToTokenHex(
                CanonicalContentHasher.Hash(Identity, CanonicalizationRules.ModelRules));
        }

        /// <summary>The expanded initiating root or selected subtree root.</summary>
        internal EventTreeOccurrenceNode Root { get; }

        /// <summary>All expanded occurrences in metadata-inert canonical pre-order.</summary>
        internal IReadOnlyList<EventTreeOccurrenceNode> CanonicalPreOrder { get; }

        /// <summary>Expanded terminal occurrences in canonical pre-order.</summary>
        internal IReadOnlyList<EventTreeOccurrenceNode> Leaves { get; }

        /// <summary>The projected identity with persistence and reference wrappers removed.</summary>
        internal XElement Identity { get; }

        /// <summary>The stable token of <see cref="Identity"/>.</summary>
        internal string IdentityToken { get; }

        /// <summary>The local and recursively referenced sampling-dimension count.</summary>
        internal int SamplingDimensions { get; }

        /// <summary>Lenient node-name fallback diagnostics discovered during expansion.</summary>
        internal IReadOnlyList<string> Warnings { get; }

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
            AssignCanonicalPaths(root, "R");
            return new EventTreeOccurrencePlan(root, compiler.Warnings.ToArray());
        }

        /// <summary>Assigns stable paths after each sibling set has been sorted by projected identity.</summary>
        private static void AssignCanonicalPaths(EventTreeOccurrenceNode node, string path)
        {
            node.CanonicalPath = path;
            string? previousToken = null;
            int occurrence = -1;
            for (int i = 0; i < node.Children.Count; i++)
            {
                EventTreeOccurrenceNode child = node.Children[i];
                if (!string.Equals(previousToken, child.IdentityToken, StringComparison.Ordinal))
                {
                    previousToken = child.IdentityToken;
                    occurrence = 0;
                }
                else
                {
                    occurrence++;
                }
                AssignCanonicalPaths(child, $"{path}/{child.IdentityToken}:{occurrence}");
            }
        }

        /// <summary>Stateful depth-first compiler used for cycle paths and lenient warnings.</summary>
        private sealed class Compiler
        {
            /// <summary>The active function-plus-node recursion stack.</summary>
            private readonly List<Frame> _active = new List<Frame>();

            /// <summary>Lenient resolution warnings.</summary>
            internal List<string> Warnings { get; } = new List<string>();

            /// <summary>Expands one authored node, following any link wrapper.</summary>
            internal EventTreeOccurrenceNode Expand(EventTreeResponse function, EventNodeBase node,
                bool linkedAncestor, IReadOnlyList<EventTreeLinkNode> pendingLinks,
                IReadOnlyList<Guid> persistencePath)
            {
                int repeatedIndex = _active.FindIndex(frame => ReferenceEquals(frame.Function, function) && frame.Node.Id == node.Id);
                if (repeatedIndex >= 0) throw CycleError(function, node);

                _active.Add(new Frame(function, node));
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
                        occurrence.AuthoredSiblingOrder = i;
                        children.Add(occurrence);
                    }
                    children = children.Select((child, index) => (Child: child, Index: index))
                        .OrderBy(item => item.Child.IdentityToken, StringComparer.Ordinal)
                        .ThenBy(item => item.Index)
                        .Select(item => item.Child)
                        .ToList();

                    bool terminal = children.Count == 0;
                    bool isFailure = terminal && pendingLinks.Count > 0 ? pendingLinks[0].IsFailure : node.IsFailure;
                    XElement identity = BuildUnderlyingIdentity(node, isFailure, terminal, children);
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

                    string displayName = pendingLinks.Count > 0 && terminal
                        ? pendingLinks[0].Name
                        : node.Name;
                    return new EventTreeOccurrenceNode(function, node, children, identity,
                        linkedAncestor || pendingLinks.Count > 0, isFailure, displayName,
                        string.Join("/", persistencePath.Select(id => id.ToString("N"))));
                }
                finally
                {
                    _active.RemoveAt(_active.Count - 1);
                }
            }

            /// <summary>Builds the projected identity of one effective non-link node.</summary>
            private static XElement BuildUnderlyingIdentity(EventNodeBase node, bool isFailure,
                bool terminal, IReadOnlyList<EventTreeOccurrenceNode> children)
            {
                var identity = new XElement("Node");
                identity.SetAttributeValue("Type", node.SerializedName);
                if (terminal) identity.SetAttributeValue(nameof(EventNodeBase.IsFailure), isFailure);
                if (node is ChanceNode chance) identity.Add(chance.ProbabilitySource.ToIdentityXElement());
                for (int i = 0; i < children.Count; i++) identity.Add(new XElement(children[i].Identity));
                return identity;
            }

            /// <summary>Builds a complete cross-function cycle diagnostic.</summary>
            private InvalidOperationException CycleError(EventTreeResponse repeatedFunction, EventNodeBase repeatedNode)
            {
                var labels = _active.Select(frame => Describe(frame.Function, frame.Node)).ToList();
                labels.Add(Describe(repeatedFunction, repeatedNode));
                return new InvalidOperationException(
                    $"Cross-function event-tree cycle detected: {string.Join(" -> ", labels)}.");
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

            /// <summary>One active recursion-stack entry.</summary>
            private readonly struct Frame
            {
                internal Frame(EventTreeResponse function, EventNodeBase node)
                {
                    Function = function;
                    Node = node;
                }

                internal EventTreeResponse Function { get; }

                internal EventNodeBase Node { get; }
            }
        }
    }

    /// <summary>One effective node occurrence in an expanded event-tree plan.</summary>
    internal sealed class EventTreeOccurrenceNode
    {
        internal EventTreeOccurrenceNode(EventTreeResponse sourceFunction, EventNodeBase sourceNode,
            IReadOnlyList<EventTreeOccurrenceNode> children, XElement identity, bool isLinkedOccurrence,
            bool isFailure, string displayName, string persistencePath)
        {
            SourceFunction = sourceFunction;
            SourceNode = sourceNode;
            Children = children;
            Identity = identity;
            IsLinkedOccurrence = isLinkedOccurrence;
            IsFailure = isFailure;
            DisplayName = displayName;
            PersistencePath = persistencePath;
            IdentityToken = CanonicalContentHasher.ToTokenHex(
                CanonicalContentHasher.Hash(identity, CanonicalizationRules.ModelRules));
        }

        /// <summary>The response owning the effective authored source node.</summary>
        internal EventTreeResponse SourceFunction { get; }

        /// <summary>The effective authored chance or remainder node.</summary>
        internal EventNodeBase SourceNode { get; }

        /// <summary>Canonical-order expanded children.</summary>
        internal IReadOnlyList<EventTreeOccurrenceNode> Children { get; }

        /// <summary>The projected occurrence identity.</summary>
        internal XElement Identity { get; }

        /// <summary>The stable token of <see cref="Identity"/>.</summary>
        internal string IdentityToken { get; }

        /// <summary>Whether this occurrence is inside at least one independent link.</summary>
        internal bool IsLinkedOccurrence { get; }

        /// <summary>The effective terminal classification.</summary>
        internal bool IsFailure { get; }

        /// <summary>The occurrence display label.</summary>
        internal string DisplayName { get; }

        /// <summary>The persistent-id occurrence path used only for stable branch addressing.</summary>
        internal string PersistencePath { get; }

        /// <summary>The source parent's persistent child order used only for topology inspection.</summary>
        internal int AuthoredSiblingOrder { get; set; }

        /// <summary>The metadata-free occurrence path assigned after canonical sibling sorting.</summary>
        internal string CanonicalPath { get; set; } = string.Empty;
    }
}
