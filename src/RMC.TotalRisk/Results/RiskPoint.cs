using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// A single risk evaluation point recorded by the adaptive integrator: the hazard level, its
    /// probability coordinates, and the parallel response-probability / consequence entry lists.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The parallel lists are the point's entries: entry <c>i</c> contributes probability mass
    /// <c>HazardProbabilityMass · ResponseProbabilities[i]</c> at consequence
    /// <c>Consequences[i]</c>. A failure mode records one entry per exposure branch (ratified Q-V)
    /// and a component pathway records one entry per branch combination, so the full set of
    /// <c>(mass, consequence)</c> pairs across all points is the exact discretized loss
    /// distribution the curve is built from. Risk points are runtime working state — never
    /// serialized; realizations clear them via <c>DumpMemory()</c> after post-processing.
    /// </para>
    /// </remarks>
    public class RiskPoint
    {
        /// <summary>
        /// Initializes an empty risk point.
        /// </summary>
        public RiskPoint()
        {
            ResponseProbabilities = new List<double>();
            Consequences = new List<double>();
        }

        /// <summary>
        /// Initializes an empty risk point with entry-list capacity pre-allocated — the engine's
        /// per-evaluation path, which knows its branch count up front.
        /// </summary>
        /// <param name="capacity">The expected number of entries. Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the capacity is negative.</exception>
        public RiskPoint(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity), "The capacity must not be negative.");
            ResponseProbabilities = new List<double>(capacity);
            Consequences = new List<double>(capacity);
        }

        /// <summary>
        /// The hazard level where the risk was evaluated.
        /// </summary>
        public double HazardLevel { get; set; }

        /// <summary>
        /// The hazard level non-exceedance probability, P[X ≤ x].
        /// </summary>
        public double HazardProbability { get; set; }

        /// <summary>
        /// The driving hazard's annual exceedance probability at the evaluation, P[X > x] from
        /// the realization's sampled hazard distribution — the X coordinate of the system
        /// response probability profile (Phase 6.6). NaN when the recording path does not supply
        /// it (the profile is then skipped). Unlike <see cref="HazardLevel"/>, this coordinate is
        /// never remapped by the profile-axis selection: exceedance probability is the
        /// normalized, transform-independent axis.
        /// </summary>
        public double HazardExceedanceProbability { get; set; } = double.NaN;

        /// <summary>
        /// The hazard probability mass, dF(x) — the quadrature weight this point carries. Recorded
        /// directly on the VEGAS path; credited from the adaptive Gauss–Kronrod ledger by
        /// <c>Curve.ApplyRecordedMass()</c> on the one-dimensional path.
        /// </summary>
        public double HazardProbabilityMass { get; set; }

        /// <summary>
        /// The response probabilities P[F|x] of the point's entries, parallel to
        /// <see cref="Consequences"/>.
        /// </summary>
        public List<double> ResponseProbabilities { get; set; }

        /// <summary>
        /// The consequences C(x) of the point's entries, parallel to
        /// <see cref="ResponseProbabilities"/>.
        /// </summary>
        public List<double> Consequences { get; set; }

        /// <summary>
        /// Appends a response-probability / consequence entry.
        /// </summary>
        /// <param name="responseProbability">The entry's response probability, P[F|x].</param>
        /// <param name="consequence">The entry's consequence given the response, C(x).</param>
        public void Add(double responseProbability, double consequence)
        {
            ResponseProbabilities.Add(responseProbability);
            Consequences.Add(consequence);
        }

        /// <summary>
        /// Computes the point's total probability and expected consequence contributions,
        /// Σ mass·pᵢ and Σ mass·pᵢ·cᵢ over the entries.
        /// </summary>
        /// <param name="totalProbability">Receives the total probability contribution.</param>
        /// <param name="expectedConsequences">Receives the expected consequence contribution.</param>
        public void SummaryStatistics(out double totalProbability, out double expectedConsequences)
        {
            totalProbability = 0d;
            expectedConsequences = 0d;
            for (int i = 0; i < ResponseProbabilities.Count; i++)
            {
                totalProbability += HazardProbabilityMass * ResponseProbabilities[i];
                expectedConsequences += HazardProbabilityMass * ResponseProbabilities[i] * Consequences[i];
            }
        }
    }
}
