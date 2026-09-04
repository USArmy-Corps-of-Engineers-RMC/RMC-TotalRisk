using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// The host's configured request limits (from the "Api" configuration section).
    /// </summary>
    public class MetadataLimitsDto
    {
        /// <summary>
        /// The maximum number of risk analyses allowed to compute concurrently.
        /// </summary>
        [JsonPropertyName("maxConcurrentRuns")]
        public int MaxConcurrentRuns { get; set; }

        /// <summary>
        /// The maximum number of system components accepted in one compute request.
        /// </summary>
        [JsonPropertyName("maxComponents")]
        public int MaxComponents { get; set; }

        /// <summary>
        /// The maximum number of ordinates accepted in any one tabular function payload.
        /// </summary>
        [JsonPropertyName("maxOrdinatesPerTable")]
        public int MaxOrdinatesPerTable { get; set; }
    }
}
