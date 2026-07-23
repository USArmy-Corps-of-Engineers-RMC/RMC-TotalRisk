using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The per-realization results of one failure mode: its five loss exceedance curve streams
    /// per consequence type.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The multi-consequence axis (Phase 6.5, Q-U closure): <see cref="Curves"/> carries the
    /// primary consequence type (position 0 of the analysis's declared axis) and
    /// <see cref="AdditionalCurves"/> entry k − 1 carries type k, in declared order — the same
    /// append-only convention every results container uses, so single-type results are
    /// shape-identical to earlier phases. The failure-mode scope is the per-consequence-terminal
    /// scope: under the ratified cascade design (Phase 6.7) each terminal is one end state, so
    /// this container is already the end-state results scope.
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
            AdditionalCurves = new List<Curves>();
        }

        /// <summary>
        /// The failure mode's display name, carried for results labeling.
        /// </summary>
        public string Name { get; set; } = "Failure Mode Risk";

        /// <summary>
        /// The five loss exceedance curve streams of the primary consequence type.
        /// </summary>
        public Curves Curves { get; set; }

        /// <summary>
        /// The five-stream curve sets of the additional consequence types, in declared order
        /// (entry k − 1 is type k). Empty on a single-type analysis.
        /// </summary>
        public List<Curves> AdditionalCurves { get; set; }

        /// <summary>
        /// Ensures the additional consequence-type slots exist (one curve set per type beyond
        /// the primary), creating any missing entries.
        /// </summary>
        /// <param name="count">The number of additional consequence types. Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
        public void EnsureAdditionalCurves(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "The additional consequence-type count must not be negative.");
            while (AdditionalCurves.Count < count)
            {
                AdditionalCurves.Add(new Curves());
            }
        }

        /// <summary>
        /// Post-processes the recorded hazard probabilities into masses on every consequence
        /// type (one-dimensional path only).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a stream's mass budget does not telescope to one.</exception>
        public void ProcessHazardProbabilities()
        {
            Curves.ProcessHazardProbabilities();
            for (int k = 0; k < AdditionalCurves.Count; k++)
            {
                AdditionalCurves[k].ProcessHazardProbabilities();
            }
        }

        /// <summary>
        /// Builds the exact curves and moments on every consequence type.
        /// </summary>
        /// <param name="outputLength">The output resolution of the stored curves.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the output length is less than two.</exception>
        public void CreateCurves(int outputLength)
        {
            Curves.CreateCurves(outputLength);
            for (int k = 0; k < AdditionalCurves.Count; k++)
            {
                AdditionalCurves[k].CreateCurves(outputLength);
            }
        }

        /// <summary>
        /// Builds the hazard profiles on every consequence type.
        /// </summary>
        public void CreateProfiles()
        {
            Curves.CreateProfiles();
            for (int k = 0; k < AdditionalCurves.Count; k++)
            {
                AdditionalCurves[k].CreateProfiles();
            }
        }

        /// <summary>
        /// Computes the risk-measure catalog on every consequence type. The consequence
        /// threshold applies to the primary type only — it is declared in the primary type's
        /// units, so the additional types compute with a NaN threshold (assurance NaN; per-type
        /// thresholds land with the risk-measures phase).
        /// </summary>
        /// <param name="consequenceThreshold">The consequence threshold for the primary type's assurance measure.</param>
        /// <param name="alpha">The exceedance level for value-at-risk and conditional value-at-risk.</param>
        /// <param name="hazardThreshold">The hazard threshold, or NaN when none applies.</param>
        public void ComputeRiskMeasures(double consequenceThreshold, double alpha, double hazardThreshold = double.NaN)
        {
            Curves.ComputeRiskMeasures(consequenceThreshold, alpha, hazardThreshold);
            for (int k = 0; k < AdditionalCurves.Count; k++)
            {
                AdditionalCurves[k].ComputeRiskMeasures(double.NaN, alpha, hazardThreshold);
            }
        }

        /// <summary>
        /// Clears the recorded risk points on every consequence type — call only after
        /// post-processing.
        /// </summary>
        public void DumpMemory()
        {
            Curves.DumpMemory();
            for (int k = 0; k < AdditionalCurves.Count; k++)
            {
                AdditionalCurves[k].DumpMemory();
            }
        }

        /// <summary>
        /// Scales the recorded risk-point masses on every consequence type (the joint system
        /// path's VEGAS weight self-normalization).
        /// </summary>
        /// <param name="factor">The positive scale factor.</param>
        internal void ScaleRecordedMass(double factor)
        {
            Curves.ScaleRecordedMass(factor);
            for (int k = 0; k < AdditionalCurves.Count; k++)
            {
                AdditionalCurves[k].ScaleRecordedMass(factor);
            }
        }
    }
}
