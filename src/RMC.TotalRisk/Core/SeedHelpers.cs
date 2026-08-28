using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Numerics.Sampling;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// Deterministic seed-derivation and sampling helpers backing the content-based seeding
    /// contract: Monte Carlo seeds derive from SHA-256 canonical content hashes plus structural
    /// indices, never from names, canvas positions, or declaration order.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The engine combines the analysis seed with each component's canonical hash and occurrence
    /// index (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §5.5.4), and each component
    /// combines its own seed with each owned
    /// function's canonical hash and structural ordinal (§5.8.7) — both through
    /// <see cref="HashCombine(int, byte[], int)"/>. The SHA-256 mix guarantees that any change to
    /// any input produces an unrelated seed, while identical inputs always reproduce the same seed
    /// on every machine and thread count.
    /// </para>
    /// </remarks>
    public static class SeedHelpers
    {
        /// <summary>
        /// Deterministically combines a seed, a canonical content hash, and a structural index into
        /// a derived PRNG seed.
        /// </summary>
        /// <param name="seed">The parent seed (e.g., the analysis PRNG seed or a component seed).</param>
        /// <param name="contentHash">The canonical content hash of the target object.</param>
        /// <param name="index">
        /// The structural index disambiguating identical-content peers: the occurrence index for
        /// components, or the function ordinal within a component's sampler walk.
        /// </param>
        /// <returns>The derived 32-bit seed (little-endian read of the first four digest bytes).</returns>
        /// <exception cref="ArgumentNullException">Thrown when the content hash is null.</exception>
        public static int HashCombine(int seed, byte[] contentHash, int index)
        {
            if (contentHash == null) throw new ArgumentNullException(nameof(contentHash));

            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(buffer[..4], seed);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], index);

            using var sha = SHA256.Create();
            byte[] head = buffer.ToArray();
            sha.TransformBlock(head, 0, head.Length, null, 0);
            sha.TransformFinalBlock(contentHash, 0, contentHash.Length);
            return BinaryPrimitives.ReadInt32LittleEndian(sha.Hash.AsSpan(0, 4));
        }

        /// <summary>
        /// Maps any 32-bit seed onto [1, int.MaxValue] deterministically.
        /// </summary>
        /// <param name="seed">The seed, possibly zero or negative (content-derived seeds span the full int range).</param>
        /// <returns>An equivalent strictly positive seed.</returns>
        /// <remarks>
        /// The Numerics Latin hypercube samplers treat a non-positive seed as "use the wall clock",
        /// which would silently destroy reproducibility — every content-derived seed from
        /// <see cref="HashCombine(int, byte[], int)"/> must be folded into the positive range before
        /// reaching any Numerics sampler. Public because the fold is applied by function samplers,
        /// the failure-mode coupling matrix, and the engine's per-realization VEGAS seeds alike;
        /// one implementation keeps every consumer bit-identical.
        /// </remarks>
        public static int ToPositiveSeed(int seed)
        {
            return (int)((uint)seed % int.MaxValue) + 1;
        }

        /// <summary>
        /// Fills an N×D matrix with independent uniform draws — the
        /// <see cref="SamplingScheme.MonteCarlo"/> fallback that preserves legacy v1.0 sampling
        /// behavior behind the same matrix shape the Latin hypercube schemes use.
        /// </summary>
        /// <param name="sampleSize">The number of realizations N (rows). Must be positive.</param>
        /// <param name="dimension">The sampling dimension D (columns). Must be positive.</param>
        /// <param name="seed">
        /// The PRNG seed. Always pass an explicit positive seed — the Mersenne Twister stream is
        /// what makes the matrix reproducible.
        /// </param>
        /// <returns>An N×D matrix of independent uniform (0, 1) draws.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="sampleSize"/> or <paramref name="dimension"/> is not positive.
        /// </exception>
        /// <remarks>
        /// Draws are generated row-major from a single <see cref="MersenneTwister"/> stream, so the
        /// same seed always produces the same matrix at any thread count.
        /// </remarks>
        public static double[,] IndependentUniform(int sampleSize, int dimension, int seed)
        {
            if (sampleSize <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSize), "The sample size must be positive.");
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension), "The dimension must be positive.");

            var prng = new MersenneTwister(seed);
            var matrix = new double[sampleSize, dimension];
            for (int i = 0; i < sampleSize; i++)
            {
                for (int j = 0; j < dimension; j++)
                {
                    matrix[i, j] = prng.NextDouble();
                }
            }
            return matrix;
        }

        /// <summary>
        /// Fills an N×D percentile matrix from one seeded Matousek-scrambled Sobol sequence —
        /// the <see cref="SamplingScheme.ScrambledSobol"/> generator behind the same matrix
        /// shape the other schemes use.
        /// </summary>
        /// <param name="sampleSize">The number of realizations N (rows). Must be positive.</param>
        /// <param name="dimension">The sampling dimension D (columns). Must be positive.</param>
        /// <param name="seed">
        /// The scramble seed. Always pass an explicit positive seed — the seeded scrambling is
        /// what makes the quasi-random matrix reproducible under the content-seed contract.
        /// </param>
        /// <returns>An N×D matrix of scrambled Sobol (0, 1) draws.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="sampleSize"/> or <paramref name="dimension"/> is not positive.
        /// </exception>
        /// <remarks>
        /// Row i is point i of one D-dimensional sequence, so a function's dimensions carry the
        /// sequence's joint equidistribution; different functions scramble independently through
        /// their independent content-derived seeds. Low-discrepancy stratification is strongest
        /// at power-of-two sample sizes (the dyadic property), which the analysis validation
        /// advises on.
        /// </remarks>
        public static double[,] ScrambledSobol(int sampleSize, int dimension, int seed)
        {
            if (sampleSize <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSize), "The sample size must be positive.");
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension), "The dimension must be positive.");

            var sequence = new SobolSequence(dimension, seed);
            var matrix = new double[sampleSize, dimension];
            for (int i = 0; i < sampleSize; i++)
            {
                double[] point = sequence.NextDouble();
                for (int j = 0; j < dimension; j++)
                {
                    matrix[i, j] = point[j];
                }
            }
            return matrix;
        }
    }
}
