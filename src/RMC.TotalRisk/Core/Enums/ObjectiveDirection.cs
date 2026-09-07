namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The optimization direction of a declared cost-benefit objective.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: objective declarations persist the
    /// direction by name, so members are never renamed or reordered.
    /// </para>
    /// </remarks>
    public enum ObjectiveDirection
    {
        /// <summary>
        /// Smaller values are preferred (costs, risks, dispersion).
        /// </summary>
        Minimize = 0,

        /// <summary>
        /// Larger values are preferred (benefits, reductions).
        /// </summary>
        Maximize = 1,
    }
}
