using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Models.Support;

namespace RMC.TotalRisk.Tests.Models.Support;

/// <summary>
/// Unit tests for <see cref="ByteArrayComparer"/> — the lexicographic hash-sorting comparer.
/// </summary>
[TestClass]
public class ByteArrayComparerTests
{
    /// <summary>Verifies equality cases: reference-equal, element-equal, and both-null.</summary>
    [TestMethod]
    public void Test_Compare_EqualArrays_ReturnZero()
    {
        // Arrange
        byte[] a = { 1, 2, 3 };
        byte[] b = { 1, 2, 3 };

        // Act / Assert
        Assert.AreEqual(0, ByteArrayComparer.Instance.Compare(a, a));
        Assert.AreEqual(0, ByteArrayComparer.Instance.Compare(a, b));
        Assert.AreEqual(0, ByteArrayComparer.Instance.Compare(null, null));
    }

    /// <summary>Verifies null sorts before any non-null array.</summary>
    [TestMethod]
    public void Test_Compare_Null_SortsFirst()
    {
        // Arrange
        byte[] a = { 0 };

        // Act / Assert
        Assert.IsTrue(ByteArrayComparer.Instance.Compare(null, a) < 0);
        Assert.IsTrue(ByteArrayComparer.Instance.Compare(a, null) > 0);
    }

    /// <summary>Verifies lexicographic ordering with a shorter strict prefix sorting first.</summary>
    [TestMethod]
    public void Test_Compare_Lexicographic_Ordering()
    {
        // Arrange
        byte[] prefix = { 1, 2 };
        byte[] longer = { 1, 2, 3 };
        byte[] larger = { 1, 3 };

        // Act / Assert
        Assert.IsTrue(ByteArrayComparer.Instance.Compare(prefix, longer) < 0, "Prefix sorts before its extension.");
        Assert.IsTrue(ByteArrayComparer.Instance.Compare(longer, larger) < 0, "Element comparison dominates length.");
        Assert.IsTrue(ByteArrayComparer.Instance.Compare(larger, prefix) > 0);
    }

    /// <summary>Verifies the comparer drives a stable, expected List sort.</summary>
    [TestMethod]
    public void Test_Compare_SortsListAsExpected()
    {
        // Arrange
        var list = new List<byte[]> { new byte[] { 2 }, new byte[] { 1, 9 }, new byte[] { 1 } };

        // Act
        list.Sort(ByteArrayComparer.Instance);

        // Assert
        CollectionAssert.AreEqual(new byte[] { 1 }, list[0]);
        CollectionAssert.AreEqual(new byte[] { 1, 9 }, list[1]);
        CollectionAssert.AreEqual(new byte[] { 2 }, list[2]);
    }
}
