namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The time basis a cost-benefit risk-measure selector reads — an annualized per-epoch
    /// value or one of the horizon aggregate forms.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: cost-benefit declarations persist the
    /// basis by name, so members are never renamed or reordered. Every ratio metric draws its
    /// numerator and denominator from one basis and one accounting convention.
    /// </para>
    /// </remarks>
    public enum MetricBasis
    {
        /// <summary>
        /// The annualized value read per epoch (evaluated at the scope the constraint declares).
        /// </summary>
        AnnualizedPerEpoch = 0,

        /// <summary>
        /// The discounted (present-value) horizon aggregate.
        /// </summary>
        HorizonPresentValue = 1,

        /// <summary>
        /// The equivalent-annual horizon aggregate — the level annual amount whose present
        /// value over the horizon equals the discounted aggregate.
        /// </summary>
        HorizonEquivalentAnnual = 2,

        /// <summary>
        /// The undiscounted cumulative horizon aggregate.
        /// </summary>
        HorizonCumulative = 3,
    }
}
