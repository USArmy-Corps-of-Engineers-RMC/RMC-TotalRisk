using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The concrete kind of a consequence input function, surfaced by
    /// <see cref="IConsequenceFunction.FunctionType"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// A runtime discriminator only, never serialized — the <c>ToXElement()</c> element name
    /// remains the serialization and canonical-hash discriminator. See
    /// <see cref="HazardFunctionType"/> for the full rationale.
    /// </para>
    /// </remarks>
    public enum ConsequenceFunctionType
    {
        /// <summary>A hazard vs. consequence table (<c>TabularConsequence</c>).</summary>
        Tabular,

        /// <summary>
        /// A closed-form power model of the hazard with optional coefficient uncertainty
        /// (<c>ParametricConsequence</c>).
        /// </summary>
        Parametric,

        /// <summary>
        /// A weighted combination of child consequence functions — additive, weighted-average, or
        /// mixture (<c>CompositeConsequence</c>).
        /// </summary>
        Composite,
    }
}
