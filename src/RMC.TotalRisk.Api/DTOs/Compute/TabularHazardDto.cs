using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Numerics.Data;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A deterministic tabular hazard (frequency) function: annual exceedance probabilities paired
    /// with hazard levels (e.g., a reservoir stage-frequency curve).
    /// </summary>
    /// <remarks>
    /// The two arrays are index-aligned. Probabilities are ANNUAL EXCEEDANCE probabilities in
    /// [0, 1], strictly DESCENDING (the frequent event first); hazard values are strictly
    /// ASCENDING. Both orderings are validated and never silently reordered.
    /// </remarks>
    public class TabularHazardDto
    {
        /// <summary>
        /// The function-kind discriminator. Must be "tabularHazard" (the default) in this contract
        /// version; the field exists so additional hazard kinds can be added without breaking the
        /// wire contract.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = FunctionTypeNames.TabularHazard;

        /// <summary>
        /// The function's display name. Metadata only — never affects results.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// The hazard type label (e.g., "Stage"). Required by the model.
        /// </summary>
        [Required]
        [JsonPropertyName("specifiedHazard")]
        public string SpecifiedHazard { get; set; } = string.Empty;

        /// <summary>
        /// The hazard unit label (e.g., "ft"). Required by the model.
        /// </summary>
        [Required]
        [JsonPropertyName("hazardUnit")]
        public string HazardUnit { get; set; } = string.Empty;

        /// <summary>
        /// The annual exceedance probabilities, strictly descending, each in [0, 1].
        /// Index-aligned with <see cref="HazardValues"/>.
        /// </summary>
        [Required]
        [JsonPropertyName("exceedanceProbabilities")]
        public List<double> ExceedanceProbabilities { get; set; } = new();

        /// <summary>
        /// The hazard levels, strictly ascending. Index-aligned with
        /// <see cref="ExceedanceProbabilities"/>.
        /// </summary>
        [Required]
        [JsonPropertyName("hazardValues")]
        public List<double> HazardValues { get; set; } = new();

        /// <summary>
        /// The interpolation transform applied to the hazard axis ("none", "logarithmic",
        /// "normalZ"). Null keeps the model default (none).
        /// </summary>
        [JsonPropertyName("hazardTransform")]
        public Transform? HazardTransform { get; set; }

        /// <summary>
        /// The interpolation transform applied to the exceedance-probability axis. Null keeps the
        /// model default (normalZ — the standard frequency-curve interpolation space).
        /// </summary>
        [JsonPropertyName("probabilityTransform")]
        public Transform? ProbabilityTransform { get; set; }

        /// <summary>
        /// The extrapolation policy outside the table range ("none" holds the endpoints — the
        /// default; "below"/"above"/"both" extend linearly in transform space; "error" refuses
        /// out-of-range evaluation). Null keeps the model default (none).
        /// </summary>
        [JsonPropertyName("extrapolation")]
        public ExtrapolationPolicy? Extrapolation { get; set; }
    }
}
