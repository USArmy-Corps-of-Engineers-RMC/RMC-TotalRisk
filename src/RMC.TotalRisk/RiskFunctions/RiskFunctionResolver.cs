using System;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions
{
    /// <summary>
    /// The stock <see cref="IRiskFunctionResolver"/>: resolves serialized function references over
    /// a pair of lookups supplied by whichever layer owns the stored functions.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// A direct counterpart of <c>RiskElementResolver</c> — same shape, same policy, one level up:
    /// that resolver re-links elements to each other inside a graph, this one re-links elements to
    /// the functions they wrap. An id is authoritative and throws when stale; a name-only
    /// reference is lenient and returns null so validation can report it.
    /// </para>
    /// <para>
    /// The lookups must return live instances, not copies — see <see cref="IRiskFunctionResolver"/>.
    /// </para>
    /// </remarks>
    public readonly struct RiskFunctionResolver : IRiskFunctionResolver
    {
        /// <summary>
        /// The id lookup over the owning store.
        /// </summary>
        private readonly Func<Guid, IRiskFunction?> _byId;

        /// <summary>
        /// The name lookup over the owning store.
        /// </summary>
        private readonly Func<string, IRiskFunction?> _byName;

        /// <summary>
        /// Initializes a resolver over a store's function lookups.
        /// </summary>
        /// <param name="byId">The id lookup; returns null when absent.</param>
        /// <param name="byName">The name lookup; returns null when absent.</param>
        /// <exception cref="ArgumentNullException">Thrown when either lookup is null.</exception>
        public RiskFunctionResolver(Func<Guid, IRiskFunction?> byId, Func<string, IRiskFunction?> byName)
        {
            _byId = byId ?? throw new ArgumentNullException(nameof(byId));
            _byName = byName ?? throw new ArgumentNullException(nameof(byName));
        }

        /// <inheritdoc/>
        public IRiskFunction? Resolve(Guid? pendingId, string? pendingName, string linkDescription)
        {
            if (pendingId is { } id && id != Guid.Empty)
            {
                var byId = _byId(id);
                if (byId == null)
                {
                    throw new InvalidOperationException(
                        $"{linkDescription} references risk function Id '{id:D}', which is not available. The serialized form is inconsistent.");
                }
                return byId;
            }

            if (!string.IsNullOrEmpty(pendingName)) return _byName(pendingName!);

            return null;
        }
    }
}
