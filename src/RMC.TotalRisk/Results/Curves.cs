using System;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The five loss exceedance curves of one realization scope — one <see cref="Curve"/> per
    /// <see cref="Core.Enums.RiskType"/> stream: Excess (incremental), Background (irreducible),
    /// Total, Fail, and NonFail.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Fail, Excess, and NonFail are defective (total probability below one); Background and Total
    /// are exhaustive — the v1.0 assignment, preserved. The fan-out methods mirror the per-curve
    /// pipeline: mass post-processing, exact curve construction at the output resolution, the
    /// hazard profiles, the risk measures, and the post-aggregation memory dump.
    /// </para>
    /// </remarks>
    public class Curves
    {
        /// <summary>
        /// Initializes the five streams with their v1.0 exhaustiveness assignments.
        /// </summary>
        public Curves()
        {
            Excess = new Curve { IsExhaustive = false };
            Background = new Curve();
            Total = new Curve();
            Fail = new Curve { IsExhaustive = false };
            NonFail = new Curve { IsExhaustive = false };
        }

        /// <summary>
        /// The incremental (excess) risk curve — the reducible risk. Defective.
        /// </summary>
        public Curve Excess { get; set; }

        /// <summary>
        /// The background (irreducible, non-breach) risk curve. Exhaustive.
        /// </summary>
        public Curve Background { get; set; }

        /// <summary>
        /// The total risk curve. Exhaustive.
        /// </summary>
        public Curve Total { get; set; }

        /// <summary>
        /// The failure risk curve — its total probability is the annualized failure probability.
        /// Defective.
        /// </summary>
        public Curve Fail { get; set; }

        /// <summary>
        /// The non-failure risk curve. Defective.
        /// </summary>
        public Curve NonFail { get; set; }

        /// <summary>
        /// Post-processes the recorded hazard probabilities into masses on every stream
        /// (one-dimensional path only).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a stream's mass budget does not telescope to one.</exception>
        public void ProcessHazardProbabilities()
        {
            Excess.ProcessHazardProbabilities();
            Background.ProcessHazardProbabilities();
            Total.ProcessHazardProbabilities();
            Fail.ProcessHazardProbabilities();
            NonFail.ProcessHazardProbabilities();
        }

        /// <summary>
        /// Builds the exact curve and moments on every stream.
        /// </summary>
        /// <param name="outputLength">The output resolution of the stored curves.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the output length is less than two.</exception>
        public void CreateCurves(int outputLength)
        {
            Excess.CreateCurve(outputLength);
            Background.CreateCurve(outputLength);
            Total.CreateCurve(outputLength);
            Fail.CreateCurve(outputLength);
            NonFail.CreateCurve(outputLength);
        }

        /// <summary>
        /// Builds the hazard profiles on every stream.
        /// </summary>
        public void CreateProfiles()
        {
            Excess.CreateProfiles();
            Background.CreateProfiles();
            Total.CreateProfiles();
            Fail.CreateProfiles();
            NonFail.CreateProfiles();
        }

        /// <summary>
        /// Computes the risk-measure catalog on every stream.
        /// </summary>
        /// <param name="consequenceThreshold">The consequence threshold for the assurance measure.</param>
        /// <param name="alpha">The exceedance level for value-at-risk and conditional value-at-risk.</param>
        /// <param name="hazardThreshold">The hazard threshold, or NaN when none applies.</param>
        public void ComputeRiskMeasures(double consequenceThreshold, double alpha, double hazardThreshold = double.NaN)
        {
            Excess.ComputeRiskMeasures(consequenceThreshold, alpha, hazardThreshold);
            Background.ComputeRiskMeasures(consequenceThreshold, alpha, hazardThreshold);
            Total.ComputeRiskMeasures(consequenceThreshold, alpha, hazardThreshold);
            Fail.ComputeRiskMeasures(consequenceThreshold, alpha, hazardThreshold);
            NonFail.ComputeRiskMeasures(consequenceThreshold, alpha, hazardThreshold);
        }

        /// <summary>
        /// Clears every stream's recorded risk points — call only after post-processing.
        /// </summary>
        public void DumpMemory()
        {
            Excess.DumpMemory();
            Background.DumpMemory();
            Total.DumpMemory();
            Fail.DumpMemory();
            NonFail.DumpMemory();
        }

        /// <summary>
        /// Creates a deep copy of the five streams.
        /// </summary>
        /// <returns>The copy.</returns>
        public Curves Clone()
        {
            return new Curves
            {
                Excess = Excess.Clone(),
                Background = Background.Clone(),
                Total = Total.Clone(),
                Fail = Fail.Clone(),
                NonFail = NonFail.Clone(),
            };
        }
    }
}
