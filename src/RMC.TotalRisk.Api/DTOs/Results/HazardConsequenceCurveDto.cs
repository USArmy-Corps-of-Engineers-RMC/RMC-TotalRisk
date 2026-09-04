using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A hazard-versus-consequence curve as index-aligned parallel arrays.
    /// </summary>
    public class HazardConsequenceCurveDto
    {
        /// <summary>
        /// The hazard levels.
        /// </summary>
        [JsonPropertyName("hazards")]
        public List<double> Hazards { get; set; } = new();

        /// <summary>
        /// The consequence ordinates, index-aligned with <see cref="Hazards"/>.
        /// </summary>
        [JsonPropertyName("consequences")]
        public List<double> Consequences { get; set; } = new();
    }
}
