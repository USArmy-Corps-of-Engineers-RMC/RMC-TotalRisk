using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A loss exceedance curve as index-aligned parallel arrays.
    /// </summary>
    public class LossExceedanceCurveDto
    {
        /// <summary>
        /// The consequence ordinates.
        /// </summary>
        [JsonPropertyName("consequences")]
        public List<double> Consequences { get; set; } = new();

        /// <summary>
        /// The annual exceedance probabilities, index-aligned with <see cref="Consequences"/>.
        /// </summary>
        [JsonPropertyName("probabilities")]
        public List<double> Probabilities { get; set; } = new();
    }
}
