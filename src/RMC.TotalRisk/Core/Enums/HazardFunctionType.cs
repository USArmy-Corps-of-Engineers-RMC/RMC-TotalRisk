using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The concrete kind of a hazard (frequency-distribution) input function, surfaced by
    /// <see cref="IHazardFunction.FunctionType"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// This is a runtime discriminator only — it lets callers (results labeling, the future UI and
    /// REST/MCP layers, and engine dispatch) branch on the function kind without type checks. It is
    /// deliberately <b>never serialized</b>: the serialization and canonical-hash discriminator is
    /// the <c>ToXElement()</c> element name, so adding this enum moves no hash and perturbs no
    /// Monte Carlo seed.
    /// </para>
    /// <para>
    /// Members are added only as concrete types land; the landing checklist requires a new
    /// concrete hazard function to add its member here alongside its
    /// <c>RiskFunctionFactory</c> case.
    /// </para>
    /// </remarks>
    public enum HazardFunctionType
    {
        /// <summary>An exceedance-probability vs. hazard table (<c>TabularHazard</c>).</summary>
        Tabular,

        /// <summary>
        /// A univariate probability distribution with optional parameter uncertainty
        /// (<c>ParametricUnivariateHazard</c>).
        /// </summary>
        ParametricUnivariate,

        /// <summary>
        /// A graphical annual-exceedance-probability curve with derived order-statistic quantile
        /// uncertainty — the HEC-FDA "less simple method" (<c>NonparametricHazard</c>).
        /// </summary>
        Nonparametric,
    }
}
