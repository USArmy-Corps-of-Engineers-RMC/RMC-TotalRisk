using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// Body of the metadata endpoint: every enum's accepted wire values, the function-kind
    /// discriminators, the effective defaults, the host limits, and the contract conventions.
    /// Agents should read this before constructing a compute request.
    /// </summary>
    public class MetadataResponse : ResponseBase
    {
        /// <summary>
        /// The accepted camelCase wire values of every enum in the contract, keyed by the
        /// camelCase enum name (e.g., "failureModeMethod" → ["jointFailures", ...]).
        /// </summary>
        [JsonPropertyName("enums")]
        public Dictionary<string, List<string>> Enums { get; set; } = new();

        /// <summary>
        /// The accepted function-kind discriminators per function role (e.g., "consequence" →
        /// ["tabularConsequence", "compositeMixture"]).
        /// </summary>
        [JsonPropertyName("functionTypes")]
        public Dictionary<string, List<string>> FunctionTypes { get; set; } = new();

        /// <summary>
        /// The effective default of every optional setting, keyed by its camelCase wire name.
        /// Includes the API-level divergences (outputAdjustedFailureModeCurves defaults true on
        /// the API).
        /// </summary>
        [JsonPropertyName("defaults")]
        public Dictionary<string, object?> Defaults { get; set; } = new();

        /// <summary>
        /// The host's configured request limits.
        /// </summary>
        [JsonPropertyName("limits")]
        public MetadataLimitsDto Limits { get; set; } = new();

        /// <summary>
        /// Human-readable contract conventions (probability direction, NaN policy, adjusted vs
        /// unadjusted semantics, contribution identities).
        /// </summary>
        [JsonPropertyName("conventions")]
        public List<string> Conventions { get; set; } = new();
    }
}
