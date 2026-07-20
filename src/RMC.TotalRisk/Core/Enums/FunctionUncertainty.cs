using RMC.TotalRisk.RiskFunctions.Hazards;

namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The knowledge-uncertainty mode of a <see cref="TabularHazard"/>: which axis of the
    /// tabular frequency curve carries per-ordinate uncertainty distributions.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Legacy v1.0 enum (nested in <c>TabularHazard</c> there; standalone here per repo convention,
    /// name and members preserved).
    /// </para>
    /// </remarks>
    public enum FunctionUncertainty
    {
        /// <summary>
        /// No uncertainty: a deterministic exceedance-probability vs. hazard table.
        /// </summary>
        None,

        /// <summary>
        /// Uncertainty around the hazard axis: each exceedance-probability ordinate carries a
        /// distribution of hazard values.
        /// </summary>
        Hazard,

        /// <summary>
        /// Uncertainty around the probability axis: each hazard ordinate carries a distribution of
        /// exceedance probabilities.
        /// </summary>
        Probability,
    }
}
