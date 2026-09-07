namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The trajectory scope a cost-benefit constraint is evaluated at — every epoch, the first
    /// epoch only, or the horizon aggregate.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: constraint declarations persist the
    /// scope by name, so members are never renamed or reordered. Annualized guideline
    /// constraints default to every epoch — the tolerable-risk reading "in every exposure
    /// year" — while aggregate-basis metrics evaluate at the horizon.
    /// </para>
    /// </remarks>
    public enum ConstraintScope
    {
        /// <summary>
        /// The constraint must hold in every epoch of the trajectory.
        /// </summary>
        EveryEpoch = 0,

        /// <summary>
        /// The constraint is evaluated on the first epoch only (the present configuration).
        /// </summary>
        FirstEpoch = 1,

        /// <summary>
        /// The constraint is evaluated on the horizon aggregate the metric's basis selects.
        /// </summary>
        Horizon = 2,
    }
}
