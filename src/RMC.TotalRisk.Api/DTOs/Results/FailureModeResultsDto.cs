using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// One failure mode's results: its raw unadjusted marginal curves, its combination-adjusted
    /// curves (when requested), and its attributed contribution to the component.
    /// </summary>
    /// <remarks>
    /// The unadjusted curves carry the mode's own marginal response probability — what an
    /// investment decision compares across modes. The adjusted curves carry the mode's share
    /// after the component's combination method has resolved the modes against one another, so
    /// adjusted values sum to the component total. Both views answer different questions.
    /// </remarks>
    public class FailureModeResultsDto
    {
        /// <summary>
        /// The failure mode's display name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The branch path descriptor for multi-stage modes; null for simple modes.
        /// </summary>
        [JsonPropertyName("pathLabel")]
        public string? PathLabel { get; set; }

        /// <summary>
        /// The mode's UNADJUSTED marginal curve streams for the primary consequence type.
        /// </summary>
        [JsonPropertyName("curves")]
        public CurveSetDto Curves { get; set; } = new();

        /// <summary>
        /// The unadjusted curve sets of the additional consequence types, in declared order.
        /// Null on a single-type analysis.
        /// </summary>
        [JsonPropertyName("additionalCurves")]
        public List<CurveSetDto>? AdditionalCurves { get; set; }

        /// <summary>
        /// The mode's combination-ADJUSTED curve streams for the primary consequence type; null
        /// unless outputAdjustedFailureModeCurves is enabled (the API default enables it).
        /// </summary>
        [JsonPropertyName("adjustedCurves")]
        public CurveSetDto? AdjustedCurves { get; set; }

        /// <summary>
        /// The adjusted curve sets of the additional consequence types, in declared order. Null
        /// on a single-type analysis or when adjusted output is off.
        /// </summary>
        [JsonPropertyName("additionalAdjustedCurves")]
        public List<CurveSetDto>? AdditionalAdjustedCurves { get; set; }

        /// <summary>
        /// The mode's attributed contribution to the component's risk (primary consequence type);
        /// null when not computed.
        /// </summary>
        [JsonPropertyName("contribution")]
        public ContributionDto? Contribution { get; set; }

        /// <summary>
        /// The attributed contributions for the additional consequence types, in declared order
        /// (entries may be null). Null on a single-type analysis.
        /// </summary>
        [JsonPropertyName("additionalContributions")]
        public List<ContributionDto?>? AdditionalContributions { get; set; }
    }
}
