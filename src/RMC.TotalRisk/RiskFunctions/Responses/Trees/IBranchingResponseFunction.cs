using System.Collections.Generic;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>A response function that exposes its mutually exclusive modeled end states.</summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public interface IBranchingResponseFunction : IResponseFunction
    {
        /// <summary>Gets the stable modeled branch descriptors.</summary>
        /// <returns>The branches in output-port order.</returns>
        IReadOnlyList<ResponseBranchDescriptor> GetBranches();

        /// <summary>Samples the mean branch-probability curves.</summary>
        /// <returns>The mean branch sample.</returns>
        ResponseBranchSample SampleBranches();

        /// <summary>Samples all branches at one co-monotonic knowledge percentile.</summary>
        /// <param name="percentile">The percentile in (0, 1).</param>
        /// <returns>The percentile branch sample.</returns>
        ResponseBranchSample SampleBranches(double percentile);

        /// <summary>Samples all branches for one pre-allocated realization.</summary>
        /// <param name="realizationIndex">The realization index.</param>
        /// <returns>The realization branch sample.</returns>
        ResponseBranchSample SampleBranches(int realizationIndex);
    }
}
