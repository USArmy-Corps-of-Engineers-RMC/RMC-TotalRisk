using System;

namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// Options for a tree node-importance analysis: the single authored hazard level to analyze,
    /// the Monte Carlo iteration count, and the base seed. The analysis is deterministic — the
    /// same options against the same tree content always produce bit-identical results.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class TreeNodeImportanceOptions
    {
        /// <summary>Initializes importance options for one authored hazard level.</summary>
        /// <param name="hazardLevel">
        /// The hazard level to analyze. It must exactly equal one of the tree response's authored
        /// hazard levels; <see cref="TreeNodeImportance.Compute(EventTrees.EventTreeResponse, TreeNodeImportanceOptions)"/>
        /// rejects any other value.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the hazard level is not finite.</exception>
        public TreeNodeImportanceOptions(double hazardLevel)
        {
            if (!double.IsFinite(hazardLevel))
                throw new ArgumentOutOfRangeException(nameof(hazardLevel), "The hazard level must be finite.");
            HazardLevel = hazardLevel;
        }

        /// <summary>The iteration-count backing field.</summary>
        private int _iterations = 1000;

        /// <summary>The authored hazard level to analyze.</summary>
        public double HazardLevel { get; }

        /// <summary>
        /// The Monte Carlo iteration count for both the joint pass and the one-at-a-time pass.
        /// At least two iterations are required for the variance statistics.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less than two.</exception>
        public int Iterations
        {
            get { return _iterations; }
            set
            {
                if (value < 2)
                    throw new ArgumentOutOfRangeException(nameof(value), "At least two iterations are required.");
                _iterations = value;
            }
        }

        /// <summary>
        /// The base seed. Each pass derives an independent stream from this seed, the tree's
        /// canonical content hash, and the pass index.
        /// </summary>
        public int Seed { get; set; } = 12345;
    }
}
