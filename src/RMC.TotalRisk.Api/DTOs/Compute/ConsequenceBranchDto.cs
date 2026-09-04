using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// One weighted branch of a composite-mixture consequence function (e.g., the day exposure
    /// branch with weight 0.58).
    /// </summary>
    public class ConsequenceBranchDto
    {
        /// <summary>
        /// The branch's exposure weight in [0, 1]. Weights across a composite's branches must sum
        /// to 1.
        /// </summary>
        [Range(0.0, 1.0)]
        [JsonPropertyName("weight")]
        public double Weight { get; set; }

        /// <summary>
        /// The branch's consequence function (typically a tabular consequence; nesting another
        /// composite is legal).
        /// </summary>
        [Required]
        [JsonPropertyName("function")]
        public ConsequenceFunctionDto Function { get; set; } = new();
    }
}
