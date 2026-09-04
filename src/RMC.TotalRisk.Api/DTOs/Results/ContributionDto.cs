using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// One scope's attributed contribution to its parent's risk: the Shapley share of the
    /// parent's failure probability, and the consequence-proportional shares of the parent's
    /// fail and excess means. Sums across sibling scopes reproduce the parent totals.
    /// </summary>
    public class ContributionDto
    {
        /// <summary>
        /// The attributed annualized failure probability (the Shapley share of the parent's
        /// failure probability mass).
        /// </summary>
        [JsonPropertyName("failureProbability")]
        public double FailureProbability { get; set; }

        /// <summary>
        /// The attributed expected annual failure consequence.
        /// </summary>
        [JsonPropertyName("failureMean")]
        public double FailureMean { get; set; }

        /// <summary>
        /// The attributed expected annual incremental (excess) consequence.
        /// </summary>
        [JsonPropertyName("excessMean")]
        public double ExcessMean { get; set; }
    }
}
