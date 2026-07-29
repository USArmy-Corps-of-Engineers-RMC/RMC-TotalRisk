namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The method used to aggregate component risk into system risk when an analysis carries more
    /// than one system component.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The type name and member names are preserved verbatim from v1.0 (the options property that
    /// carries this enum is named <c>SystemRiskMethod</c>, also the v1.0 name). The semantics of
    /// <see cref="AdditiveRiskMethod"/> changed in v1.1
    /// (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §7.8): additive
    /// aggregation now assumes strictly independent components — supplying a hazard dependency or
    /// correlation matrix under the additive method is a validation error — which is exactly the
    /// assumption that lets the system loss exceedance curve be built by FFT convolution of
    /// zero-inflated component curves, a curve v1.0 never produced. Cross-component hazard
    /// dependence belongs to <see cref="JointRiskMethod"/> alone.
    /// </para>
    /// </remarks>
    public enum SystemRiskType
    {
        /// <summary>
        /// Aggregate strictly independent components: each component's risk integrates in one
        /// dimension, system means add, and the system loss exceedance curves are built by FFT
        /// convolution of the zero-inflated component curves.
        /// </summary>
        AdditiveRiskMethod,

        /// <summary>
        /// Integrate the full joint hazard across components with VEGAS adaptive importance
        /// sampling — supports hazard dependence through the multivariate normal copula and the
        /// joint consequence combination rules, and enumerates real component failure/non-failure
        /// combinations.
        /// </summary>
        JointRiskMethod,
    }
}
