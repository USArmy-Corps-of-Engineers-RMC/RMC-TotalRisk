using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A complete, self-contained risk-analysis definition for the stateless round-trip compute
    /// endpoint: analysis metadata, the system components (hazard, failure modes, consequences),
    /// the engine settings, and the result-shaping options.
    /// </summary>
    public class ComputeRiskAnalysisRequest
    {
        /// <summary>
        /// The analysis display name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// The analysis description.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// The primary consequence type label (e.g., "Life Loss").
        /// </summary>
        [Required]
        [JsonPropertyName("specifiedConsequence")]
        public string SpecifiedConsequence { get; set; } = string.Empty;

        /// <summary>
        /// The primary consequence unit label (e.g., "lives").
        /// </summary>
        [Required]
        [JsonPropertyName("consequenceUnit")]
        public string ConsequenceUnit { get; set; } = string.Empty;

        /// <summary>
        /// Additional declared consequence types beyond the primary, in order: entry k − 1
        /// declares consequence position k. When present, every failure mode and non-fail path
        /// must carry one consequence function per declared type, in the same order.
        /// </summary>
        [JsonPropertyName("additionalConsequenceTypes")]
        public List<ConsequenceTypeDto>? AdditionalConsequenceTypes { get; set; }

        /// <summary>
        /// The system components (a screening analysis sends one).
        /// </summary>
        [Required]
        [MinLength(1)]
        [JsonPropertyName("components")]
        public List<ComponentDto> Components { get; set; } = new();

        /// <summary>
        /// The engine settings. Null runs with the engine defaults (mean-only, adjusted
        /// failure-mode curves on).
        /// </summary>
        [JsonPropertyName("options")]
        public RiskAnalysisOptionsDto? Options { get; set; }

        /// <summary>
        /// The API-level result-shaping options. Null includes everything.
        /// </summary>
        [JsonPropertyName("resultOptions")]
        public ResultOptionsDto? ResultOptions { get; set; }
    }
}
