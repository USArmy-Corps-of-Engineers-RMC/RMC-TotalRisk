using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Responses
{
    /// <summary>
    /// Abstract base for response (fragility) input functions, anchoring the cluster on the model
    /// kernel.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public abstract class ResponseFunctionBase : RiskFunctionBase, IResponseFunction
    {
        /// <inheritdoc/>
        public abstract ResponseFunctionType FunctionType { get; }

        /// <inheritdoc/>
        public virtual bool SupportsOrderedCurveSampling => true;

        /// <inheritdoc/>
        public abstract OrderedPairedData SampleResponseFunction();

        /// <inheritdoc/>
        public abstract OrderedPairedData SampleResponseFunction(double percentile);

        /// <inheritdoc/>
        public abstract OrderedPairedData SampleResponseFunction(int realizationIndex);

        /// <inheritdoc/>
        public abstract IUnivariateDistribution SampleFunction();

        /// <inheritdoc/>
        public abstract IUnivariateDistribution SampleFunction(double percentile);

        /// <inheritdoc/>
        public abstract IUnivariateDistribution SampleFunction(int realizationIndex);

        /// <inheritdoc/>
        public abstract bool IsMonotonic();

        /// <inheritdoc/>
        public abstract double MinHazard();

        /// <inheritdoc/>
        public abstract double MaxHazard();

        /// <inheritdoc/>
        public abstract double MinProbability();

        /// <inheritdoc/>
        public abstract double MaxProbability();
    }
}
