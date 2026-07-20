using System;
using System.Xml.Linq;
using RMC.TotalRisk.Core.Enums;
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
        /// <param name="resolver">
        /// The function resolver, required only when the form was written
        /// <see cref="RiskSerializationMode.ByReference"/>; null for self-contained forms.
        /// </param>
        /// <returns>The reconstructed element, or null when the local name is not a known element type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IRiskElement? CreateFromXElement(XElement xElement, IRiskFunctionResolver? resolver = null)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            return xElement.Name.LocalName switch
            {
                nameof(HazardElement) => new HazardElement(xElement, resolver),
                nameof(TransformElement) => new TransformElement(xElement, resolver),
                nameof(ResponseElement) => new ResponseElement(xElement, resolver),
                nameof(ConsequenceElement) => new ConsequenceElement(xElement, resolver),
                _ => null,
            };
        }

        /// <summary>
        /// Creates the risk-graph element that carries a given input function, wrapping it: an
        /// <see cref="IHazardFunction"/> yields a <see cref="HazardElement"/>, and so on for each
        /// cluster.
        /// </summary>
        /// <param name="function">The function to wrap.</param>
        /// <param name="name">The element name; defaults to the function's name when omitted.</param>
        /// <returns>The element wrapping the function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the function is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the function belongs to no known cluster.</exception>
        /// <remarks>
        /// The authoring entry point for graph editors: it removes the need for a caller to map
        /// function clusters onto element types itself, which is the mapping most likely to drift
        /// as new clusters land.
        /// </remarks>
        public static IRiskElement CreateForFunction(IRiskFunction function, string? name = null)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));

            RiskElementBase element = function switch
            {
                IHazardFunction hazard => new HazardElement { Function = hazard },
                ITransformFunction transform => new TransformElement { Function = transform },
                IResponseFunction response => new ResponseElement { Function = response },
                IConsequenceFunction consequence => CreateConsequenceElement(consequence),
                _ => throw new ArgumentException(
                    $"The risk function type '{function.GetType().Name}' belongs to no known cluster and has no graph element.",
                    nameof(function)),
            };

            element.Name = string.IsNullOrEmpty(name) ? function.Name : name!;
            return element;
        }

        /// <summary>
        /// Creates an empty element of the given role, for editors that place a node first and
        /// assign its function afterwards.
        /// </summary>
        /// <param name="elementType">The role the element plays in the compute chain.</param>
        /// <param name="name">The element name; empty when omitted.</param>
        /// <returns>The created element, with no function assigned.</returns>
        /// <exception cref="ArgumentException">Thrown when the role is not a known element type.</exception>
        public static IRiskElement Create(RiskElementType elementType, string? name = null)
        {
            RiskElementBase element = elementType switch
            {
                RiskElementType.Hazard => new HazardElement(),
                RiskElementType.Transform => new TransformElement(),
                RiskElementType.Response => new ResponseElement(),
                RiskElementType.Consequence => new ConsequenceElement(),
                _ => throw new ArgumentException($"Unknown risk element type '{elementType}'.", nameof(elementType)),
            };

            element.Name = name ?? string.Empty;
            return element;
        }

        /// <summary>
        /// Creates a consequence element seeded with its first ordered consequence function.
        /// </summary>
        /// <param name="consequence">The primary consequence function (ordered index 0).</param>
        /// <returns>The consequence element.</returns>
        private static ConsequenceElement CreateConsequenceElement(IConsequenceFunction consequence)
        {
            var element = new ConsequenceElement();
            element.Functions.Add(consequence);
            return element;
        }
    }
}
