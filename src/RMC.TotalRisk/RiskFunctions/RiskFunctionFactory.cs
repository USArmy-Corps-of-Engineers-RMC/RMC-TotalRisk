using System;
using System.Xml.Linq;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.RiskFunctions
{
    /// <summary>
    /// Reconstructs concrete risk functions from their serialized <see cref="XElement"/> forms —
    /// the model-library-wide factory behind self-contained container serialization (failure
    /// modes, response stages, and graph elements own their functions inline).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Mirrors the Hydrologics <c>BasinElementFactory</c> pattern: a closed switch on the
    /// element's local name, which by the serialization contract is the concrete type name.
    /// Unknown names return null so each caller chooses its own failure policy — graph-level
    /// callers skip unknown elements gracefully (forward compatibility), while compute-chain
    /// callers (<c>FailureMode</c>, <c>ResponseStage</c>) treat an unreconstructable child as
    /// corruption and throw, because silently dropping a function would change results.
    /// </para>
    /// <para>
    /// Landing checklist: every new concrete function type adds its case here, alongside its
    /// kitchen-sink hash-invariance registry entry and its Ported Types Matrix row.
    /// </para>
    /// </remarks>
    public static class RiskFunctionFactory
    {
        /// <summary>
        /// Reconstructs a concrete risk function from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <returns>The reconstructed function, or null when the local name is not a known concrete type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IRiskFunction? CreateFromXElement(XElement xElement)
        {
            return CreateFromXElement(xElement, null);
        }

        /// <summary>
        /// Reconstructs a concrete risk function from its serialized form, resolving any
        /// by-reference children through the supplied resolver.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <param name="resolver">
        /// The function resolver handed to container function types whose serialized children may
        /// be <c>FunctionReference</c> markers (composite functions). Leaf types ignore it; null
        /// reads self-contained forms.
        /// </param>
        /// <returns>The reconstructed function, or null when the local name is not a known concrete type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IRiskFunction? CreateFromXElement(XElement xElement, IRiskFunctionResolver? resolver)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            return xElement.Name.LocalName switch
            {
                nameof(TabularHazard) => new TabularHazard(xElement),
                nameof(ParametricUnivariateHazard) => new ParametricUnivariateHazard(xElement),
                nameof(NonparametricHazard) => new NonparametricHazard(xElement),
                nameof(CompositeHazard) => new CompositeHazard(xElement, resolver),
                nameof(TabularTransform) => new TabularTransform(xElement),
                nameof(LinearTransform) => new LinearTransform(xElement),
                nameof(PowerTransform) => new PowerTransform(xElement),
                nameof(CompositeTransform) => new CompositeTransform(xElement, resolver),
                nameof(TabularResponse) => new TabularResponse(xElement),
                nameof(ParametricResponse) => new ParametricResponse(xElement),
                nameof(NonFailResponse) => new NonFailResponse(xElement),
                nameof(CompositeResponse) => new CompositeResponse(xElement, resolver),
                nameof(TabularConsequence) => new TabularConsequence(xElement),
                nameof(ParametricConsequence) => new ParametricConsequence(xElement),
                nameof(CompositeConsequence) => new CompositeConsequence(xElement, resolver),
                _ => null,
            };
        }

        /// <summary>
        /// Reconstructs a hazard function from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <returns>The reconstructed hazard function, or null when the local name is unknown or names a non-hazard type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IHazardFunction? CreateHazardFunction(XElement xElement)
        {
            return CreateFromXElement(xElement) as IHazardFunction;
        }

        /// <summary>
        /// Reconstructs a hazard function from its serialized form, resolving any by-reference
        /// children through the supplied resolver.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <param name="resolver">
        /// The function resolver handed to container hazard types (composite functions); leaf types
        /// ignore it, and null reads self-contained forms.
        /// </param>
        /// <returns>The reconstructed hazard function, or null when the local name is unknown or names a non-hazard type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IHazardFunction? CreateHazardFunction(XElement xElement, IRiskFunctionResolver? resolver)
        {
            return CreateFromXElement(xElement, resolver) as IHazardFunction;
        }

        /// <summary>
        /// Reconstructs a transform function from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <returns>The reconstructed transform function, or null when the local name is unknown or names a non-transform type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static ITransformFunction? CreateTransformFunction(XElement xElement)
        {
            return CreateFromXElement(xElement) as ITransformFunction;
        }

        /// <summary>
        /// Reconstructs a transform function from its serialized form, resolving any by-reference
        /// children through the supplied resolver.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <param name="resolver">
        /// The function resolver handed to container transform types (composite functions); leaf
        /// types ignore it, and null reads self-contained forms.
        /// </param>
        /// <returns>The reconstructed transform function, or null when the local name is unknown or names a non-transform type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static ITransformFunction? CreateTransformFunction(XElement xElement, IRiskFunctionResolver? resolver)
        {
            return CreateFromXElement(xElement, resolver) as ITransformFunction;
        }

        /// <summary>
        /// Reconstructs a response function from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <returns>The reconstructed response function, or null when the local name is unknown or names a non-response type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IResponseFunction? CreateResponseFunction(XElement xElement)
        {
            return CreateFromXElement(xElement) as IResponseFunction;
        }

        /// <summary>
        /// Reconstructs a response function from its serialized form, resolving any by-reference
        /// children through the supplied resolver.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <param name="resolver">
        /// The function resolver handed to container response types (composite functions); leaf
        /// types ignore it, and null reads self-contained forms.
        /// </param>
        /// <returns>The reconstructed response function, or null when the local name is unknown or names a non-response type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IResponseFunction? CreateResponseFunction(XElement xElement, IRiskFunctionResolver? resolver)
        {
            return CreateFromXElement(xElement, resolver) as IResponseFunction;
        }

        /// <summary>
        /// Reconstructs a consequence function from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <returns>The reconstructed consequence function, or null when the local name is unknown or names a non-consequence type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IConsequenceFunction? CreateConsequenceFunction(XElement xElement)
        {
            return CreateFromXElement(xElement) as IConsequenceFunction;
        }

        /// <summary>
        /// Reconstructs a consequence function from its serialized form, resolving any
        /// by-reference children through the supplied resolver.
        /// </summary>
        /// <param name="xElement">The serialized form; the local name selects the concrete type.</param>
        /// <param name="resolver">
        /// The function resolver handed to container consequence types (composite functions);
        /// leaf types ignore it, and null reads self-contained forms.
        /// </param>
        /// <returns>The reconstructed consequence function, or null when the local name is unknown or names a non-consequence type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public static IConsequenceFunction? CreateConsequenceFunction(XElement xElement, IRiskFunctionResolver? resolver)
        {
            return CreateFromXElement(xElement, resolver) as IConsequenceFunction;
        }
    }
}
