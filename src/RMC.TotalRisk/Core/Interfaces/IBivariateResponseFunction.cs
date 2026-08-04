using Numerics.Data;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// The bivariate response contract: a deterministic two-dimensional failure-probability
    /// surface P(f | x, y) over a primary and a secondary hazard, consumed in one of two modes
    /// selected entirely by the parent — collapsed onto the primary axis through the stored
    /// secondary weights, or evaluated jointly at (x, y) pairs.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Unlike the bivariate transform and consequence tables, a bivariate response KEEPS a working
    /// univariate surface: the inherited <see cref="IResponseFunction"/> sampling members return
    /// the weighted collapse of the surface onto the primary axis, so under a univariate hazard
    /// the function behaves exactly like a deterministic tabular response. Under a bivariate
    /// hazard a consumer instead evaluates the surface jointly through
    /// <see cref="SurfaceProbability"/> or <see cref="CreateInterpolator"/>, and the stored
    /// weights are inert. The function itself holds no mode state.
    /// </para>
    /// <para>
    /// <b>Extrapolation policy (deliberate — the native <see cref="Bilinear"/> behavior the
    /// legacy evaluator shipped):</b> when both coordinates fall outside their axes, the nearest
    /// corner cell is returned exactly; when one coordinate falls outside, it clamps to its
    /// nearest edge row or column and the result is one-dimensional linear interpolation along
    /// the in-range axis (in transform space). The response adds one rule on top: the
    /// back-transformed result is clamped to the probability range [0, 1].
    /// <see cref="SurfaceProbability"/> applies the clamp itself; a consumer evaluating through a
    /// raw <see cref="CreateInterpolator"/> instance must clamp each interpolated value the same
    /// way.
    /// </para>
    /// <para>
    /// <b>Interpolator thread discipline:</b> a Numerics <see cref="Bilinear"/> instance is not
    /// safe for concurrent reads (its inner interpolators keep a mutable correlated-search state),
    /// so consumers build one fresh configured instance per sampled realization via
    /// <see cref="CreateInterpolator"/> — never per evaluation call, never shared across threads —
    /// and nothing is cached on the function.
    /// </para>
    /// </remarks>
    public interface IBivariateResponseFunction : IResponseFunction
    {
        /// <summary>
        /// The hazard type of the secondary axis (e.g., "Pool Elevation"). Axis-label metadata —
        /// serialized, never hashed. The primary axis uses the inherited
        /// <see cref="IRiskFunction.SpecifiedHazard"/>.
        /// </summary>
        string SecondarySpecifiedHazard { get; set; }

        /// <summary>
        /// The unit of the secondary axis (e.g., "ft"). Axis-label metadata — serialized, never
        /// hashed. The primary axis uses the inherited <see cref="IRiskFunction.HazardUnit"/>.
        /// </summary>
        string SecondaryHazardUnit { get; set; }

        /// <summary>
        /// The number of secondary hazard levels defining the surface. Joint evaluation under a
        /// bivariate hazard requires at least two (a single level degenerates to a univariate
        /// response); the collapse mode admits one.
        /// </summary>
        int SecondaryLevelCount { get; }

        /// <summary>
        /// Evaluates the failure probability P(f | x, y) at a primary and secondary hazard pair,
        /// clamped to [0, 1] after back-transform. An allocating convenience — each call builds
        /// one interpolator through <see cref="CreateInterpolator"/>; per-realization consumers
        /// hold their own interpolator instead and apply the same clamp.
        /// </summary>
        /// <param name="x">The primary hazard value.</param>
        /// <param name="y">The secondary hazard value.</param>
        /// <returns>The interpolated failure probability in [0, 1].</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the surface is invalid.</exception>
        double SurfaceProbability(double x, double y);

        /// <summary>
        /// Creates a fresh bilinear interpolator over the failure-probability surface, configured
        /// with the function's per-axis interpolation transforms — the engine seam. Each call
        /// returns a new instance; callers own it for exactly one sampled realization, never share
        /// it across threads, and clamp each interpolated value to [0, 1].
        /// </summary>
        /// <returns>A newly configured interpolator over the surface.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the surface is invalid.</exception>
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
