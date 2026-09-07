namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The tolerable-risk proximity a disproportionality evaluation declares — which ALARP
    /// justification band table applies to the study's risks.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: cost-benefit declarations persist the
    /// proximity by name, so members are never renamed or reordered. The default band
    /// thresholds follow ER 1110-2-1156: {1, 4, 20} for risks just below the tolerable limit
    /// and {0.3, 1, 6} for risks just above broadly acceptable, with a per-study override
    /// seat.
    /// </para>
    /// </remarks>
    public enum AlarpProximity
    {
        /// <summary>
        /// The risks sit just below the tolerable-risk limit (the stricter band table).
        /// </summary>
        JustBelowTolerableLimit = 0,

        /// <summary>
        /// The risks sit just above the broadly acceptable level (the more lenient band table).
        /// </summary>
        JustAboveBroadlyAcceptable = 1,
    }
}
