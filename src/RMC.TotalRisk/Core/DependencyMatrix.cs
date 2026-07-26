using System;
using Numerics;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// The shared construction constants for the automatic dependency modes' equicorrelated
    /// matrices.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Two places build one of these: the component's failure-mode dependence
    /// (<c>SystemComponent.UpdateMultivariateNormal</c>, which also writes the derived matrix back
    /// to the correlation-matrix field per v1.0) and the analysis's component-hazard dependence
    /// (<c>RiskAnalysis.BuildHazardMultivariateNormal</c>, which extracts a Cholesky factor from
    /// it). Their surrounding code legitimately differs, but the off-diagonal constants are the
    /// same modelling decision and are reproduced against v1.0 — so they live here rather than in
    /// two switch statements that could drift apart unnoticed.
    /// </para>
    /// </remarks>
    internal static class DependencyMatrix
    {
        /// <summary>
        /// The off-diagonal correlation for an automatic dependency mode: zero (independent),
        /// <c>1 − √εmach</c> (perfectly positive), or <c>−1/(D − 1) + √εmach</c> (perfectly
        /// negative — the most negative exchangeable equicorrelation that stays positive
        /// semi-definite).
        /// </summary>
        /// <param name="dependency">The dependency mode; the correlation-matrix mode has no automatic constant.</param>
        /// <param name="dimension">The matrix dimension D.</param>
        /// <returns>The off-diagonal value, or zero for independent and unrecognized modes.</returns>
        /// <remarks>
        /// The √εmach offsets are v1.0's, and are not cosmetic: an exact ±1 correlation is singular,
        /// and the perfectly-negative bound is exactly attained at <c>−1/(D − 1)</c>, so both are
        /// nudged just inside the admissible region.
        /// </remarks>
        internal static double AutomaticOffDiagonal(DependencyType dependency, int dimension)
        {
            switch (dependency)
            {
                case DependencyType.PerfectlyPositive:
                    return 1d - Math.Sqrt(Tools.DoubleMachineEpsilon);
                case DependencyType.PerfectlyNegative:
                    return -1d / (dimension - 1) + Math.Sqrt(Tools.DoubleMachineEpsilon);
                default:
                    return 0d;
            }
        }

        /// <summary>
        /// Fills a square matrix with a unit diagonal and a constant off-diagonal.
        /// </summary>
        /// <param name="target">The matrix to fill, at least <paramref name="dimension"/> square.</param>
        /// <param name="dimension">The matrix dimension D.</param>
        /// <param name="offDiagonal">The off-diagonal correlation.</param>
        /// <exception cref="ArgumentNullException">Thrown when the target matrix is null.</exception>
        internal static void FillEquicorrelated(double[,] target, int dimension, double offDiagonal)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            for (int i = 0; i < dimension; i++)
            {
                for (int j = 0; j < dimension; j++)
                {
                    target[i, j] = i == j ? 1d : offDiagonal;
                }
            }
        }
    }
}
