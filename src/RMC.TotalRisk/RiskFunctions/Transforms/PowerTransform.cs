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
    /// A power transform function: <c>Y = α·(X − ξ)^β</c> with optional multiplicative lognormal
    /// knowledge uncertainty (a log-space Gaussian residual with standard error σ) and an optional
    /// inverse form that maps through the power relation backwards.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>PowerTransform</c> with the domain surface preserved verbatim
    /// (property names, defaults, sampling semantics, and validation rules; the v1.0
    /// column-per-property project persistence becomes the XElement attribute set here). The wrapper adds
    /// domain labels, validation, serialization, and hash identity over the Numerics
    /// <see cref="PowerFunction"/> — zero math lives in this class. Uncertainty is log-space:
    /// the percentile p multiplies the curve by <c>exp(z_p·σ)</c> (forward form), so σ is a
    /// log-space standard error, not a transformed-hazard-unit offset.
    /// </para>
    /// <para>
    /// <b>Minimum is wrapper state only.</b> Numerics <see cref="PowerFunction.Minimum"/> has
    /// always derived from ξ (its setter was a silent no-op in every shipped v1.0 build and
    /// throws today), so the legacy code line that assigned it never took effect — evaluation has
    /// always clamped at ξ, not at the wrapper's <see cref="Minimum"/>. v1.1 preserves that exact
    /// behavior, keeps <see cref="Minimum"/> on the v1.0 API and hash surface, and surfaces the
    /// latent mismatch as a validation warning when <see cref="Minimum"/> &lt; <see cref="Xi"/>.
    /// </para>
    /// <para>
    /// Knowledge uncertainty is sampled co-monotonically (one percentile drives the whole curve).
    /// Sampling dimension D = 1 while <see cref="IsUncertain"/>, else 0 (architecture doc
    /// §5.8.4); the realization-index overload returns the deterministic function when no
    /// percentile matrix exists. The <see cref="Sigma"/> attribute is serialized (and therefore
    /// hashed) only while <see cref="IsUncertain"/> is true — the §5.5.3 recipe-literal
    /// conditional; a deterministic instance whose stored σ differs from the default round-trips
    /// to the default σ, and the discarded value is compute-inert by construction.
    /// </para>
    /// </remarks>
    public class PowerTransform : TransformFunctionBase
    {
        #region Construction

        /// <summary>
        /// Initializes a power transform with the v1.0 defaults:
        /// α = 1, β = 1.5, ξ = 0, σ = 0.1 (log space), uncertain, not inverted, over [0, 100].
        /// </summary>
        public PowerTransform()
        {
        }

        /// <summary>
        /// Restores a power transform from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public PowerTransform(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            ReadIdentityAttributes(xElement);
            SpecifiedHazard = SerializationUtilities.ReadString(xElement, nameof(SpecifiedHazard));
            HazardUnit = SerializationUtilities.ReadString(xElement, nameof(HazardUnit));
            TransformedHazard = SerializationUtilities.ReadString(xElement, nameof(TransformedHazard));
            TransformedHazardUnit = SerializationUtilities.ReadString(xElement, nameof(TransformedHazardUnit));
            _alpha = SerializationUtilities.ReadDouble(xElement, nameof(Alpha), 1d);
            _beta = SerializationUtilities.ReadDouble(xElement, nameof(Beta), 1.5d);
            _xi = SerializationUtilities.ReadDouble(xElement, nameof(Xi), 0d);
            _isUncertain = SerializationUtilities.ReadBoolean(xElement, nameof(IsUncertain), true);
            _sigma = SerializationUtilities.ReadDouble(xElement, nameof(Sigma), 0.1d);
            _isInverse = SerializationUtilities.ReadBoolean(xElement, nameof(IsInverse), false);
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
        private double _alpha = 1d;

        /// <summary>
        /// Backing field for <see cref="Beta"/> — the v1.0 default.
        /// </summary>
        private double _beta = 1.5d;

        /// <summary>
        /// Backing field for <see cref="Xi"/> — the v1.0 default.
        /// </summary>
        private double _xi = 0d;

        /// <summary>
        /// Backing field for <see cref="Sigma"/> — the v1.0 default.
        /// </summary>
        private double _sigma = 0.1d;

        /// <summary>
        /// Backing field for <see cref="IsInverse"/> — the v1.0 default.
        /// </summary>
        private bool _isInverse = false;

        /// <summary>
        /// Backing field for <see cref="IsUncertain"/> — the v1.0 default.
        /// </summary>
        private bool _isUncertain = true;

        /// <summary>
        /// The minimum input-hazard value allowed to be transformed. Wrapper state only:
        /// evaluation clamps at ξ (see the class remarks), and <see cref="MinHazard"/> reports
        /// this value as the v1.0 API did.
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
        /// The coefficient parameter (α); must be positive (the power relation is evaluated in
        /// log space).
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
        /// The exponent parameter (β), in [−10, 10].
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
        /// The location parameter (ξ) — the input-hazard value where the transformed hazard is
        /// zero; evaluation clamps input hazards at ξ.
        /// </summary>
        public double Xi
        {
            get { return _xi; }
            set
            {
                if (_xi != value)
                {
                    _xi = value;
                    RaisePropertyChange(nameof(Xi));
                }
            }
        }

        /// <summary>
        /// The log-space standard error (σ) of the multiplicative lognormal residual. Used only
        /// while <see cref="IsUncertain"/> is true.
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
        /// Whether the power relation is inverted: the transform maps through
        /// <c>X = α·(Y − ξ)^β</c> backwards instead of evaluating the forward form.
        /// </summary>
        public bool IsInverse
        {
            get { return _isInverse; }
            set
            {
                if (_isInverse != value)
                {
                    _isInverse = value;
                    RaisePropertyChange(nameof(IsInverse));
                }
            }
        }

        /// <summary>
        /// Whether the transform carries knowledge uncertainty (the lognormal residual). When
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
        public override TransformFunctionType FunctionType => TransformFunctionType.Power;

        /// <inheritdoc/>
        public override bool IsDeterministic => !IsUncertain;

        /// <inheritdoc/>
        public override int SamplingDimensions => IsUncertain ? 1 : 0;

        #endregion

        #region IRiskFunction Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Errors (invalidating): missing axis labels; non-finite range, coefficient, exponent, or
        /// location; a minimum at or above the maximum; a non-positive α; a β outside [−10, 10];
        /// and — only while <see cref="IsUncertain"/> — a non-finite or non-positive σ (the exact
        /// v1.0 rule set, PTF-ERR-005..018). One advisory beyond v1.0: a <see cref="Minimum"/>
        /// below ξ warns that evaluation clamps at ξ (see the class remarks).
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var messages = new List<string>();

            if (string.IsNullOrEmpty(SpecifiedHazard))
                messages.Add("Error: The power transform function does not have a specified hazard type.");
            if (string.IsNullOrEmpty(HazardUnit))
                messages.Add("Error: The power transform function does not have a specified hazard unit.");
            if (string.IsNullOrEmpty(TransformedHazard))
                messages.Add("Error: The power transform function does not have a transformed hazard type.");
            if (string.IsNullOrEmpty(TransformedHazardUnit))
                messages.Add("Error: The power transform function does not have a specified transformed hazard unit.");

            if (!Tools.IsFinite(Minimum))
                messages.Add("Error: Invalid minimum X-value.");
            if (!Tools.IsFinite(Maximum))
                messages.Add("Error: Invalid maximum X-value.");
            if (!double.IsNaN(Minimum) && !double.IsNaN(Maximum) && Minimum >= Maximum)
                messages.Add("Error: The minimum X-value must be less than the maximum X-value.");

            if (!Tools.IsFinite(Alpha))
                messages.Add("Error: Invalid coefficient parameter (α).");
            else if (Alpha <= 0d)
                messages.Add("Error: The coefficient parameter (α) must be positive.");

            if (!Tools.IsFinite(Beta))
                messages.Add("Error: Invalid exponent coefficient (β).");
            else if (Beta < -10d || Beta > 10d)
                messages.Add("Error: The exponent coefficient (β) must be between -10 and 10.");

            if (!Tools.IsFinite(Xi))
                messages.Add("Error: Invalid location parameter (ξ).");

            if (IsUncertain)
            {
                if (!Tools.IsFinite(Sigma))
                    messages.Add("Error: Invalid standard error (σ).");
                else if (Sigma <= 0d)
                    messages.Add("Error: The standard error (σ) must be greater than zero.");
            }

            if (!double.IsNaN(Minimum) && !double.IsNaN(Xi) && Minimum < Xi)
                messages.Add("Warning: The minimum X-value is below the location parameter (ξ); input hazards below ξ evaluate at ξ.");

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The mean (deterministic) transform: <c>Y = α·(X − ξ)^β</c> (or the inverse form),
        /// clamped to (ξ, <see cref="Maximum"/>] — always deterministic regardless of
        /// <see cref="IsUncertain"/> (the v1.0 mean function). Numerics
        /// <see cref="PowerFunction.Minimum"/> is derived from ξ and is deliberately not assigned
        /// (see the class remarks). Improved over v1.0: an unusable configuration throws instead
        /// of propagating silently.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the configuration is unusable.</exception>
        public override IUnivariateFunction SampleFunction()
        {
            ThrowIfUnusable();
            return new PowerFunction(Alpha, Beta, Xi, SafeSigma()) { IsDeterministic = true, IsInverse = IsInverse, Maximum = Maximum };
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The co-monotonic percentile transform: the whole curve is multiplied by
        /// <c>exp(z_p·σ)</c> in the forward form (the inverse form folds the same residual through
        /// the inversion). When the transform is deterministic the percentile is ignored (the
        /// v1.0 behavior — the returned function evaluates the mean).
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the configuration is unusable.</exception>
        public override IUnivariateFunction SampleFunction(double percentile)
        {
            ThrowIfUnusable();
            return new PowerFunction(Alpha, Beta, Xi, SafeSigma())
            {
                IsDeterministic = IsDeterministic,
                IsInverse = IsInverse,
                ConfidenceLevel = percentile,
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
        /// input-hazard values from <see cref="Minimum"/> to <see cref="Maximum"/> inclusive
        /// (values below ξ evaluate at ξ, matching the sampled functions). Callers pair the
        /// summary curves with these hazards.
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
        /// simulation. The deterministic curve is the median (the lognormal residual has median
        /// 1); the mean applies the lognormal mean factor — <c>exp(σ²/2)</c> on the forward form,
        /// and <c>exp(σ²/(2β²))</c> on the shifted value (Y − ξ) for the inverse form, whose
        /// residual folds through the inversion with log-space standard error σ/|β|. The
        /// confidence bounds are the per-hazard envelope of the two co-monotonic percentile
        /// curves at (1 ∓ width)/2 — the envelope form because the inverse relation reverses the
        /// percentile direction when β is positive (and the forward direction when β is
        /// negative). Null when the configuration is unusable.
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
            double meanFactor = IsUncertain
                ? (IsInverse ? Math.Exp(Sigma * Sigma / (2d * Beta * Beta)) : Math.Exp(Sigma * Sigma / 2d))
                : 1d;

            var results = new UncertaintyAnalysisResults
            {
                MeanCurve = new double[hazards.Length],
                ModeCurve = new double[hazards.Length],
                ConfidenceIntervals = new double[hazards.Length, 2],
            };

            for (int i = 0; i < hazards.Length; i++)
            {
                double median = central.Function(hazards[i]);
                double lo = lower.Function(hazards[i]);
                double hi = upper.Function(hazards[i]);
                results.MeanCurve[i] = IsInverse ? (median - Xi) * meanFactor + Xi : median * meanFactor;
                results.ModeCurve[i] = median;
                results.ConfidenceIntervals[i, 0] = Math.Min(lo, hi);
                results.ConfidenceIntervals[i, 1] = Math.Max(lo, hi);
            }

            return results;
        }

        #endregion

        #region Serialization

        /// <inheritdoc/>
        /// <remarks>
        /// Attribute order follows the §5.5.3 canonical recipe: Alpha, Beta, Xi, IsUncertain,
        /// [Sigma while uncertain], IsInverse, Minimum, Maximum. The σ attribute is omitted while
        /// deterministic so a compute-inert σ can never reach the hash surface.
        /// </remarks>
        public override XElement ToXElement()
        {
            var element = new XElement(nameof(PowerTransform));
            WriteIdentityAttributes(element);
            element.SetAttributeValue(nameof(SpecifiedHazard), SpecifiedHazard);
            element.SetAttributeValue(nameof(HazardUnit), HazardUnit);
            element.SetAttributeValue(nameof(TransformedHazard), TransformedHazard);
            element.SetAttributeValue(nameof(TransformedHazardUnit), TransformedHazardUnit);
            element.SetAttributeValue(nameof(Alpha), SerializationUtilities.FormatDouble(Alpha));
            element.SetAttributeValue(nameof(Beta), SerializationUtilities.FormatDouble(Beta));
            element.SetAttributeValue(nameof(Xi), SerializationUtilities.FormatDouble(Xi));
            element.SetAttributeValue(nameof(IsUncertain), IsUncertain.ToString());
            if (IsUncertain)
            {
                element.SetAttributeValue(nameof(Sigma), SerializationUtilities.FormatDouble(Sigma));
            }
            element.SetAttributeValue(nameof(IsInverse), IsInverse.ToString());
            element.SetAttributeValue(nameof(Minimum), SerializationUtilities.FormatDouble(Minimum));
            element.SetAttributeValue(nameof(Maximum), SerializationUtilities.FormatDouble(Maximum));
            return element;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Determines whether the numeric configuration can be sampled: finite range with
        /// Minimum &lt; Maximum, a positive finite α, a finite β in [−10, 10], a finite ξ, and —
        /// while uncertain — a finite positive σ. Label checks are advisory for sampling and are
        /// not applied here.
        /// </summary>
        /// <returns>True when the configuration is usable.</returns>
        private bool ConfigurationIsUsable()
        {
            if (!Tools.IsFinite(Minimum)) return false;
            if (!Tools.IsFinite(Maximum)) return false;
            if (Minimum >= Maximum) return false;
            if (!Tools.IsFinite(Alpha) || Alpha <= 0d) return false;
            if (!Tools.IsFinite(Beta) || Beta < -10d || Beta > 10d) return false;
            if (!Tools.IsFinite(Xi)) return false;
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
                throw new InvalidOperationException("The power transform configuration is invalid. Call Validate() and correct the reported errors before sampling.");
        }

        /// <summary>
        /// The σ handed to the Numerics function: the stored σ while uncertain, else a benign
        /// placeholder (the deterministic function never evaluates its residual, but the Numerics
        /// parameter validator requires σ ≥ 0).
        /// </summary>
        /// <returns>The log-space standard error to construct the Numerics function with.</returns>
        private double SafeSigma()
        {
            return IsUncertain ? Sigma : (double.IsNaN(Sigma) || Sigma < 0d ? 0d : Sigma);
        }

        #endregion
    }
}
