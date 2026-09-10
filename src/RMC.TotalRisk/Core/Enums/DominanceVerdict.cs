namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The verdict of one pairwise stochastic-dominance comparison between two alternatives'
    /// loss distributions.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Dominance is a screen, not a ranking: a verdict names the pair's strongest supported
    /// ordering — first-order dominance implies second-order, so the first-order verdicts are
    /// reported when both hold. The catalog is runtime-only today; member names become
    /// append-only serialized contract when the results body is serialized, so members are
    /// never renamed or reordered.
    /// </para>
    /// </remarks>
    public enum DominanceVerdict
    {
        /// <summary>
        /// Neither alternative dominates — the distributions cross in both orders.
        /// </summary>
        None = 0,

        /// <summary>
        /// The first alternative dominates the second at first order.
        /// </summary>
        FirstDominatesFirstOrder = 1,

        /// <summary>
        /// The first alternative dominates the second at second order only.
        /// </summary>
        FirstDominatesSecondOrder = 2,

        /// <summary>
        /// The second alternative dominates the first at first order.
        /// </summary>
        SecondDominatesFirstOrder = 3,

        /// <summary>
        /// The second alternative dominates the first at second order only.
        /// </summary>
        SecondDominatesSecondOrder = 4,

        /// <summary>
        /// The compared distributions are identical at every probe.
        /// </summary>
        Identical = 5,
    }
}
