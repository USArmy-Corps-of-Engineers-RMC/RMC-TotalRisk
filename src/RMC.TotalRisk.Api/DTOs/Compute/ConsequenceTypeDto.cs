using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// One additional declared consequence type on the analysis's ordered consequence-type axis
    /// (the primary type is declared by the request's top-level specifiedConsequence /
    /// consequenceUnit pair).
    /// </summary>
    public class ConsequenceTypeDto
    {
        /// <summary>
        /// The consequence type label (e.g., "Damages"). Blank declares a wildcard position that
        /// matches any function label.
        /// </summary>
        [JsonPropertyName("specifiedConsequence")]
        public string? SpecifiedConsequence { get; set; }

        /// <summary>
        /// The consequence unit label (e.g., "$"). Blank declares a wildcard.
        /// </summary>
        [JsonPropertyName("consequenceUnit")]
        public string? ConsequenceUnit { get; set; }

        /// <summary>
        /// The consequence threshold for this type's assurance measure, in this type's own units.
        /// Null skips the per-type assurance lookup.
        /// </summary>
        [JsonPropertyName("consequenceThreshold")]
        public double? ConsequenceThreshold { get; set; }
    }
}
