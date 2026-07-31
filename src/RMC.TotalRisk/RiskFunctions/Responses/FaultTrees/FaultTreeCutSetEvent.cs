using System;

namespace RMC.TotalRisk.RiskFunctions.Responses.FaultTrees
{
    /// <summary>
    /// One basic-event member of a minimal cut set, addressed by its authored node and the
    /// canonical occurrence path of its unified variable's first expanded occurrence.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class FaultTreeCutSetEvent
    {
        /// <summary>Initializes one cut-set member.</summary>
        /// <param name="nodeId">The authored basic-event node id.</param>
        /// <param name="name">The authored display name.</param>
        /// <param name="canonicalPath">The unified variable's first-occurrence canonical path.</param>
        /// <exception cref="ArgumentNullException">Thrown when a label is null.</exception>
        internal FaultTreeCutSetEvent(Guid nodeId, string name, string canonicalPath)
        {
            NodeId = nodeId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            CanonicalPath = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath));
        }

        /// <summary>The authored basic-event node id.</summary>
        public Guid NodeId { get; }

        /// <summary>The authored display name.</summary>
        public string Name { get; }

        /// <summary>The unified variable's first-occurrence canonical path.</summary>
        public string CanonicalPath { get; }
    }
}
