using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// The mean risk results: the system-level curve streams and the per-component (and
    /// per-failure-mode) result trees.
    /// </summary>
    public class SystemResultsDto
    {
        /// <summary>
        /// The realization label ("Mean" for a mean-only run).
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The declared consequence type labels, entry 0 the primary type.
        /// </summary>
        [JsonPropertyName("consequenceLabels")]
        public List<string> ConsequenceLabels { get; set; } = new();

        /// <summary>
        /// The declared consequence unit labels, index-aligned with
        /// <see cref="ConsequenceLabels"/>.
        /// </summary>
        [JsonPropertyName("consequenceUnits")]
        public List<string> ConsequenceUnits { get; set; } = new();

        /// <summary>
        /// The system-level curve streams for the primary consequence type.
        /// </summary>
        [JsonPropertyName("curves")]
        public CurveSetDto Curves { get; set; } = new();

        /// <summary>
        /// The system-level curve sets of the additional consequence types, in declared order.
        /// Null on a single-type analysis.
        /// </summary>
        [JsonPropertyName("additionalCurves")]
        public List<CurveSetDto>? AdditionalCurves { get; set; }

        /// <summary>
        /// The total number of integrand evaluations behind this realization.
        /// </summary>
        [JsonPropertyName("functionEvaluations")]
        public double FunctionEvaluations { get; set; }

        /// <summary>
        /// The integrator's error estimate for this realization.
        /// </summary>
        [JsonPropertyName("standardError")]
        public double StandardError { get; set; }

        /// <summary>
        /// The per-component results, in the request's component order.
        /// </summary>
        [JsonPropertyName("components")]
        public List<ComponentResultsDto> Components { get; set; } = new();
    }
}
