namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The objective function the adaptive risk integrator refines against — the quantity whose
    /// accuracy decides where along the hazard probability domain the integrator concentrates its
    /// evaluations.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The risk integral's returned value is discarded: the engine uses the adaptive integrator as
    /// an importance sampler whose side effect — the evaluation points recorded as risk points — is
    /// the actual product (see <c>docs/technical-reference/risk-integration.md</c>). This enum
    /// therefore steers <i>where evaluations land</i>, never <i>what is reported</i>: all five
    /// <see cref="RiskType"/> loss exceedance curves and every risk measure are produced regardless
    /// of the selected member.
    /// </para>
    /// <para>
    /// This is an analysis option (a hashed field on <c>RiskAnalysisOptions</c>), not a runtime
    /// discriminator: it is serialized with the options and participates in the options canonical
    /// hash, because moving the refinement objective moves the recorded point set. It never feeds
    /// Monte Carlo seeds — those derive solely from the PRNG seed and component content hashes.
    /// </para>
    /// <para>
    /// Members are functions of the hazard non-exceedance probability <c>p</c>, with
    /// <c>P_F</c> the combined failure probability at hazard <c>h(p)</c>, <c>C_F</c> the failure
    /// consequence, and <c>C_NF</c> the non-failure consequence.
    /// <see cref="TailConditionalRisk"/> and <see cref="ThresholdExceedanceProbability"/> are
    /// discontinuous in <c>p</c>; the engine injects each discontinuity as a stratification-bin
    /// boundary so no quadrature panel spans the jump. The tail members exist because conditional
    /// value-at-risk (expected shortfall) is the coherent tail measure — the regulatory successor
    /// to plain value-at-risk — and for life-safety loss exceedance curves the tail is the
    /// decision-relevant region (references [17], [18] in <c>docs/references.md</c>).
    /// </para>
    /// </remarks>
    public enum RiskIntegrand
    {
        /// <summary>
        /// Refine the mean annualized total consequence, <c>P_F·E[C_F] + P_NF·C_NF</c> — the v1.0
        /// objective, reproducing its point placement exactly. The default.
        /// </summary>
        MeanTotalRisk,

        /// <summary>
        /// Refine the mean annualized incremental (excess) consequence,
        /// <c>P_F·E[(C_F − C_NF)⁺]</c> — concentrates evaluations where the reducible risk lives.
        /// </summary>
        MeanIncrementalRisk,

        /// <summary>
        /// Refine the annualized failure probability, <c>P_F(p)</c> — concentrates evaluations
        /// where the fragility is steep. Pairs with <see cref="RiskAnalysisMode.Reliability"/>.
        /// </summary>
        TotalProbabilityOfFailure,

        /// <summary>
        /// Refine the tail of the mean failure risk, <c>P_F·E[C_F]·1{p ≤ α}</c> with α the analysis
        /// exceedance level — concentrates evaluations in the α-tail that drives value-at-risk,
        /// conditional value-at-risk, and the F-N curve tail. Discontinuous at <c>p = α</c>.
        /// </summary>
        TailConditionalRisk,

        /// <summary>
        /// Refine the probability the consequence exceeds the analysis consequence threshold,
        /// <c>P(C &gt; threshold | p)</c> — concentrates evaluations around the assurance /
        /// tolerable-risk decision point. Discontinuous where the consequence crosses the threshold.
        /// </summary>
        ThresholdExceedanceProbability,

        /// <summary>
        /// Refine the conditional second moment, <c>E[C² | p]</c> — concentrates evaluations where
        /// the loss exceedance curve's variance is decided.
        /// </summary>
        SecondMoment,

        /// <summary>
        /// Refine a normalized sum of <see cref="MeanTotalRisk"/>, <see cref="SecondMoment"/>, and
        /// <see cref="TailConditionalRisk"/> so the mean, spread, and tail converge together — a
        /// sensible choice for headless callers that read every measure from one run.
        /// </summary>
        Balanced,
    }
}
