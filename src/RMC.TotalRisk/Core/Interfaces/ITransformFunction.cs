using Numerics.Functions;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// The transform input-function contract: a sampled transform maps one hazard domain onto
    /// another (e.g., flow to stage through a rating curve) as a univariate function evaluated
    /// inside the risk integrand.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The v1.0 domain surface is preserved verbatim: the transformed-hazard labels, the mean and
    /// percentile <c>SampleFunction</c> overloads returning <see cref="IUnivariateFunction"/>, the
    /// unqualified input-hazard bounds, and the <c>meanOnly</c>-qualified transformed-hazard bounds.
    /// The realization-index overload is the v1.1 sampler addition.
    /// </para>
    /// </remarks>
    public interface ITransformFunction : IRiskFunction
    {
        /// <summary>
        /// The hazard type this function's output axis represents (e.g., "Stage"). Axis-label
        /// metadata — serialized, never hashed.
        /// </summary>
        string TransformedHazard { get; set; }

        /// <summary>
        /// The unit of the output hazard axis (e.g., "ft"). Axis-label metadata — serialized,
        /// never hashed.
        /// </summary>
        string TransformedHazardUnit { get; set; }

        /// <summary>
        /// Samples the mean transform — the expected function across knowledge uncertainty.
        /// </summary>
        /// <returns>The mean transform function.</returns>
        IUnivariateFunction SampleFunction();

        /// <summary>
        /// Samples the transform at a fixed knowledge-uncertainty percentile.
        /// </summary>
        /// <param name="percentile">The percentile in (0, 1) driving the co-monotonic sample.</param>
        /// <returns>The sampled transform function.</returns>
        IUnivariateFunction SampleFunction(double percentile);

        /// <summary>
        /// Samples the transform for the given realization, reading this function's pre-allocated
        /// percentile row. <see cref="IRiskFunction.SetupSampler"/> must be called first.
        /// </summary>
        /// <param name="realizationIndex">The realization index in [0, sample size).</param>
        /// <returns>The sampled transform function.</returns>
        IUnivariateFunction SampleFunction(int realizationIndex);

        /// <summary>
        /// The minimum input-hazard value the transform is defined for.
        /// </summary>
        /// <returns>The minimum input hazard.</returns>
        double MinHazard();

        /// <summary>
        /// The maximum input-hazard value the transform is defined for.
        /// </summary>
        /// <returns>The maximum input hazard.</returns>
        double MaxHazard();

        /// <summary>
        /// The minimum transformed-hazard value the function can produce.
        /// </summary>
        /// <param name="meanOnly">
        /// When true, the bound of the mean function only; when false, the bound across the
        /// function's full knowledge uncertainty.
        /// </param>
        /// <returns>The minimum transformed hazard.</returns>
        double MinTransformedHazard(bool meanOnly);

        /// <summary>
        /// The maximum transformed-hazard value the function can produce.
        /// </summary>
        /// <param name="meanOnly">
        /// When true, the bound of the mean function only; when false, the bound across the
        /// function's full knowledge uncertainty.
        /// </param>
        /// <returns>The maximum transformed hazard.</returns>
        double MaxTransformedHazard(bool meanOnly);
    }
}
