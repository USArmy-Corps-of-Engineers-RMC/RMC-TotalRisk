namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The options for combining multiple potential failure modes within a system component.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>RiskAnalysis.FailureModeMethod</c> with member names and declared order
    /// preserved — the names are serialized contract. The Monte Carlo combination math itself is
    /// the risk engine's concern (v1.0 <c>SampledComponent</c>; ported with the engine phase):
    /// joint failures draw correlated latent normals per the component's
    /// <see cref="DependencyType"/>, competing failures use the cumulative-incidence treatment,
    /// and the common-cause and mutually-exclusive methods select at most one mode per
    /// realization from adjusted or raw marginal probabilities.
    /// </para>
    /// </remarks>
    public enum FailureModeMethod
    {
        /// <summary>
        /// Multiple failure modes may occur together in a realization. Failure indicators are
        /// drawn jointly per the component's failure-mode dependency (a Gaussian copula over the
        /// response probabilities), and the consequences of co-occurring failures combine per
        /// <see cref="JointConsequenceType"/>. The v1.0 default.
        /// </summary>
        JointFailures,

        /// <summary>
        /// Failure modes compete to occur first: the mode that occurs governs the realization,
        /// per the competing-risks (cumulative incidence function) treatment.
        /// </summary>
        CompetingFailures,

        /// <summary>
        /// Failure modes share a common initiating cause: marginal failure probabilities are
        /// scaled by the common-cause adjustment and at most one mode is selected per
        /// realization.
        /// </summary>
        CommonCauseFailures,

        /// <summary>
        /// Failure modes are mutually exclusive by definition: at most one mode is selected per
        /// realization from the mutually-exclusive-adjusted marginal probabilities.
        /// </summary>
        MutuallyExclusive,
    }
}
