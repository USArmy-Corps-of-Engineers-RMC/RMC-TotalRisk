using System.Text.Json.Serialization;
using Numerics.Data;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A hazard-domain transform function in a failure mode's hazard-to-response chain,
    /// discriminated by <see cref="Type"/>: a tabular conversion table ("tabularTransform" — e.g.
    /// a stage-discharge rating curve), a linear form Y = alpha + beta·X ("linearTransform" —
    /// e.g. a stage-to-overtopping-depth crest offset), or a power form Y = alpha·(X − xi)^beta
    /// ("powerTransform" — the weir shape). All kinds are deterministic in this contract version.
    /// </summary>
    /// <remarks>
    /// A transform FUNCTION converts the hazard signal itself (stage to depth, stage to
    /// discharge) and is distinct from the <c>hazardTransform</c>/<c>probabilityTransform</c>
    /// INTERPOLATION-space fields carried by tabular functions. Blank input labels inherit the
    /// incoming signal's pair (the component hazard for the first chain entry, the previous
    /// transform's output for later entries); the output labels are required because they define
    /// the axis the next function reads.
    /// </remarks>
    public class TransformFunctionDto
    {
        /// <summary>
        /// The function-kind discriminator: "tabularTransform", "linearTransform", or
        /// "powerTransform".
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = FunctionTypeNames.TabularTransform;

        /// <summary>
        /// The function's display name. Metadata only — never affects results.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// The INPUT hazard type label (e.g., "Reservoir Stage"). Blank inherits the incoming
        /// signal's label — the component hazard's for the first chain entry, the previous
        /// transform's output for later entries.
        /// </summary>
        [JsonPropertyName("specifiedHazard")]
        public string? SpecifiedHazard { get; set; }

        /// <summary>
        /// The INPUT hazard unit label (e.g., "ft"). Blank inherits the incoming signal's unit.
        /// </summary>
        [JsonPropertyName("hazardUnit")]
        public string? HazardUnit { get; set; }

        /// <summary>
        /// The OUTPUT hazard type label (e.g., "Overtopping Depth"). Required — it defines the
        /// axis the next chain entry or the response reads and cannot be inferred.
        /// </summary>
        [JsonPropertyName("transformedHazard")]
        public string? TransformedHazard { get; set; }

        /// <summary>
        /// The OUTPUT hazard unit label (e.g., "ft"). Required.
        /// </summary>
        [JsonPropertyName("transformedHazardUnit")]
        public string? TransformedHazardUnit { get; set; }

        /// <summary>
        /// Tabular kind only: the input hazard levels, strictly ascending. Index-aligned with
        /// <see cref="TransformedHazardValues"/>.
        /// </summary>
        [JsonPropertyName("hazardValues")]
        public List<double>? HazardValues { get; set; }

        /// <summary>
        /// Tabular kind only: the transformed hazard values, index-aligned with
        /// <see cref="HazardValues"/>. Order is unconstrained (rating curves may plateau).
        /// </summary>
        [JsonPropertyName("transformedHazardValues")]
        public List<double>? TransformedHazardValues { get; set; }

        /// <summary>
        /// Tabular kind only: the interpolation transform applied to the input hazard (X) axis.
        /// Null keeps the model default (none).
        /// </summary>
        [JsonPropertyName("hazardTransform")]
        public Transform? HazardTransform { get; set; }

        /// <summary>
        /// Tabular kind only: the interpolation transform applied to the transformed hazard (Y)
        /// axis. Null keeps the model default (none).
        /// </summary>
        [JsonPropertyName("transformedHazardTransform")]
        public Transform? TransformedHazardTransform { get; set; }

        /// <summary>
        /// Tabular kind only: the extrapolation policy outside the table range. Null keeps the
        /// model default (none — the endpoint hold).
        /// </summary>
        [JsonPropertyName("extrapolation")]
        public ExtrapolationPolicy? Extrapolation { get; set; }

        /// <summary>
        /// Linear kind: the intercept (null keeps 0). Power kind: the positive scale coefficient
        /// (null keeps 1).
        /// </summary>
        [JsonPropertyName("alpha")]
        public double? Alpha { get; set; }

        /// <summary>
        /// Linear kind: the slope (null keeps 1). Power kind: the exponent in [-10, 10] (null
        /// keeps 1.5, the broad-crested weir exponent).
        /// </summary>
        [JsonPropertyName("beta")]
        public double? Beta { get; set; }

        /// <summary>
        /// Power kind only: the location parameter (null keeps 0). Inputs below xi evaluate at
        /// xi.
        /// </summary>
        [JsonPropertyName("xi")]
        public double? Xi { get; set; }

        /// <summary>
        /// Power kind only: whether the power relation maps backwards through its inverse (null
        /// keeps false — the forward form).
        /// </summary>
        [JsonPropertyName("isInverse")]
        public bool? IsInverse { get; set; }

        /// <summary>
        /// Linear and power kinds: the lower evaluation bound. Required — inputs outside
        /// [minimum, maximum] clamp to the bounds, so the range must cover the hazard table's
        /// full extent.
        /// </summary>
        [JsonPropertyName("minimum")]
        public double? Minimum { get; set; }

        /// <summary>
        /// Linear and power kinds: the upper evaluation bound. Required.
        /// </summary>
        [JsonPropertyName("maximum")]
        public double? Maximum { get; set; }
    }
}
