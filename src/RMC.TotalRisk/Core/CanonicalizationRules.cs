using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// The audited rule set that reduces a model type's persisted XML form to its compute-relevant
    /// canonical content: attribute and element names to strip (identity, display, and presentation
    /// metadata), plus optional structural rewrites applied before stripping.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Canonical content hashing (see <see cref="CanonicalContentHasher"/>) rides the mandatory
    /// <c>ToXElement()</c> serialization contract: the persistence surface IS the compute-configuration
    /// surface, so new properties join the hash automatically. This class is the single audit point
    /// deciding what does NOT belong to compute content. The list is APPEND-ONLY: every new
    /// non-compute property lands here AND in the kitchen-sink hash-invariance test, and stripped
    /// names are never removed or repurposed — hashes are contract.
    /// </para>
    /// <para>
    /// <b>Stripped (identity/display):</b> <c>Name</c>, <c>Description</c> — renaming or
    /// re-describing a model object must never change its content hash or re-roll Monte Carlo seeds.
    /// <b>Stripped (axis labels):</b> <c>SpecifiedHazard</c>, <c>HazardUnit</c>,
    /// <c>TransformedHazard</c>, <c>TransformedHazardUnit</c>, <c>SpecifiedConsequence</c>,
    /// <c>ConsequenceUnit</c> — labels describe the axes, not the math.
    /// <b>Stripped (UI-envelope, defensive):</b> <c>NameOnDisk</c>, <c>Guid</c>, <c>LeftPosition</c>,
    /// <c>TopPosition</c>, <c>ChartSettings</c> — the model library never writes these, but the
    /// future UI layer wraps model XML in project-tree envelopes; stripping them here guarantees a
    /// UI-wrapped form still hashes to the same content (the v1.0 canvas-position seed bug can
    /// never return).
    /// </para>
    /// <para>
    /// Owned child-element ORDER is preserved by the hasher and is therefore semantic content:
    /// reordering owned sub-items (e.g., table ordinates) is a compute edit that re-rolls draws.
    /// </para>
    /// </remarks>
    public sealed class CanonicalizationRules
    {
        /// <summary>
        /// Backing set of attribute names removed everywhere in the subtree (ordinal match).
        /// </summary>
        private readonly HashSet<string> _strippedAttributes;

        /// <summary>
        /// Backing set of element names removed everywhere in the subtree (ordinal match).
        /// </summary>
        private readonly HashSet<string> _strippedElements;

        /// <summary>
        /// Backing list of structural rewrites applied to the working copy before stripping, in order.
        /// </summary>
        private readonly List<Action<XElement>> _rewriters;

        /// <summary>
        /// Backing field for the audited model rule set.
        /// </summary>
        private static readonly CanonicalizationRules _modelRules = new CanonicalizationRules(
            strippedAttributes: new[]
            {
                // Identity and display metadata.
                "Name", "Description",
                // Axis labels — they describe units and hazard/consequence types, not the math.
                "SpecifiedHazard", "HazardUnit", "TransformedHazard", "TransformedHazardUnit",
                "SpecifiedConsequence", "ConsequenceUnit",
                // UI-envelope attributes stripped defensively (never written by the model library).
                "NameOnDisk", "Guid", "LeftPosition", "TopPosition", "ChartSettings",
            },
            strippedElements: Array.Empty<string>(),
            rewriters: Array.Empty<Action<XElement>>());

        /// <summary>
        /// Initializes a new rule set.
        /// </summary>
        /// <param name="strippedAttributes">Attribute names removed everywhere in the subtree (ordinal match).</param>
        /// <param name="strippedElements">Element names removed everywhere in the subtree (ordinal match).</param>
        /// <param name="rewriters">Structural rewrites applied to the working copy before stripping, in order.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public CanonicalizationRules(IEnumerable<string> strippedAttributes,
            IEnumerable<string> strippedElements, IEnumerable<Action<XElement>> rewriters)
        {
            if (strippedAttributes == null) throw new ArgumentNullException(nameof(strippedAttributes));
            if (strippedElements == null) throw new ArgumentNullException(nameof(strippedElements));
            if (rewriters == null) throw new ArgumentNullException(nameof(rewriters));

            _strippedAttributes = new HashSet<string>(strippedAttributes, StringComparer.Ordinal);
            _strippedElements = new HashSet<string>(strippedElements, StringComparer.Ordinal);
            _rewriters = new List<Action<XElement>>(rewriters);
        }

        /// <summary>
        /// Gets the audited rule set for model types — the single exclusion list backing the
        /// library's content-based seed-identity contract.
        /// </summary>
        public static CanonicalizationRules ModelRules => _modelRules;

        /// <summary>
        /// Gets the attribute names removed everywhere in the subtree.
        /// </summary>
        public IReadOnlySet<string> StrippedAttributes => _strippedAttributes;

        /// <summary>
        /// Gets the element names removed everywhere in the subtree.
        /// </summary>
        public IReadOnlySet<string> StrippedElements => _strippedElements;

        /// <summary>
        /// Gets the structural rewrites applied before stripping, in order.
        /// </summary>
        public IReadOnlyList<Action<XElement>> Rewriters => _rewriters;
    }
}
