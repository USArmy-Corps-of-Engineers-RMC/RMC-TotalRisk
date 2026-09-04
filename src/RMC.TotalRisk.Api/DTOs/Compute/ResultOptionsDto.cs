using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// API-level result-shaping options (what to include in the response body). The risk
    /// settings themselves — measure flags, adjusted curves, curve resolution — live on
    /// <see cref="RiskAnalysisOptionsDto"/> because they change what the engine computes; these
    /// options only trim the response payload.
    /// </summary>
    public class ResultOptionsDto
    {
        /// <summary>
        /// Whether to include the curve arrays (loss exceedance, hazard frequency, conditional
        /// mean, and profile arrays) in the response. Default true. When false the response
        /// keeps every scalar statistic and contribution and drops only the arrays.
        /// </summary>
        [JsonPropertyName("includeCurves")]
        public bool IncludeCurves { get; set; } = true;
    }
}
