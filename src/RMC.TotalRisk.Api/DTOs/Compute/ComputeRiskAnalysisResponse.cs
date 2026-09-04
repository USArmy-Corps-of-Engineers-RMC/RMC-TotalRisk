using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// Body of the compute endpoint: the mean risk results with provenance, the effective engine
    /// settings the run used, and any validation warnings or computation diagnostics.
    /// </summary>
    public class ComputeRiskAnalysisResponse : ResponseBase
    {
        /// <summary>
        /// The deterministic run provenance (content hashes, seed, versions); null when the run
        /// did not publish a manifest.
        /// </summary>
        [JsonPropertyName("provenance")]
        public ProvenanceDto? Provenance { get; set; }

        /// <summary>
        /// The engine settings the run actually used — the request settings with every
        /// unsupplied field resolved to its effective default. Agents can replay a run exactly
        /// by sending these back.
        /// </summary>
        [JsonPropertyName("effectiveOptions")]
        public RiskAnalysisOptionsDto? EffectiveOptions { get; set; }

        /// <summary>
        /// Plain-text warnings raised during the computation (e.g., truncated combination
        /// expansions); null when none.
        /// </summary>
        [JsonPropertyName("computationWarnings")]
        public List<string>? ComputationWarnings { get; set; }

        /// <summary>
        /// Structured diagnostics raised during the computation; null when none.
        /// </summary>
        [JsonPropertyName("computationDiagnostics")]
        public List<ComputationDiagnosticDto>? ComputationDiagnostics { get; set; }

        /// <summary>
        /// The mean risk results tree (system, components, failure modes); null on failure.
        /// </summary>
        [JsonPropertyName("results")]
        public SystemResultsDto? Results { get; set; }
    }
}
