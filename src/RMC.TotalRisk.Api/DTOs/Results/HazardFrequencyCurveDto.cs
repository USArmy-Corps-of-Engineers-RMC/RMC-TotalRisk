using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A hazard-frequency curve as index-aligned parallel arrays.
    /// </summary>
    public class HazardFrequencyCurveDto
    {
        /// <summary>
        /// The hazard levels.
        /// </summary>
        [JsonPropertyName("hazards")]
        public List<double> Hazards { get; set; } = new();

        /// <summary>
        /// The annual exceedance probabilities, index-aligned with <see cref="Hazards"/>.
        /// </summary>
        [JsonPropertyName("probabilities")]
        public List<double> Probabilities { get; set; } = new();
    }
}
