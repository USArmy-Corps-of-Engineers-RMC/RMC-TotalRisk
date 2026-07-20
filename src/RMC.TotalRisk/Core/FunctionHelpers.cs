using System;
using Numerics;
using Numerics.Data;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// Shared numerical helpers for sampled risk-function curves.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from the v1.0 <c>FunctionHelpers</c> with the BinaryFormatter-based
    /// <c>GenerateSeedFromObject</c> dropped — content-based seeding now derives from
    /// <see cref="CanonicalContentHasher"/> + <see cref="SeedHelpers"/>.
    /// </para>
    /// </remarks>
    public static class FunctionHelpers
    {
        /// <summary>
        /// Forces a sampled curve to be strictly monotonic per its declared X and Y sort orders by
        /// nudging each violating ordinate minimally past its predecessor in a single forward pass.
        /// Curves without a declared order on either axis are left untouched.
        /// </summary>
        /// <param name="opd">The sampled curve, mutated in place.</param>
        /// <exception cref="ArgumentNullException">Thrown when the curve is null.</exception>
        /// <remarks>
        /// <para>
        /// <b>Improved over v1.0.</b> The legacy repair nudged by the absolute machine epsilon
        /// (≈1.11e−16), which is (a) not representable at magnitudes ≥ 1 — at real hazard scales
        /// (stages, flows) the nudge rounded away and the violation survived — and (b) exactly the
        /// tolerance the <see cref="Ordinate"/> equality operator treats as "equal", so the
        /// tolerance-checking ordinate setter silently discarded tie repairs at any scale. v1.1
        /// nudges to the further of one representable step (<see cref="Math.BitIncrement(double)"/> /
        /// <see cref="Math.BitDecrement(double)"/>) and two machine epsilons past the predecessor:
        /// scale-appropriate, minimal, strictly ordered at every magnitude, and always distinct
        /// enough to land through the tolerance-equal setter.
        /// </para>
        /// <para>
        /// The violation checks match the four v1.0 sort-order branches exactly; only the nudge
        /// arithmetic is improved.
        /// </para>
        /// </remarks>
        public static void ForceMonotonic(OrderedPairedData opd)
        {
            if (opd is null) throw new ArgumentNullException(nameof(opd));

            int directionX = Direction(opd.OrderX);
            int directionY = Direction(opd.OrderY);
            if (directionX == 0 || directionY == 0) return;

            for (int i = 1; i < opd.Count; i++)
            {
                bool xViolates = directionX > 0 ? opd[i].X <= opd[i - 1].X : opd[i].X >= opd[i - 1].X;
                bool yViolates = directionY > 0 ? opd[i].Y <= opd[i - 1].Y : opd[i].Y >= opd[i - 1].Y;
                if (xViolates || yViolates)
                {
                    double x = xViolates ? Nudge(opd[i - 1].X, directionX) : opd[i].X;
                    double y = yViolates ? Nudge(opd[i - 1].Y, directionY) : opd[i].Y;
                    opd[i] = new Ordinate(x, y);
                }
            }
        }

        /// <summary>
        /// Maps a sort order onto a monotonic direction: +1 ascending, −1 descending, 0 none.
        /// </summary>
        /// <param name="order">The declared sort order.</param>
        /// <returns>The direction sign.</returns>
        private static int Direction(SortOrder order)
        {
            return order == SortOrder.Ascending ? 1 : order == SortOrder.Descending ? -1 : 0;
        }

        /// <summary>
        /// Returns the minimal value strictly past <paramref name="value"/> in the given direction
        /// that also clears the ordinate-equality tolerance.
        /// </summary>
        /// <param name="value">The predecessor value to move past.</param>
        /// <param name="direction">+1 to move up, −1 to move down.</param>
        /// <returns>The nudged value.</returns>
        /// <remarks>
        /// The further of one representable step and two machine epsilons: the representable step
        /// dominates at large magnitudes (where an absolute epsilon rounds away), the two-epsilon
        /// floor dominates near zero (where one ulp is smaller than the ordinate-equality
        /// tolerance).
        /// </remarks>
        private static double Nudge(double value, int direction)
        {
            return direction > 0
                ? Math.Max(Math.BitIncrement(value), value + 2d * Tools.DoubleMachineEpsilon)
                : Math.Min(Math.BitDecrement(value), value - 2d * Tools.DoubleMachineEpsilon);
        }
    }
}
