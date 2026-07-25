using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Tests.RiskFunctions.Responses;

namespace RMC.TotalRisk.Tests.Core;

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

        // Phase 2 — core input functions. (NonFailResponse is deliberately absent: it carries no
        // compute content by design; its content-free hashing is pinned in NonFailResponseTests.)
        yield return new RegistryEntry(
            nameof(TabularHazard),
            () => new TabularHazard { Name = "Hazard", SpecifiedHazard = "Stage", HazardUnit = "ft" },
            f => ((TabularHazard)f).UncertaintyValue = FunctionUncertainty.Hazard);

        yield return new RegistryEntry(
            nameof(ParametricUnivariateHazard),
            () => new ParametricUnivariateHazard { Name = "Hazard", SpecifiedHazard = "Flow", HazardUnit = "cfs", ParentDistribution = new Normal(100d, 20d) },
            f => ((ParametricUnivariateHazard)f).PRNGSeed = 999);

        yield return new RegistryEntry(
            nameof(TabularTransform),
            () => new TabularTransform { Name = "Rating", SpecifiedHazard = "Flow", HazardUnit = "cfs", TransformedHazard = "Stage", TransformedHazardUnit = "ft" },
            f => ((TabularTransform)f).UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(2d, new Deterministic(3d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic));

        // Phase 7 — the nonparametric hazard (inputs-only identity: the derived table never
        // serializes, so only input edits can move the hash).
        yield return new RegistryEntry(
            nameof(NonparametricHazard),
            () => new NonparametricHazard { Name = "Graphical", SpecifiedHazard = "Flow", HazardUnit = "cfs" },
            f => ((NonparametricHazard)f).EffectiveRecordLength = 200);

        // Phase 7 — closed-form transforms, registered uncertain so the conditional sigma
        // attribute participates in the hash surface.
        yield return new RegistryEntry(
            nameof(LinearTransform),
            () => new LinearTransform { Name = "Stage shift", SpecifiedHazard = "Flow", HazardUnit = "cfs", TransformedHazard = "Stage", TransformedHazardUnit = "ft", Alpha = 2d, Beta = 0.5d, Sigma = 1.5d, IsUncertain = true },
            f => ((LinearTransform)f).Beta = 2d);

        yield return new RegistryEntry(
            nameof(PowerTransform),
            () => new PowerTransform { Name = "Rating", SpecifiedHazard = "Stage", HazardUnit = "ft", TransformedHazard = "Flow", TransformedHazardUnit = "cfs", Alpha = 5d, Beta = 2d, Xi = 1d, Sigma = 0.1d, IsUncertain = true },
            f => ((PowerTransform)f).IsInverse = !((PowerTransform)f).IsInverse);

        yield return new RegistryEntry(
            nameof(TabularResponse),
            () => new TabularResponse { Name = "Fragility", SpecifiedHazard = "Stage", HazardUnit = "ft" },
            f => ((TabularResponse)f).ProbabilityTransform = Transform.NormalZ);

        yield return new RegistryEntry(
            nameof(ParametricResponse),
            () => new ParametricResponse { Name = "Fragility", SpecifiedHazard = "Stage", HazardUnit = "ft", ParentDistribution = new Normal(10d, 2d) },
            f => ((ParametricResponse)f).EffectiveRecordLength = 77);

        yield return new RegistryEntry(
            nameof(TabularConsequence),
            () => new TabularConsequence { Name = "Damages", SpecifiedHazard = "Stage", HazardUnit = "ft", SpecifiedConsequence = "Damages", ConsequenceUnit = "$" },
            f => ((TabularConsequence)f).HazardTransform = Transform.Logarithmic);

        // Pre-Phase-4 consequence-cluster completion — an uncertain instance so the conditional
        // sigma attributes participate in the hash surface.
        yield return new RegistryEntry(
            nameof(ParametricConsequence),
            () => new ParametricConsequence
            {
                Name = "Life loss",
                SpecifiedHazard = "Stage",
                HazardUnit = "ft",
                SpecifiedConsequence = "Life Loss",
                ConsequenceUnit = "lives",
                Alpha = 10d,
                Beta = 1.5d,
                Threshold = 2d,
                UpperBound = 500d,
                IsUncertain = true,
                SigmaAlpha = 0.3d,
                SigmaBeta = 0.2d,
            },
            f => ((ParametricConsequence)f).Alpha = 2.5d);

        yield return new RegistryEntry(
            nameof(CompositeConsequence),
            () => new CompositeConsequence(new[]
            {
                new WeightedConsequenceFunction(new TabularConsequence { Name = "Day", SpecifiedHazard = "Stage", HazardUnit = "ft", SpecifiedConsequence = "Life Loss", ConsequenceUnit = "lives" }, 0.42d),
                new WeightedConsequenceFunction(new TabularConsequence { Name = "Night", SpecifiedHazard = "Stage", HazardUnit = "ft", SpecifiedConsequence = "Life Loss", ConsequenceUnit = "lives" }, 0.58d),
            })
            {
                Name = "Day/Night",
                SpecifiedHazard = "Stage",
                HazardUnit = "ft",
                SpecifiedConsequence = "Life Loss",
                ConsequenceUnit = "lives",
                CompositeFunctionType = CompositeFunctionType.Average,
            },
            f => ((CompositeConsequence)f).CompositeFunctionType = CompositeFunctionType.Mixture);
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
