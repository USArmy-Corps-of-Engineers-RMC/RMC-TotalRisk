using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// One curve stream: its scalar risk measures plus the stored curve arrays. All paired
    /// arrays are index-aligned; an absent (null) block means the engine did not record that
    /// curve for this stream, or the request excluded curve arrays.
    /// </summary>
    public class CurveDto
    {
        /// <summary>
        /// The stream's scalar risk measures.
        /// </summary>
        [JsonPropertyName("stats")]
        public CurveStatsDto Stats { get; set; } = new();

        /// <summary>
        /// The loss exceedance curve: consequences with their annual exceedance probabilities.
        /// </summary>
        [JsonPropertyName("lec")]
        public LossExceedanceCurveDto? Lec { get; set; }

        /// <summary>
        /// The hazard-frequency curve: hazard levels with their annual exceedance probabilities.
        /// </summary>
        [JsonPropertyName("hazardFrequency")]
        public HazardFrequencyCurveDto? HazardFrequency { get; set; }

        /// <summary>
        /// The conditional-mean consequence curve: hazard levels with the mean consequence given
        /// that hazard level.
        /// </summary>
        [JsonPropertyName("hazardVsConditionalMean")]
        public HazardConsequenceCurveDto? HazardVsConditionalMean { get; set; }

        /// <summary>
        /// The cumulative risk profiles over hazard (present when the riskProfiles measure is
        /// enabled).
        /// </summary>
        [JsonPropertyName("profiles")]
        public RiskProfilesDto? Profiles { get; set; }
    }
}
