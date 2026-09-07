namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The study policy for the do-no-harm screen — whether an alternative whose total risk
    /// increases from the baseline is excluded from recommendations, merely flagged, or the
    /// screen is not evaluated.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: cost-benefit declarations persist the
    /// policy by name, so members are never renamed or reordered. The screen is a display and
    /// recommendation gate, never a mutation of any metric — flagged alternatives stay in
    /// every table, visibly marked.
    /// </para>
    /// </remarks>
    public enum DoNoHarmPolicy
    {
        /// <summary>
        /// Flag failing alternatives and exclude them from every strategy recommendation
        /// (the default).
        /// </summary>
        Enforce = 0,

        /// <summary>
        /// Flag failing alternatives with a warning but leave them eligible for
        /// recommendations.
        /// </summary>
        WarnOnly = 1,

        /// <summary>
        /// Do not evaluate the screen.
        /// </summary>
        Off = 2,
    }
}
