using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// Body of the GET api/info endpoint describing the service and its capabilities.
    /// </summary>
    public class ApiInfoDto
    {
        /// <summary>
        /// The service name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "RMC-TotalRisk API";

        /// <summary>
        /// The API assembly version.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        /// <summary>
        /// The wire-contract version of the compute request/response DTOs. Additive contract
        /// changes keep the major version; breaking changes increment it.
        /// </summary>
        [JsonPropertyName("apiContractVersion")]
        public string ApiContractVersion { get; set; } = "1.0.0";

        /// <summary>
        /// A short description of what the service provides.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// The feature areas currently exposed by the service (e.g., "compute", "metadata").
        /// </summary>
        [JsonPropertyName("features")]
        public List<string> Features { get; set; } = new();
    }
}
