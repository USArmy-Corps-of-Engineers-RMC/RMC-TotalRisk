namespace RMC.TotalRisk.RiskFunctions.Responses.FaultTrees
{
    /// <summary>
    /// Identifies the parametric common-cause failure model of a fault-tree CCF group. Every
    /// model expands through the same per-multiplicity factor kernel; the members differ only in
    /// how their published parameters map onto the multiplicity factors.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Serialized by name on the group element. Member names and values are append-only
    /// serialized contract.
    /// </para>
    /// </remarks>
    public enum FaultTreeCcfModel
    {
        /// <summary>The beta-factor model: one parameter β splitting each member into an independent share (1 − β) and a common-cause failure of the whole group (β).</summary>
        BetaFactor = 0,

        /// <summary>The multiple Greek letter model: conditional parameters (β, γ, δ, …) distributing the common-cause share across multiplicities two through the group size.</summary>
        MultipleGreekLetter = 1,

        /// <summary>The alpha-factor model: one fraction α_k per multiplicity k, mapped through the non-staggered convention.</summary>
        AlphaFactor = 2,
    }
}
