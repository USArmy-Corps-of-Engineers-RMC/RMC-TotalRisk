namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The scalar risk-measure catalog a stored ensemble carries per risk-type stream — the
    /// output selector of the measure-level sensitivity analysis, mirroring the
    /// <c>SummaryRiskResults</c> property order.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: a tolerable-risk criterion persists
    /// its measure by name (and echoes it in the results), so members are never renamed or
    /// reordered. On the Fail stream <see cref="TotalProbability"/> is the annualized failure
    /// probability.
    /// </para>
    /// </remarks>
    public enum RiskMeasure
    {
        /// <summary>
        /// The stream's total probability (the annualized failure probability on Fail).
        /// </summary>
        TotalProbability = 0,

        /// <summary>
        /// The conditional mean given the stream's event occurs.
        /// </summary>
        ConditionalMean = 1,

        /// <summary>
        /// The unconditional mean (the expected annual consequence).
        /// </summary>
        Mean = 2,

        /// <summary>
        /// The standard deviation of the loss distribution.
        /// </summary>
        StandardDeviation = 3,

        /// <summary>
        /// The normalized skewness of the loss distribution.
        /// </summary>
        Skewness = 4,

        /// <summary>
        /// The normalized kurtosis of the loss distribution.
        /// </summary>
        Kurtosis = 5,

        /// <summary>
        /// The probability the consequence exceeds the declared consequence threshold.
        /// </summary>
        ConsequenceThresholdProbability = 6,

        /// <summary>
        /// The probability the hazard exceeds the component hazard threshold.
        /// </summary>
        HazardThresholdProbability = 7,

        /// <summary>
        /// The consequence quantile at the analysis exceedance level α.
        /// </summary>
        ValueAtRisk = 8,

        /// <summary>
        /// The conditional value-at-risk (expected shortfall) at the analysis exceedance level α.
        /// </summary>
        ConditionalValueAtRisk = 9,
    }
}
