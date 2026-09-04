using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// The cumulative risk profiles over hazard. The cumulative arrays are index-aligned with the
    /// stream's hazard-frequency hazards; the terminal ordinate of the cumulative failure
    /// probability profile is the annualized failure probability.
    /// </summary>
    public class RiskProfilesDto
    {
        /// <summary>
        /// Cumulative failure probability by hazard (index-aligned with the stream's
        /// hazardFrequency hazards).
        /// </summary>
        [JsonPropertyName("cumulativeFailureProbabilities")]
        public List<double>? CumulativeFailureProbabilities { get; set; }

        /// <summary>
        /// Cumulative expected consequence by hazard (index-aligned with the stream's
        /// hazardFrequency hazards).
        /// </summary>
        [JsonPropertyName("cumulativeExpectedConsequences")]
        public List<double>? CumulativeExpectedConsequences { get; set; }

        /// <summary>
        /// The hazard levels of the system response probability profile (the AEP axis pairs with
        /// <see cref="SystemResponseProbabilities"/>).
        /// </summary>
        [JsonPropertyName("systemResponseExceedanceProbabilities")]
        public List<double>? SystemResponseExceedanceProbabilities { get; set; }

        /// <summary>
        /// The system response probabilities, index-aligned with
        /// <see cref="SystemResponseExceedanceProbabilities"/>.
        /// </summary>
        [JsonPropertyName("systemResponseProbabilities")]
        public List<double>? SystemResponseProbabilities { get; set; }
    }
}
