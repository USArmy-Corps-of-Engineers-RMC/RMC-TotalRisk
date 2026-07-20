namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The options for statistical dependence between failure modes within a system component —
    /// and, at the analysis level, between the hazards of different system components.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>RiskAnalysis.DependencyType</c> with member names and declared order
    /// preserved — the names are serialized contract. Dependence is realized as a Gaussian copula:
    /// the perfectly positive and perfectly negative options build the component's multivariate
    /// normal with off-diagonal correlations of <c>1 − √εmach</c> and <c>−1/(D − 1) + √εmach</c>
    /// respectively (the latter is the most negative exchangeable equicorrelation that remains
    /// positive semi-definite for D failure modes), exactly as v1.0 did.
    /// </para>
    /// </remarks>
    public enum DependencyType
    {
        /// <summary>
        /// No dependence — failure indicators (or component hazards) are drawn independently.
        /// </summary>
        Independent,

        /// <summary>
        /// Perfectly positive (comonotonic) dependence — off-diagonal correlations of
        /// <c>1 − √εmach</c> in the underlying multivariate normal.
        /// </summary>
        PerfectlyPositive,

        /// <summary>
        /// Perfectly negative (countermonotonic) dependence — off-diagonal correlations of
        /// <c>−1/(D − 1) + √εmach</c> for D failure modes.
        /// </summary>
        PerfectlyNegative,

        /// <summary>
        /// User-specified correlation matrix; must be positive definite (validated via Cholesky
        /// decomposition).
        /// </summary>
        CorrelationMatrix,
    }
}
