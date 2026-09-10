namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The decision-strategy family of one published strategy ranking — the rule that ordered
    /// the alternatives and produced the recommendation.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// A member identifies the strategy family only; per-instance multiplicity (the criterion,
    /// the consequence type, the confidence level, the risk-aversion parameter) lives in the
    /// ranking's criterion and parameter echoes. <see cref="MeanPlusDispersion"/> serves both
    /// the aleatory and the epistemic layer — the ranking's layer label disambiguates — and the
    /// epistemic conditional value-at-risk is a band-table measure rather than a ranking member.
    /// The catalog is runtime-only today; member names become append-only serialized contract
    /// when the results body is serialized, so members are never renamed or reordered.
    /// </para>
    /// </remarks>
    public enum DecisionStrategy
    {
        /// <summary>
        /// The expected-value rule over a declared consequence stream.
        /// </summary>
        ExpectedValue = 0,

        /// <summary>
        /// The mean plus k standard deviations rule (aleatory or epistemic per the layer label).
        /// </summary>
        MeanPlusDispersion = 1,

        /// <summary>
        /// The minimum conditional-value-at-risk rule over the aleatory loss-exceedance tail.
        /// </summary>
        ConditionalValueAtRisk = 2,

        /// <summary>
        /// The partitioned multiobjective risk-method conditional means over declared
        /// exceedance regions.
        /// </summary>
        PartitionedConditionalMean = 3,

        /// <summary>
        /// The expected-utility rule reported as a certainty equivalent.
        /// </summary>
        CertaintyEquivalent = 4,

        /// <summary>
        /// The minimum total expected annual cost rule.
        /// </summary>
        TotalExpectedAnnualCost = 5,

        /// <summary>
        /// The maximum net present value rule.
        /// </summary>
        NetPresentValue = 6,

        /// <summary>
        /// The maximum benefit-cost ratio rule.
        /// </summary>
        BenefitCostRatio = 7,

        /// <summary>
        /// The minimum adjusted cost per statistical life saved rule.
        /// </summary>
        CostPerLifeSaved = 8,

        /// <summary>
        /// The minimum annualized failure probability rule.
        /// </summary>
        AnnualizedFailureProbability = 9,

        /// <summary>
        /// The constrained selection: the declared primary objective optimized over the
        /// alternatives satisfying every declared constraint.
        /// </summary>
        ConstrainedSelection = 10,

        /// <summary>
        /// The multi-criteria weighted-sum score echoed as a ranking.
        /// </summary>
        MultiCriteriaScore = 11,

        /// <summary>
        /// The Laplace rule: the equal-ignorance weighted mean over the stored epistemic
        /// ensemble.
        /// </summary>
        Laplace = 12,

        /// <summary>
        /// The Wald maximin rule: optimize the epistemic worst case.
        /// </summary>
        WaldMaximin = 13,

        /// <summary>
        /// The maximax rule: optimize the epistemic best case.
        /// </summary>
        Maximax = 14,

        /// <summary>
        /// The Hurwicz rule: the declared optimism-weighted blend of the epistemic best and
        /// worst cases.
        /// </summary>
        Hurwicz = 15,

        /// <summary>
        /// The quantile-regret rule over the ensemble band quantiles (distinct from the
        /// shared-state regret strategies).
        /// </summary>
        QuantileRegret = 16,

        /// <summary>
        /// The chance-constrained selection: the declared primary objective optimized over the
        /// alternatives meeting every declared constraint at a declared confidence.
        /// </summary>
        ChanceConstrainedSelection = 17,

        /// <summary>
        /// The Savage minimax-regret rule over shared logic-tree states.
        /// </summary>
        MinimaxRegret = 18,

        /// <summary>
        /// The expected-regret rule over shared logic-tree states under the exact branch
        /// weights.
        /// </summary>
        ExpectedRegret = 19,
    }
}
