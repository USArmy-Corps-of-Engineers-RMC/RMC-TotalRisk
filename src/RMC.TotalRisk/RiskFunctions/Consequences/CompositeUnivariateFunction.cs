using System;
using System.Collections.Generic;
using Numerics.Functions;

namespace RMC.TotalRisk.RiskFunctions.Consequences
{
    /// <summary>
    /// A pointwise-exact weighted combination of already-sampled child curves:
    /// C(x) = max(0, Σ wᵢ·fᵢ(x)) — the per-realization result of a
    /// <see cref="CompositeConsequence"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Improved over v1.0, which snapshotted the combine onto a tabular grid over the union of the
    /// child knots: this adapter evaluates the children pointwise at every query, so it agrees
    /// with the legacy snapshot exactly at every union knot, is exact between knots (where the
    /// snapshot linearly interpolated), and stays exact when a child has no knots at all (a
    /// parametric consequence curve is not piecewise linear). The engine only ever evaluates the
    /// returned function pointwise, so nothing requires a tabular snapshot. The negative clamp is
    /// the legacy composite clamp. Should an upstream Numerics <c>CompositeFunction</c>
    /// expansion cover this combine, this adapter is a candidate to fold into it.
    /// </para>
    /// <para>
    /// A Mixture realization passes a single selected child with weight one, so every composite
    /// mode returns the same adapter shape. Additive passes weights of one per child.
    /// </para>
    /// </remarks>
    public class CompositeUnivariateFunction : IUnivariateFunction
    {
        #region Construction

        /// <summary>
        /// Initializes the combine over already-sampled child curves.
        /// </summary>
        /// <param name="functions">The child curves; at least one, no nulls.</param>
        /// <param name="weights">The weight per child, aligned with <paramref name="functions"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when either array is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the arrays are empty, differ in length, or contain a null child.
        /// </exception>
        public CompositeUnivariateFunction(IUnivariateFunction[] functions, double[] weights)
        {
            if (functions == null) throw new ArgumentNullException(nameof(functions));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (functions.Length == 0) throw new ArgumentException("At least one child function is required.", nameof(functions));
            if (functions.Length != weights.Length) throw new ArgumentException("The weights must align one-to-one with the child functions.", nameof(weights));
            for (int i = 0; i < functions.Length; i++)
            {
                if (functions[i] == null) throw new ArgumentException("The child functions cannot contain null entries.", nameof(functions));
            }

            _functions = functions;
            _weights = weights;
        }

        #endregion

        #region Members

        /// <summary>
        /// The child curves being combined.
        /// </summary>
        private readonly IUnivariateFunction[] _functions;

        /// <summary>
        /// The weight per child, aligned with <see cref="_functions"/>.
        /// </summary>
        private readonly double[] _weights;

        /// <summary>
        /// Backing field for <see cref="ConfidenceLevel"/> — stored only to honor the interface;
        /// the combine is over already-realized child curves, so the value never affects
        /// evaluation.
        /// </summary>
        private double _confidenceLevel = -1;

        /// <inheritdoc/>
        /// <remarks>Zero — the combine has no parameters of its own; they live on the children.</remarks>
        public int NumberOfParameters => 0;

        /// <inheritdoc/>
        public bool ParametersValid
        {
            get
            {
                for (int i = 0; i < _functions.Length; i++)
                {
                    if (!_functions[i].ParametersValid) return false;
                }
                return true;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The child envelope: the smallest child minimum. Derived, so the setter throws.
        /// </remarks>
        /// <exception cref="NotSupportedException">Always thrown by the setter.</exception>
        public double Minimum
        {
            get
            {
                double minimum = double.MaxValue;
                for (int i = 0; i < _functions.Length; i++)
                {
                    if (_functions[i].Minimum < minimum) minimum = _functions[i].Minimum;
                }
                return minimum;
            }
            set { throw new NotSupportedException("The minimum is the child-function envelope; it cannot be set on the combine."); }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The child envelope: the largest child maximum. Derived, so the setter throws.
        /// </remarks>
        /// <exception cref="NotSupportedException">Always thrown by the setter.</exception>
        public double Maximum
        {
            get
            {
                double maximum = double.MinValue;
                for (int i = 0; i < _functions.Length; i++)
                {
                    if (_functions[i].Maximum > maximum) maximum = _functions[i].Maximum;
                }
                return maximum;
            }
            set { throw new NotSupportedException("The maximum is the child-function envelope; it cannot be set on the combine."); }
        }

        /// <inheritdoc/>
        /// <remarks>Empty — the combine has no parameters of its own.</remarks>
        public double[] MinimumOfParameters => Array.Empty<double>();

        /// <inheritdoc/>
        /// <remarks>Empty — the combine has no parameters of its own.</remarks>
        public double[] MaximumOfParameters => Array.Empty<double>();

        /// <inheritdoc/>
        /// <remarks>
        /// Always true — the children are already-realized curves. Setting false is rejected
        /// loudly rather than ignored.
        /// </remarks>
        /// <exception cref="NotSupportedException">Thrown when set to false.</exception>
        public bool IsDeterministic
        {
            get { return true; }
            set
            {
                if (!value) throw new NotSupportedException("The composite combine holds already-realized child curves and is always deterministic.");
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Stored but inert: per the interface contract the confidence level only applies "when
        /// the function has uncertainty", and the combine never does.
        /// </remarks>
        public double ConfidenceLevel
        {
            get { return _confidenceLevel; }
            set { _confidenceLevel = value; }
        }

        #endregion

        #region Methods

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">Always thrown — the combine has no parameters of its own.</exception>
        public void SetParameters(IList<double> parameters)
        {
            throw new NotSupportedException("The composite combine has no parameters of its own; set parameters on the child functions.");
        }

        /// <inheritdoc/>
        /// <remarks>Always returns null — the combine has no parameters of its own to validate.</remarks>
        public ArgumentOutOfRangeException? ValidateParameters(IList<double> parameters, bool throwException)
        {
            return null;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The weighted pointwise sum of the child curves, clamped at zero (the legacy composite
        /// clamp — combined consequences are never negative).
        /// </remarks>
        public double Function(double x)
        {
            double sum = 0d;
            for (int i = 0; i < _functions.Length; i++)
            {
                sum += _weights[i] * _functions[i].Function(x);
            }
            return sum < 0d ? 0d : sum;
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// Always thrown — a weighted sum of consequence curves need not be monotonic, so a
        /// root-find could silently return an arbitrary crossing. Invert the individual child
        /// functions instead.
        /// </exception>
        public double InverseFunction(double y)
        {
            throw new NotSupportedException("Composite consequence curves need not be monotonic; invert the individual child functions instead.");
        }

        #endregion
    }
}
