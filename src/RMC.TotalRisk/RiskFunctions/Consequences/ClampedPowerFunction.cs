using System;
using System.Collections.Generic;
using Numerics.Functions;

namespace RMC.TotalRisk.RiskFunctions.Consequences
{
    /// <summary>
    /// A deterministic univariate function for the clamped threshold-power consequence form
    /// C(h) = clamp(a · max(h − h₀, 0)^b, 0, U): zero at and below the hazard threshold h₀,
    /// power-law growth above it, and saturation at the upper bound U.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// This adapter carries fixed, already-realized coefficients: <c>ParametricConsequence</c>
    /// realizes its uncertain coefficients first (one (a, b) pair per realization) and then
    /// constructs this deterministic curve, so the adapter itself has no noise model. It is not
    /// a wrapper over <see cref="PowerFunction"/> deliberately: that class has no upper-bound
    /// clamp, returns an epsilon-offset value at the location parameter instead of an exact
    /// zero, and its multiplicative residual keyed to <see cref="ConfidenceLevel"/> is the wrong
    /// uncertainty shape for realized coefficients. Should an upstream Numerics
    /// <c>CompositeFunction</c> expansion cover this shape, this form is a candidate to
    /// fold into the Numerics functions namespace alongside it.
    /// </para>
    /// <para>
    /// Parameter layout for <see cref="SetParameters"/> is <c>[a, b, h₀, U]</c> with a &gt; 0,
    /// b &gt; 0, h₀ finite, and U &gt; 0 (positive infinity means "no saturation cap").
    /// </para>
    /// </remarks>
    public class ClampedPowerFunction : IUnivariateFunction
    {
        #region Construction

        /// <summary>
        /// Initializes the function with the cluster defaults: a = 1, b = 1.5, h₀ = 0, and no
        /// saturation cap.
        /// </summary>
        public ClampedPowerFunction()
            : this(1d, 1.5d, 0d, double.PositiveInfinity)
        {
        }

        /// <summary>
        /// Initializes the function with realized coefficients.
        /// </summary>
        /// <param name="alpha">The scale coefficient a. Must be positive and finite.</param>
        /// <param name="beta">The exponent b. Must be positive and finite.</param>
        /// <param name="threshold">The hazard threshold h₀ below which the function is zero. Must be finite.</param>
        /// <param name="upperBound">The saturation cap U. Must be positive; positive infinity means no cap.</param>
        /// <remarks>
        /// Invalid coefficients are recorded rather than thrown here (the Numerics function
        /// idiom): <see cref="Function(double)"/> and <see cref="InverseFunction(double)"/> throw
        /// on first use when <see cref="ParametersValid"/> is false.
        /// </remarks>
        public ClampedPowerFunction(double alpha, double beta, double threshold, double upperBound)
        {
            StoreParameters(alpha, beta, threshold, upperBound);
        }

        #endregion

        #region Members

        /// <summary>
        /// The scale coefficient a.
        /// </summary>
        private double _alpha;

        /// <summary>
        /// The exponent b.
        /// </summary>
        private double _beta;

        /// <summary>
        /// The hazard threshold h₀ below which the function is zero.
        /// </summary>
        private double _threshold;

        /// <summary>
        /// The saturation cap U; positive infinity means no cap.
        /// </summary>
        private double _upperBound;

        /// <summary>
        /// Backing field for <see cref="ParametersValid"/>, refreshed on every parameter store.
        /// </summary>
        private bool _parametersValid;

        /// <summary>
        /// Backing field for <see cref="ConfidenceLevel"/> — stored only to honor the interface;
        /// the adapter is always deterministic, so the value never affects evaluation.
        /// </summary>
        private double _confidenceLevel = -1;

        /// <summary>
        /// The scale coefficient a.
        /// </summary>
        public double Alpha => _alpha;

        /// <summary>
        /// The exponent b.
        /// </summary>
        public double Beta => _beta;

        /// <summary>
        /// The hazard threshold h₀ below which the function is zero.
        /// </summary>
        public double Threshold => _threshold;

        /// <summary>
        /// The saturation cap U; positive infinity means no cap.
        /// </summary>
        public double UpperBound => _upperBound;

        /// <inheritdoc/>
        public int NumberOfParameters => 4;

        /// <inheritdoc/>
        public bool ParametersValid => _parametersValid;

