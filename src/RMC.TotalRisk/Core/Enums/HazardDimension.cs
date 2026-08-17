namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// Identifies which output of a hazard function a binding consumes: the primary hazard
    /// dimension, or the secondary dimension a bivariate hazard exposes.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Dimensional binding (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §6.5). The numeric
    /// values are explicit because they double as output-port indices on graph elements: a
    /// univariate hazard exposes only port 0 (<see cref="Primary"/>); a bivariate hazard
    /// additionally exposes port 1 (<see cref="Secondary"/>). The values are serialized binding
    /// contract — append-only, never renumbered.
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
        /// The secondary hazard dimension — output port 1; exposed only by bivariate
        /// hazards.
        /// </summary>
        Secondary = 1,
    }
}
