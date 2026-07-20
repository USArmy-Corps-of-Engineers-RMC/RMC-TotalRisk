using Numerics.Functions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Consequences
{
    /// <summary>
    /// Abstract base for consequence input functions: anchors the cluster on the model kernel and
    /// backs the consequence axis labels.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public abstract class ConsequenceFunctionBase : RiskFunctionBase, IConsequenceFunction
    {
        /// <summary>
        /// Backing field for <see cref="SpecifiedConsequence"/>.
        /// </summary>
        private string _specifiedConsequence = string.Empty;

        /// <summary>
        /// Backing field for <see cref="ConsequenceUnit"/>.
        /// </summary>
        private string _consequenceUnit = string.Empty;

        /// <inheritdoc/>
        public string SpecifiedConsequence
        {
            get { return _specifiedConsequence; }
            set
            {
                if (_specifiedConsequence != value)
                {
                    _specifiedConsequence = value;
                    RaisePropertyChange(nameof(SpecifiedConsequence));
                }
            }
        }

        /// <inheritdoc/>
        public string ConsequenceUnit
        {
            get { return _consequenceUnit; }
            set
            {
                if (_consequenceUnit != value)
                {
                    _consequenceUnit = value;
                    RaisePropertyChange(nameof(ConsequenceUnit));
                }
            }
        }

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
    }
}
