namespace RMC.TotalRisk.Models.RiskAnalysis.Components
{
    /// <summary>
    /// Identifies which output of a hazard function a binding consumes: the primary hazard
    /// dimension, or the secondary dimension a future bivariate hazard exposes.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Architecture doc §6.5 (dimensional binding). The numeric values are explicit because they
    /// double as output-port indices on graph elements: a univariate hazard exposes only port 0
    /// (<see cref="Primary"/>); a bivariate hazard (Phase 11) additionally exposes port 1
    /// (<see cref="Secondary"/>). Landing the enum now keeps the serialized binding shape stable
    /// when bivariate hazard functions arrive — no attribute or value changes, only new function
    /// types.
    /// </para>
    /// </remarks>
    public enum HazardDimension
    {
        /// <summary>
        /// The primary hazard dimension — output port 0; the only dimension a univariate hazard
        /// exposes.
        /// </summary>
        Primary = 0,

        /// <summary>
        /// The secondary hazard dimension — output port 1; exposed only by bivariate hazards
        /// (Phase 11).
        /// </summary>
        Secondary = 1,
    }
}
