using System;
using Numerics;

namespace RMC.TotalRisk.RiskFunctions.Responses.FaultTrees
{
    /// <summary>
    /// Options for the exact fault-tree importance measures: the analyzed hazard level and the
    /// knowledge percentile the basic-event probabilities are evaluated at.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The default percentile (−1) evaluates every uncertain source at its mean — the
    /// deterministic baseline the exact measures are usually quoted at; a percentile strictly
    /// inside (0, 1) evaluates every source co-monotonically at that knowledge level instead.
    /// </para>
    /// </remarks>
    public sealed class FaultTreeImportanceOptions
    {
        /// <summary>
        /// Initializes the options.
        /// </summary>
        /// <param name="hazardLevel">The analyzed hazard level (must be an authored level of the response).</param>
        public FaultTreeImportanceOptions(double hazardLevel)
        {
            HazardLevel = hazardLevel;
        }

        /// <summary>
        /// The analyzed hazard level; must be one of the response's authored hazard levels.
        /// </summary>
        public double HazardLevel { get; }

        /// <summary>
        /// The knowledge percentile the basic-event probabilities are evaluated at: −1 (the
        /// default) selects every source's mean; a value strictly inside (0, 1) evaluates every
        /// source at that percentile.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside (0, 1) and not −1.</exception>
        public double Percentile
        {
            get { return _percentile; }
            set
            {
                if (value != -1d && (!Tools.IsFinite(value) || value <= 0d || value >= 1d))
                    throw new ArgumentOutOfRangeException(nameof(value), "The percentile must be −1 (the mean) or strictly inside (0, 1).");
                _percentile = value;
            }
        }

        /// <summary>
        /// Backing field for <see cref="Percentile"/>.
        /// </summary>
        private double _percentile = -1d;
    }
}
