using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// The response (fragility) input-function contract: a sampled response gives the probability
    /// of failure as a function of the (transformed) hazard, consumed either as an ordered curve or
    /// as a univariate distribution whose CDF is the system response probability.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The v1.0 domain surface is preserved verbatim — the richest of the four clusters: three
    /// <c>SampleResponseFunction</c> overloads returning <see cref="OrderedPairedData"/>, the
    /// distribution-form <c>SampleFunction</c> overloads, the monotonicity check, and the
    /// hazard/probability bounds. One documented v1.1 semantic change: the integer overloads take a
    /// REALIZATION INDEX into the pre-allocated percentile matrix
    /// (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §5.8) — v1.0's
    /// <c>SampleResponseFunction(int)</c> treated the integer as a PRNG seed.
    /// </para>
    /// </remarks>
    public interface IResponseFunction : IRiskFunction
    {
        /// <summary>
        /// The concrete kind of this response function. A runtime discriminator for callers that
        /// branch on function kind; never serialized and never hashed.
        /// </summary>
        ResponseFunctionType FunctionType { get; }

        /// <summary>
        /// Gets whether the three <c>SampleResponseFunction</c> overloads can emit an ordered
        /// hazard/failure-probability curve. Callers can inspect this capability without using an
        /// exception as feature discovery.
        /// </summary>
        bool SupportsOrderedCurveSampling { get; }

        /// <summary>
        /// Samples the mean response curve — each ordinate at its mean failure probability.
        /// </summary>
        /// <returns>The mean response curve (hazard vs. failure probability).</returns>
        OrderedPairedData SampleResponseFunction();

        /// <summary>
        /// Samples the response curve at a fixed knowledge-uncertainty percentile (co-monotonic:
        /// one percentile drives every ordinate).
        /// </summary>
        /// <param name="percentile">The percentile in (0, 1).</param>
        /// <returns>The sampled response curve.</returns>
        OrderedPairedData SampleResponseFunction(double percentile);

        /// <summary>
        /// Samples the response curve for the given realization, reading this function's
        /// pre-allocated percentile row. <see cref="IRiskFunction.SetupSampler"/> must be called
        /// first. (v1.0 treated this integer as a PRNG seed; v1.1 realization-index semantics.)
        /// </summary>
        /// <param name="realizationIndex">The realization index in [0, sample size).</param>
        /// <returns>The sampled response curve.</returns>
        OrderedPairedData SampleResponseFunction(int realizationIndex);

        /// <summary>
        /// Samples the mean response as a distribution whose CDF is the failure probability.
        /// </summary>
        /// <returns>The mean response distribution.</returns>
        IUnivariateDistribution SampleFunction();

        /// <summary>
        /// Samples the response distribution at a fixed knowledge-uncertainty percentile.
        /// </summary>
        /// <param name="percentile">The percentile in (0, 1).</param>
        /// <returns>The sampled response distribution.</returns>
        IUnivariateDistribution SampleFunction(double percentile);

        /// <summary>
        /// Samples the response distribution for the given realization.
        /// <see cref="IRiskFunction.SetupSampler"/> must be called first.
        /// </summary>
        /// <param name="realizationIndex">The realization index in [0, sample size).</param>
        /// <returns>The sampled response distribution.</returns>
        IUnivariateDistribution SampleFunction(int realizationIndex);

        /// <summary>
        /// Determines whether the response is monotonic — failure probability never decreasing
        /// with increasing hazard — across its knowledge uncertainty.
        /// </summary>
        /// <returns>True when the sampled curves are non-decreasing.</returns>
        bool IsMonotonic();

        /// <summary>
        /// The minimum hazard value the response is defined for.
        /// </summary>
        /// <returns>The minimum hazard.</returns>
        double MinHazard();

        /// <summary>
        /// The maximum hazard value the response is defined for.
        /// </summary>
        /// <returns>The maximum hazard.</returns>
        double MaxHazard();

        /// <summary>
        /// The failure probability at the response's minimum hazard (mean curve).
        /// </summary>
        /// <returns>The minimum failure probability.</returns>
        double MinProbability();

        /// <summary>
        /// The failure probability at the response's maximum hazard (mean curve).
        /// </summary>
        /// <returns>The maximum failure probability.</returns>
        double MaxProbability();
    }
}
