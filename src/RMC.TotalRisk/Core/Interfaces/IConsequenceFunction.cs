using System.Collections.Generic;
using Numerics.Functions;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// The consequence input-function contract: a sampled consequence function maps hazard to
    /// consequence magnitude (life loss, damages) and is evaluated inside the risk integrand for
    /// both failure and non-failure branches.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The v1.0 domain surface is preserved verbatim: consequence axis labels, the mean and
    /// percentile <c>SampleFunction</c> overloads returning <see cref="IUnivariateFunction"/>, and
    /// the hazard bounds. The realization-index overload is the v1.1 sampler addition.
    /// </para>
    /// <para>
    /// The exposure-branch surface (<see cref="SampleExposureBranches()"/> and friends) is the
    /// ratified Q-V contract (architecture doc §6.4.1): mixture weights are aleatory exposure
    /// probabilities (which day/night state occurs), not knowledge uncertainty, so the risk engine
    /// enumerates the weighted branches at every hazard point — in the mean-only and full Monte
    /// Carlo paths alike — instead of collapsing a mixture to its weighted-mean curve. Collapsing
    /// keeps the mean algebraically unchanged but destroys the loss-exceedance tail (the v1.0
    /// day/night defect). Non-composite functions are a single unit-weight branch, so nothing else
    /// changes shape.
    /// </para>
    /// </remarks>
    public interface IConsequenceFunction : IRiskFunction
    {
        /// <summary>
        /// The concrete kind of this consequence function. A runtime discriminator for callers that
        /// branch on function kind; never serialized and never hashed.
        /// </summary>
        ConsequenceFunctionType FunctionType { get; }

        /// <summary>
        /// The consequence type this function's output axis represents (e.g., "Life Loss",
        /// "Damages"). Axis-label metadata — serialized, never hashed.
        /// </summary>
        string SpecifiedConsequence { get; set; }

        /// <summary>
        /// The unit of the consequence axis (e.g., "lives", "$"). Axis-label metadata — serialized,
        /// never hashed.
        /// </summary>
        string ConsequenceUnit { get; set; }

        /// <summary>
        /// Samples the mean consequence function — the expected function across knowledge
        /// uncertainty.
        /// </summary>
        /// <returns>The mean consequence function.</returns>
        IUnivariateFunction SampleFunction();

        /// <summary>
        /// Samples the consequence function at a fixed knowledge-uncertainty percentile.
        /// </summary>
        /// <param name="percentile">The percentile in (0, 1) driving the co-monotonic sample.</param>
        /// <returns>The sampled consequence function.</returns>
        IUnivariateFunction SampleFunction(double percentile);

        /// <summary>
        /// Samples the consequence function for the given realization, reading this function's
        /// pre-allocated percentile row. <see cref="IRiskFunction.SetupSampler"/> must be called
        /// first.
        /// </summary>
        /// <param name="realizationIndex">The realization index in [0, sample size).</param>
        /// <returns>The sampled consequence function.</returns>
        IUnivariateFunction SampleFunction(int realizationIndex);

        /// <summary>
        /// Samples the weighted exposure branches of the mean consequence function — one entry per
        /// aleatory exposure state, with weights summing to one.
        /// </summary>
        /// <returns>
        /// The weighted branches. Non-composite functions (and additive/average composites, which
        /// are genuine pointwise combinations rather than exposure states) return a single entry
        /// with weight one; a mixture composite returns one entry per positively weighted child,
        /// with nested mixtures flattened by multiplied weights.
        /// </returns>
        IReadOnlyList<(double Weight, IUnivariateFunction Function)> SampleExposureBranches();

        /// <summary>
        /// Samples the weighted exposure branches at a fixed knowledge-uncertainty percentile: the
        /// branch set is structural (weights identical to <see cref="SampleExposureBranches()"/>),
        /// and every branch curve is sampled co-monotonically at the given percentile — the same
        /// shared draw that couples paired failure and non-failure consequences (Q-N).
        /// </summary>
        /// <param name="percentile">The percentile in (0, 1) driving every branch's co-monotonic sample.</param>
        /// <returns>The weighted branches at the given knowledge percentile.</returns>
        IReadOnlyList<(double Weight, IUnivariateFunction Function)> SampleExposureBranches(double percentile);

        /// <summary>
        /// Counts the exposure branches <see cref="SampleExposureBranches()"/> would return,
        /// without sampling — the structural input to the engine's branch-explosion guardrails
        /// (warn above 64 combined branches per failure mode, error above 1024).
        /// </summary>
        /// <returns>The branch count (at least one for a usable function).</returns>
        int CountExposureBranches();

        /// <summary>
        /// The minimum hazard value the consequence function is defined for.
        /// </summary>
        /// <returns>The minimum hazard.</returns>
        double MinHazard();

        /// <summary>
        /// The maximum hazard value the consequence function is defined for.
        /// </summary>
        /// <returns>The maximum hazard.</returns>
        double MaxHazard();
    }
}
