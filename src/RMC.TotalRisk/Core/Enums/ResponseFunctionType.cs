using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The concrete kind of a response (fragility) input function, surfaced by
    /// <see cref="IResponseFunction.FunctionType"/>.
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
    public enum ResponseFunctionType
    {
        /// <summary>A hazard vs. conditional-failure-probability table (<c>TabularResponse</c>).</summary>
        Tabular,

        /// <summary>
        /// A capacity distribution evaluated as a fragility curve (<c>ParametricResponse</c>).
        /// </summary>
        Parametric,

        /// <summary>
        /// The non-failure sentinel: zero failure probability at every hazard level
        /// (<c>NonFailResponse</c>).
        /// </summary>
        NonFail,
    }
}
