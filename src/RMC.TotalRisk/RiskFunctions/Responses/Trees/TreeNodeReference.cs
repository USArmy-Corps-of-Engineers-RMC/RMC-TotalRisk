using System;

namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// A persistent reference to a node in the current tree or in another tree response.
    /// Identifiers are authoritative; names are lenient migration fallbacks and display aids.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class TreeNodeReference
    {
        /// <summary>Initializes a node reference.</summary>
        /// <param name="functionId">The external function id, or null for an internal reference.</param>
        /// <param name="nodeId">The target node id.</param>
        /// <param name="functionName">The external function-name fallback.</param>
        /// <param name="nodeName">The node-name fallback.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="nodeId"/> is empty.</exception>
        public TreeNodeReference(Guid? functionId, Guid nodeId, string? functionName = null, string? nodeName = null)
        {
            if (nodeId == Guid.Empty) throw new ArgumentException("A tree node reference requires a non-empty node id.", nameof(nodeId));
            FunctionId = functionId;
            NodeId = nodeId;
            FunctionName = functionName;
            NodeName = nodeName;
        }

        /// <summary>The external function id, or null for an internal reference.</summary>
        public Guid? FunctionId { get; }

        /// <summary>The target node id.</summary>
        public Guid NodeId { get; }

        /// <summary>The external function-name fallback.</summary>
        public string? FunctionName { get; }

        /// <summary>The node-name fallback.</summary>
        public string? NodeName { get; }
    }
}
