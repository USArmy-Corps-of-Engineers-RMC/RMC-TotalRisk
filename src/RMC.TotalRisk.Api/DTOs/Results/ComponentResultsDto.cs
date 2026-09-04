using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// One system component's results: its curve streams, its failure modes, and its attributed
    /// contribution to the system.
    /// </summary>
    public class ComponentResultsDto
    {
        /// <summary>
        /// The component's display name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The component-level curve streams for the primary consequence type.
        /// </summary>
        [JsonPropertyName("curves")]
        public CurveSetDto Curves { get; set; } = new();

        /// <summary>
        /// The component-level curve sets of the additional consequence types, in declared order.
        /// Null on a single-type analysis.
        /// </summary>
        [JsonPropertyName("additionalCurves")]
        public List<CurveSetDto>? AdditionalCurves { get; set; }

        /// <summary>
        /// The component's attributed contribution to the system's risk (primary consequence
        /// type); null when not computed.
        /// </summary>
        [JsonPropertyName("systemContribution")]
        public ContributionDto? SystemContribution { get; set; }

        /// <summary>
        /// The attributed system contributions for the additional consequence types, in declared
        /// order (entries may be null). Null on a single-type analysis.
        /// </summary>
        [JsonPropertyName("additionalSystemContributions")]
        public List<ContributionDto?>? AdditionalSystemContributions { get; set; }

        /// <summary>
        /// The smallest hazard level observed for this component.
        /// </summary>
        [JsonPropertyName("minHazard")]
        public double MinHazard { get; set; }

        /// <summary>
        /// The largest hazard level observed for this component.
        /// </summary>
        [JsonPropertyName("maxHazard")]
        public double MaxHazard { get; set; }

        /// <summary>
        /// The per-failure-mode results, one entry per failure path in the component's declared
        /// order. The non-fail path is not a row here — its risk rides the component-level
        /// background and non-fail streams.
        /// </summary>
        [JsonPropertyName("failureModes")]
        public List<FailureModeResultsDto> FailureModes { get; set; } = new();
    }
}
