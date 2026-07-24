using System;
using System.IO;
using System.Text.Json;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The compact per-realization summary ensemble a full-uncertainty run publishes: one
    /// <see cref="SystemRiskResults"/> per realization.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// One of the two persisted results roots (architecture doc §7.5): serialization is
    /// System.Text.Json through <see cref="ToJson"/>/<see cref="FromJson"/> and the
    /// GZip-compressed byte overloads — the v1.0 BinaryFormatter byte arrays are deliberately not
    /// readable in v1.1; old projects re-run their analyses. The indexer preserves the v1.0
    /// null-tolerant semantics: out-of-range reads return null and out-of-range writes are
    /// ignored, so percentile post-processing can probe without guarding.
    /// </para>
    /// </remarks>
    public class EnsembleResults
    {
        /// <summary>
        /// Backing field for <see cref="Realizations"/>.
        /// </summary>
        private SystemRiskResults?[] _realizations = Array.Empty<SystemRiskResults?>();

        /// <summary>
        /// Initializes an empty ensemble.
        /// </summary>
        public EnsembleResults()
        {
        }

        /// <summary>
        /// Initializes an ensemble sized for the given realization count.
        /// </summary>
        /// <param name="length">The realization count. Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the length is negative.</exception>
        public EnsembleResults(int length)
        {
            SetEnsembleLength(length);
        }

        /// <summary>
        /// The per-realization summaries — the serialized state. Assigning null coerces to empty.
        /// </summary>
        public SystemRiskResults?[] Realizations
        {
            get { return _realizations; }
            set { _realizations = value ?? Array.Empty<SystemRiskResults?>(); }
        }

        /// <summary>
        /// Gets or sets the summary at the given index with the v1.0 null-tolerant semantics:
        /// out-of-range reads return null; out-of-range writes are ignored.
        /// </summary>
        /// <param name="index">The zero-based realization index.</param>
        /// <returns>The summary, or null when absent or out of range.</returns>
        public SystemRiskResults? this[int index]
        {
            get
            {
                if (index < 0 || index >= _realizations.Length) return null;
                return _realizations[index];
            }
            set
            {
                if (index < 0 || index >= _realizations.Length) return;
                _realizations[index] = value;
            }
        }

        /// <summary>
        /// The number of realization slots.
        /// </summary>
        public int Count => _realizations.Length;

        /// <summary>
        /// The percentile confidence intervals on every scalar risk measure plus the aggregated
        /// convergence diagnostics (Phase 6.6), populated by the engine at the end of a
        /// full-uncertainty run and recomputable from any loaded ensemble via
        /// <see cref="ComputeSummary"/>. Null on older payloads and mean-only runs — "not
        /// computed" (append-only results JSON).
        /// </summary>
        public EnsembleSummary? Summary { get; set; }

        /// <summary>
        /// Computes the scalar-measure percentile summary and convergence diagnostics from the
        /// stored realizations — the same reduction the engine runs, available on any loaded
        /// ensemble (the stored <see cref="Summary"/> is not modified).
        /// </summary>
        /// <param name="confidenceIntervalWidth">The confidence-interval width, in (0, 1).</param>
        /// <returns>The summary, or null when the ensemble holds no realizations.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the width is outside (0, 1).</exception>
        public EnsembleSummary? ComputeSummary(double confidenceIntervalWidth)
        {
            return EnsembleSummary.Compute(this, confidenceIntervalWidth);
        }

        /// <summary>
        /// Resizes the ensemble, clearing all current summaries.
        /// </summary>
        /// <param name="length">The realization count. Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the length is negative.</exception>
        public void SetEnsembleLength(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "The ensemble length must not be negative.");
            _realizations = new SystemRiskResults?[length];
        }

        /// <summary>
        /// Serializes this ensemble to its JSON form.
        /// </summary>
        /// <returns>The JSON text.</returns>
        public string ToJson()
        {
            return ResultsJson.ToJson(this);
        }

        /// <summary>
        /// Restores an ensemble from its JSON form.
        /// </summary>
        /// <param name="json">The JSON text produced by <see cref="ToJson"/>.</param>
        /// <returns>The restored ensemble.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the JSON text is null.</exception>
        /// <exception cref="JsonException">Thrown when the text is not a serialized ensemble.</exception>
        public static EnsembleResults FromJson(string json)
        {
            return ResultsJson.FromJson<EnsembleResults>(json);
        }

        /// <summary>
        /// Serializes this ensemble to GZip-compressed UTF-8 JSON bytes.
        /// </summary>
        /// <returns>The compressed bytes.</returns>
        public byte[] ToCompressedBytes()
        {
            return ResultsJson.ToCompressedBytes(this);
        }

        /// <summary>
        /// Restores an ensemble from GZip-compressed UTF-8 JSON bytes.
        /// </summary>
        /// <param name="bytes">The bytes produced by <see cref="ToCompressedBytes"/>.</param>
        /// <returns>The restored ensemble.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the byte array is null.</exception>
        /// <exception cref="InvalidDataException">Thrown when the bytes are not a GZip stream.</exception>
        /// <exception cref="JsonException">Thrown when the decompressed text is not a serialized ensemble.</exception>
        public static EnsembleResults FromCompressedBytes(byte[] bytes)
        {
            return ResultsJson.FromCompressedBytes<EnsembleResults>(bytes);
        }
    }
}
