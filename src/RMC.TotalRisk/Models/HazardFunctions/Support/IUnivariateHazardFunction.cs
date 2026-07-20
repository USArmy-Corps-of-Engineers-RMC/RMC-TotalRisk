namespace RMC.TotalRisk.Models.HazardFunctions
{
    /// <summary>
    /// Marker contract for univariate hazard functions — hazard functions whose sampled form is a
    /// single-variable distribution. The bivariate hazard contract (marginals + copula + conditional
    /// discretization) arrives with the bivariate cluster in a later phase and extends
    /// <see cref="IHazardFunction"/> separately.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public interface IUnivariateHazardFunction : IHazardFunction
    {
    }
}
