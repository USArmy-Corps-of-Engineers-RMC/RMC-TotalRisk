namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The accounting convention a life-cycle aggregate is read under — whether every exposure
    /// year counts, or each year is weighted by the probability the system survived every
    /// earlier year.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: cost-benefit declarations persist the
    /// convention by name, so members are never renamed or reordered. A trajectory always
    /// carries both conventions; this selector says which one a headline metric reads.
    /// </para>
    /// </remarks>
    public enum LifeCycleAccounting
    {
        /// <summary>
        /// Every exposure year counts (the annual-renewal reading — continuous with the
        /// exposure-period conversions and the equivalent-annual planning currency).
        /// </summary>
        NonAbsorbing = 0,

        /// <summary>
        /// Each year is weighted by the probability every earlier year survived (the
        /// first-failure-terminates reading).
        /// </summary>
        Absorbing = 1,
    }
}
