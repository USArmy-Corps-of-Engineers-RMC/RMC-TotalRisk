using System;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>
/// Unit tests for <see cref="SerializationUtilities"/> — the G17/invariant-culture double contract
/// and permissive attribute reads.
/// </summary>
[TestClass]
public class SerializationUtilitiesTests
{
    /// <summary>Verifies bit-exact round-trip for ordinary and pathological doubles.</summary>
    [TestMethod]
    public void Test_FormatParseDouble_RoundTripsBitExact()
    {
        // Arrange
        double[] values =
        {
            0d, 1d, -1d, Math.PI, 1.0 / 3.0, 12345.6789e-40, double.Epsilon,
            double.MaxValue, double.MinValue, 1e-300, -2.2250738585072014e-308,
        };

        foreach (double value in values)
        {
            // Act
            double roundTripped = SerializationUtilities.ParseDouble(SerializationUtilities.FormatDouble(value));

            // Assert — bit-exact, not tolerance-based.
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(roundTripped),
                $"Value {value:R} did not round-trip bit-exact.");
        }
    }

    /// <summary>Verifies negative zero, NaN, and the infinities round-trip exactly.</summary>
    [TestMethod]
    public void Test_FormatParseDouble_NonFiniteAndNegativeZero_RoundTrip()
    {
        // Act / Assert — negative zero preserves its sign bit.
        double negZero = SerializationUtilities.ParseDouble(SerializationUtilities.FormatDouble(-0d));
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-0d), BitConverter.DoubleToInt64Bits(negZero));

        Assert.IsTrue(double.IsNaN(SerializationUtilities.ParseDouble(SerializationUtilities.FormatDouble(double.NaN))));
        Assert.IsTrue(double.IsPositiveInfinity(SerializationUtilities.ParseDouble(SerializationUtilities.FormatDouble(double.PositiveInfinity))));
        Assert.IsTrue(double.IsNegativeInfinity(SerializationUtilities.ParseDouble(SerializationUtilities.FormatDouble(double.NegativeInfinity))));
    }

    /// <summary>Verifies formatting and parsing ignore a comma-decimal current culture.</summary>
    [TestMethod]
    public void Test_FormatParseDouble_CultureInvariant()
    {
        // Arrange
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            // Act
            string text = SerializationUtilities.FormatDouble(1234.5);
            double parsed = SerializationUtilities.ParseDouble("1234.5");

            // Assert — a period decimal separator regardless of the ambient culture.
            StringAssert.Contains(text, ".");
            Assert.IsFalse(text.Contains(','), "Serialized doubles must never use the culture decimal comma.");
            Assert.AreEqual(1234.5, parsed, 0d);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Verifies permissive parsing falls back to the default on null or garbage.</summary>
    [TestMethod]
    public void Test_ParseDouble_Permissive_Defaults()
    {
        // Act / Assert
        Assert.AreEqual(7.5, SerializationUtilities.ParseDouble(null, 7.5), 0d);
        Assert.AreEqual(7.5, SerializationUtilities.ParseDouble("not-a-number", 7.5), 0d);
        Assert.AreEqual(1000d, SerializationUtilities.ParseDouble("1,000"), 0d); // NumberStyles.Any admits thousands separators
    }

    /// <summary>Verifies the null-safe attribute readers for each supported type.</summary>
    [TestMethod]
    public void Test_AttributeReaders_NullSafeAndPermissive()
    {
        // Arrange
        var element = new XElement("E",
            new XAttribute("D", "2.5"),
            new XAttribute("I", "42"),
            new XAttribute("B", "true"),
            new XAttribute("S", "text"),
            new XAttribute("Scheme", nameof(SamplingScheme.LatinHypercubeMedian)),
            new XAttribute("Bad", "zzz"));

        // Act / Assert — present and parseable.
        Assert.AreEqual(2.5, SerializationUtilities.ReadDouble(element, "D"), 0d);
        Assert.AreEqual(42, SerializationUtilities.ReadInt32(element, "I"));
        Assert.IsTrue(SerializationUtilities.ReadBoolean(element, "B"));
        Assert.AreEqual("text", SerializationUtilities.ReadString(element, "S"));
        Assert.AreEqual(SamplingScheme.LatinHypercubeMedian,
            SerializationUtilities.ReadEnum(element, "Scheme", SamplingScheme.MonteCarlo));

        // Missing attributes and null elements return defaults.
        Assert.AreEqual(-1d, SerializationUtilities.ReadDouble(element, "Missing", -1d), 0d);
        Assert.AreEqual(-2, SerializationUtilities.ReadInt32(null, "I", -2));
        Assert.IsTrue(SerializationUtilities.ReadBoolean(null, "B", true));
        Assert.AreEqual("dft", SerializationUtilities.ReadString(null, "S", "dft"));
        Assert.AreEqual(SamplingScheme.LatinHypercube,
            SerializationUtilities.ReadEnum(null, "Scheme", SamplingScheme.LatinHypercube));

        // Unparseable values return defaults.
        Assert.AreEqual(9, SerializationUtilities.ReadInt32(element, "Bad", 9));
        Assert.IsFalse(SerializationUtilities.ReadBoolean(element, "Bad"));
        Assert.AreEqual(SamplingScheme.MonteCarlo,
            SerializationUtilities.ReadEnum(element, "Bad", SamplingScheme.MonteCarlo));
    }

    /// <summary>
    /// A square matrix round-trips bit-exact through the G17 row/value text format, and the empty
    /// cases collapse to empty text and back to null.
    /// </summary>
    [TestMethod]
    public void Test_FormatParseMatrix_RoundTripsBitExact()
    {
        // Arrange
        var matrix = new double[,]
        {
            { 1d, 0.5d, -0.25d },
            { 0.5d, 1d, 0.1d / 3d },
            { -0.25d, 0.1d / 3d, 1d },
        };

        // Act
        string text = SerializationUtilities.FormatMatrix(matrix);
        double[,]? parsed = SerializationUtilities.ParseMatrix(text);

        // Assert — the format is rows ';'-separated, values ','-separated.
        Assert.AreEqual(2, text.Split(';').Length - 1);
        Assert.IsNotNull(parsed);
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Assert.AreEqual(matrix[i, j], parsed![i, j], 0d);
            }
        }

        // Empty cases.
        Assert.AreEqual(string.Empty, SerializationUtilities.FormatMatrix(null));
        Assert.AreEqual(string.Empty, SerializationUtilities.FormatMatrix(new double[0, 0]));
        Assert.IsNull(SerializationUtilities.ParseMatrix(null));
        Assert.IsNull(SerializationUtilities.ParseMatrix(string.Empty));
    }

    /// <summary>
    /// Parsing is strict: a ragged shape or an unparseable value rejects the whole matrix, so a
    /// caller never computes with a half-read correlation structure.
    /// </summary>
    [TestMethod]
    public void Test_ParseMatrix_RaggedOrUnparseable_ReturnsNull()
    {
        // Assert — a row with the wrong value count.
        Assert.IsNull(SerializationUtilities.ParseMatrix("1,0.5;0.5"));

        // A non-square shape (two rows, three columns).
        Assert.IsNull(SerializationUtilities.ParseMatrix("1,0.5,0.2;0.5,1,0.3"));

        // An unparseable value anywhere rejects the whole matrix.
        Assert.IsNull(SerializationUtilities.ParseMatrix("1,not-a-number;0.5,1"));
    }
}
