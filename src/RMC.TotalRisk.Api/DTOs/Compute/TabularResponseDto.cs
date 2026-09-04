using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Numerics.Data;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A deterministic tabular response function: hazard levels paired with system response
    /// probabilities (a fragility curve).
    /// </summary>
    /// <remarks>
    /// The two arrays are index-aligned. Hazard values are strictly ascending; response
    /// probabilities are in [0, 1] and MAY plateau (0/1 plateaus are legal and common).
    /// </remarks>
    public class TabularResponseDto
    {
        /// <summary>
        /// The function-kind discriminator. Must be "tabularResponse" (the default) in this
        /// contract version.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = FunctionTypeNames.TabularResponse;

        /// <summary>
        /// The function's display name. Metadata only — never affects results.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// The hazard type label (e.g., "Stage"). Blank inherits the component hazard's label.
        /// </summary>
        [JsonPropertyName("specifiedHazard")]
        public string? SpecifiedHazard { get; set; }

        /// <summary>
        /// The hazard unit label (e.g., "ft"). Blank inherits the component hazard's unit.
        /// </summary>
        [JsonPropertyName("hazardUnit")]
        public string? HazardUnit { get; set; }

        /// <summary>
        /// The hazard levels, strictly ascending. Index-aligned with
        /// <see cref="ResponseProbabilities"/>.
        /// </summary>
        [Required]
        [JsonPropertyName("hazardValues")]
        public List<double> HazardValues { get; set; } = new();

        /// <summary>
        /// The system response probabilities in [0, 1], index-aligned with
        /// <see cref="HazardValues"/>. Plateaus at 0 or 1 are legal.
        /// </summary>
        [Required]
        [JsonPropertyName("responseProbabilities")]
        public List<double> ResponseProbabilities { get; set; } = new();

        /// <summary>
        /// The interpolation transform applied to the hazard (X) axis. Null keeps the model
        /// default (none).
        /// </summary>
        [JsonPropertyName("hazardTransform")]
        public Transform? HazardTransform { get; set; }

        /// <summary>
        /// The interpolation transform applied to the response-probability (Y) axis. Null keeps
        /// the model default (none).
        /// </summary>
        [JsonPropertyName("probabilityTransform")]
        public Transform? ProbabilityTransform { get; set; }

        /// <summary>
        /// The extrapolation policy outside the table range. Null keeps the model default (none —
        /// the endpoint hold).
        /// </summary>
        [JsonPropertyName("extrapolation")]
        public ExtrapolationPolicy? Extrapolation { get; set; }
    }
}
