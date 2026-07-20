using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Models.Support;

namespace RMC.TotalRisk.Tests.Models.Support;

/// <summary>
/// The kitchen-sink hash-invariance registry — the landing-checklist guard for the content-based
/// seed-identity contract. EVERY concrete model type registers one entry here as it lands
/// (a factory producing a representative configured instance plus one compute-relevant mutation);
/// the tests then prove, for every registered type: metadata edits are hash-inert, every audited
/// stripped attribute is inert anywhere in the serialized tree, serialization is deterministic,
/// and a compute edit moves the hash.
/// </summary>
[TestClass]
public class HashInvarianceKitchenSinkTests
{
    /// <summary>
    /// One registry entry per concrete model type: a display name, a factory for a representative
    /// instance, and a compute-relevant mutation of that instance.
    /// </summary>
    /// <param name="TypeName">The display name reported on assert failures.</param>
    /// <param name="Factory">Creates a representative configured instance.</param>
    /// <param name="ComputeMutation">Mutates compute-relevant state on the instance.</param>
    public sealed record RegistryEntry(string TypeName, Func<IRiskFunction> Factory, Action<IRiskFunction> ComputeMutation);

    /// <summary>
    /// The registry. PHASE LANDING CHECKLIST: add one entry per new concrete function type.
    /// (Phase 3's SystemComponent/FailureMode assert the same contract in their own test classes
    /// via the delegate-based <see cref="HashInvariance"/> helpers.)
    /// </summary>
    private static IEnumerable<RegistryEntry> RegisteredTypes()
    {
        // Phase 1 — the kernel stub proving the scaffold itself.
        yield return new RegistryEntry(
            nameof(StubRiskFunction),
            () => new StubRiskFunction { Name = "Stub", Description = "Rep", SpecifiedHazard = "Flow", HazardUnit = "cfs", Value = 2.5 },
            f => ((StubRiskFunction)f).Value = 99.5);
    }

    /// <summary>Verifies metadata edits (rename/re-describe/relabel) never move any registered type's hash.</summary>
    [TestMethod]
    public void Test_KitchenSink_MetadataEdits_HashInvariant()
    {
        foreach (var entry in RegisteredTypes())
        {
            // Arrange
            var instance = entry.Factory();

            // Act / Assert
            try
            {
                HashInvariance.AssertMetadataInvariant(instance);
            }
            catch (Exception ex)
            {
                Assert.Fail($"{entry.TypeName}: {ex.Message}");
            }
        }
    }

    /// <summary>Verifies every audited stripped attribute is inert on every registered type's serialized form.</summary>
    [TestMethod]
    public void Test_KitchenSink_StrippedAttributes_Inert()
    {
        foreach (var entry in RegisteredTypes())
        {
            // Arrange
            var instance = entry.Factory();

            // Act / Assert
            try
            {
                HashInvariance.AssertStrippedAttributesInert(instance.ToXElement());
            }
            catch (Exception ex)
            {
                Assert.Fail($"{entry.TypeName}: {ex.Message}");
            }
        }
    }

    /// <summary>Verifies serialization (and therefore hashing) is deterministic per instance.</summary>
    [TestMethod]
    public void Test_KitchenSink_Hash_Deterministic()
    {
        foreach (var entry in RegisteredTypes())
        {
            // Arrange
            var instance = entry.Factory();

            // Act / Assert — repeated hashing of the same state is bit-identical, and two
            // independently built representative instances hash alike.
            CollectionAssert.AreEqual(instance.CanonicalHash(), instance.CanonicalHash(), $"{entry.TypeName}: hash not stable.");
            CollectionAssert.AreEqual(instance.CanonicalHash(), entry.Factory().CanonicalHash(),
                $"{entry.TypeName}: two identically configured instances must hash alike.");
        }
    }

    /// <summary>Verifies a compute-relevant edit moves every registered type's hash.</summary>
    [TestMethod]
    public void Test_KitchenSink_ComputeEdit_MovesHash()
    {
        foreach (var entry in RegisteredTypes())
        {
            // Arrange
            var instance = entry.Factory();

            // Act / Assert
            try
            {
                HashInvariance.AssertComputeSensitive(instance.CanonicalHash, () => entry.ComputeMutation(instance));
            }
            catch (Exception ex)
            {
                Assert.Fail($"{entry.TypeName}: {ex.Message}");
            }
        }
    }
}
