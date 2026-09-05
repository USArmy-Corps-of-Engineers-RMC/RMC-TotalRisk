namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// Identifies which axis of a bivariate response surface the owning tree's hazard drives when
    /// the surface backs a tree probability source. The other axis is supplied by the source's
    /// hazard-transform chain evaluated at the same tree hazard.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Serialized by name as a conditional attribute on the probability source: the attribute is
    /// present only when a bivariate axis is declared, so every source without one keeps a
    /// byte-identical serialized form and canonical hash. Member names and values are append-only
    /// serialized contract.
    /// </para>
    /// </remarks>
    public enum BivariateSourceAxis
    {
        /// <summary>The tree hazard drives the surface's primary axis; the transform chain supplies the secondary coordinate.</summary>
        Primary = 0,

        /// <summary>The tree hazard drives the surface's secondary axis; the transform chain supplies the primary coordinate.</summary>
        Secondary = 1,
    }
}
