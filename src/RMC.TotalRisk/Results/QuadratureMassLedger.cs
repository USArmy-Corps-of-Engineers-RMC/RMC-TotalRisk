using System;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// The probability mass each abscissa of an adaptive Gauss–Kronrod pass carries, taken from
    /// the composite rule's own quadrature weights.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The integrator records only the nodes of ACCEPTED intervals, so an evaluation absent from
    /// the ledger belongs to an interval the refinement superseded and carries no measure. Weights
    /// are summed per abscissa: a hazard curve that saturates collapses several stratification bins
    /// onto one probability, and a zero-width bin is then evaluated repeatedly at the same point
    /// with zero weight.
    /// </para>
    /// <para>
    /// Populated by a single-threaded integration pass and read afterwards; not thread-safe.
    /// </para>
    /// </remarks>
    public sealed class QuadratureMassLedger
    {
        /// <summary>
        /// Initializes an empty ledger.
        /// </summary>
        /// <param name="capacity">The expected node count.</param>
        public QuadratureMassLedger(int capacity = 8192)
        {
            int initial = capacity < 64 ? 64 : capacity;
            _abscissas = new double[initial];
            _weights = new double[initial];
        }

        /// <summary>The recorded abscissas, sorted once <see cref="Seal"/> has run.</summary>
        private double[] _abscissas;

        /// <summary>The weights parallel to <see cref="_abscissas"/>.</summary>
        private double[] _weights;

        /// <summary>The number of entries in use.</summary>
        private int _count;

        /// <summary>The compensation term of the running weight total.</summary>
        private double _compensation;

        /// <summary>Whether <see cref="Seal"/> has run.</summary>
        private bool _sealed;

        /// <summary>
        /// The sum of every recorded weight, accumulated with compensated summation. Equals the
        /// width of the integration domain when the recorded set is complete.
        /// </summary>
        public double TotalWeight { get; private set; }

        /// <summary>
        /// The number of recorded nodes, before duplicate abscissas are coalesced.
        /// </summary>
        public int NodeCount => _count;

        /// <summary>
        /// The number of distinct abscissas; valid only after <see cref="Seal"/>.
        /// </summary>
        public int DistinctAbscissaCount { get; private set; }

        /// <summary>
        /// The <c>AdaptiveGaussKronrod.Recorder</c> callback.
        /// </summary>
        /// <param name="abscissa">The node's position.</param>
        /// <param name="weight">The node's quadrature weight, scaled by its interval's half-length.</param>
        /// <param name="value">The integrand value there; unused — the engine's integrand already consumed it.</param>
        /// <exception cref="InvalidOperationException">Thrown when the ledger has already been sealed.</exception>
        public void Record(double abscissa, double weight, double value)
        {
            if (_sealed) throw new InvalidOperationException("The quadrature ledger is sealed and cannot record further nodes.");
            if (_count == _abscissas.Length)
            {
                Array.Resize(ref _abscissas, _count * 2);
                Array.Resize(ref _weights, _count * 2);
            }
            _abscissas[_count] = abscissa;
            _weights[_count] = weight;
            _count++;

            // Neumaier compensation: the total gates the recorded set against the domain width, so
            // it must not accumulate the drift of a naive sum over tens of thousands of nodes.
            double sum = TotalWeight + weight;
            _compensation += Math.Abs(TotalWeight) >= Math.Abs(weight)
                ? (TotalWeight - sum) + weight
                : (weight - sum) + TotalWeight;
            TotalWeight = sum;
        }

        /// <summary>
        /// Sorts and coalesces the recorded nodes. Call once, after the integration returns.
        /// </summary>
        public void Seal()
        {
            if (_sealed) return;
            _sealed = true;
            TotalWeight += _compensation;
            _compensation = 0d;
            if (_count == 0)
            {
                DistinctAbscissaCount = 0;
                return;
            }

            Array.Sort(_abscissas, _weights, 0, _count);

            int write = 0;
            for (int read = 1; read < _count; read++)
            {
                if (_abscissas[read] == _abscissas[write])
                {
                    _weights[write] += _weights[read];
                }
                else
                {
                    write++;
                    _abscissas[write] = _abscissas[read];
                    _weights[write] = _weights[read];
                }
            }
            DistinctAbscissaCount = write + 1;
        }

        /// <summary>
        /// The mass at an abscissa, or zero when it was never accepted.
        /// </summary>
        /// <param name="abscissa">The abscissa to look up; matched exactly.</param>
        /// <returns>The mass.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the ledger has not been sealed.</exception>
        public double MassAt(double abscissa)
        {
            TryGetMass(abscissa, out double mass);
            return mass;
        }

        /// <summary>
        /// Looks up an abscissa, reporting whether the quadrature accepted it. Distinguishes an
        /// accepted node of zero weight — a degenerate stratification bin produces those — from
        /// one the refinement superseded.
        /// </summary>
        /// <param name="abscissa">The abscissa to look up; matched exactly.</param>
        /// <param name="mass">Receives the mass, or zero when the abscissa was not accepted.</param>
        /// <returns>True when the abscissa was accepted.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the ledger has not been sealed.</exception>
        public bool TryGetMass(double abscissa, out double mass)
        {
            if (!_sealed) throw new InvalidOperationException("The quadrature ledger must be sealed before it is read.");
            int index = Array.BinarySearch(_abscissas, 0, DistinctAbscissaCount, abscissa);
            mass = index >= 0 ? _weights[index] : 0d;
            return index >= 0;
        }
    }
}
