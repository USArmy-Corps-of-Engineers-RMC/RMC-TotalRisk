using System;

namespace RMC.TotalRisk.Analyses
{
    /// <summary>
    /// One house-event override of a configuration-risk query: the fault-tree response function,
    /// the house-event node inside it, and the deterministic state the queried configuration
    /// holds it at. Runtime-only input — never serialized, hashed, or applied to the authored
    /// model; the query flips the state on throwaway clones.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class HouseEventState
    {
        /// <summary>
        /// Initializes a house-event override.
        /// </summary>
        /// <param name="functionId">The <see cref="Core.Interfaces.IRiskFunction.Id"/> of the fault-tree response owning the house event.</param>
        /// <param name="nodeId">The persistent id of the house-event node inside that function.</param>
        /// <param name="state">The deterministic Boolean state of the queried configuration.</param>
        /// <exception cref="ArgumentException">Thrown when either id is empty.</exception>
        public HouseEventState(Guid functionId, Guid nodeId, bool state)
        {
            if (functionId == Guid.Empty)
                throw new ArgumentException("The house-event override requires a function id.", nameof(functionId));
            if (nodeId == Guid.Empty)
                throw new ArgumentException("The house-event override requires a node id.", nameof(nodeId));
            FunctionId = functionId;
            NodeId = nodeId;
            State = state;
        }

        /// <summary>
        /// The id of the fault-tree response function owning the house event — rename-proof and
        /// clone-stable.
        /// </summary>
        public Guid FunctionId { get; }

        /// <summary>The persistent id of the house-event node inside the function.</summary>
        public Guid NodeId { get; }

        /// <summary>The deterministic Boolean state the queried configuration holds the event at.</summary>
        public bool State { get; }
    }
}
