using RMC.TotalRisk.Core.Interfaces;
namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The sampling scheme used to draw knowledge-uncertainty percentiles for each risk function.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Each function pre-allocates an N×D percentile matrix in
    /// <see cref="IRiskFunction.SetupSampler(int, int, SamplingScheme)"/>, where N is the number of
    /// realizations and D is the function's intrinsic sampling dimension. The scheme controls how
    /// that matrix is filled. Switching schemes is the only edit that changes Monte Carlo results
    /// without changing the underlying math, so it is exposed as an explicit analysis option.
    /// </para>
    /// </remarks>
    public enum SamplingScheme
    {
        /// <summary>
        /// Independent uniform draws; legacy v1.0 behavior. Standard error scales as 1/√N.
        /// </summary>
        MonteCarlo,

        /// <summary>
        /// Latin hypercube sampling with random placement within bins (unbiased). The default:
        /// stratifying each marginal typically cuts variance 5–50× at the same realization count.
        /// </summary>
        LatinHypercube,

        /// <summary>
        /// Latin hypercube sampling with median bin centers; deterministic per seed and useful at
        /// very small realization counts.
        /// </summary>
        LatinHypercubeMedian,
    }
}
