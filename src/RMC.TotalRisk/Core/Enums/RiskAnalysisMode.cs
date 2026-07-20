namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// What a risk analysis run computes: full risk (consequences integrated over the hazard
    /// frequency curve) or reliability only (annual failure probability).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Reliability is deliberately a mode of the one risk analysis rather than a separate analysis
    /// type: both walk the same component graph over the same hazard, response, and dependency
    /// machinery, and duplicating that traversal would be two implementations of one contract to
    /// keep in agreement. Reliability simply stops before the consequence stage, so consequence
    /// functions are not required for a component to run in this mode.
    /// </para>
    /// </remarks>
    public enum RiskAnalysisMode
    {
        /// <summary>
        /// Full risk: probability-weighted consequences integrated over the hazard frequency curve,
        /// producing the <c>RiskType</c> streams.
        /// </summary>
        Risk,

        /// <summary>
        /// Reliability only: annual failure probability by failure mode and for the system, with
        /// the consequence stage skipped.
        /// </summary>
        Reliability,
    }
}
