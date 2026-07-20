namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The risk streams a component's consequences decompose into — which probability-weighted
    /// combination of the failure and non-failure branches a reported risk measure represents.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>RiskAnalysis.RiskType</c> with member names and declared order
    /// preserved — the names are serialized contract. At a hazard level with per-mode failure
    /// probabilities and consequences, the v1.0 integrand (<c>SampledComponent</c>) computes:
    /// Fail = Σ pₖ·cFₖ; NonFail = (1 − Σ pₖ)·cNF; Total = Fail + NonFail;
    /// Excess = Σ pₖ·(cFₖ − cNF); Background = cNF unconditionally. The engine phase ports that
    /// math; this enumeration is the shared vocabulary for results and options.
    /// </para>
    /// </remarks>
    public enum RiskType
    {
        /// <summary>
        /// Incremental (excess) risk: the probability-weighted failure consequences minus the
        /// non-failure consequences at the same hazard level — the dam- and levee-safety
        /// incremental measure.
        /// </summary>
        Excess,

        /// <summary>
        /// Background risk: the non-failure (non-breach) consequences unconditionally — flood
        /// risk as if no failure could occur.
        /// </summary>
        Background,

        /// <summary>
        /// Total risk: the probability-weighted failure consequences plus the non-failure branch
        /// (probability of no failure times the non-failure consequences).
        /// </summary>
        Total,

        /// <summary>
        /// The probability-weighted failure-path consequences alone.
        /// </summary>
        Fail,

        /// <summary>
        /// The non-failure branch alone: the probability of no failure times the non-failure
        /// consequences.
        /// </summary>
        NonFail,
    }
}
