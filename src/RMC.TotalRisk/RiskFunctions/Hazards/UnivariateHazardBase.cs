using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Hazards
{
    /// <summary>
    /// Abstract base for univariate hazard input functions — the anchor every single-variable
    /// hazard type derives from (tabular, parametric, and the later nonparametric, RFA, and
    /// composite types).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public abstract class UnivariateHazardBase : HazardFunctionBase, IUnivariateHazardFunction
    {
    }
}
