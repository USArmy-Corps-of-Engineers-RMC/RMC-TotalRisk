using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The concrete kind of a transform input function, surfaced by
    /// <see cref="ITransformFunction.FunctionType"/>.
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
    public enum TransformFunctionType
    {
        /// <summary>A tabular hazard-to-transformed-hazard rating table (<c>TabularTransform</c>).</summary>
        Tabular,
    }
}
