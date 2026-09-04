using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// One potential failure mode: a response (fragility) function of the component hazard plus
    /// the consequences of failing by this mode.
    /// </summary>
    public class FailureModeDto
    {
        /// <summary>
        /// The failure mode's display name (e.g., "Overtopping Erosion"). Labels the mode's
        /// results row when the primary consequence function carries no name of its own.
        /// </summary>
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The mode's system response probability (fragility) function of the component hazard.
        /// </summary>
        [Required]
        [JsonPropertyName("response")]
        public TabularResponseDto Response { get; set; } = new();

        /// <summary>
        /// The mode's fail consequence functions, one per declared consequence type in declared
        /// order (index 0 is the primary type). A single-type analysis supplies exactly one entry.
        /// </summary>
        [Required]
        [MinLength(1)]
        [JsonPropertyName("consequences")]
        public List<ConsequenceFunctionDto> Consequences { get; set; } = new();
    }
}
