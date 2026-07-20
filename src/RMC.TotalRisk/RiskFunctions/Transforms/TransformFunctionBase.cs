using Numerics.Functions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Transforms
{
    /// <summary>
    /// Abstract base for transform input functions: anchors the cluster on the model kernel and
    /// backs the transformed-hazard axis labels.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public abstract class TransformFunctionBase : RiskFunctionBase, ITransformFunction
    {
        /// <summary>
        /// Backing field for <see cref="TransformedHazard"/>.
        /// </summary>
        private string _transformedHazard = string.Empty;

        /// <summary>
        /// Backing field for <see cref="TransformedHazardUnit"/>.
        /// </summary>
        private string _transformedHazardUnit = string.Empty;

        /// <inheritdoc/>
        public string TransformedHazard
        {
            get { return _transformedHazard; }
            set
            {
                if (_transformedHazard != value)
                {
                    _transformedHazard = value;
                    RaisePropertyChange(nameof(TransformedHazard));
                }
            }
        }

        /// <inheritdoc/>
        public string TransformedHazardUnit
        {
            get { return _transformedHazardUnit; }
            set
            {
                if (_transformedHazardUnit != value)
                {
                    _transformedHazardUnit = value;
                    RaisePropertyChange(nameof(TransformedHazardUnit));
                }
            }
        }

        /// <inheritdoc/>
        public abstract TransformFunctionType FunctionType { get; }

        /// <inheritdoc/>
        public abstract IUnivariateFunction SampleFunction();

        /// <inheritdoc/>
        public abstract IUnivariateFunction SampleFunction(double percentile);

        /// <inheritdoc/>
        public abstract IUnivariateFunction SampleFunction(int realizationIndex);

        /// <inheritdoc/>
        public abstract double MinHazard();

        /// <inheritdoc/>
        public abstract double MaxHazard();

        /// <inheritdoc/>
        public abstract double MinTransformedHazard(bool meanOnly);

        /// <inheritdoc/>
        public abstract double MaxTransformedHazard(bool meanOnly);
    }
}
