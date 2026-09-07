using System;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// One alternative's economics row: the priced cost block, the monetized and economic
    /// benefit aggregates in both accounting conventions, the net-benefit metrics, the
    /// annualized failure probabilities, and the equivalent-annual lives saved.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// A presentation-contract row (the designated baseline is row zero of the study's
    /// table). Unprefixed benefit and net-benefit members carry the non-absorbing
    /// (every-year-exposed) convention; the Absorbing twins carry the
    /// first-failure-terminates convention — the study's echoed accounting selection says
    /// which one is the headline. Costs are commitments, so the cost block is identical
    /// under both conventions and every benefit-cost ratio divides by the same cost present
    /// value (documented alongside the ratio discipline: numerator and denominator always
    /// share one convention and one annualization basis). Ratios with a non-positive
    /// denominator are NaN — cost per unit of nothing saved is undefined, never clamped.
    /// </para>
    /// </remarks>
    public sealed class AlternativeEconomics
    {
        /// <summary>
        /// Initializes an economics row.
        /// </summary>
        /// <param name="name">The alternative's display name.</param>
        /// <param name="description">The alternative's display description.</param>
        /// <param name="isBaseline">True when this row is the designated baseline.</param>
        /// <param name="capitalPresentValue">The present value of the dated capital entries.</param>
        /// <param name="operationsAndMaintenancePresentValue">The present value of the operations-and-maintenance segments.</param>
        /// <param name="operatingChangePresentValue">The present value of the operating-change segments.</param>
        /// <param name="totalCostPresentValue">The present value of the whole cost stream.</param>
        /// <param name="equivalentAnnualCost">The equivalent annual cost over the study horizon.</param>
        /// <param name="cumulativeCost">The undiscounted cumulative cost over the study horizon.</param>
        /// <param name="monetizedPresentValueBenefit">The monetized present-value benefit (non-absorbing).</param>
        /// <param name="economicPresentValueBenefit">The economic present-value benefit, excluding the life-safety type (non-absorbing).</param>
        /// <param name="netPresentValue">The net present value (non-absorbing).</param>
        /// <param name="netAnnualBenefit">The net annual benefit (non-absorbing).</param>
        /// <param name="benefitCostRatio">The benefit-cost ratio (non-absorbing).</param>
        /// <param name="absorbingMonetizedPresentValueBenefit">The monetized present-value benefit (absorbing).</param>
        /// <param name="absorbingEconomicPresentValueBenefit">The economic present-value benefit (absorbing).</param>
        /// <param name="absorbingNetPresentValue">The net present value (absorbing).</param>
        /// <param name="absorbingNetAnnualBenefit">The net annual benefit (absorbing).</param>
        /// <param name="absorbingBenefitCostRatio">The benefit-cost ratio (absorbing).</param>
        /// <param name="annualizedFailureProbability">The survival-equivalent annualized failure probability over the horizon.</param>
        /// <param name="annualizedFailureProbabilityReduction">The signed annualized failure-probability reduction vs the baseline.</param>
        /// <param name="yearZeroFailureProbability">The first epoch's annualized failure probability.</param>
        /// <param name="yearZeroFailureProbabilityReduction">The signed first-epoch failure-probability reduction vs the baseline.</param>
        /// <param name="livesSavedEquivalentAnnual">The equivalent-annual Excess life-loss reduction vs the baseline; NaN when no life-safety type is declared.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name or description is null.</exception>
        public AlternativeEconomics(string name, string description, bool isBaseline,
            double capitalPresentValue, double operationsAndMaintenancePresentValue,
            double operatingChangePresentValue, double totalCostPresentValue,
            double equivalentAnnualCost, double cumulativeCost,
            double monetizedPresentValueBenefit, double economicPresentValueBenefit,
            double netPresentValue, double netAnnualBenefit, double benefitCostRatio,
            double absorbingMonetizedPresentValueBenefit, double absorbingEconomicPresentValueBenefit,
            double absorbingNetPresentValue, double absorbingNetAnnualBenefit,
            double absorbingBenefitCostRatio,
            double annualizedFailureProbability, double annualizedFailureProbabilityReduction,
            double yearZeroFailureProbability, double yearZeroFailureProbabilityReduction,
            double livesSavedEquivalentAnnual)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            IsBaseline = isBaseline;
            CapitalPresentValue = capitalPresentValue;
            OperationsAndMaintenancePresentValue = operationsAndMaintenancePresentValue;
            OperatingChangePresentValue = operatingChangePresentValue;
            TotalCostPresentValue = totalCostPresentValue;
            EquivalentAnnualCost = equivalentAnnualCost;
            CumulativeCost = cumulativeCost;
            MonetizedPresentValueBenefit = monetizedPresentValueBenefit;
            EconomicPresentValueBenefit = economicPresentValueBenefit;
            NetPresentValue = netPresentValue;
            NetAnnualBenefit = netAnnualBenefit;
            BenefitCostRatio = benefitCostRatio;
            AbsorbingMonetizedPresentValueBenefit = absorbingMonetizedPresentValueBenefit;
            AbsorbingEconomicPresentValueBenefit = absorbingEconomicPresentValueBenefit;
            AbsorbingNetPresentValue = absorbingNetPresentValue;
            AbsorbingNetAnnualBenefit = absorbingNetAnnualBenefit;
            AbsorbingBenefitCostRatio = absorbingBenefitCostRatio;
            AnnualizedFailureProbability = annualizedFailureProbability;
            AnnualizedFailureProbabilityReduction = annualizedFailureProbabilityReduction;
            YearZeroFailureProbability = yearZeroFailureProbability;
            YearZeroFailureProbabilityReduction = yearZeroFailureProbabilityReduction;
            LivesSavedEquivalentAnnual = livesSavedEquivalentAnnual;
        }

        /// <summary>The alternative's display name.</summary>
        public string Name { get; }

        /// <summary>The alternative's display description.</summary>
        public string Description { get; }

        /// <summary>True when this row is the designated baseline (row zero).</summary>
        public bool IsBaseline { get; }

        /// <summary>The present value of the dated capital entries.</summary>
        public double CapitalPresentValue { get; }

        /// <summary>The present value of the operations-and-maintenance segments.</summary>
        public double OperationsAndMaintenancePresentValue { get; }

        /// <summary>The present value of the operating-change segments.</summary>
        public double OperatingChangePresentValue { get; }

        /// <summary>The present value of the whole cost stream (all three kinds).</summary>
        public double TotalCostPresentValue { get; }

        /// <summary>The equivalent annual cost — the total cost present value over the horizon annuity.</summary>
        public double EquivalentAnnualCost { get; }

        /// <summary>The undiscounted cumulative cost over the study horizon.</summary>
        public double CumulativeCost { get; }

        /// <summary>
        /// The monetized present-value benefit on the study's benefit stream (non-absorbing);
        /// NaN when no monetized types exist.
        /// </summary>
        public double MonetizedPresentValueBenefit { get; }

        /// <summary>
        /// The economic present-value benefit — the monetized aggregate excluding the declared
        /// life-safety type (non-absorbing); NaN when no monetized types exist.
        /// </summary>
        public double EconomicPresentValueBenefit { get; }

        /// <summary>The net present value — monetized benefit minus total cost (non-absorbing).</summary>
        public double NetPresentValue { get; }

        /// <summary>The net annual benefit — the net present value over the horizon annuity (non-absorbing).</summary>
        public double NetAnnualBenefit { get; }

        /// <summary>
        /// The benefit-cost ratio (non-absorbing); NaN when the cost present value is not
        /// positive.
        /// </summary>
        public double BenefitCostRatio { get; }

        /// <summary>The monetized present-value benefit (absorbing).</summary>
        public double AbsorbingMonetizedPresentValueBenefit { get; }

        /// <summary>The economic present-value benefit (absorbing).</summary>
        public double AbsorbingEconomicPresentValueBenefit { get; }

        /// <summary>The net present value (absorbing).</summary>
        public double AbsorbingNetPresentValue { get; }

        /// <summary>The net annual benefit (absorbing).</summary>
        public double AbsorbingNetAnnualBenefit { get; }

        /// <summary>The benefit-cost ratio (absorbing); NaN when the cost present value is not positive.</summary>
        public double AbsorbingBenefitCostRatio { get; }

        /// <summary>
        /// The survival-equivalent annualized failure probability over the horizon —
        /// 1 − (1 − P_T)^(1/T).
        /// </summary>
        public double AnnualizedFailureProbability { get; }

        /// <summary>The signed annualized failure-probability reduction vs the baseline (positive is safer).</summary>
        public double AnnualizedFailureProbabilityReduction { get; }

        /// <summary>The first epoch's annualized failure probability (the present configuration).</summary>
        public double YearZeroFailureProbability { get; }

        /// <summary>The signed first-epoch failure-probability reduction vs the baseline.</summary>
        public double YearZeroFailureProbabilityReduction { get; }

        /// <summary>
        /// The equivalent-annual Excess life-loss reduction vs the baseline (the life-saved
        /// axis of the cost-effectiveness family); NaN when no life-safety type is declared.
        /// </summary>
        public double LivesSavedEquivalentAnnual { get; }
    }
}
