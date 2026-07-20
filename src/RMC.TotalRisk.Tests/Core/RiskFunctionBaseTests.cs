using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>
/// Unit tests for <see cref="RiskFunctionBase"/> via <see cref="StubRiskFunction"/>: property-change
/// notification, the canonical-hash pipeline, and the percentile sampler machinery.
/// </summary>
[TestClass]
public class RiskFunctionBaseTests
{
    /// <summary>Verifies INPC raises for each metadata property, and only on real changes.</summary>
    [TestMethod]
    public void Test_PropertyChange_RaisesOncePerRealChange()
    {
        // Arrange
        var stub = new StubRiskFunction();
        var raised = new List<string>();
        stub.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        // Act
        stub.Name = "A";
        stub.Name = "A";                 // unchanged — no raise
        stub.Description = "D";
        stub.SpecifiedHazard = "Stage";
        stub.HazardUnit = "ft";
        stub.Value = 2.0;

        // Assert
        CollectionAssert.AreEqual(
            new[] { nameof(stub.Name), nameof(stub.Description), nameof(stub.SpecifiedHazard), nameof(stub.HazardUnit), nameof(stub.Value) },
            raised);
    }

    /// <summary>Verifies metadata edits never move the canonical hash (the seed-identity contract).</summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataInvariant()
    {
        // Arrange
        var stub = new StubRiskFunction { Name = "Original", Description = "Desc", SpecifiedHazard = "Flow", HazardUnit = "cfs" };

        // Act / Assert
        HashInvariance.AssertMetadataInvariant(stub);
        HashInvariance.AssertStrippedAttributesInert(stub.ToXElement());
    }

    /// <summary>Verifies a compute-relevant edit moves the canonical hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_ComputeSensitive()
    {
        // Arrange
        var stub = new StubRiskFunction();

        // Act / Assert
        HashInvariance.AssertComputeSensitive(stub.CanonicalHash, () => stub.Value = 3.14);
    }

    /// <summary>Verifies the sampler allocates an N×D matrix with draws in (0, 1) for every scheme.</summary>
    [TestMethod]
    public void Test_SetupSampler_AllSchemes_ShapeAndRange()
    {
        // Arrange
        var stub = new StubRiskFunction { Dimensions = 3 };

        foreach (SamplingScheme scheme in Enum.GetValues<SamplingScheme>())
        {
            // Act
            stub.SetupSampler(40, 12345, scheme);

            // Assert
            Assert.AreEqual(40, stub.SampleSize);
            for (int i = 0; i < 40; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double p = stub.PercentileAt(i, j);
                    Assert.IsTrue(p > 0d && p < 1d, $"{scheme} draw [{i},{j}] out of (0,1).");
                }
            }
        }
    }

    /// <summary>Verifies Latin hypercube stratification: exactly one draw per 1/N bin per column.</summary>
    [TestMethod]
    public void Test_SetupSampler_LatinHypercube_StratifiesEachColumn()
    {
        // Arrange
        const int n = 100;
        var stub = new StubRiskFunction { Dimensions = 2 };

        // Act
        stub.SetupSampler(n, 12345, SamplingScheme.LatinHypercube);

        // Assert — each column occupies every stratification bin exactly once.
        for (int j = 0; j < 2; j++)
        {
            var occupied = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int bin = (int)(stub.PercentileAt(i, j) * n);
                Assert.IsFalse(occupied[bin], $"Column {j} bin {bin} occupied twice — not stratified.");
                occupied[bin] = true;
            }
        }
    }

    /// <summary>Verifies per-seed determinism (bit-identical), including negative content-derived seeds.</summary>
    [TestMethod]
    public void Test_SetupSampler_SeedDeterminism_IncludingNegativeSeeds()
    {
        // Arrange
        var a = new StubRiskFunction { Dimensions = 2 };
        var b = new StubRiskFunction { Dimensions = 2 };
        var c = new StubRiskFunction { Dimensions = 2 };

        foreach (int seed in new[] { 12345, -98765, int.MinValue, 0 })
        {
            // Act
            a.SetupSampler(25, seed, SamplingScheme.LatinHypercube);
            b.SetupSampler(25, seed, SamplingScheme.LatinHypercube);
            c.SetupSampler(25, seed == 0 ? 1 : seed / 2, SamplingScheme.LatinHypercube);

            // Assert
            bool anyDiffer = false;
            for (int i = 0; i < 25; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    Assert.AreEqual(a.PercentileAt(i, j), b.PercentileAt(i, j), 0d, $"Seed {seed} not reproducible.");
                    anyDiffer |= a.PercentileAt(i, j) != c.PercentileAt(i, j);
                }
            }
            Assert.IsTrue(anyDiffer, $"Distinct seeds should give distinct matrices (seed {seed}).");
        }
    }

    /// <summary>Verifies the Monte Carlo scheme produces a different matrix than Latin hypercube.</summary>
    [TestMethod]
    public void Test_SetupSampler_MonteCarlo_DiffersFromLatinHypercube()
    {
        // Arrange
        var mc = new StubRiskFunction { Dimensions = 1 };
        var lhs = new StubRiskFunction { Dimensions = 1 };

        // Act
        mc.SetupSampler(30, 12345, SamplingScheme.MonteCarlo);
        lhs.SetupSampler(30, 12345, SamplingScheme.LatinHypercube);

        // Assert
        bool anyDiffer = false;
        for (int i = 0; i < 30; i++)
        {
            anyDiffer |= mc.PercentileAt(i, 0) != lhs.PercentileAt(i, 0);
        }
        Assert.IsTrue(anyDiffer);
    }

    /// <summary>Verifies re-setup is idempotent: same arguments reproduce the same matrix.</summary>
    [TestMethod]
    public void Test_SetupSampler_Idempotent()
    {
        // Arrange
        var stub = new StubRiskFunction { Dimensions = 2 };
        stub.SetupSampler(15, 777, SamplingScheme.LatinHypercubeMedian);
        var first = new double[15, 2];
        for (int i = 0; i < 15; i++)
            for (int j = 0; j < 2; j++)
                first[i, j] = stub.PercentileAt(i, j);

        // Act
        stub.SetupSampler(15, 777, SamplingScheme.LatinHypercubeMedian);

        // Assert
        for (int i = 0; i < 15; i++)
            for (int j = 0; j < 2; j++)
                Assert.AreEqual(first[i, j], stub.PercentileAt(i, j), 0d);
    }

    /// <summary>Verifies dimension-zero functions allocate no matrix but record the sample size.</summary>
    [TestMethod]
    public void Test_SetupSampler_ZeroDimensions_RecordsSampleSizeOnly()
    {
        // Arrange
        var stub = new StubRiskFunction { Dimensions = 0 };

        // Act
        stub.SetupSampler(500, 12345, SamplingScheme.LatinHypercube);

        // Assert
        Assert.AreEqual(500, stub.SampleSize);
        Assert.IsTrue(stub.IsDeterministic);
        Assert.ThrowsException<InvalidOperationException>(() => stub.PercentileAt(0, 0));
    }

    /// <summary>Verifies argument validation and the pre-setup sampling guard.</summary>
    [TestMethod]
    public void Test_SetupSampler_Guards()
    {
        // Arrange
        var stub = new StubRiskFunction { Dimensions = 1 };

        // Act / Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => stub.SetupSampler(0, 1, SamplingScheme.LatinHypercube));
        Assert.ThrowsException<InvalidOperationException>(() => stub.PercentileAt(0, 0));
    }
}
