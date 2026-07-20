using System;
using System.Xml.Linq;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Systems.Components.Graph
{
    /// <summary>
    /// Reconstructs concrete risk-graph elements from their serialized <see cref="XElement"/>
    /// forms — the graph-level counterpart of the function factory.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Mirrors the Hydrologics <c>BasinElementFactory</c>: a closed switch on the element's local
    /// name (the concrete type name by contract); unknown names return null and the graph
    /// constructor skips them (forward compatibility) — any connections that referenced a skipped
    /// element surface loudly through the Id-authoritative resolver or the dangling-connection
    /// validation. Landing checklist: every new concrete element type adds its case here.
    /// </para>
    /// </remarks>
    public static class RiskElementFactory
    {
        /// <summary>
        /// Reconstructs a concrete risk-graph element from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <returns>The reconstructed element, or null when the local name is not a known element type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IRiskElement? CreateFromXElement(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            return xElement.Name.LocalName switch
            {
                nameof(HazardElement) => new HazardElement(xElement),
                nameof(TransformElement) => new TransformElement(xElement),
                nameof(ResponseElement) => new ResponseElement(xElement),
                nameof(ConsequenceElement) => new ConsequenceElement(xElement),
                _ => null,
            };
        }
    }
}
