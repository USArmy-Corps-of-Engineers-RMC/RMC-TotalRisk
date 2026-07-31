using System;
using System.Collections.Generic;
using System.Linq;

namespace RMC.TotalRisk.RiskFunctions.Responses.FaultTrees
{
    /// <summary>
    /// One minimal cut set of a coherent fault tree: a smallest combination of unified basic
    /// events whose joint occurrence guarantees the top event. Cut sets are inspection output
    /// only; the response's probability always comes from the exact decision diagram.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class FaultTreeCutSet
    {
        /// <summary>Initializes one minimal cut set.</summary>
        /// <param name="events">The member events ordered by unified variable.</param>
        /// <exception cref="ArgumentNullException">Thrown when the member list is null.</exception>
        internal FaultTreeCutSet(IEnumerable<FaultTreeCutSetEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            Events = events.ToArray();
        }

        /// <summary>The member events ordered by unified variable.</summary>
        public IReadOnlyList<FaultTreeCutSetEvent> Events { get; }
    }
}
