namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// How a composite function combines its weighted child functions.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from the v1.0 <c>CompositeConsequence.CompositeType</c> nested enum (default
    /// <see cref="Mixture"/>). The Average and Mixture modes share the same mean — the weighted
    /// average of the child means — but differ in variance: averaging pools the children every
    /// realization (variance Σw²σ²), while the mixture samples one child per realization
    /// (variance Σw(σ² + μ²) − (Σwμ)², always at least as large).
    /// </para>
    /// </remarks>
    public enum CompositeFunctionType
    {
        /// <summary>
        /// The children are summed each realization, ignoring weights — aggregating consequences
        /// estimated separately by sector (properties, industry, agriculture) into a total.
        /// </summary>
        Additive,

        /// <summary>
        /// The children are combined as a weighted average each realization, with weights summing
        /// to one — the historic day/night exposure practice.
        /// </summary>
        Average,

        /// <summary>
        /// One child is sampled per realization with probability equal to its weight — the full
        /// mixture-distribution treatment of scenario uncertainty (the preferred day/night
        /// exposure model), and the v1.0 default.
        /// </summary>
        Mixture,
    }
}
