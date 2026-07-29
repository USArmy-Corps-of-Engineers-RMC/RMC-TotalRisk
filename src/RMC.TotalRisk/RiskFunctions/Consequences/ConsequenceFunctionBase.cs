using System.Collections.Generic;
using Numerics.Functions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Consequences
{
    /// <summary>
    /// Abstract base for consequence input functions: anchors the cluster on the model kernel,
    /// backs the consequence axis labels, and supplies the single-branch default for the
    /// exposure-branch contract.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The exposure-branch members (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §6.4.1) are
    /// virtual with a
    /// single unit-weight default, so every non-composite consequence type participates in the
    /// engine's branch enumeration without changing shape; only <c>CompositeConsequence</c> in
    /// Mixture mode overrides them with real branches.
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
        public abstract ConsequenceFunctionType FunctionType { get; }

        /// <inheritdoc/>
        public abstract IUnivariateFunction SampleFunction();

        /// <inheritdoc/>
        public abstract IUnivariateFunction SampleFunction(double percentile);

        /// <inheritdoc/>
        public abstract IUnivariateFunction SampleFunction(int realizationIndex);

        /// <inheritdoc/>
        /// <remarks>
        /// The single-branch default: one unit-weight entry carrying the mean function. Composite
        /// mixtures override with their real exposure branches.
        /// </remarks>
        public virtual IReadOnlyList<(double Weight, IUnivariateFunction Function)> SampleExposureBranches()
        {
            return new[] { (1d, SampleFunction()) };
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The single-branch default: one unit-weight entry carrying the function sampled
        /// co-monotonically at the given knowledge percentile.
        /// </remarks>
        public virtual IReadOnlyList<(double Weight, IUnivariateFunction Function)> SampleExposureBranches(double percentile)
        {
            return new[] { (1d, SampleFunction(percentile)) };
        }

        /// <inheritdoc/>
        /// <remarks>The single-branch default. Composite mixtures override with their leaf count.</remarks>
        public virtual int CountExposureBranches()
        {
            return 1;
        }

        /// <inheritdoc/>
        public abstract double MinHazard();

        /// <inheritdoc/>
        public abstract double MaxHazard();
    }
}
