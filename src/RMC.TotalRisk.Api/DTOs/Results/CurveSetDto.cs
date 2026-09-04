using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// The five curve streams of one consequence type at one scope: excess (incremental),
    /// background, total, fail, and non-fail.
    /// </summary>
    public class CurveSetDto
    {
        /// <summary>
        /// The incremental (excess) risk stream — the reducible risk. Defective.
        /// </summary>
        [JsonPropertyName("excess")]
        public CurveDto Excess { get; set; } = new();

        /// <summary>
        /// The background (irreducible, non-breach) risk stream. Exhaustive.
        /// </summary>
        [JsonPropertyName("background")]
        public CurveDto Background { get; set; } = new();

        /// <summary>
        /// The total risk stream. Exhaustive.
        /// </summary>
        [JsonPropertyName("total")]
        public CurveDto Total { get; set; } = new();

        /// <summary>
        /// The failure risk stream — its total probability is the annualized failure probability.
        /// Defective.
        /// </summary>
        [JsonPropertyName("fail")]
        public CurveDto Fail { get; set; } = new();

        /// <summary>
        /// The non-failure risk stream. Defective.
        /// </summary>
        [JsonPropertyName("nonFail")]
        public CurveDto NonFail { get; set; } = new();
    }
}
