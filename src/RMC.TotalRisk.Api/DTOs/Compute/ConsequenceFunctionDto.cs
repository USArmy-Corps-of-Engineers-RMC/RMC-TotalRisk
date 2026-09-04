using System.Text.Json.Serialization;
using Numerics.Data;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// A consequence function, discriminated by <see cref="Type"/>: either a deterministic
    /// tabular consequence ("tabularConsequence" — supply the two table arrays) or a weighted
    /// exposure mixture ("compositeMixture" — supply <see cref="Branches"/>, e.g. day/night
    /// consequence pairs with exposure weights).
    /// </summary>
    /// <remarks>
    /// Blank labels inherit: the hazard pair from the component's hazard function, and the
    /// consequence pair from the analysis's declared consequence type at the position the
    /// function fills — so a minimal request only labels the analysis once.
    /// </remarks>
    public class ConsequenceFunctionDto
    {
        /// <summary>
        /// The function-kind discriminator: "tabularConsequence" or "compositeMixture".
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = FunctionTypeNames.TabularConsequence;

        /// <summary>
        /// The function's display name. Metadata only — but note the primary consequence
        /// function's name labels the failure-mode results row, so a blank name inherits the
        /// owning failure mode's name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// The hazard type label. Blank inherits the component hazard's label.
        /// </summary>
        [JsonPropertyName("specifiedHazard")]
        public string? SpecifiedHazard { get; set; }

        /// <summary>
        /// The hazard unit label. Blank inherits the component hazard's unit.
        /// </summary>
        [JsonPropertyName("hazardUnit")]
        public string? HazardUnit { get; set; }

        /// <summary>
        /// The consequence type label (e.g., "Life Loss"). Blank inherits the analysis's declared
        /// consequence type at this function's position.
        /// </summary>
        [JsonPropertyName("specifiedConsequence")]
        public string? SpecifiedConsequence { get; set; }

        /// <summary>
        /// The consequence unit label (e.g., "lives"). Blank inherits the analysis's declared
        /// consequence unit at this function's position.
        /// </summary>
        [JsonPropertyName("consequenceUnit")]
        public string? ConsequenceUnit { get; set; }

        /// <summary>
        /// Tabular kind only: the hazard levels, strictly ascending. Index-aligned with
        /// <see cref="ConsequenceValues"/>.
        /// </summary>
        [JsonPropertyName("hazardValues")]
        public List<double>? HazardValues { get; set; }

        /// <summary>
        /// Tabular kind only: the consequence values, index-aligned with
        /// <see cref="HazardValues"/>. Negative values clamp to zero during simulation.
        /// </summary>
        [JsonPropertyName("consequenceValues")]
        public List<double>? ConsequenceValues { get; set; }

        /// <summary>
        /// Tabular kind only: the interpolation transform applied to the hazard (X) axis. Null
        /// keeps the model default (none).
        /// </summary>
        [JsonPropertyName("hazardTransform")]
        public Transform? HazardTransform { get; set; }

        /// <summary>
        /// Tabular kind only: the interpolation transform applied to the consequence (Y) axis.
        /// Null keeps the model default (none).
        /// </summary>
        [JsonPropertyName("consequenceTransform")]
        public Transform? ConsequenceTransform { get; set; }

        /// <summary>
        /// Tabular kind only: the extrapolation policy outside the table range. Null keeps the
        /// model default (none — the endpoint hold).
        /// </summary>
        [JsonPropertyName("extrapolation")]
        public ExtrapolationPolicy? Extrapolation { get; set; }

        /// <summary>
        /// Composite kind only: the weighted child branches (e.g., day and night). Weights must be
        /// in [0, 1] and sum to 1; the engine enumerates the weighted branches as aleatory
        /// exposure probabilities (a mixture, not an average — the correct exposure model).
        /// </summary>
        [JsonPropertyName("branches")]
        public List<ConsequenceBranchDto>? Branches { get; set; }
    }
}
