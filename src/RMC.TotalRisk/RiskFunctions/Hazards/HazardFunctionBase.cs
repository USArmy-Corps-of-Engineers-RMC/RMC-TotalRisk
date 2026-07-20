using Numerics.Distributions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Hazards
{
    /// <summary>
    /// Abstract base for hazard input functions, anchoring the cluster on the model kernel.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public abstract class HazardFunctionBase : RiskFunctionBase, IHazardFunction
    {
        /// <inheritdoc/>
        public abstract HazardFunctionType FunctionType { get; }

        /// <inheritdoc/>
        public abstract IUnivariateDistribution SampleFunction();

        /// <inheritdoc/>
        public abstract IUnivariateDistribution SampleFunction(double percentile);

        /// <inheritdoc/>
        public abstract IUnivariateDistribution SampleFunction(int realizationIndex);

        /// <inheritdoc/>
        public abstract double MinHazard(bool meanOnly);

        /// <inheritdoc/>
        public abstract double MaxHazard(bool meanOnly);
    }
}
