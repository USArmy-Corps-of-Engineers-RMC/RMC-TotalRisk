using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>
/// Unit tests for <see cref="SeedHelpers"/> — deterministic seed derivation and the Monte Carlo
/// fallback percentile matrix.
/// </summary>
[TestClass]
public class SeedHelpersTests
{
    /// <summary>Verifies HashCombine is deterministic for identical inputs.</summary>
    [TestMethod]
    public void Test_HashCombine_SameInputs_SameSeed()
    {
        // Arrange
        byte[] hash = { 1, 2, 3, 4, 5 };

        // Act / Assert
        Assert.AreEqual(
            SeedHelpers.HashCombine(12345, hash, 0),
            SeedHelpers.HashCombine(12345, hash, 0));
    }

    /// <summary>Verifies HashCombine is sensitive to each of its three inputs.</summary>
    [TestMethod]
    public void Test_HashCombine_EachInput_ChangesSeed()
    {
        // Arrange
        byte[] hash = { 1, 2, 3, 4, 5 };
        byte[] otherHash = { 1, 2, 3, 4, 6 };
        int baseline = SeedHelpers.HashCombine(12345, hash, 0);

        // Act / Assert
        Assert.AreNotEqual(baseline, SeedHelpers.HashCombine(12346, hash, 0), "Seed input must matter.");
        Assert.AreNotEqual(baseline, SeedHelpers.HashCombine(12345, otherHash, 0), "Content hash must matter.");
        Assert.AreNotEqual(baseline, SeedHelpers.HashCombine(12345, hash, 1), "Index must matter.");
    }

    /// <summary>Verifies a null content hash throws.</summary>
    [TestMethod]
    public void Test_HashCombine_NullHash_Throws()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => SeedHelpers.HashCombine(1, null!, 0));
    }

    /// <summary>Verifies the independent-uniform matrix has the requested shape with draws in (0, 1).</summary>
    [TestMethod]
    public void Test_IndependentUniform_ShapeAndRange()
    {
        // Act
        double[,] matrix = SeedHelpers.IndependentUniform(50, 3, 12345);

        // Assert
        Assert.AreEqual(50, matrix.GetLength(0));
        Assert.AreEqual(3, matrix.GetLength(1));
        for (int i = 0; i < 50; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Assert.IsTrue(matrix[i, j] > 0d && matrix[i, j] < 1d, $"Draw [{i},{j}] out of (0,1).");
            }
        }
    }

    /// <summary>Verifies the matrix is reproducible per seed and differs across seeds.</summary>
    [TestMethod]
    public void Test_IndependentUniform_SeedDeterminism()
    {
        // Act
        double[,] a = SeedHelpers.IndependentUniform(20, 2, 12345);
        double[,] b = SeedHelpers.IndependentUniform(20, 2, 12345);
        double[,] c = SeedHelpers.IndependentUniform(20, 2, 54321);

        // Assert — bit-identical for the same seed; different stream for a different seed.
        bool anyDifferentAcrossSeeds = false;
        for (int i = 0; i < 20; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                Assert.AreEqual(a[i, j], b[i, j], 0d);
                anyDifferentAcrossSeeds |= a[i, j] != c[i, j];
            }
        }
        Assert.IsTrue(anyDifferentAcrossSeeds, "Different seeds must produce different draws.");
    }

    /// <summary>Verifies non-positive dimensions are rejected.</summary>
    [TestMethod]
    public void Test_IndependentUniform_NonPositiveArguments_Throw()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => SeedHelpers.IndependentUniform(0, 1, 1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => SeedHelpers.IndependentUniform(1, 0, 1));
    }

    /// <summary>
    /// Verifies the positive-seed fold maps the full int range into [1, int.MaxValue] — the
    /// Numerics Latin hypercube samplers treat non-positive seeds as "use the wall clock", which
    /// would silently destroy reproducibility.
    /// </summary>
    [TestMethod]
    public void Test_ToPositiveSeed_FullRange_MapsPositive()
    {
        // Arrange — the edge and representative cases across the int range.
        int[] seeds = { int.MinValue, -12345, -1, 0, 1, 12345, int.MaxValue };

        // Act / Assert
        foreach (int seed in seeds)
        {
            int folded = SeedHelpers.ToPositiveSeed(seed);
            Assert.IsTrue(folded >= 1, $"Seed {seed} folded to non-positive {folded}.");
        }
    }

    /// <summary>Verifies the fold is deterministic and preserves already-positive seeds' identity of stream selection.</summary>
    [TestMethod]
    public void Test_ToPositiveSeed_Deterministic()
    {
        // Act / Assert — same input, same fold; distinct inputs stay distinct for typical values.
        Assert.AreEqual(SeedHelpers.ToPositiveSeed(-987654), SeedHelpers.ToPositiveSeed(-987654));
        Assert.AreEqual(12346, SeedHelpers.ToPositiveSeed(12345), "A positive seed folds to seed + 1 under the modular map.");
        Assert.AreNotEqual(SeedHelpers.ToPositiveSeed(1), SeedHelpers.ToPositiveSeed(2));
    }
}
