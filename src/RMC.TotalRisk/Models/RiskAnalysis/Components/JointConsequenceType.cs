namespace RMC.TotalRisk.Models.RiskAnalysis.Components
{
    /// <summary>
    /// The options for combining the consequences of failure modes that occur together in the same
    /// realization.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>RiskAnalysis.JointConsequenceType</c> with member names and declared
    /// order preserved — the names are serialized contract. Applies under
    /// <see cref="FailureModeMethod.JointFailures"/> when more than one mode fails in a
    /// realization, and again at the system level when multiple components fail jointly. The v1.0
    /// default is <see cref="Maximum"/> at the component level and <see cref="Additive"/> at the
    /// system level.
    /// </para>
    /// </remarks>
    public enum JointConsequenceType
    {
        /// <summary>
        /// The joint consequence is the sum of the failed modes' consequences.
        /// </summary>
        Additive,

        /// <summary>
        /// The joint consequence is the mean of the failed modes' consequences.
        /// </summary>
        Average,

        /// <summary>
        /// The joint consequence is the largest of the failed modes' consequences. The v1.0
        /// component-level default.
        /// </summary>
        Maximum,

        /// <summary>
        /// The joint consequence is the smallest of the failed modes' consequences.
        /// </summary>
        Minimum,
    }
}
