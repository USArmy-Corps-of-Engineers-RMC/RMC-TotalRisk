using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// Body of the validate endpoint: the structured validation verdict for a compute request,
    /// produced without running the engine.
    /// </summary>
    public class ValidateRiskAnalysisResponse : ResponseBase
    {
        /// <summary>
        /// True when the request maps to a model with no error-severity issues (warnings may
        /// still be present in validationIssues).
        /// </summary>
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }
    }
}
