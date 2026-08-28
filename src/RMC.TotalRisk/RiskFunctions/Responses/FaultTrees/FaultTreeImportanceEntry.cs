using System;

namespace RMC.TotalRisk.RiskFunctions.Responses.FaultTrees
{
    /// <summary>
    /// The exact importance measures of one unified basic-event variable at the analyzed hazard
    /// level: Birnbaum, criticality, Fussell-Vesely, risk achievement worth, and risk reduction
    /// worth, from two exact decision-diagram evaluations with the variable forced certain and
    /// impossible.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// With P the top-event probability, q this variable's probability, and P(1)/P(0) the exact
    /// conditional evaluations at q = 1 and q = 0: Birnbaum B = P(1) − P(0) (the partial
    /// derivative ∂P/∂q), criticality B·q/P, Fussell-Vesely 1 − P(0)/P, risk achievement worth
    /// P(1)/P, and risk reduction worth P/P(0). Conventions: a variable reduced out of the
    /// frozen diagram has B exactly zero (its conditional evaluations coincide); when P = 0 the
    /// ratio measures are NaN (no baseline risk to attribute); when P(0) = 0 with P &gt; 0 the
    /// risk reduction worth is positive infinity (removing the variable eliminates failure).
    /// A plain query result — never serialized.
    /// </para>
    /// </remarks>
    public sealed class FaultTreeImportanceEntry
    {
        /// <summary>
        /// Initializes one entry.
        /// </summary>
        /// <param name="nodeId">The basic-event node id (the unified variable's first occurrence).</param>
        /// <param name="name">The basic-event display name.</param>
        /// <param name="canonicalPath">The first occurrence's canonical tree path.</param>
        /// <param name="baselineProbability">The variable's probability at the evaluated percentile.</param>
        /// <param name="birnbaum">The Birnbaum measure P(1) − P(0).</param>
        /// <param name="criticality">The criticality measure B·q/P.</param>
        /// <param name="fussellVesely">The Fussell-Vesely measure 1 − P(0)/P.</param>
        /// <param name="riskAchievementWorth">The risk achievement worth P(1)/P.</param>
        /// <param name="riskReductionWorth">The risk reduction worth P/P(0).</param>
        internal FaultTreeImportanceEntry(Guid nodeId, string name, string canonicalPath,
            double baselineProbability, double birnbaum, double criticality, double fussellVesely,
            double riskAchievementWorth, double riskReductionWorth)
        {
            NodeId = nodeId;
            Name = name;
            CanonicalPath = canonicalPath;
            BaselineProbability = baselineProbability;
            Birnbaum = birnbaum;
            Criticality = criticality;
            FussellVesely = fussellVesely;
            RiskAchievementWorth = riskAchievementWorth;
            RiskReductionWorth = riskReductionWorth;
        }

        /// <summary>
        /// The basic-event node id (the unified variable's first occurrence).
        /// </summary>
        public Guid NodeId { get; }

        /// <summary>
        /// The basic-event display name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The first occurrence's canonical tree path.
        /// </summary>
        public string CanonicalPath { get; }

        /// <summary>
        /// The variable's probability at the evaluated percentile (the q entering the measures).
        /// </summary>
        public double BaselineProbability { get; }

        /// <summary>
        /// The Birnbaum measure P(1) − P(0) — the exact partial derivative of the top-event
        /// probability with respect to this variable's probability.
        /// </summary>
        public double Birnbaum { get; }

        /// <summary>
        /// The criticality measure B·q/P — the probability-weighted Birnbaum share.
        /// </summary>
        public double Criticality { get; }

        /// <summary>
        /// The Fussell-Vesely measure 1 − P(0)/P — the fraction of the top-event probability
        /// removed by making this variable impossible.
        /// </summary>
        public double FussellVesely { get; }

        /// <summary>
        /// The risk achievement worth P(1)/P — the top-event multiplier if this variable were
        /// certain.
        /// </summary>
        public double RiskAchievementWorth { get; }

        /// <summary>
        /// The risk reduction worth P/P(0) — the top-event divisor if this variable were
        /// impossible.
        /// </summary>
        public double RiskReductionWorth { get; }
    }
}
