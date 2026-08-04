using Numerics.Data;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// The bivariate transform contract: a deterministic two-way table z = f(x, y) that maps a
    /// primary and a secondary hazard onto a transformed hazard through bilinear interpolation,
    /// evaluated jointly inside the risk integrand.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// A bivariate transform has no univariate form, so the inherited one-argument
    /// <see cref="ITransformFunction.SampleFunction()"/> surface throws
    /// <see cref="System.NotSupportedException"/> by contract — a fabricated univariate bridge
    /// would silently evaluate the surface at a meaningless secondary value. Graph arity and
    /// validation keep bivariate functions out of univariate chains, so the throw is unreachable
    /// in valid models.
    /// </para>
    /// <para>
    /// <b>Interpolator thread discipline:</b> a Numerics <see cref="Bilinear"/> instance is not
    /// safe for concurrent reads (its inner interpolators keep a mutable correlated-search state),
    /// so consumers build one fresh configured instance per sampled realization via
    /// <see cref="CreateInterpolator"/> — never per evaluation call, never shared across threads —
    /// and nothing is cached on the function.
    /// </para>
    /// </remarks>
    public interface IBivariateTransformFunction : ITransformFunction
    {
        /// <summary>
        /// The hazard type of the secondary (X2) axis (e.g., "Pool Elevation"). Axis-label
        /// metadata — serialized, never hashed. The primary axis uses the inherited
        /// <see cref="IRiskFunction.SpecifiedHazard"/>.
        /// </summary>
        string SecondarySpecifiedHazard { get; set; }

        /// <summary>
        /// The unit of the secondary (X2) axis (e.g., "ft"). Axis-label metadata — serialized,
        /// never hashed. The primary axis uses the inherited <see cref="IRiskFunction.HazardUnit"/>.
        /// </summary>
        string SecondaryHazardUnit { get; set; }

        /// <summary>
        /// Evaluates the transformed hazard z = f(x, y) at a primary and secondary hazard pair.
        /// An allocating convenience — each call builds one interpolator through
        /// <see cref="CreateInterpolator"/>; per-realization consumers hold their own interpolator
        /// instead.
        /// </summary>
        /// <param name="x">The primary hazard value.</param>
        /// <param name="y">The secondary hazard value.</param>
        /// <returns>The interpolated transformed-hazard value.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the table is invalid.</exception>
        double Evaluate(double x, double y);

        /// <summary>
        /// Creates a fresh bilinear interpolator over the surface, configured with the function's
        /// per-axis interpolation transforms — the engine seam. Each call returns a new instance;
        /// callers own it for exactly one sampled realization and never share it across threads.
        /// </summary>
        /// <returns>A newly configured interpolator over the surface.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the table is invalid.</exception>
        Bilinear CreateInterpolator();

        /// <summary>
        /// The minimum secondary hazard value the surface is defined for.
        /// </summary>
        /// <returns>The minimum secondary hazard.</returns>
        double MinSecondaryHazard();

        /// <summary>
        /// The maximum secondary hazard value the surface is defined for.
        /// </summary>
        /// <returns>The maximum secondary hazard.</returns>
        double MaxSecondaryHazard();
    }
}
