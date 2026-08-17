namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// How a composite hazard or response function combines its weighted child <i>distributions</i>.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from the v1.0 <c>CompositeHazard.IsMixture</c> / <c>CompositeResponse.IsMixture</c>
    /// boolean, widened to an enum so an additional combination rule (a convolution "sum of random
    /// variables" mode, say) appends rather than breaks the serialized form. <see cref="Mixture"/>
    /// is the default, matching the v1.0 <c>IsMixture = true</c>.
    /// </para>
    /// <para>
    /// This is deliberately <b>not</b> <see cref="CompositeFunctionType"/>. That enum is pointwise
    /// <i>curve</i> algebra over consequence and transform functions — summing CDFs is meaningless,
    /// and the weighted average of two CDFs <i>is</i> their mixture, so two of its three members
    /// would be nonsense or a duplicate here. Hazard and response composites combine distributions,
    /// which is a different algebra with a different member set.
    /// </para>
    /// <para>
    /// <b>Choosing a mode.</b> If the children are alternative <i>descriptions</i> of one loading or
    /// one response — only one of them acts, with the stated probabilities — use
    /// <see cref="Mixture"/>. If they are simultaneous <i>mechanisms</i>, all acting at once with
    /// the governing one controlling the outcome, use <see cref="CompetingRisks"/>. See
    /// <c>docs/technical-reference/composite-functions.md</c>.
    /// </para>
    /// </remarks>
    public enum CompositeCombinationType
    {
        /// <summary>
        /// A mixture distribution: <c>F(x) = Σ ωᵢ·Fᵢ(x)</c>, weights in [0, 1] summing to one.
        /// The weights are aleatory — the fraction of the event population each child describes —
        /// so the mixture is a single distribution carried through every realization, not a
        /// per-realization branch selection. The v1.0 default.
        /// </summary>
        Mixture,

        /// <summary>
        /// A competing-risks combination: every child acts simultaneously and the governing one
        /// controls, under the owning type's rule (maximum for hazards, minimum — the weakest
        /// link — for responses) and the configured statistical dependence. Weights are inert in
        /// this mode, and are coerced out of the canonical hash accordingly.
        /// </summary>
        CompetingRisks,
    }
}
