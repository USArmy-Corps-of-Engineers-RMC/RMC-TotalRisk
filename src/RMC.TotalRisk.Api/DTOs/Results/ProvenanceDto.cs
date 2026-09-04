using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// The deterministic run provenance: schema and assembly versions, the content hashes that
    /// seed the run, and the seed itself. Two requests with identical compute-relevant content
    /// and the same seed carry identical hashes and produce bit-identical results.
    /// </summary>
    public class ProvenanceDto
    {
        /// <summary>
        /// The results schema version of the engine's result containers.
        /// </summary>
        [JsonPropertyName("resultsSchemaVersion")]
        public int ResultsSchemaVersion { get; set; }

        /// <summary>
        /// The RMC.TotalRisk assembly version that produced the results.
        /// </summary>
        [JsonPropertyName("totalRiskVersion")]
        public string? TotalRiskVersion { get; set; }

        /// <summary>
        /// The RMC.Numerics assembly version behind the engine.
        /// </summary>
        [JsonPropertyName("numericsVersion")]
        public string? NumericsVersion { get; set; }

        /// <summary>
        /// The API wire-contract version that served this response.
        /// </summary>
        [JsonPropertyName("apiContractVersion")]
        public string? ApiContractVersion { get; set; }

        /// <summary>
        /// The canonical content hash of the whole analysis definition.
        /// </summary>
        [JsonPropertyName("analysisContentHash")]
        public string? AnalysisContentHash { get; set; }

        /// <summary>
        /// The canonical hash of the effective run options.
        /// </summary>
        [JsonPropertyName("effectiveOptionsHash")]
        public string? EffectiveOptionsHash { get; set; }

        /// <summary>
        /// The canonical content hash of each component, in component order.
        /// </summary>
        [JsonPropertyName("componentContentHashes")]
        public List<string>? ComponentContentHashes { get; set; }

        /// <summary>
        /// The occurrence index assigned to each component (disambiguates identical-content
        /// components), in component order.
        /// </summary>
        [JsonPropertyName("componentOccurrenceIndices")]
        public List<int>? ComponentOccurrenceIndices { get; set; }

        /// <summary>
        /// The pseudo-random seed the run folded with the content hashes.
        /// </summary>
        [JsonPropertyName("prngSeed")]
        public int PrngSeed { get; set; }
    }
}
