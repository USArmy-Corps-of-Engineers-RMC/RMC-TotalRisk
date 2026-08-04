using System;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// Resolves serialized risk-function references back to live function instances when a risk
    /// graph is read from its by-reference form. Implemented by the layer that owns the stored
    /// functions.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The contract mirrors <c>RiskElementResolver</c>, which resolves element-to-element links
    /// inside a graph, and carries the same policy: <b>an id is authoritative and loud</b> (a
    /// serialized id that resolves to nothing throws, because a dangling persistent reference
    /// means the stored form is inconsistent), while <b>a name-only reference is lenient</b>
    /// (returns null, and graph validation reports the unresolved reference).
    /// </para>
    /// <para>
    /// The resolver must return the <b>live</b> instance rather than a copy. That is the whole
    /// point of the by-reference mode: a graph and the store it was loaded from must observe the
    /// same function object, so an edit in one place is seen in the other.
    /// </para>
    /// </remarks>
    public interface IRiskFunctionResolver
    {
        /// <summary>
        /// Resolves a serialized function reference.
        /// </summary>
        /// <param name="pendingId">The serialized function id, when present.</param>
        /// <param name="pendingName">The serialized function name, when present.</param>
        /// <param name="linkDescription">
        /// A short description of the reference (for example, "The hazard element 'Inflow'"), used
        /// to make the stale-id message actionable.
        /// </param>
        /// <returns>
        /// The live function, or null when no reference was serialized or a name-only fallback
        /// found nothing.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a serialized id cannot be resolved.
        /// </exception>
        IRiskFunction? Resolve(Guid? pendingId, string? pendingName, string linkDescription);

        /// <summary>
        /// Resolves a serialized metadata-link reference leniently: the id is tried first, a name
        /// falls back when the id is absent or finds nothing, and any miss returns null — this
        /// overload never throws.
        /// </summary>
        /// <param name="pendingId">The serialized function id, when present.</param>
        /// <param name="pendingName">The serialized function name, when present.</param>
        /// <returns>The live function, or null on any miss.</returns>
        /// <remarks>
        /// The counterpart of <see cref="Resolve"/> for references that are provenance metadata
        /// rather than compute content — for example, a bivariate response's stored
        /// secondary-hazard link, whose serialized weights remain the authoritative compute
        /// content when the link's target has been deleted or renamed. A dangling metadata
        /// reference must degrade to a validation advisory, never abort a load, so the loud
        /// stale-id policy deliberately does not apply here.
        /// </remarks>
        IRiskFunction? TryResolve(Guid? pendingId, string? pendingName);
    }
}
