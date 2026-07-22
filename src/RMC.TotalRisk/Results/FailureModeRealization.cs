using System;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The per-realization results of one failure mode: its five loss exceedance curve streams.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public class FailureModeRealization
    {
        /// <summary>
        /// Initializes an empty failure-mode realization.
        /// </summary>
        public FailureModeRealization()
        {
            Curves = new Curves();
        }

        /// <summary>
        /// The failure mode's display name, carried for results labeling.
        /// </summary>
        public string Name { get; set; } = "Failure Mode Risk";

        /// <summary>
        /// The five loss exceedance curve streams.
        /// </summary>
        public Curves Curves { get; set; }

        /// <summary>
        /// Post-processes the recorded hazard probabilities into masses (one-dimensional path only).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a stream's mass budget does not telescope to one.</exception>
        public void ProcessHazardProbabilities()
        {
            Curves.ProcessHazardProbabilities();
        }

        /// <summary>
        /// Builds the exact curves and moments.
        /// </summary>
        /// <param name="outputLength">The output resolution of the stored curves.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the output length is less than two.</exception>
        public void CreateCurves(int outputLength)
        {
            Curves.CreateCurves(outputLength);
        }

        /// <summary>
        /// Builds the hazard profiles.
        /// </summary>
        public void CreateProfiles()
        {
            Curves.CreateProfiles();
        }

        /// <summary>
        /// Computes the risk-measure catalog.
        /// </summary>
        /// <param name="consequenceThreshold">The consequence threshold for the assurance measure.</param>
        /// <param name="alpha">The exceedance level for value-at-risk and conditional value-at-risk.</param>
        /// <param name="hazardThreshold">The hazard threshold, or NaN when none applies.</param>
        public void ComputeRiskMeasures(double consequenceThreshold, double alpha, double hazardThreshold = double.NaN)
        {
            Curves.ComputeRiskMeasures(consequenceThreshold, alpha, hazardThreshold);
        }

        /// <summary>
        /// Clears the recorded risk points — call only after post-processing.
        /// </summary>
        public void DumpMemory()
        {
            Curves.DumpMemory();
        }
    }
}
