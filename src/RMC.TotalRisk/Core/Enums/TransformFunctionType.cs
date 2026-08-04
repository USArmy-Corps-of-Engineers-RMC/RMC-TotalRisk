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

        /// <summary>
        /// A linear relation <c>Y = α + β·X</c> with optional additive Gaussian uncertainty
        /// (<c>LinearTransform</c>).
        /// </summary>
        Linear,

        /// <summary>
        /// A power relation <c>Y = α·(X − ξ)^β</c> with optional log-space uncertainty and an
        /// optional inverse form (<c>PowerTransform</c>).
        /// </summary>
        Power,

        /// <summary>
        /// A weighted average of child transform functions (<c>CompositeTransform</c>).
        /// </summary>
        Composite,

        /// <summary>
        /// A deterministic two-way table z = f(x, y) over a primary and a secondary hazard,
        /// evaluated by bilinear interpolation (<c>BivariateTransform</c>).
        /// </summary>
        Bivariate,
    }
}
