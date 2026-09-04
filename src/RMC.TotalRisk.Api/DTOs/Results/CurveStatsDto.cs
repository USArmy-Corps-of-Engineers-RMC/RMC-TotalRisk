using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// The scalar risk measures of one curve stream. The mean, standard deviation, total
    /// probability, and mass balance are always computed; nullable measures are null when the
    /// engine did not compute them (a disabled measure flag, or an undefined value such as the
    /// conditional mean of an empty stream).
    /// </summary>
    public class CurveStatsDto
    {
        /// <summary>
        /// The stream's total (annualized) probability. For the fail stream this is the
        /// annualized failure probability.
        /// </summary>
        [JsonPropertyName("totalProbability")]
        public double TotalProbability { get; set; }

        /// <summary>
        /// The recorded probability mass behind the stream (a mass-accounting witness; 1 for an
        /// exhaustive stream).
        /// </summary>
        [JsonPropertyName("massBalance")]
        public double MassBalance { get; set; }

        /// <summary>
        /// Whether the stream is exhaustive (carries probability 1 by construction, like the
        /// total stream) rather than defective (like the fail stream).
        /// </summary>
        [JsonPropertyName("isExhaustive")]
        public bool IsExhaustive { get; set; }

        /// <summary>
        /// The stream's annualized mean consequence (e.g., expected annual life loss or damages).
        /// </summary>
        [JsonPropertyName("mean")]
        public double Mean { get; set; }

        /// <summary>
        /// The mean conditioned on the stream occurring (mean / totalProbability); null when the
        /// stream carries no probability.
        /// </summary>
        [JsonPropertyName("conditionalMean")]
        public double? ConditionalMean { get; set; }

        /// <summary>
        /// The stream's consequence standard deviation.
        /// </summary>
        [JsonPropertyName("standardDeviation")]
        public double StandardDeviation { get; set; }

        /// <summary>
        /// The consequence skewness; null unless the higherMoments measure is enabled.
        /// </summary>
        [JsonPropertyName("skewness")]
        public double? Skewness { get; set; }

        /// <summary>
        /// The consequence kurtosis; null unless the higherMoments measure is enabled.
        /// </summary>
        [JsonPropertyName("kurtosis")]
        public double? Kurtosis { get; set; }

        /// <summary>
        /// The value at risk at the requested alpha; null unless the valueAtRisk measure is
        /// enabled.
        /// </summary>
        [JsonPropertyName("valueAtRisk")]
        public double? ValueAtRisk { get; set; }

        /// <summary>
        /// The conditional value at risk (expected consequence beyond the value at risk); null
        /// unless the valueAtRisk measure is enabled.
        /// </summary>
        [JsonPropertyName("conditionalValueAtRisk")]
        public double? ConditionalValueAtRisk { get; set; }

        /// <summary>
        /// The assurance measure P(C &gt; consequenceThreshold); null unless the
        /// thresholdProbabilities measure is enabled.
        /// </summary>
        [JsonPropertyName("consequenceThresholdProbability")]
        public double? ConsequenceThresholdProbability { get; set; }

        /// <summary>
        /// The hazard assurance measure P(H &gt; hazardThreshold); null unless the
        /// thresholdProbabilities measure is enabled and a hazard threshold applies.
        /// </summary>
        [JsonPropertyName("hazardThresholdProbability")]
        public double? HazardThresholdProbability { get; set; }
    }
}
