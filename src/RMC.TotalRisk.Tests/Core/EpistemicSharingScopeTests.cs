using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Sampling;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>
/// Unit tests for <see cref="EpistemicSharingScope"/> — the ambient shared-epistemic-variable
/// scope: enter/restore nesting, column lookup, and the deterministic per-variable column
/// derivation.
/// </summary>
[TestClass]
public class EpistemicSharingScopeTests
{
    /// <summary>Verifies enter/dispose restores the prior scope, nesting included.</summary>
    [TestMethod]
    public void Test_Enter_RestoresPriorScope()
    {
        // Arrange
        Assert.IsFalse(EpistemicSharingScope.IsActive);
        var outer = new Dictionary<string, double[]> { ["A"] = new[] { 0.25d } };
        var inner = new Dictionary<string, double[]> { ["B"] = new[] { 0.75d } };

        // Act / Assert — nesting saves and restores.
        using (EpistemicSharingScope.Enter(outer))
        {
            Assert.IsTrue(EpistemicSharingScope.IsActive);
            Assert.IsNotNull(EpistemicSharingScope.TryGetColumn("A"));
            using (EpistemicSharingScope.Enter(inner))
            {
                Assert.IsNull(EpistemicSharingScope.TryGetColumn("A"));
                Assert.IsNotNull(EpistemicSharingScope.TryGetColumn("B"));
            }
            Assert.IsNotNull(EpistemicSharingScope.TryGetColumn("A"));
        }
        Assert.IsFalse(EpistemicSharingScope.IsActive);
        Assert.IsNull(EpistemicSharingScope.TryGetColumn("A"));
    }

    /// <summary>Verifies the lookup answers null outside a scope and for unknown variables.</summary>
    [TestMethod]
    public void Test_TryGetColumn_NullOutsideScopeAndForUnknown()
    {
        // Assert — no ambient scope.
        Assert.IsNull(EpistemicSharingScope.TryGetColumn("Anything"));

        // Arrange / Assert — an entered scope answers only its own variables.
        using (EpistemicSharingScope.Enter(new Dictionary<string, double[]> { ["Known"] = new[] { 0.5d } }))
        {
            Assert.IsNotNull(EpistemicSharingScope.TryGetColumn("Known"));
            Assert.IsNull(EpistemicSharingScope.TryGetColumn("Unknown"));
            Assert.IsNull(EpistemicSharingScope.TryGetColumn(string.Empty));
        }
    }

    /// <summary>
    /// Verifies the column derivation: deterministic for identical inputs, bit-equal to the
    /// declared recipe (the base seed folded with the SHA-256 of the variable name through the
    /// scheme generator), and moved by the base seed, the variable name, and the scheme.
    /// </summary>
    [TestMethod]
    public void Test_BuildColumns_DeterministicAndMovedByIdentity()
    {
        // Act
        var first = EpistemicSharingScope.BuildColumns(new[] { "Rating Model" }, 4321, 16, SamplingScheme.LatinHypercube);
        var again = EpistemicSharingScope.BuildColumns(new[] { "Rating Model" }, 4321, 16, SamplingScheme.LatinHypercube);
        var otherSeed = EpistemicSharingScope.BuildColumns(new[] { "Rating Model" }, 4322, 16, SamplingScheme.LatinHypercube);
        var otherName = EpistemicSharingScope.BuildColumns(new[] { "Rating Model B" }, 4321, 16, SamplingScheme.LatinHypercube);
        var otherScheme = EpistemicSharingScope.BuildColumns(new[] { "Rating Model" }, 4321, 16, SamplingScheme.MonteCarlo);

        // Assert — deterministic reproduction and the expected length.
        Assert.AreEqual(16, first["Rating Model"].Length);
        CollectionAssert.AreEqual(first["Rating Model"], again["Rating Model"]);

        // The declared recipe, reproduced independently.
        int seed = SeedHelpers.ToPositiveSeed(SeedHelpers.HashCombine(4321,
            global::System.Security.Cryptography.SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes("Rating Model")), 0));
        var expected = LatinHypercube.Random(16, 1, seed);
        for (int i = 0; i < 16; i++)
        {
            Assert.AreEqual(expected[i, 0], first["Rating Model"][i], 0d);
        }

        // Identity movement.
        CollectionAssert.AreNotEqual(first["Rating Model"], otherSeed["Rating Model"]);
        CollectionAssert.AreNotEqual(first["Rating Model"], otherName["Rating Model B"]);
        CollectionAssert.AreNotEqual(first["Rating Model"], otherScheme["Rating Model"]);
    }

    /// <summary>Verifies the argument guards and the empty/duplicate variable handling.</summary>
    [TestMethod]
    public void Test_BuildColumns_GuardsAndFilters()
    {
        // Assert
        Assert.ThrowsException<ArgumentNullException>(() =>
            EpistemicSharingScope.BuildColumns(null!, 1, 8, SamplingScheme.LatinHypercube));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            EpistemicSharingScope.BuildColumns(new[] { "A" }, 1, 0, SamplingScheme.LatinHypercube));
        Assert.ThrowsException<ArgumentNullException>(() => EpistemicSharingScope.Enter(null!));

        // Empty names are skipped; duplicates collapse to one column.
        var columns = EpistemicSharingScope.BuildColumns(new[] { "", "A", "A" }, 1, 8, SamplingScheme.LatinHypercube);
        Assert.AreEqual(1, columns.Count);
        Assert.IsTrue(columns.ContainsKey("A"));
    }
}
