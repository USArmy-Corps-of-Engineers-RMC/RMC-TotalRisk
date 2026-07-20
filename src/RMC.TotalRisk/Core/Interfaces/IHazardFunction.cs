using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// The hazard (frequency-distribution) input-function contract: a sampled hazard function is a
    /// univariate distribution of the hazard variable (e.g., peak flow, stage), and risk analysis
    /// integrates expected consequences over its probability axis.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The v1.0 domain surface is preserved verbatim: the mean and percentile
    /// <c>SampleFunction</c> overloads and the <c>meanOnly</c>-qualified hazard bounds. The
    /// realization-index overload is the v1.1 addition backing per-function Latin hypercube
    /// sampling (architecture doc §5.8).
    /// </para>
    /// </remarks>
    public interface IHazardFunction : IRiskFunction
    {
        /// <summary>
        /// The concrete kind of this hazard function. A runtime discriminator for callers that
        /// branch on function kind; never serialized and never hashed.
        /// </summary>
        HazardFunctionType FunctionType { get; }

        /// <summary>
        /// Samples the mean hazard function — the expected (mean) frequency curve across the
        /// function's knowledge uncertainty. Deterministic functions return their single curve.
        /// </summary>
        /// <returns>The mean hazard distribution.</returns>
        IUnivariateDistribution SampleFunction();

        /// <summary>
        /// Samples the hazard function at a fixed knowledge-uncertainty percentile.
        /// </summary>
        /// <param name="percentile">The percentile in (0, 1) driving the co-monotonic sample.</param>
        /// <returns>The sampled hazard distribution.</returns>
        IUnivariateDistribution SampleFunction(double percentile);

        /// <summary>
        /// Samples the hazard function for the given realization, reading this function's
        /// pre-allocated percentile row (or posterior index). <see cref="IRiskFunction.SetupSampler"/>
        /// must be called first.
        /// </summary>
        /// <param name="realizationIndex">The realization index in [0, sample size).</param>
        /// <returns>The sampled hazard distribution.</returns>
        IUnivariateDistribution SampleFunction(int realizationIndex);

        /// <summary>
        /// The minimum hazard value the function can produce.
        /// </summary>
        /// <param name="meanOnly">
        /// When true, the bound of the mean curve only; when false, the bound across the
        /// function's full knowledge uncertainty.
        /// </param>
        /// <returns>The minimum hazard value.</returns>
        double MinHazard(bool meanOnly);

        /// <summary>
        /// The maximum hazard value the function can produce.
        /// </summary>
        /// <param name="meanOnly">
        /// When true, the bound of the mean curve only; when false, the bound across the
        /// function's full knowledge uncertainty.
        /// </param>
        /// <returns>The maximum hazard value.</returns>
        double MaxHazard(bool meanOnly);
    }
}