        /// <inheritdoc/>
        /// <remarks>
        /// The minimum supported hazard is the threshold h₀ (the curve is identically zero at and
        /// below it). Derived from the parameters, so the setter throws — the
        /// <see cref="PowerFunction"/> location-parameter precedent.
        /// </remarks>
        /// <exception cref="NotSupportedException">Always thrown by the setter.</exception>
        public double Minimum
        {
            get { return _threshold; }
            set { throw new NotSupportedException("The minimum is the hazard threshold h₀; set it through the parameters."); }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The saturation crossing h₀ + (U/a)^(1/b) when the cap is finite (the curve is flat at U
        /// beyond it), otherwise <see cref="double.MaxValue"/> (the interface's "unbounded"
        /// convention). Derived from the parameters, so the setter throws.
        /// </remarks>
        /// <exception cref="NotSupportedException">Always thrown by the setter.</exception>
        public double Maximum
        {
            get { return CapCrossing(); }
            set { throw new NotSupportedException("The maximum is the saturation crossing derived from the parameters."); }
        }

        /// <inheritdoc/>
        public double[] MinimumOfParameters => new[] { 0d, 0d, double.MinValue, 0d };

        /// <inheritdoc/>
        public double[] MaximumOfParameters => new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };

        /// <inheritdoc/>
        /// <remarks>
        /// Always true — the coefficients are realized before construction. Setting false is
        /// rejected loudly rather than ignored, because a caller expecting a noise band would
        /// otherwise get silently deterministic values.
        /// </remarks>
        /// <exception cref="NotSupportedException">Thrown when set to false.</exception>
        public bool IsDeterministic
        {
            get { return true; }
            set
            {
                if (!value) throw new NotSupportedException("The clamped power function carries realized coefficients and is always deterministic.");
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Stored but inert: per the interface contract the confidence level only applies "when
        /// the function has uncertainty", and this adapter never does.
        /// </remarks>
        public double ConfidenceLevel
        {
            get { return _confidenceLevel; }
            set { _confidenceLevel = value; }
        }

        #endregion

        #region Methods

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">Thrown when the parameter list is null.</exception>
        public void SetParameters(IList<double> parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (parameters.Count != 4)
                throw new ArgumentOutOfRangeException(nameof(parameters), "The clamped power function requires exactly four parameters: [a, b, h₀, U].");
            StoreParameters(parameters[0], parameters[1], parameters[2], parameters[3]);
        }

        /// <inheritdoc/>
        public ArgumentOutOfRangeException? ValidateParameters(IList<double> parameters, bool throwException)
        {
            ArgumentOutOfRangeException? exception = null;
            if (parameters == null || parameters.Count != 4)
            {
                exception = new ArgumentOutOfRangeException(nameof(parameters), "The clamped power function requires exactly four parameters: [a, b, h₀, U].");
            }
            else if (!(double.IsFinite(parameters[0]) && parameters[0] > 0d))
            {
                exception = new ArgumentOutOfRangeException(nameof(parameters), "The scale coefficient a must be a positive, finite number.");
            }
            else if (!(double.IsFinite(parameters[1]) && parameters[1] > 0d))
            {
                exception = new ArgumentOutOfRangeException(nameof(parameters), "The exponent b must be a positive, finite number.");
            }
            else if (!double.IsFinite(parameters[2]))
            {
                exception = new ArgumentOutOfRangeException(nameof(parameters), "The hazard threshold h₀ must be a finite number.");
            }
            else if (double.IsNaN(parameters[3]) || parameters[3] <= 0d)
            {
                exception = new ArgumentOutOfRangeException(nameof(parameters), "The upper bound U must be greater than zero; use positive infinity for no cap.");
            }

            if (exception != null && throwException) throw exception;
            return exception;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Exact closed form: zero at and below h₀ (not an epsilon offset), a · (x − h₀)^b above
        /// it, and U once the curve reaches the cap. NaN inputs propagate as NaN.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the parameters are invalid.</exception>
        public double Function(double x)
        {
            ThrowIfParametersInvalid();
            if (x <= _threshold) return 0d;
            double value = _alpha * Math.Pow(x - _threshold, _beta);
            return value >= _upperBound ? _upperBound : value;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Exact closed form: h₀ for y ≤ 0 (the smallest hazard at which the flat zero segment
        /// ends), h₀ + (y/a)^(1/b) on the power segment, and the saturation crossing for y at or
        /// above a finite cap (the smallest hazard that produces U).
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the parameters are invalid.</exception>
        public double InverseFunction(double y)
        {
            ThrowIfParametersInvalid();
            if (y <= 0d) return _threshold;
            if (!double.IsPositiveInfinity(_upperBound) && y >= _upperBound) return CapCrossing();
            return _threshold + Math.Pow(y / _alpha, 1d / _beta);
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Stores the parameters and refreshes <see cref="ParametersValid"/> without throwing (the
        /// Numerics function idiom: invalid parameters fail loudly on first evaluation).
        /// </summary>
        /// <param name="alpha">The scale coefficient a.</param>
        /// <param name="beta">The exponent b.</param>
        /// <param name="threshold">The hazard threshold h₀.</param>
        /// <param name="upperBound">The saturation cap U.</param>
        private void StoreParameters(double alpha, double beta, double threshold, double upperBound)
        {
            _alpha = alpha;
            _beta = beta;
            _threshold = threshold;
            _upperBound = upperBound;
            _parametersValid = ValidateParameters(new[] { alpha, beta, threshold, upperBound }, throwException: false) == null;
        }

        /// <summary>
        /// Throws when the stored parameters are invalid — the evaluation-time gate promised by
        /// <see cref="ParametersValid"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the parameters are invalid.</exception>
        private void ThrowIfParametersInvalid()
        {
            if (!_parametersValid)
                ValidateParameters(new[] { _alpha, _beta, _threshold, _upperBound }, throwException: true);
        }

        /// <summary>
        /// Computes the saturation crossing h₀ + (U/a)^(1/b) for a finite cap, or
        /// <see cref="double.MaxValue"/> when there is no cap (the interface's unbounded
        /// convention for <see cref="Maximum"/>).
        /// </summary>
        /// <returns>The smallest hazard at which the function reaches the cap, or <see cref="double.MaxValue"/>.</returns>
        private double CapCrossing()
        {
            if (double.IsPositiveInfinity(_upperBound)) return double.MaxValue;
            return _threshold + Math.Pow(_upperBound / _alpha, 1d / _beta);
        }

        #endregion
    }
}
