using System;
using System.Xml.Linq;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.Analyses
{
    /// <summary>
    /// One declared consequence type on a risk analysis's ordered consequence-type axis: the
    /// type label (e.g., "Damages") and its unit (e.g., "$").
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The consequence-type axis is declared at the analysis level (Phase 6.5, user-ratified):
    /// the primary type is the analysis's <c>SpecifiedConsequence</c>/<c>ConsequenceUnit</c>
    /// scalar pair, and each additional type is one of these descriptors, in declared order —
    /// entry k − 1 of <c>RiskAnalysis.AdditionalConsequenceTypes</c> declares consequence
    /// position k. Validation strictly matches every failure and non-failure path against the
    /// declaration; the descriptor itself is immutable label metadata (replace an entry to edit
    /// it — the <c>RiskConnection</c> idiom), so it needs no change notification and is
    /// thread-safe by construction.
    /// </para>
    /// <para>
    /// The attribute names reuse the audited label strip rules
    /// (<see cref="CanonicalizationRules"/>): a declared type can never enter a canonical hash,
    /// so the axis can never gate or rewire compute for a valid model — validity may depend on
    /// metadata consistency while results identity depends only on compute-relevant content.
    /// </para>
    /// </remarks>
    public sealed class ConsequenceTypeDescriptor
    {
        #region Construction

        /// <summary>
        /// Initializes a declared consequence type.
        /// </summary>
        /// <param name="specifiedConsequence">The consequence type label (e.g., "Damages"). Null coerces to empty.</param>
        /// <param name="consequenceUnit">The consequence unit label (e.g., "$"). Null coerces to empty.</param>
        public ConsequenceTypeDescriptor(string? specifiedConsequence, string? consequenceUnit)
        {
            SpecifiedConsequence = specifiedConsequence ?? string.Empty;
            ConsequenceUnit = consequenceUnit ?? string.Empty;
        }

        /// <summary>
        /// Restores a declared consequence type from its serialized form. Missing attributes
        /// fall back to empty labels, so older forms load forward.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public ConsequenceTypeDescriptor(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            SpecifiedConsequence = SerializationUtilities.ReadString(xElement, nameof(SpecifiedConsequence));
            ConsequenceUnit = SerializationUtilities.ReadString(xElement, nameof(ConsequenceUnit));
        }

        #endregion

        #region Members

        /// <summary>
        /// The declared consequence type label (e.g., "Damages"). Never null; blank declares a
        /// wildcard position that matches any function label.
        /// </summary>
        public string SpecifiedConsequence { get; }

        /// <summary>
        /// The declared consequence unit label (e.g., "$"). Never null; blank declares a
        /// wildcard position that matches any function unit.
        /// </summary>
        public string ConsequenceUnit { get; }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes the declared consequence type. The attribute names match the audited label
        /// strip rules, so the axis is persistence-only metadata everywhere it appears.
        /// </summary>
        /// <returns>The serialized form.</returns>
        public XElement ToXElement()
        {
            var element = new XElement(nameof(ConsequenceTypeDescriptor));
            element.SetAttributeValue(nameof(SpecifiedConsequence), SpecifiedConsequence);
            element.SetAttributeValue(nameof(ConsequenceUnit), ConsequenceUnit);
            return element;
        }

        #endregion
    }
}
