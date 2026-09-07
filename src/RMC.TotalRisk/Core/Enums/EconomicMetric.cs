namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The economics-metric catalog of the cost-benefit study — the cost, net-benefit,
    /// cost-effectiveness, and reliability quantities computed per alternative against the
    /// designated baseline.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: cost-benefit declarations persist the
    /// metric by name, so members are never renamed or reordered. The cost-per-life-saved
    /// family follows ER 1110-2-1156 Appendix L on the Excess life-loss stream; ratio metrics
    /// are NaN when their denominator is not positive.
    /// </para>
    /// </remarks>
    public enum EconomicMetric
    {
        /// <summary>
        /// The present value of the alternative's total cost (capital plus recurring streams).
        /// </summary>
        PresentValueOfTotalCost = 0,

        /// <summary>
        /// The equivalent annual cost — the total cost present value divided by the horizon
        /// annuity factor.
        /// </summary>
        EquivalentAnnualCost = 1,

        /// <summary>
        /// The total expected annual cost — equivalent annual cost plus the selected-stream
        /// expected annual consequences.
        /// </summary>
        TotalExpectedAnnualCost = 2,

        /// <summary>
        /// The net present value — the monetized present-value benefit minus the total cost
        /// present value.
        /// </summary>
        NetPresentValue = 3,

        /// <summary>
        /// The net annual benefit — the net present value divided by the horizon annuity
        /// factor.
        /// </summary>
        NetAnnualBenefit = 4,

        /// <summary>
        /// The benefit-cost ratio — the monetized present-value benefit divided by the total
        /// cost present value.
        /// </summary>
        BenefitCostRatio = 5,

        /// <summary>
        /// The unadjusted cost per statistical life saved — annualized cost divided by the
        /// equivalent-annual Excess life-loss reduction.
        /// </summary>
        CostPerStatisticalLifeSavedUnadjusted = 6,

        /// <summary>
        /// The adjusted cost per statistical life saved — the annualized cost net of economic
        /// and operating-cost reductions (clamped at zero) divided by the equivalent-annual
        /// Excess life-loss reduction.
        /// </summary>
        CostPerStatisticalLifeSavedAdjusted = 7,

        /// <summary>
        /// The equity-weighted adjusted cost per statistical life saved — the adjusted ratio
        /// scaled by the individual-risk ratio raised to the equity exponent.
        /// </summary>
        EquityWeightedAdjustedCostPerStatisticalLifeSaved = 8,

        /// <summary>
        /// The cost per statistical failure prevented — annualized cost divided by the
        /// survival-equivalent annualized failure-probability reduction.
        /// </summary>
        CostPerStatisticalFailurePrevented = 9,

        /// <summary>
        /// The absorbing-basis adjusted cost per statistical life saved — the adjusted ratio
        /// composed from the survival-weighted aggregates.
        /// </summary>
        AbsorbingAdjustedCostPerStatisticalLifeSaved = 10,

        /// <summary>
        /// The disproportionality ratio — the adjusted cost per statistical life saved divided
        /// by the declared willingness to pay.
        /// </summary>
        DisproportionalityRatio = 11,

        /// <summary>
        /// The annualized failure probability level (the reliability decision axis).
        /// </summary>
        AnnualizedFailureProbability = 12,

        /// <summary>
        /// The annualized failure-probability reduction relative to the designated baseline.
        /// </summary>
        AnnualizedFailureProbabilityReduction = 13,

        /// <summary>
        /// The monetized present-value benefit — the sum over monetized types of the
        /// present-value reduction times its factor (the default frontier's benefit axis).
        /// </summary>
        MonetizedPresentValueBenefit = 14,
    }
}
