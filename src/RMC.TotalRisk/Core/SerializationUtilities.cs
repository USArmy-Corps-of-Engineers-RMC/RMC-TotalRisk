using System;
using System.Globalization;
using System.Xml.Linq;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// Formatting and parsing helpers for the model library's XElement serialization contract:
    /// doubles are written round-trip-exact with "G17" and <see cref="CultureInfo.InvariantCulture"/>,
    /// and reads are permissive (null-safe attribute access, <c>TryParse</c> with
    /// <see cref="NumberStyles.Any"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// <c>ToXElement()</c> output is also the canonical-hash identity surface, so the exact text
    /// produced here is contract: "G17" with the invariant culture round-trips every IEEE-754
    /// double — including negative zero, NaN, the infinities, and denormals — to distinct,
    /// bit-faithful text. Never format serialized doubles any other way.
    /// </para>
    /// </remarks>
    public static class SerializationUtilities
    {
        /// <summary>
        /// Formats a double as round-trip-exact invariant-culture text ("G17").
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>The "G17" invariant-culture text for the value.</returns>
        public static string FormatDouble(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses a double from serialized text, permissively.
        /// </summary>
        /// <param name="text">The text to parse; may be null.</param>
        /// <param name="defaultValue">The value returned when the text is null or unparseable.</param>
        /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
        public static double ParseDouble(string? text, double defaultValue = 0d)
        {
            if (text == null) return defaultValue;
            // NumberStyles.Any does not admit the "NaN"/"Infinity" literals G17 emits for
            // non-finite values, so those round-trip through double.Parse's default styles first.
            if (string.Equals(text, "NaN", StringComparison.Ordinal)) return double.NaN;
            if (string.Equals(text, "Infinity", StringComparison.Ordinal)) return double.PositiveInfinity;
            if (string.Equals(text, "-Infinity", StringComparison.Ordinal)) return double.NegativeInfinity;
            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double result)
                ? result
                : defaultValue;
        }

        /// <summary>
        /// Reads a double attribute from an element, permissively.
        /// </summary>
        /// <param name="element">The element to read from; may be null.</param>
        /// <param name="attributeName">The attribute name.</param>
        /// <param name="defaultValue">The value returned when the attribute is missing or unparseable.</param>
        /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
        public static double ReadDouble(XElement? element, string attributeName, double defaultValue = 0d)
        {
            return ParseDouble(element?.Attribute(attributeName)?.Value, defaultValue);
        }

        /// <summary>
        /// Reads an integer attribute from an element, permissively.
        /// </summary>
        /// <param name="element">The element to read from; may be null.</param>
        /// <param name="attributeName">The attribute name.</param>
        /// <param name="defaultValue">The value returned when the attribute is missing or unparseable.</param>
        /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
        public static int ReadInt32(XElement? element, string attributeName, int defaultValue = 0)
        {
            string? text = element?.Attribute(attributeName)?.Value;
            if (text == null) return defaultValue;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : defaultValue;
        }

        /// <summary>
        /// Reads a boolean attribute from an element, permissively.
        /// </summary>
        /// <param name="element">The element to read from; may be null.</param>
        /// <param name="attributeName">The attribute name.</param>
        /// <param name="defaultValue">The value returned when the attribute is missing or unparseable.</param>
        /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
        public static bool ReadBoolean(XElement? element, string attributeName, bool defaultValue = false)
        {
            string? text = element?.Attribute(attributeName)?.Value;
            if (text == null) return defaultValue;
            return bool.TryParse(text, out bool result) ? result : defaultValue;
        }

        /// <summary>
        /// Reads a string attribute from an element.
        /// </summary>
        /// <param name="element">The element to read from; may be null.</param>
        /// <param name="attributeName">The attribute name.</param>
        /// <param name="defaultValue">The value returned when the attribute is missing.</param>
        /// <returns>The attribute value, or <paramref name="defaultValue"/>.</returns>
        public static string ReadString(XElement? element, string attributeName, string defaultValue = "")
        {
            return element?.Attribute(attributeName)?.Value ?? defaultValue;
        }

        /// <summary>
        /// Reads an enum attribute from an element, permissively.
        /// </summary>
        /// <typeparam name="TEnum">The enum type to parse.</typeparam>
        /// <param name="element">The element to read from; may be null.</param>
        /// <param name="attributeName">The attribute name.</param>
        /// <param name="defaultValue">The value returned when the attribute is missing or unparseable.</param>
        /// <returns>The parsed enum value, or <paramref name="defaultValue"/>.</returns>
        public static TEnum ReadEnum<TEnum>(XElement? element, string attributeName, TEnum defaultValue)
            where TEnum : struct, Enum
        {
            string? text = element?.Attribute(attributeName)?.Value;
            if (text == null) return defaultValue;
            return Enum.TryParse(text, ignoreCase: false, out TEnum result) ? result : defaultValue;
        }
    }
}
