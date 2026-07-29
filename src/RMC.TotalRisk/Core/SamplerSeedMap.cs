using System;
using System.Collections.Generic;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// A captured snapshot of every sampler seed one analysis run resolved — the seed-stable
    /// perturbation mode (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §5.5.8): per
    /// component, the effective seed at each sampler
    /// walk ordinal (function positions and the failure modes' coupling positions), plus the
    /// joint system's VEGAS seed base. Pinning a captured map onto a perturbed model replays
    /// the identical Monte Carlo streams, so result deltas are pure parameter effects.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Content-based seeding intentionally re-rolls a function's stream when its numeric
    /// content changes — the right default for reproducibility, but noise on top of the signal
    /// in a perturbation study. Pinning must happen at the <i>function ordinal</i> level: a
    /// perturbed parameter moves that function's canonical hash, so even a pinned component
    /// seed would re-roll its percentile matrix; the map therefore stores the resolved seed per
    /// walk position. Runtime-only state — never serialized, never hashed, never part of any
    /// identity surface. A map only fits the model shape it was captured from: applying it
    /// validates the walk consumes exactly the captured ordinals per component (a perturbation
    /// that changes the walk shape — adding a mode, a function, or a consequence position — is
    /// not a "small perturbation" and faults loudly).
    /// </para>
    /// </remarks>
    public sealed class SamplerSeedMap
    {
        /// <summary>
        /// Initializes a captured map.
        /// </summary>
        /// <param name="componentSeeds">Per component (analysis order), the effective seed at each walk ordinal.</param>
        /// <param name="jointSeedBase">The joint system's VEGAS seed base.</param>
        /// <exception cref="ArgumentNullException">Thrown when the seed list is null.</exception>
        internal SamplerSeedMap(List<int[]> componentSeeds, int jointSeedBase)
        {
            ComponentSeeds = componentSeeds ?? throw new ArgumentNullException(nameof(componentSeeds));
            JointSeedBase = jointSeedBase;
        }

        /// <summary>
        /// Per component (analysis order), the effective seed at each sampler walk ordinal.
        /// </summary>
        internal List<int[]> ComponentSeeds { get; }

        /// <summary>
        /// The joint system's VEGAS seed base (the fold of every component's identity).
        /// </summary>
        internal int JointSeedBase { get; }

        /// <summary>
        /// The number of components the map was captured from.
        /// </summary>
        public int ComponentCount => ComponentSeeds.Count;
    }

    /// <summary>
    /// The sampler walk's seed scribe: captures each resolved seed by walk ordinal and, in
    /// pinned runs, overrides the content-derived seed with the captured one — the one
    /// mechanism serving both halves of the seed-stable perturbation mode
    /// (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §5.5.8).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    internal sealed class SeedScribe
    {
        /// <summary>
        /// The seeds to apply by ordinal, or null for a capture-only scribe.
        /// </summary>
        private readonly int[]? _apply;

        /// <summary>
        /// The captured effective seeds by ordinal (zero at positions the dedup rule skips).
        /// </summary>
        private readonly List<int> _captured = new List<int>();

        /// <summary>
        /// Initializes a scribe.
        /// </summary>
        /// <param name="apply">The pinned seeds by ordinal, or null to capture only.</param>
        public SeedScribe(int[]? apply)
        {
            _apply = apply;
        }

        /// <summary>
        /// Resolves the effective seed at a walk ordinal: the pinned seed when applying, the
        /// computed content-derived seed otherwise — recorded either way.
        /// </summary>
        /// <param name="ordinal">The walk ordinal.</param>
        /// <param name="computedSeed">The content-derived seed the walk computed.</param>
        /// <returns>The effective seed.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a pinned map runs out of ordinals — the model's walk shape changed since
        /// the capture, which a seed pin cannot bridge.
        /// </exception>
        public int Resolve(int ordinal, int computedSeed)
        {
            int seed = computedSeed;
            if (_apply != null)
            {
                if (ordinal >= _apply.Length)
                {
                    throw new InvalidOperationException(
                        "The pinned sampler seed map does not cover this model's sampler walk. The model's structure changed since the map was captured — re-capture from a baseline run of the current structure.");
                }
                seed = _apply[ordinal];
            }
            while (_captured.Count <= ordinal)
            {
                _captured.Add(0);
            }
            _captured[ordinal] = seed;
            return seed;
        }

        /// <summary>
        /// Validates a pinned map was consumed exactly and returns the captured seeds.
        /// </summary>
        /// <param name="finalOrdinal">The walk's final (exclusive) ordinal.</param>
        /// <returns>The captured effective seeds by ordinal.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a pinned map carries more ordinals than the walk consumed — the model's
        /// walk shape changed since the capture.
        /// </exception>
        public int[] Finish(int finalOrdinal)
        {
            if (_apply != null && _apply.Length != finalOrdinal)
            {
                throw new InvalidOperationException(
                    "The pinned sampler seed map does not match this model's sampler walk. The model's structure changed since the map was captured — re-capture from a baseline run of the current structure.");
            }
            while (_captured.Count < finalOrdinal)
            {
                _captured.Add(0);
            }
            return _captured.ToArray();
        }
    }
}
