using Numerics.Functions;

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
    /// </remarks>
    public interface IConsequenceFunction : IRiskFunction
    {
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
