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
        /// The mode's ordered hazard-to-response transform chain, applied to the component hazard
        /// signal ahead of the response (e.g., stage to overtopping depth, stage to discharge).
        /// Null or empty means the response reads the raw component hazard directly.
        /// </summary>
        [JsonPropertyName("transforms")]
        public List<TransformFunctionDto>? Transforms { get; set; }

        /// <summary>
        /// The mode's system response probability (fragility) function. Keyed to the LAST
        /// transform's output axis when <see cref="Transforms"/> is present; otherwise to the
        /// component hazard.
        /// </summary>
        [Required]
        [JsonPropertyName("response")]
        public TabularResponseDto Response { get; set; } = new();

        /// <summary>
        /// The chain position whose hazard signal feeds the mode's consequences: 0 is the raw
        /// component hazard, k is the signal after the k-th transform. Valid range is
        /// [0, transform count]. Omitted defaults to 0 — consequences read the raw component
        /// hazard (e.g., stage) even when the response is keyed to a transformed axis. The
        /// position is shared by every consequence type of the mode.
        /// </summary>
        [JsonPropertyName("consequenceHazardPosition")]
        public int? ConsequenceHazardPosition { get; set; }

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
