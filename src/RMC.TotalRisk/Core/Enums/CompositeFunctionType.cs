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
        /// exposure model), and the v1.0 default. The weights are aleatory: every realization
        /// experiences the whole population of children in proportion to them.
        /// </summary>
        Mixture,

        /// <summary>
        /// An epistemic mixture — the logic tree: exactly one child is the true function for the
        /// whole period of analysis, with the weights stating the analyst's credence in each, and
        /// one child selected per realization by a knowledge draw. Distinct from
        /// <see cref="Mixture"/>, whose weights are aleatory fractions of each realization's
        /// event population. Selecting this mode is compute-relevant hashed content.
        /// </summary>
        EpistemicMixture,
    }
}
