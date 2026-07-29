using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Systems.Components.Graph
{
    /// <summary>
    /// One hazard signal available at an element's position in the graph: the upstream element
    /// (and output port) producing it, its chain position, and its advisory display labels.
    /// </summary>
    /// <param name="Element">The upstream element producing the signal (the hazard element or a transform element).</param>
    /// <param name="OutputPort">The producing output port; 0 until bivariate hazards are introduced.</param>
    /// <param name="ChainPosition">The chain position of the signal: 0 is the raw hazard, k is the signal after the k-th transform on the path.</param>
    /// <param name="HazardLabel">The hazard-type display label of the signal (e.g., "Peak Flow"). Advisory metadata — never load-bearing.</param>
    /// <param name="HazardUnit">The hazard-unit display label of the signal (e.g., "ft³/s"). Advisory metadata — never load-bearing.</param>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Returned by <c>ComponentGraph.GetAvailableHazardSources</c> — the discoverability API that
    /// UI and agentic callers use to offer binding choices (e.g., "compute consequences from Peak
    /// Flow"), and that binding validation reuses so the picker and the validator can never
    /// disagree. The element reference and chain position are the structural facts; the labels
    /// exist only for display.
    /// </para>
    /// </remarks>
    public sealed record HazardSourceOption(IRiskElement Element, int OutputPort, int ChainPosition, string HazardLabel, string HazardUnit);
}
