using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Numerics;
using Numerics.Distributions;
using Numerics.Functions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.RiskFunctions.Transforms
{
    /// <summary>
    /// A linear transform function: <c>Y = α + β·X</c> with optional additive Gaussian knowledge
    /// uncertainty <c>ε ~ N(0, σ)</c>, evaluated over the allowed input-hazard range
    /// [<see cref="Minimum"/>, <see cref="Maximum"/>].
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>LinearTransform</c> with the domain surface preserved verbatim
    /// (property names, defaults, sampling semantics, and validation rules; the v1.0
    /// column-per-property project persistence becomes the XElement attribute set here). The wrapper adds
    /// domain labels, validation, serialization, and hash identity over the Numerics
    /// <see cref="LinearFunction"/> — zero math lives in this class.
    /// </para>
    /// <para>
    /// Knowledge uncertainty is sampled co-monotonically: one percentile sets
    /// <c>LinearFunction.ConfidenceLevel</c>, so every hazard level shifts by the same
    /// <c>Normal(0, σ).InverseCDF(p)</c> offset (perfect rank correlation along the curve — the
    /// exact v1.0 behavior). Sampling dimension D = 1 while <see cref="IsUncertain"/>, else 0
    /// (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §5.8.4); the realization-index overload
    /// returns the deterministic function when no percentile matrix exists.
    /// </para>
    /// <para>
    /// The <see cref="Sigma"/> attribute is serialized (and therefore hashed) only while
    /// <see cref="IsUncertain"/> is true — the §5.5.3 recipe-literal conditional. A deterministic
    /// instance whose stored σ differs from the default therefore round-trips to the default σ;
    /// the discarded value is compute-inert by construction.
    /// </para>
    /// </remarks>
    public class LinearTransform : TransformFunctionBase
    {
        #region Construction

        /// <summary>
        /// Initializes a linear transform with the v1.0 defaults:
        /// α = 0, β = 1, σ = 10, uncertain, over [0, 100].
        /// </summary>
        public LinearTransform()
        {
        }

        /// <summary>
        /// Restores a linear transform from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public LinearTransform(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            ReadIdentityAttributes(xElement);
            SpecifiedHazard = SerializationUtilities.ReadString(xElement, nameof(SpecifiedHazard));
            HazardUnit = SerializationUtilities.ReadString(xElement, nameof(HazardUnit));
            TransformedHazard = SerializationUtilities.ReadString(xElement, nameof(TransformedHazard));
            TransformedHazardUnit = SerializationUtilities.ReadString(xElement, nameof(TransformedHazardUnit));
            _alpha = SerializationUtilities.ReadDouble(xElement, nameof(Alpha), 0d);
            _beta = SerializationUtilities.ReadDouble(xElement, nameof(Beta), 1d);
            _isUncertain = SerializationUtilities.ReadBoolean(xElement, nameof(IsUncertain), true);
            _sigma = SerializationUtilities.ReadDouble(xElement, nameof(Sigma), 10d);
            _minimum = SerializationUtilities.ReadDouble(xElement, nameof(Minimum), 0d);
            _maximum = SerializationUtilities.ReadDouble(xElement, nameof(Maximum), 100d);
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Minimum"/> — the v1.0 default.
        /// </summary>
        private double _minimum = 0d;

        /// <summary>
        /// Backing field for <see cref="Maximum"/> — the v1.0 default.
        /// </summary>
        private double _maximum = 100d;

        /// <summary>
        /// Backing field for <see cref="Alpha"/> — the v1.0 default.
        /// </summary>
        private double _alpha = 0d;

        /// <summary>
        /// Backing field for <see cref="Beta"/> — the v1.0 default.
        /// </summary>
        private double _beta = 1d;

        /// <summary>
        /// Backing field for <see cref="Sigma"/> — the v1.0 default.
        /// </summary>
        private double _sigma = 10d;

        /// <summary>
        /// Backing field for <see cref="IsUncertain"/> — the v1.0 default.
        /// </summary>
        private bool _isUncertain = true;

        /// <summary>
        /// The minimum input-hazard value allowed to be transformed; evaluation clamps below it.
        /// </summary>
        public double Minimum
        {
            get { return _minimum; }
            set
            {
                if (_minimum != value)
                {
                    _minimum = value;
                    RaisePropertyChange(nameof(Minimum));
                }
            }
        }

        /// <summary>
        /// The maximum input-hazard value allowed to be transformed; evaluation clamps above it.
        /// </summary>
        public double Maximum
        {
            get { return _maximum; }
            set
            {
                if (_maximum != value)
                {
                    _maximum = value;
                    RaisePropertyChange(nameof(Maximum));
                }
            }
        }

        /// <summary>
        /// The intercept parameter (α).
        /// </summary>
        public double Alpha
        {
            get { return _alpha; }
            set
            {
                if (_alpha != value)
                {
                    _alpha = value;
                    RaisePropertyChange(nameof(Alpha));
                }
            }
        }

        /// <summary>
        /// The slope parameter (β).
        /// </summary>
        public double Beta
        {
            get { return _beta; }
            set
            {
                if (_beta != value)
                {
                    _beta = value;
                    RaisePropertyChange(nameof(Beta));
                }
            }
        }

        /// <summary>
        /// The standard error (σ) of the additive Gaussian residual, in transformed-hazard units.
        /// Used only while <see cref="IsUncertain"/> is true.
        /// </summary>
        public double Sigma
        {
            get { return _sigma; }
            set
            {
                if (_sigma != value)
                {
                    _sigma = value;
                    RaisePropertyChange(nameof(Sigma));
                }
            }
        }

        /// <summary>
        /// Whether the transform carries knowledge uncertainty (the Gaussian residual). When
        /// false, the transform is deterministic and consumes no sampling dimension.
        /// </summary>
        public bool IsUncertain
        {
            get { return _isUncertain; }
            set
            {
                if (_isUncertain != value)
                {
                    _isUncertain = value;
                    RaisePropertyChange(nameof(IsUncertain));
                }
            }
        }

        /// <inheritdoc/>
        public override TransformFunctionType FunctionType => TransformFunctionType.Linear;

        /// <inheritdoc/>
        public override bool IsDeterministic => !IsUncertain;

        /// <inheritdoc/>
        public override int SamplingDimensions => IsUncertain ? 1 : 0;

        #endregion

        #region IRiskFunction Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Errors (invalidating): missing axis labels; non-finite range, intercept, or slope; a
        /// minimum at or above the maximum; and — only while <see cref="IsUncertain"/> — a
        /// non-finite or non-positive standard error (the exact v1.0 rule set, LTF-ERR-005..015).
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var messages = new List<string>();

            if (string.IsNullOrEmpty(SpecifiedHazard))
                messages.Add("Error: The linear transform function does not have a specified hazard type.");
            if (string.IsNullOrEmpty(HazardUnit))
                messages.Add("Error: The linear transform function does not have a specified hazard unit.");
            if (string.IsNullOrEmpty(TransformedHazard))
                messages.Add("Error: The linear transform function does not have a transformed hazard type.");
            if (string.IsNullOrEmpty(TransformedHazardUnit))
                messages.Add("Error: The linear transform function does not have a specified transformed hazard unit.");

            if (!Tools.IsFinite(Minimum))
                messages.Add("Error: Invalid minimum X-value.");
            if (!Tools.IsFinite(Maximum))
                messages.Add("Error: Invalid maximum X-value.");
            if (!double.IsNaN(Minimum) && !double.IsNaN(Maximum) && Minimum >= Maximum)
                messages.Add("Error: The minimum X-value must be less than the maximum X-value.");

            if (!Tools.IsFinite(Alpha))
                messages.Add("Error: Invalid intercept parameter (α).");
            if (!Tools.IsFinite(Beta))
                messages.Add("Error: Invalid slope parameter (β).");

            if (IsUncertain)
            {
                if (!Tools.IsFinite(Sigma))
                    messages.Add("Error: Invalid standard error (σ).");
                else if (Sigma <= 0d)
                    messages.Add("Error: The standard error (σ) must be greater than zero.");
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The mean (deterministic) transform: <c>Y = α + β·X</c>, clamped to the allowed range —
        /// always deterministic regardless of <see cref="IsUncertain"/> (the v1.0 mean function).
        /// Improved over v1.0: an unusable configuration throws instead of propagating silently.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the configuration is unusable.</exception>
        public override IUnivariateFunction SampleFunction()
        {
            ThrowIfUnusable();
            return new LinearFunction(Alpha, Beta, SafeSigma()) { IsDeterministic = true, Minimum = Minimum, Maximum = Maximum };
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The co-monotonic percentile transform: the whole curve shifts by
        /// <c>Normal(0, σ).InverseCDF(percentile)</c>. When the transform is deterministic the
        /// percentile is ignored (the v1.0 behavior — the returned function evaluates the mean).
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the configuration is unusable.</exception>
        public override IUnivariateFunction SampleFunction(double percentile)
        {
            ThrowIfUnusable();
            return new LinearFunction(Alpha, Beta, SafeSigma())
            {
                IsDeterministic = IsDeterministic,
                ConfidenceLevel = percentile,
                Minimum = Minimum,
                Maximum = Maximum,
            };
        }

        /// <inheritdoc/>
        /// <remarks>
        /// While uncertain, reads the pre-allocated percentile row (dimension 0); when
        /// deterministic the function has no percentile matrix (D = 0) and the mean transform is
        /// returned directly.
        /// </remarks>
        public override IUnivariateFunction SampleFunction(int realizationIndex)
        {
            return IsUncertain ? SampleFunction(Percentile(realizationIndex, 0)) : SampleFunction();
        }

        /// <inheritdoc/>
        public override double MinHazard()
        {
            return Minimum;
        }

        /// <inheritdoc/>
        public override double MaxHazard()
        {
            return Maximum;
        }

        /// <inheritdoc/>
        public override double MinTransformedHazard(bool meanOnly)
        {
            var function = meanOnly ? SampleFunction() : SampleFunction(0.00001d);
            return function.Function(Minimum);
        }

        /// <inheritdoc/>
        public override double MaxTransformedHazard(bool meanOnly)
        {
            var function = meanOnly ? SampleFunction() : SampleFunction(1d - 0.00001d);
            return function.Function(Maximum);
        }

        /// <summary>
        /// The evaluation grid the uncertainty summary is computed over: 100 evenly spaced
        /// input-hazard values from <see cref="Minimum"/> to <see cref="Maximum"/> inclusive.
        /// Callers pair the summary curves with these hazards.
        /// </summary>
        /// <returns>The evaluation hazards, ascending.</returns>
        public double[] UncertaintySummaryHazards()
        {
            const int count = 100;
            var hazards = new double[count];
            double step = (Maximum - Minimum) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                hazards[i] = Minimum + i * step;
            }
            hazards[count - 1] = Maximum;
            return hazards;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Exact closed-form evaluation over <see cref="UncertaintySummaryHazards"/> — no
        /// simulation: the additive Gaussian residual is symmetric, so the mean and median curves
        /// are both <c>α + β·X</c>, and the confidence bounds are the co-monotonic percentile
        /// curves at (1 ∓ width)/2. Null when the configuration is unusable.
        /// </remarks>
        public override UncertaintyAnalysisResults? ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9)
        {
            if (confidenceIntervalWidth <= 0d || confidenceIntervalWidth >= 1d)
                throw new ArgumentOutOfRangeException(nameof(confidenceIntervalWidth), "The confidence interval width must be between 0 and 1.");
            if (!ConfigurationIsUsable())
                return null;

            double alpha = (1d - confidenceIntervalWidth) / 2d;
            double[] hazards = UncertaintySummaryHazards();
            var central = SampleFunction();
            var lower = SampleFunction(alpha);
            var upper = SampleFunction(1d - alpha);

            var results = new UncertaintyAnalysisResults
            {
                MeanCurve = new double[hazards.Length],
                ModeCurve = new double[hazards.Length],
                ConfidenceIntervals = new double[hazards.Length, 2],
            };

            for (int i = 0; i < hazards.Length; i++)
            {
                double value = central.Function(hazards[i]);
                double lo = lower.Function(hazards[i]);
                double hi = upper.Function(hazards[i]);
                results.MeanCurve[i] = value;
                results.ModeCurve[i] = value;
                results.ConfidenceIntervals[i, 0] = Math.Min(lo, hi);
                results.ConfidenceIntervals[i, 1] = Math.Max(lo, hi);
            }

            return results;
        }

        #endregion

        #region Serialization

        /// <inheritdoc/>
        /// <remarks>
        /// Attribute order follows the §5.5.3 canonical recipe: Alpha, Beta, IsUncertain,
        /// [Sigma while uncertain], Minimum, Maximum. The σ attribute is omitted while
        /// deterministic so a compute-inert σ can never reach the hash surface.
        /// </remarks>
        public override XElement ToXElement()
        {
            var element = new XElement(nameof(LinearTransform));
            WriteIdentityAttributes(element);
            element.SetAttributeValue(nameof(SpecifiedHazard), SpecifiedHazard);
            element.SetAttributeValue(nameof(HazardUnit), HazardUnit);
            element.SetAttributeValue(nameof(TransformedHazard), TransformedHazard);
            element.SetAttributeValue(nameof(TransformedHazardUnit), TransformedHazardUnit);
            element.SetAttributeValue(nameof(Alpha), SerializationUtilities.FormatDouble(Alpha));
            element.SetAttributeValue(nameof(Beta), SerializationUtilities.FormatDouble(Beta));
            element.SetAttributeValue(nameof(IsUncertain), IsUncertain.ToString());
            if (IsUncertain)
            {
                element.SetAttributeValue(nameof(Sigma), SerializationUtilities.FormatDouble(Sigma));
            }
            element.SetAttributeValue(nameof(Minimum), SerializationUtilities.FormatDouble(Minimum));
            element.SetAttributeValue(nameof(Maximum), SerializationUtilities.FormatDouble(Maximum));
            return element;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Determines whether the numeric configuration can be sampled: finite range with
        /// Minimum &lt; Maximum, finite α and β, and — while uncertain — a finite positive σ.
        /// Label checks are advisory for sampling and are not applied here.
        /// </summary>
        /// <returns>True when the configuration is usable.</returns>
        private bool ConfigurationIsUsable()
        {
            if (!Tools.IsFinite(Minimum)) return false;
            if (!Tools.IsFinite(Maximum)) return false;
            if (Minimum >= Maximum) return false;
            if (!Tools.IsFinite(Alpha)) return false;
            if (!Tools.IsFinite(Beta)) return false;
            if (IsUncertain && (!Tools.IsFinite(Sigma) || Sigma <= 0d)) return false;
            return true;
        }

        /// <summary>
        /// Throws when the numeric configuration cannot be sampled (the v1.1 upgrade of the v1.0
        /// silent propagation).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the configuration is unusable.</exception>
        private void ThrowIfUnusable()
        {
            if (!ConfigurationIsUsable())
                throw new InvalidOperationException("The linear transform configuration is invalid. Call Validate() and correct the reported errors before sampling.");
        }

        /// <summary>
        /// The σ handed to the Numerics function: the stored σ while uncertain, else a benign
        /// placeholder (the deterministic function never evaluates its residual, but the Numerics
        /// parameter validator requires σ ≥ 0).
        /// </summary>
        /// <returns>The standard error to construct the Numerics function with.</returns>
        private double SafeSigma()
        {
            return IsUncertain ? Sigma : (double.IsNaN(Sigma) || Sigma < 0d ? 0d : Sigma);
        }

        #endregion
    }
}
