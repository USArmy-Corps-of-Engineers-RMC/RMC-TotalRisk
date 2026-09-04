using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// One system component (e.g., a dam): its hazard function, its failure modes, its non-fail
    /// consequences, and how the failure modes combine.
    /// </summary>
    public class ComponentDto
    {
        /// <summary>
        /// The component's display name (e.g., "Dam").
        /// </summary>
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The component's hazard (frequency) function.
        /// </summary>
        [Required]
        [JsonPropertyName("hazard")]
        public TabularHazardDto Hazard { get; set; } = new();

        /// <summary>
        /// The component's failure modes. May be empty for a pure background-risk (damage
        /// frequency) component that carries only non-fail consequences.
        /// </summary>
        [JsonPropertyName("failureModes")]
        public List<FailureModeDto> FailureModes { get; set; } = new();

        /// <summary>
        /// The non-failure consequence functions on the component's response-free path, one per
        /// declared consequence type in declared order (index 0 is the primary type). Empty when
        /// the component carries no non-fail consequences; a component must define at least one
        /// failure mode or one non-fail consequence.
        /// </summary>
        [JsonPropertyName("nonFailConsequences")]
        public List<ConsequenceFunctionDto> NonFailConsequences { get; set; } = new();

        /// <summary>
        /// How multiple failure modes combine ("jointFailures", "competingFailures",
        /// "commonCauseFailures", "mutuallyExclusive"). Null keeps the model default
        /// (jointFailures).
        /// </summary>
        [JsonPropertyName("failureModeMethod")]
        public FailureModeMethod? FailureModeMethod { get; set; }

        /// <summary>
        /// The statistical dependence between failure modes ("independent", "perfectlyPositive",
        /// "perfectlyNegative", "correlationMatrix"). Null keeps the model default (independent).
        /// The common-cause and mutually-exclusive methods carry no dependence model and coerce
        /// this to independent.
        /// </summary>
        [JsonPropertyName("failureModeDependency")]
        public DependencyType? FailureModeDependency { get; set; }

        /// <summary>
        /// The failure-mode correlation matrix (one row per failure mode, positive definite).
        /// Required only under the "correlationMatrix" dependency.
        /// </summary>
        [JsonPropertyName("failureModeCorrelationMatrix")]
        public List<List<double>>? FailureModeCorrelationMatrix { get; set; }

        /// <summary>
        /// How the consequences of jointly failing modes combine ("additive", "average",
        /// "maximum", "minimum"). Null keeps the model default (maximum).
        /// </summary>
        [JsonPropertyName("jointConsequences")]
        public JointConsequenceType? JointConsequences { get; set; }

        /// <summary>
        /// The hazard level threshold for the component's hazard assurance measure (the
        /// probability of hazard levels exceeding the threshold). Null keeps the model default (0).
        /// </summary>
        [JsonPropertyName("hazardThreshold")]
        public double? HazardThreshold { get; set; }
    }
}
