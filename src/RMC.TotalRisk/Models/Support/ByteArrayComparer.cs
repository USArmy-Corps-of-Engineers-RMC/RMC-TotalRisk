using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Models.Support
{
    /// <summary>
    /// Lexicographic comparer for byte arrays — used to sort canonical content hashes when
    /// assigning occurrence indices to identical-content components.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The canonical ordering of system components is "sort by <c>CanonicalHash()</c>, then by
    /// declared array index for ties" (architecture doc §5.5.4). This comparer supplies the first
    /// key: an ordinal, element-by-element comparison with shorter-prefix arrays ordered first,
    /// matching <see cref="MemoryExtensions.SequenceCompareTo{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>.
    /// </para>
    /// </remarks>
    public sealed class ByteArrayComparer : IComparer<byte[]>
    {
        /// <summary>
        /// The shared singleton instance. The comparer is stateless and thread-safe.
        /// </summary>
        public static readonly ByteArrayComparer Instance = new ByteArrayComparer();

        /// <summary>
        /// Initializes the comparer. Private — use <see cref="Instance"/>.
        /// </summary>
        private ByteArrayComparer()
        {
        }

        /// <summary>
        /// Compares two byte arrays lexicographically.
        /// </summary>
        /// <param name="x">The first array; null sorts before any non-null array.</param>
        /// <param name="y">The second array; null sorts before any non-null array.</param>
        /// <returns>
        /// A negative value when <paramref name="x"/> precedes <paramref name="y"/>, zero when they
        /// are equal element-by-element (or reference-equal), and a positive value otherwise.
        /// </returns>
        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return x.AsSpan().SequenceCompareTo(y);
        }
    }
}
