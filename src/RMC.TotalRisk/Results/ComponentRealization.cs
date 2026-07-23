using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The per-realization results of one system component: its five loss exceedance curve
    /// streams, the per-failure-mode realizations, and the hazard/consequence extent tracking.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public class ComponentRealization
    {
        /// <summary>
        /// Initializes an empty component realization.
        /// </summary>
        public ComponentRealization()
        {
            Curves = new Curves();
            FailureModes = new List<FailureModeRealization>();
        }

        /// <summary>
        /// Initializes a component realization with the given number of empty failure-mode
        /// realizations.
        /// </summary>
        /// <param name="failureModes">The failure-mode count. Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
        public ComponentRealization(int failureModes)
        {
            if (failureModes < 0) throw new ArgumentOutOfRangeException(nameof(failureModes), "The failure-mode count must not be negative.");
            Curves = new Curves();
            FailureModes = new List<FailureModeRealization>(failureModes);
            for (int i = 0; i < failureModes; i++)
            {
                FailureModes.Add(new FailureModeRealization());
            }
        }

        /// <summary>
        /// The component's display name, carried for results labeling.
        /// </summary>
        public string Name { get; set; } = "Component Risk";

        /// <summary>
        /// The per-failure-mode realizations, in the component's projected failure-mode order.
        /// </summary>
        public List<FailureModeRealization> FailureModes { get; set; }

        /// <summary>
        /// The component-level five loss exceedance curve streams.
        /// </summary>
        public Curves Curves { get; set; }

        /// <summary>
        /// The smallest consequence observed for this component in this realization.
        /// </summary>
        public double MinN { get; set; } = double.MaxValue;

        /// <summary>
        /// The largest consequence observed for this component in this realization.
        /// </summary>
        public double MaxN { get; set; } = double.MinValue;

        /// <summary>
        /// The smallest hazard level observed for this component in this realization.
        /// </summary>
        public double MinH { get; set; } = double.MaxValue;

        /// <summary>
        /// The largest hazard level observed for this component in this realization.
        /// </summary>
        public double MaxH { get; set; } = double.MinValue;

        /// <summary>
        /// Post-processes the recorded hazard probabilities into masses on the component and every
        /// failure mode (one-dimensional path only).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a stream's mass budget does not telescope to one.</exception>
        public void ProcessHazardProbabilities()
        {
            Curves.ProcessHazardProbabilities();
            for (int i = 0; i < FailureModes.Count; i++)
            {
                FailureModes[i].ProcessHazardProbabilities();
            }
        }

        /// <summary>
        /// Builds the exact curves and moments on the component and every failure mode.
        /// </summary>
        /// <param name="outputLength">The output resolution of the stored curves.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the output length is less than two.</exception>
        public void CreateCurves(int outputLength)
        {
            Curves.CreateCurves(outputLength);
            for (int i = 0; i < FailureModes.Count; i++)
            {
                FailureModes[i].CreateCurves(outputLength);
            }
        }

        /// <summary>
        /// Builds the component-level hazard profiles. Failure-mode profiles are not built (v1.0
        /// behavior — the component profiles carry the reporting surface).
        /// </summary>
        public void CreateProfiles()
        {
            Curves.CreateProfiles();
        }

        /// <summary>
        /// Computes the risk-measure catalog on the component (with the hazard threshold) and on
        /// every failure mode (without — v1.0 behavior).
        /// </summary>
        /// <param name="consequenceThreshold">The consequence threshold for the assurance measure.</param>
        /// <param name="alpha">The exceedance level for value-at-risk and conditional value-at-risk.</param>
        /// <param name="hazardThreshold">The component hazard threshold, or NaN when none applies.</param>
        public void ComputeRiskMeasures(double consequenceThreshold, double alpha, double hazardThreshold = double.NaN)
        {
            Curves.ComputeRiskMeasures(consequenceThreshold, alpha, hazardThreshold);
            for (int i = 0; i < FailureModes.Count; i++)
            {
                FailureModes[i].ComputeRiskMeasures(consequenceThreshold, alpha);
            }
        }

        /// <summary>
        /// Scales the recorded risk-point masses on the component and every failure mode (the
        /// joint system path's VEGAS weight self-normalization).
        /// </summary>
        /// <param name="factor">The positive scale factor.</param>
        internal void ScaleRecordedMass(double factor)
        {
            Curves.ScaleRecordedMass(factor);
            for (int i = 0; i < FailureModes.Count; i++)
            {
                FailureModes[i].Curves.ScaleRecordedMass(factor);
            }
        }

        /// <summary>
        /// Clears the recorded risk points on the component and every failure mode — call only
        /// after post-processing.
        /// </summary>
        public void DumpMemory()
        {
            Curves.DumpMemory();
            for (int i = 0; i < FailureModes.Count; i++)
            {
                FailureModes[i].DumpMemory();
            }
        }
    }
}
