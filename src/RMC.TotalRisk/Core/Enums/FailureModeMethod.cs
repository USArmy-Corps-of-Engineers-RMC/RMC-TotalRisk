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
    /// preserved — the names are serialized contract. The combination math itself is
    /// the risk engine's concern (<c>SampledComponent</c>, as in v1.0), and it is analytic — no
    /// indicators are ever drawn: joint failures decompose into exclusive pathway probabilities
    /// per the component's <see cref="DependencyType"/> (independent products, the comonotone
    /// bound, or the product-of-conditional-marginals approximation of the Gaussian copula over
    /// pairwise bivariate normal probabilities), competing failures use the
    /// cumulative-incidence treatment, and the common-cause and mutually-exclusive methods
    /// weight at most one mode per hazard level from adjusted or raw marginal probabilities.
    /// </para>
    /// </remarks>
    public enum FailureModeMethod
    {
        /// <summary>
        /// Multiple failure modes may occur together in a realization. The exclusive
        /// failure-pathway probabilities are computed analytically per the component's
        /// failure-mode dependency (a Gaussian copula over the response probabilities, evaluated
        /// through closed-form products or the product-of-conditional-marginals recursion — no
        /// indicators are drawn), and the consequences of co-occurring failures combine per
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
