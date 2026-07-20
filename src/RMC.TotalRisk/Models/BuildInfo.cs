using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RMC.TotalRisk.Tests")]

namespace RMC.TotalRisk.Models
{
    /// <summary>
    /// Build metadata seed proving the Phase 0 project wire-up. Retired once real model
    /// content lands (Phase 1+).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    internal static class BuildInfo
    {
        /// <summary>The RMC-TotalRisk release line this library targets.</summary>
        public const string Version = "1.1.0";

        /// <summary>The product name.</summary>
        public const string Product = "RMC-TotalRisk";
    }
}
