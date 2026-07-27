using System;

namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// Selects which of the optional risk measures the engine computes.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The mean, standard deviation, total probability, and mass balance are never optional — they
    /// are the engine's contract and every aggregation depends on them. Everything here is
    /// computed per stream, per consequence type, and per failure mode of every realization, so on
    /// a long ensemble the cost is real; a measure switched off stores <see cref="double.NaN"/>,
    /// the same value the containers already use for "not computed".
    /// </para>
    /// </remarks>
    [Flags]
    public enum RiskMeasureOptions
    {
        /// <summary>No optional measures.</summary>
        None = 0,

        /// <summary>Skewness and kurtosis of each stream's consequence distribution.</summary>
        HigherMoments = 1,

        /// <summary>Value at risk and conditional value at risk at the configured exceedance level.</summary>
        ValueAtRisk = 2,

        /// <summary>The consequence- and hazard-threshold exceedance probabilities.</summary>
        ThresholdProbabilities = 4,

        /// <summary>The cumulative failure-probability, expected-consequence, and system-response profiles.</summary>
        RiskProfiles = 8,

        /// <summary>The per-failure-mode and per-component contributions to risk.</summary>
        Contributions = 16,

        /// <summary>Every optional measure — the default.</summary>
        All = HigherMoments | ValueAtRisk | ThresholdProbabilities | RiskProfiles | Contributions,
    }
}
