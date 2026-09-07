namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The form a cost-benefit metric takes — an alternative's own level, or its signed
    /// reduction relative to the study's designated baseline.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: cost-benefit declarations persist the
    /// form by name, so members are never renamed or reordered. Reductions are signed
    /// baseline − alternative (positive is good) and never clamped.
    /// </para>
    /// </remarks>
    public enum MetricForm
    {
        /// <summary>
        /// The alternative's own value.
        /// </summary>
        Level = 0,

        /// <summary>
        /// The signed reduction relative to the designated baseline (baseline − alternative).
        /// </summary>
        ReductionVsBaseline = 1,
    }
}
