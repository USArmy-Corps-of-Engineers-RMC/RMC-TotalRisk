using System.Text.Json.Serialization;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A machine-readable validation issue: a stable diagnostic code, a severity, the caller-facing
    /// message, and the definition object path the issue is anchored to.
    /// </summary>
    /// <remarks>
    /// Model-layer issues carry the engine's own codes (for example "TRV0001"); issues raised by
    /// the API's request-shape checks use the "API_" code family so agents can distinguish contract
    /// problems from model problems.
    /// </remarks>
    public class ValidationIssueDto
    {
        /// <summary>
        /// The stable machine-readable code (model codes such as "TRV0001", or API request-shape
        /// codes such as "API_MEAN_ONLY_REQUIRED").
        /// </summary>
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// The issue severity, serialized as a camelCase string ("error", "warning", "informational").
        /// </summary>
        [JsonPropertyName("severity")]
        public DiagnosticSeverity Severity { get; set; }

        /// <summary>
        /// The caller-facing message without a severity prefix.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The definition object path the issue is anchored to (for example
        /// "components[0].failureModes[1].response"); empty when no narrower path is known.
        /// </summary>
        [JsonPropertyName("objectPath")]
        public string ObjectPath { get; set; } = string.Empty;

        /// <summary>
        /// Maps a model-layer validation issue to its API DTO.
        /// </summary>
        /// <param name="issue">The model-layer issue.</param>
        /// <returns>The DTO carrying the same code, severity, message, and path.</returns>
        public static ValidationIssueDto FromModel(ValidationIssue issue)
        {
            ArgumentNullException.ThrowIfNull(issue);
            return new ValidationIssueDto
            {
                Code = issue.Code,
                Severity = issue.Severity,
                Message = issue.Message,
                ObjectPath = issue.ObjectPath,
            };
        }

        /// <summary>
        /// Builds an API request-shape issue (an "API_" family code) at error severity.
        /// </summary>
        /// <param name="code">The stable API code (for example "API_MEAN_ONLY_REQUIRED").</param>
        /// <param name="message">The caller-facing message, echoing the offending value where possible.</param>
        /// <param name="objectPath">The request object path the issue is anchored to.</param>
        /// <returns>The error-severity DTO.</returns>
        public static ValidationIssueDto ApiError(string code, string message, string objectPath = "")
        {
            return new ValidationIssueDto
            {
                Code = code,
                Severity = DiagnosticSeverity.Error,
                Message = message,
                ObjectPath = objectPath,
            };
        }
    }
}
