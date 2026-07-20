using System;

namespace RMC.TotalRisk.Models.RiskAnalysis.Graph
{
    /// <summary>
    /// Resolves pending serialized element references (dual Id + Name) back to live element
    /// instances during graph deserialization — Id-authoritative and loud, name-fallback lenient.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Verbatim adaptation of the Hydrologics <c>ElementResolver</c> policy: when a serialized Id
    /// is present it is authoritative — an unresolvable Id throws, because a dangling persistent
    /// reference means the serialized form is inconsistent. When only a legacy/hand-authored name
    /// is present the lookup is lenient — an unresolved name returns null and graph validation
    /// reports the dangling connection.
    /// </para>
    /// </remarks>
    public readonly struct RiskElementResolver
    {
        /// <summary>
        /// The Id lookup over the owning graph.
        /// </summary>
        private readonly Func<Guid, IRiskElement?> _byId;

        /// <summary>
        /// The name lookup over the owning graph.
        /// </summary>
        private readonly Func<string, IRiskElement?> _byName;

        /// <summary>
        /// Initializes a resolver over a graph's element lookups.
        /// </summary>
        /// <param name="byId">The Id lookup; returns null when absent.</param>
        /// <param name="byName">The name lookup; returns null when absent.</param>
        /// <exception cref="ArgumentNullException">Thrown when either lookup is null.</exception>
        public RiskElementResolver(Func<Guid, IRiskElement?> byId, Func<string, IRiskElement?> byName)
        {
            _byId = byId ?? throw new ArgumentNullException(nameof(byId));
            _byName = byName ?? throw new ArgumentNullException(nameof(byName));
        }

        /// <summary>
        /// Resolves a pending reference: Id first (authoritative — throws when stale), then name
        /// (lenient — null when absent), null when neither was serialized.
        /// </summary>
        /// <param name="pendingId">The serialized element Id, when present.</param>
        /// <param name="pendingName">The serialized element name, when present.</param>
        /// <param name="linkDescription">A short description of the link, used in the stale-Id message.</param>
        /// <returns>The resolved element, or null when no reference was serialized or the name fallback missed.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a serialized Id is not present in the graph.</exception>
        public IRiskElement? Resolve(Guid? pendingId, string? pendingName, string linkDescription)
        {
            if (pendingId is { } id && id != Guid.Empty)
            {
                var byId = _byId(id);
                if (byId == null)
                {
                    throw new InvalidOperationException(
                        $"{linkDescription} references element Id '{id:D}', which is not in the graph. The serialized form is inconsistent.");
                }
                return byId;
            }

            if (!string.IsNullOrEmpty(pendingName)) return _byName(pendingName!);

            return null;
        }

        /// <summary>
        /// Parses a serialized Id attribute value into a pending Id.
        /// </summary>
        /// <param name="text">The attribute text; may be null.</param>
        /// <returns>The parsed Id, or null when the text is missing or unparseable.</returns>
        public static Guid? ParsePendingId(string? text)
        {
            return Guid.TryParse(text, out var id) ? id : null;
        }
    }
}
