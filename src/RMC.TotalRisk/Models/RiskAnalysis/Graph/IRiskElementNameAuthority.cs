namespace RMC.TotalRisk.Models.RiskAnalysis.Graph
{
    /// <summary>
    /// The name-uniqueness authority an element consults before accepting a rename — implemented
    /// by <see cref="ComponentGraph"/> and attached to elements as they are added.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Mirrors the Hydrologics <c>IElementNameAuthority</c> pattern: renaming an attached element
    /// to a colliding name throws; the graph offers the non-throwing
    /// <c>TryRenameElement</c>/<c>GetUniqueName</c> paths. Internal because the hook is wiring
    /// between the graph and <see cref="RiskElementBase"/>, not caller API; external
    /// <see cref="IRiskElement"/> implementations that do not derive from the base are backstopped
    /// by the graph's duplicate-name validation instead.
    /// </para>
    /// </remarks>
    internal interface IRiskElementNameAuthority
    {
        /// <summary>
        /// Determines whether a name is available within the authority's scope.
        /// </summary>
        /// <param name="name">The candidate name.</param>
        /// <param name="excluding">An element to exclude from the check (the element being renamed).</param>
        /// <returns>True when no other element holds the name.</returns>
        bool IsNameAvailable(string name, IRiskElement? excluding = null);
    }
}
