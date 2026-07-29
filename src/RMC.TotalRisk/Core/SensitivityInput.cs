using System;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// One labeled knowledge-input column of the sensitivity engine: a display label and a
    /// reader over the input's sampled percentile row — a per-function-dimension
    /// draw or a failure mode's consequence-coupling column. Runtime-only; built by the
    /// component's sampler walk so labels and columns can never drift from the seeded streams.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    internal sealed class SensitivityInput
    {
        /// <summary>
        /// Initializes a labeled input column.
        /// </summary>
        /// <param name="label">The display label (component/function role naming).</param>
        /// <param name="read">Reads the column's percentile for a realization index.</param>
        /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
        public SensitivityInput(string label, Func<int, double> read)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Read = read ?? throw new ArgumentNullException(nameof(read));
        }

        /// <summary>
        /// The display label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Reads the column's sampled percentile for a realization index.
        /// </summary>
        public Func<int, double> Read { get; }
    }
}
