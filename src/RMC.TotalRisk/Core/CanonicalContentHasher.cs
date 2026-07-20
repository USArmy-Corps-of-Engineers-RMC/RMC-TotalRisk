using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// Computes SHA-256 canonical content hashes over a model type's <c>ToXElement()</c> form,
    /// after applying <see cref="CanonicalizationRules"/> — the basis of the library's name-free,
    /// content-based Monte Carlo seed-identity contract.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Pipeline: deep-copy the persisted form → apply the rule set's structural rewrites → strip
    /// identity/display/presentation attributes and elements → emit self-describing canonical
    /// bytes → SHA-256. The byte emission is a length-prefixed binary encoding (never
    /// <c>XElement.ToString()</c>), so the hash is independent of any XML formatting or writer
    /// behavior: element and attribute names and values are written as <see cref="BinaryWriter"/>
    /// length-prefixed UTF-8 strings, attributes are sorted ordinally by name (attribute order is
    /// not semantic), child nodes keep document order (owned-collection order IS semantic), and
    /// node counts plus type markers make the encoding injective — two different canonical trees
    /// can never produce the same byte stream.
    /// </para>
    /// <para>
    /// Doubles participate as their persisted "G17" invariant-culture text
    /// (<see cref="SerializationUtilities.FormatDouble(double)"/>), which is bit-faithful: distinct
    /// IEEE-754 values, including negative zero, NaN, and infinities, always produce distinct text.
    /// </para>
    /// <para>
    /// Cost: one small allocation-bearing pass per model object, intended for analysis-setup time
    /// only — never call inside per-realization loops.
    /// </para>
    /// <para>
    ///     <b>References:</b>
    ///     RMC (2026). RMC-TotalRisk Model Library Architecture, §5.5 Canonical hashing and
    ///     content-based seeding (v0.6 XML-canonicalization mechanism, adapted from the
    ///     Hydrologics implementation of the same specification).
    /// </para>
    /// </remarks>
    public static class CanonicalContentHasher
    {
        /// <summary>
        /// Marker byte opening an element node in the canonical stream.
        /// </summary>
        private const byte ElementMarker = 0x01;

        /// <summary>
        /// Marker byte opening a text node in the canonical stream.
        /// </summary>
        private const byte TextMarker = 0x02;

        /// <summary>
        /// Marker byte closing an element node in the canonical stream.
        /// </summary>
        private const byte EndMarker = 0x03;

        /// <summary>
        /// Hashes a model type's persisted XML form to its canonical SHA-256 content hash.
        /// </summary>
        /// <param name="persistedForm">The model type's <c>ToXElement()</c> output; not modified.</param>
        /// <param name="rules">The canonicalization rule set to apply.</param>
        /// <returns>The 32-byte SHA-256 hash of the canonical content.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
        public static byte[] Hash(XElement persistedForm, CanonicalizationRules rules)
        {
            if (persistedForm == null) throw new ArgumentNullException(nameof(persistedForm));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            var working = new XElement(persistedForm);
            for (int i = 0; i < rules.Rewriters.Count; i++)
            {
                rules.Rewriters[i](working);
            }
            Strip(working, rules);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteCanonical(working, writer);
            }
            stream.Position = 0;
            return SHA256.HashData(stream);
        }

        /// <summary>
        /// Formats a content hash as lowercase hexadecimal — the text form used for dictionary keys
        /// and diagnostics.
        /// </summary>
        /// <param name="hash">The hash bytes.</param>
        /// <returns>The lowercase hex string (64 characters for SHA-256).</returns>
        /// <exception cref="ArgumentNullException">Thrown when the hash is null.</exception>
        public static string ToTokenHex(byte[] hash)
        {
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            return Convert.ToHexStringLower(hash);
        }

        /// <summary>
        /// Removes stripped attributes and elements everywhere in the working subtree.
        /// </summary>
        /// <param name="element">The working-copy root, mutated in place.</param>
        /// <param name="rules">The rule set naming the strips.</param>
        private static void Strip(XElement element, CanonicalizationRules rules)
        {
            if (rules.StrippedElements.Count > 0)
            {
                element.Descendants()
                    .Where(d => rules.StrippedElements.Contains(d.Name.LocalName))
                    .Remove();
            }
            foreach (var node in element.DescendantsAndSelf())
            {
                node.Attributes()
                    .Where(a => rules.StrippedAttributes.Contains(a.Name.LocalName))
                    .Remove();
            }
        }

        /// <summary>
        /// Writes one element node in the injective canonical encoding: marker, tag name, ordinally
        /// sorted attributes with counts, then child nodes in document order.
        /// </summary>
        /// <param name="element">The element to encode.</param>
        /// <param name="writer">The canonical stream writer.</param>
        private static void WriteCanonical(XElement element, BinaryWriter writer)
        {
            writer.Write(ElementMarker);
            writer.Write(element.Name.LocalName);

            var attributes = element.Attributes()
                .OrderBy(a => a.Name.LocalName, StringComparer.Ordinal)
                .ToList();
            writer.Write(attributes.Count);
            for (int i = 0; i < attributes.Count; i++)
            {
                writer.Write(attributes[i].Name.LocalName);
                writer.Write(attributes[i].Value);
            }

            foreach (var node in element.Nodes())
            {
                switch (node)
                {
                    case XElement child:
                        WriteCanonical(child, writer);
                        break;
                    case XText text:
                        writer.Write(TextMarker);
                        writer.Write(text.Value);
                        break;
                    default:
                        // Comments and processing instructions are never produced by ToXElement
                        // implementations and carry no compute content.
                        break;
                }
            }
            writer.Write(EndMarker);
        }
    }
}
