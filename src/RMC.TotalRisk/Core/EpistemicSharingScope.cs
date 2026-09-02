using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Numerics.Sampling;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// The ambient shared-epistemic-variable scope. While a scope is entered on the current
    /// thread, every epistemic-mixture composite whose <c>EpistemicVariable</c> names one of the
    /// scope's variables overwrites its branch-selector column with the variable's shared draw
    /// during <c>SetupSampler</c>, so every binder of one variable selects the same branch draw
    /// per realization — the state-of-knowledge-correlation requirement of a logic tree.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// An ambient thread-static scope rather than a threaded parameter for the same reason the
    /// tree compilation scope is: <c>SetupSampler(int, int, SamplingScheme)</c> is the closed
    /// cluster contract, and a bound composite nested inside another composite is seeded by its
    /// parent's recursion, which no walk-level parameter can reach. Seeding is single-threaded at
    /// run start, so the thread-static is race-free by construction; a <c>SetupSampler</c> call
    /// with no scope entered (a standalone function, a clone summary) leaves the composite's own
    /// content-seeded selector untouched — binding without a scope degrades to an independent
    /// selection, never an error.
    /// </para>
    /// <para>
    /// The shared draw is applied by overwriting the selector column <i>after</i> the composite's
    /// own sampler is seeded (<c>RiskFunctionBase.OverrideSelectorColumn</c>, the fractile-pin
    /// pattern), so the share is seed-inert: no walk ordinal is added, no other function's stream
    /// moves, and captured sampler seed maps stay valid.
    /// </para>
    /// <para>
    /// Each variable's column derives from the scope's base seed and the variable name alone —
    /// <c>HashCombine(baseSeed, SHA-256(UTF-8(name)), 0)</c> through the run's sampling scheme —
    /// so binders agree no matter where they sit, renaming a variable deliberately re-rolls its
    /// draw (the name is the variable's identity), and two scopes with the same base seed and
    /// realization count reproduce bit-identically.
    /// </para>
    /// </remarks>
    internal static class EpistemicSharingScope
    {
        /// <summary>
        /// The columns of the currently entered scope, keyed by variable name; null when no scope
        /// is active on this thread.
        /// </summary>
        [ThreadStatic]
        private static Dictionary<string, double[]>? _current;

        /// <summary>
        /// True when a scope is entered on the current thread.
        /// </summary>
        internal static bool IsActive => _current != null;

        /// <summary>
        /// Looks up the shared selector column for a variable in the current scope.
        /// </summary>
        /// <param name="variable">The variable name.</param>
        /// <returns>The shared column, or null when no scope is active or the scope does not
        /// carry the variable.</returns>
        internal static double[]? TryGetColumn(string variable)
        {
            var current = _current;
            if (current == null || string.IsNullOrEmpty(variable)) return null;
            return current.TryGetValue(variable, out var column) ? column : null;
        }

        /// <summary>
        /// Enters a scope holding the given columns, returning the token that restores the prior
        /// scope on dispose (scopes nest by save-and-restore).
        /// </summary>
        /// <param name="columns">The shared columns, keyed by variable name.</param>
        /// <returns>The restoration token.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the map is null.</exception>
        internal static IDisposable Enter(Dictionary<string, double[]> columns)
        {
            if (columns == null) throw new ArgumentNullException(nameof(columns));
            var previous = _current;
            _current = columns;
            return new Restorer(previous);
        }

        /// <summary>
        /// Generates one shared selector column per variable from the base seed under the given
        /// scheme — the single derivation both the analysis run and the standalone component path
        /// use, differing only in the base seed they hand in.
        /// </summary>
        /// <param name="variables">The distinct variable names.</param>
        /// <param name="baseSeed">The deriving seed (the run's PRNG seed at analysis scope; the
        /// component seed on the standalone path).</param>
        /// <param name="sampleSize">The realization count.</param>
        /// <param name="scheme">The run's sampling scheme.</param>
        /// <returns>The columns, keyed by variable name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the variable set is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the sample size is not positive.</exception>
        /// <exception cref="NotSupportedException">Thrown when the sampling scheme is unrecognized.</exception>
        internal static Dictionary<string, double[]> BuildColumns(
            IEnumerable<string> variables, int baseSeed, int sampleSize, SamplingScheme scheme)
        {
            if (variables == null) throw new ArgumentNullException(nameof(variables));
            if (sampleSize <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSize), "The sample size must be positive.");

            var columns = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (string variable in variables)
            {
                if (string.IsNullOrEmpty(variable) || columns.ContainsKey(variable)) continue;
                int seed = SeedHelpers.ToPositiveSeed(SeedHelpers.HashCombine(baseSeed, HashVariableName(variable), 0));
                double[,] matrix = scheme switch
                {
                    SamplingScheme.LatinHypercube => LatinHypercube.Random(sampleSize, 1, seed),
                    SamplingScheme.LatinHypercubeMedian => LatinHypercube.Median(sampleSize, 1, seed),
                    SamplingScheme.MonteCarlo => SeedHelpers.IndependentUniform(sampleSize, 1, seed),
                    SamplingScheme.ScrambledSobol => SeedHelpers.ScrambledSobol(sampleSize, 1, seed),
                    _ => throw new NotSupportedException($"The sampling scheme '{scheme}' is not supported."),
                };
                var column = new double[sampleSize];
                for (int i = 0; i < sampleSize; i++)
                {
                    column[i] = matrix[i, 0];
                }
                columns.Add(variable, column);
            }
            return columns;
        }

        /// <summary>
        /// The content hash of a variable name — the identity a shared column derives from.
        /// </summary>
        /// <param name="variable">The variable name.</param>
        /// <returns>The SHA-256 of the name's UTF-8 bytes.</returns>
        private static byte[] HashVariableName(string variable)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(variable));
        }

        /// <summary>
        /// The scope-restoration token <see cref="Enter"/> hands out.
        /// </summary>
        private sealed class Restorer : IDisposable
        {
            /// <summary>
            /// The scope to restore on dispose (null when the scope being entered was outermost).
            /// </summary>
            private readonly Dictionary<string, double[]>? _previous;

            /// <summary>
            /// True once disposed, making a double dispose inert.
            /// </summary>
            private bool _disposed;

            /// <summary>
            /// Captures the scope to restore.
            /// </summary>
            /// <param name="previous">The prior scope.</param>
            public Restorer(Dictionary<string, double[]>? previous)
            {
                _previous = previous;
            }

            /// <summary>
            /// Restores the prior scope.
            /// </summary>
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _current = _previous;
            }
        }
    }
}
