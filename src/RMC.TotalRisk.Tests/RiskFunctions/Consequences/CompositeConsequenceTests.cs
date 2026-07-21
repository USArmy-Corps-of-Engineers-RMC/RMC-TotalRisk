using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.RiskFunctions.Consequences;

/// <summary>
/// Unit tests for <see cref="CompositeConsequence"/> — v1.0 defaults and combination semantics
/// (Additive/Average/Mixture), deterministic RNG-free sampling, content-derived child seeding,
/// dual-mode serialization over the shared function-entry contract, projected-identity hashing,
/// the validation matrix, and the observable weighted-entry wiring.
/// </summary>
[TestClass]
public class CompositeConsequenceTests
{
    /// <summary>The standard axis labels shared by composites and children in these tests.</summary>
    private static void Label(IConsequenceFunction function)
    {
        function.SpecifiedHazard = "Stage";
        function.HazardUnit = "ft";
        function.SpecifiedConsequence = "Life Loss";
        function.ConsequenceUnit = "lives";
    }

    /// <summary>Builds a labeled deterministic child: (0 → 0), (10 → value), linear between.</summary>
    private static TabularConsequence DeterministicChild(string name, double valueAtTen)
    {
        var child = new TabularConsequence
        {
            Name = name,
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(10d, new Deterministic(valueAtTen)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
        Label(child);
        return child;
    }

    /// <summary>Builds a labeled uncertain child: (0 → Deterministic(0)), (10 → Normal(mean, sd)).</summary>
    private static TabularConsequence NormalChild(string name, double mean, double sd)
    {
        var child = new TabularConsequence
        {
            Name = name,
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Normal(0d, 0d)), new UncertainOrdinate(10d, new Normal(mean, sd)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal),
        };
        Label(child);
        return child;
    }

    /// <summary>Builds a labeled composite over the given (child, weight) pairs.</summary>
    private static CompositeConsequence Composite(CompositeFunctionType type, params (IConsequenceFunction Function, double Weight)[] entries)
    {
        var composite = new CompositeConsequence(entries.Select(e => new WeightedConsequenceFunction(e.Function, e.Weight)))
        {
            Name = "Composite",
            CompositeFunctionType = type,
        };
        Label(composite);
        return composite;
    }

    /// <summary>Verifies the v1.0 default construction state (Mixture, empty).</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var c = new CompositeConsequence();

        // Assert
        Assert.AreEqual(CompositeFunctionType.Mixture, c.CompositeFunctionType);
        Assert.AreEqual(0, c.ConsequenceFunctions.Count);
        Assert.AreEqual(ConsequenceFunctionType.Composite, c.FunctionType);
        Assert.AreEqual(1, c.SamplingDimensions, "Mixture mode owns one selector dimension.");
        Assert.IsTrue(c.IsDeterministic, "No positively weighted branches and no children.");
    }

    /// <summary>Verifies the convenience constructor wires entries and their subscriptions.</summary>
    [TestMethod]
    public void Test_Ctor_FromEnumerable_WiresEntries()
    {
        // Arrange
        var day = DeterministicChild("Day", 100d);
        var composite = Composite(CompositeFunctionType.Mixture, (day, 1d));
        var raised = new List<string>();
        composite.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act — a child content edit must reach the composite through the entry subscription.
        day.HazardTransform = Transform.Logarithmic;

        // Assert
        Assert.AreEqual(1, composite.ConsequenceFunctions.Count);
        CollectionAssert.Contains(raised, nameof(CompositeConsequence.ConsequenceFunctions));
    }

    /// <summary>Verifies the validation matrix: labels, children, weights, and the warning downgrade.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // A labeled Average composite with matching children and weights summing to one is clean.
        var valid = Composite(CompositeFunctionType.Average,
            (DeterministicChild("Day", 100d), 0.42d), (DeterministicChild("Night", 300d), 0.58d));
        var (ok, okMessages) = valid.Validate();
        Assert.IsTrue(ok);
        Assert.AreEqual(0, okMessages.Count);

        // Unlabeled and empty: 4 label errors + the no-functions error.
        var empty = new CompositeConsequence();
        var (isValid, messages) = empty.Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(4, messages.Count(m => m.Contains("does not have a specified")));
        Assert.IsTrue(messages.Any(m => m.Contains("No consequence functions have been defined")));

        // A null-function entry is an error.
        var nullChild = Composite(CompositeFunctionType.Average, (DeterministicChild("Day", 100d), 1d));
        nullChild.ConsequenceFunctions.Add(new WeightedConsequenceFunction());
        Assert.IsTrue(nullChild.Validate().ValidationMessages.Any(m => m.Contains("has not been defined for the composite function")));

        // Non-Additive weight rules: out-of-range, then sum-to-one.
        var outOfRange = Composite(CompositeFunctionType.Average, (DeterministicChild("Day", 100d), 1.2d));
        Assert.IsTrue(outOfRange.Validate().ValidationMessages.Any(m => m.Contains("must be between 0 and 1")));
        var badSum = Composite(CompositeFunctionType.Mixture,
            (DeterministicChild("Day", 100d), 0.3d), (DeterministicChild("Night", 300d), 0.3d));
        Assert.IsTrue(badSum.Validate().ValidationMessages.Any(m => m.Contains("do not sum to 1")));

        // Additive ignores the weight rules entirely (weights are coerced to one in compute).
        var additive = Composite(CompositeFunctionType.Additive,
            (DeterministicChild("Day", 100d), 5d), (DeterministicChild("Night", 300d), -2d));
        var (additiveValid, additiveMessages) = additive.Validate();
        Assert.IsTrue(additiveValid);
        Assert.AreEqual(0, additiveMessages.Count);

        // Child label mismatches are warnings, never errors (labels are unhashed metadata).
        var mismatched = Composite(CompositeFunctionType.Average, (DeterministicChild("Day", 100d), 1d));
        mismatched.ConsequenceFunctions[0].ConsequenceFunction!.SpecifiedHazard = "Flow";
        mismatched.ConsequenceFunctions[0].ConsequenceFunction!.ConsequenceUnit = "$";
        var (stillValid, warnMessages) = mismatched.Validate();
        Assert.IsTrue(stillValid, "Label mismatches must not invalidate.");
        Assert.AreEqual(2, warnMessages.Count(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("does not match")));

        // An invalid child yields the summary line only.
        var invalidChild = Composite(CompositeFunctionType.Average, (new TabularConsequence { Name = "Bare" }, 1d));
        Assert.IsTrue(invalidChild.Validate().ValidationMessages.Any(m => m.Contains("The selected consequence function 'Bare' is invalid")));
    }

    /// <summary>Verifies direct and nested circular references are reported (and terminate).</summary>
    [TestMethod]
    public void Test_Validate_CircularReference_DirectAndNested()
    {
        // Direct self-reference.
        var self = Composite(CompositeFunctionType.Average, (DeterministicChild("Day", 100d), 1d));
        self.ConsequenceFunctions.Add(new WeightedConsequenceFunction(self, 0d));
        Assert.IsTrue(self.Validate().ValidationMessages.Any(m => m.Contains("Circular reference error")));

        // A → B → A.
        var a = Composite(CompositeFunctionType.Average, (DeterministicChild("Day", 100d), 1d));
        a.Name = "A";
        var b = Composite(CompositeFunctionType.Average, (a, 1d));
        b.Name = "B";
        a.ConsequenceFunctions.Add(new WeightedConsequenceFunction(b, 0d));
        Assert.IsTrue(a.Validate().ValidationMessages.Any(m => m.Contains("Circular reference error in the selected composite consequence function 'B'")));
        Assert.IsTrue(a.ContainsComposite(a));
        Assert.IsTrue(b.ContainsComposite(b));

        // Acyclic nesting is fine.
        var inner = Composite(CompositeFunctionType.Additive, (DeterministicChild("Residential", 50d), 1d));
        inner.Name = "Sectors";
        var outer = Composite(CompositeFunctionType.Mixture, (inner, 1d));
        Assert.IsFalse(outer.ContainsComposite(outer));
        Assert.IsTrue(outer.Validate().IsValid);
    }

    /// <summary>
    /// Verifies the mean-curve combine: Additive sums, Average weight-averages, and Mixture's
    /// mean equals Average's (the legacy mean path has no mixture branch).
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Mean_AdditiveAverageMixture_ExactCombine()
    {
        // Arrange
        var day = DeterministicChild("Day", 100d);
        var night = DeterministicChild("Night", 300d);

        // Act / Assert — Additive: Σf, weights ignored.
        var additive = Composite(CompositeFunctionType.Additive, (day, 0.25d), (night, 0.75d));
        Assert.AreEqual(400d, additive.SampleFunction().Function(10d), 1e-12);
        Assert.AreEqual(200d, additive.SampleFunction().Function(5d), 1e-12, "Linear children combine linearly between knots.");

        // Average: Σw·f.
        var average = Composite(CompositeFunctionType.Average, (day, 0.25d), (night, 0.75d));
        Assert.AreEqual(0.25d * 100d + 0.75d * 300d, average.SampleFunction().Function(10d), 1e-12);

        // Mixture mean ≡ Average mean.
        var mixture = Composite(CompositeFunctionType.Mixture, (day, 0.25d), (night, 0.75d));
        Assert.AreEqual(average.SampleFunction().Function(10d), mixture.SampleFunction().Function(10d), 0d);
    }

    /// <summary>
    /// Verifies percentile sampling is deterministic and RNG-free: repeated calls agree, Average
    /// is co-monotonic, and Mixture uses cumulative-weight bucket selection with the rescaled
    /// within-bucket remainder.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Percentile_Deterministic_NoRngReseed()
    {
        // Arrange — three distinct deterministic children, the report weights.
        var a = DeterministicChild("A", 100d);
        var b = DeterministicChild("B", 200d);
        var c = DeterministicChild("C", 300d);
        var mixture = Composite(CompositeFunctionType.Mixture, (a, 0.3d), (b, 0.2d), (c, 0.5d));

        // Act / Assert — bucket selection: p ≤ 0.3 → A; 0.3 < p ≤ 0.5 → B; p > 0.5 → C.
        Assert.AreEqual(100d, mixture.SampleFunction(0.05d).Function(10d), 0d);
        Assert.AreEqual(200d, mixture.SampleFunction(0.4d).Function(10d), 0d);
        Assert.AreEqual(300d, mixture.SampleFunction(0.9d).Function(10d), 0d);

        // Repeated calls at the same percentile are bit-identical (no RNG anywhere).
        Assert.AreEqual(
            mixture.SampleFunction(0.4d).Function(10d),
            mixture.SampleFunction(0.4d).Function(10d), 0d);

        // The within-bucket remainder rescales onto the selected child: with weights 0.5/0.5 and
        // an uncertain second child, p = 0.75 lands in bucket two at its median, and p = 0.875 at
        // its 75th percentile.
        var uncertain = NormalChild("N", 100d, 10d);
        var rescaled = Composite(CompositeFunctionType.Mixture, (DeterministicChild("D", 50d), 0.5d), (uncertain, 0.5d));
        Assert.AreEqual(100d, rescaled.SampleFunction(0.75d).Function(10d), 1e-9);
        Assert.AreEqual(new Normal(100d, 10d).InverseCDF(0.75d), rescaled.SampleFunction(0.875d).Function(10d), 1e-9);

        // Average samples every child co-monotonically at the same percentile.
        var average = Composite(CompositeFunctionType.Average, (NormalChild("N1", 10d, 2d), 0.5d), (NormalChild("N2", 20d, 4d), 0.5d));
        double expected = 0.5d * new Normal(10d, 2d).InverseCDF(0.95d) + 0.5d * new Normal(20d, 4d).InverseCDF(0.95d);
        Assert.AreEqual(expected, average.SampleFunction(0.95d).Function(10d), 1e-9);
    }

    /// <summary>
    /// Verifies the engine path: the Mixture selector reads this composite's own dimension, and
    /// degenerate weights always pick the same child; sampling before setup throws.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_RealizationIndex_MixtureSelectorReadsOwnDimension()
    {
        // Arrange — weight one on Day: every realization must select it.
        var day = DeterministicChild("Day", 100d);
        var night = DeterministicChild("Night", 300d);
        var mixture = Composite(CompositeFunctionType.Mixture, (day, 1d), (night, 0d));

        // Act / Assert — before setup, the selector dimension is unavailable.
        Assert.ThrowsException<InvalidOperationException>(() => mixture.SampleFunction(0));

        mixture.SetupSampler(64, 12345, SamplingScheme.LatinHypercube);
        for (int i = 0; i < 64; i += 7)
        {
            Assert.AreEqual(100d, mixture.SampleFunction(i).Function(10d), 0d, "A zero-weight branch must be unreachable.");
        }

        // Flipping the weights flips the branch.
        var flipped = Composite(CompositeFunctionType.Mixture, (DeterministicChild("Day", 100d), 0d), (DeterministicChild("Night", 300d), 1d));
        flipped.SetupSampler(64, 12345, SamplingScheme.LatinHypercube);
        Assert.AreEqual(300d, flipped.SampleFunction(3).Function(10d), 0d);
    }

    /// <summary>
    /// Verifies §5.8.5 child seeding: identical-content siblings draw independently (ordinal in
    /// the seed), the recursion reproduces exactly from the documented seed recipe, and child
    /// renames cannot move the draws (content-based seeds).
    /// </summary>
    [TestMethod]
    public void Test_SetupSampler_RecursesWithContentDerivedChildSeeds()
    {
        // Arrange — two identical-content uncertain children in an Average composite.
        const int Seed = 20260721;
        var average = Composite(CompositeFunctionType.Average,
            (NormalChild("First", 100d, 10d), 0.5d), (NormalChild("Second", 100d, 10d), 0.5d));
        average.SetupSampler(32, Seed, SamplingScheme.LatinHypercube);

        // Independently reproduce each child's stream from the documented recipe.
        var replica0 = NormalChild("Replica0", 100d, 10d);
        var replica1 = NormalChild("Replica1", 100d, 10d);
        replica0.SetupSampler(32, RMC.TotalRisk.Core.SeedHelpers.HashCombine(Seed, replica0.CanonicalHash(), 0), SamplingScheme.LatinHypercube);
        replica1.SetupSampler(32, RMC.TotalRisk.Core.SeedHelpers.HashCombine(Seed, replica1.CanonicalHash(), 1), SamplingScheme.LatinHypercube);

        bool siblingsDiffer = false;
        for (int i = 0; i < 32; i++)
        {
            double expected = 0.5d * replica0.SampleFunction(i).Function(10d) + 0.5d * replica1.SampleFunction(i).Function(10d);
            Assert.AreEqual(expected, average.SampleFunction(i).Function(10d), 1e-12,
                "Child draws must follow the documented HashCombine(seed, childHash, ordinal) recipe.");
            if (Math.Abs(replica0.SampleFunction(i).Function(10d) - replica1.SampleFunction(i).Function(10d)) > 1e-9)
                siblingsDiffer = true;
        }
        Assert.IsTrue(siblingsDiffer, "Identical-content siblings must draw independently (ordinal in the seed).");

        // Renaming children is hash-inert, so re-setup reproduces the identical stream.
        double before = average.SampleFunction(7).Function(10d);
        average.ConsequenceFunctions[0].ConsequenceFunction!.Name = "First (renamed)";
        average.ConsequenceFunctions[1].ConsequenceFunction!.AssignNewId();
        average.SetupSampler(32, Seed, SamplingScheme.LatinHypercube);
        Assert.AreEqual(before, average.SampleFunction(7).Function(10d), 0d, "Metadata edits must never move Monte Carlo draws.");
    }

    /// <summary>Verifies the composite's own sampling dimensions by mode.</summary>
    [TestMethod]
    public void Test_SamplingDimensions_ByMode()
    {
        // Arrange
        var c = Composite(CompositeFunctionType.Mixture, (DeterministicChild("Day", 100d), 1d));

        // Assert
        Assert.AreEqual(1, c.SamplingDimensions);
        c.CompositeFunctionType = CompositeFunctionType.Average;
        Assert.AreEqual(0, c.SamplingDimensions);
        c.CompositeFunctionType = CompositeFunctionType.Additive;
        Assert.AreEqual(0, c.SamplingDimensions);
    }

    /// <summary>Verifies the hazard envelope and the empty-composite throw (no legacy sentinels).</summary>
    [TestMethod]
    public void Test_MinMaxHazard_UnionOfChildren_EmptyThrows()
    {
        // Arrange — children spanning (0..10) and (5..20).
        var narrow = DeterministicChild("Narrow", 100d);
        var wide = new TabularConsequence
        {
            Name = "Wide",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(5d, new Deterministic(0d)), new UncertainOrdinate(20d, new Deterministic(50d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
        Label(wide);
        var composite = Composite(CompositeFunctionType.Additive, (narrow, 1d), (wide, 1d));

        // Assert
        Assert.AreEqual(0d, composite.MinHazard(), 0d);
        Assert.AreEqual(20d, composite.MaxHazard(), 0d);
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeConsequence().MinHazard());
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeConsequence().MaxHazard());
    }

    /// <summary>
    /// Verifies the improved determinism answer: a Mixture over multiple reachable branches is
    /// never deterministic, even with deterministic children.
    /// </summary>
    [TestMethod]
    public void Test_IsDeterministic_MixtureWithDeterministicChildren_IsFalse()
    {
        // Arrange
        var day = DeterministicChild("Day", 100d);
        var night = DeterministicChild("Night", 300d);

        // Assert — branch picks are real variability.
        Assert.IsFalse(Composite(CompositeFunctionType.Mixture, (day, 0.42d), (night, 0.58d)).IsDeterministic);

        // A single reachable branch collapses to the child's determinism.
        Assert.IsTrue(Composite(CompositeFunctionType.Mixture, (day, 1d), (night, 0d)).IsDeterministic);

        // Average pools deterministically over deterministic children.
        Assert.IsTrue(Composite(CompositeFunctionType.Average, (day, 0.5d), (night, 0.5d)).IsDeterministic);
        Assert.IsFalse(Composite(CompositeFunctionType.Average, (day, 0.5d), (NormalChild("N", 100d, 10d), 0.5d)).IsDeterministic);
    }

    /// <summary>Verifies the self-contained round-trip deep-copies children and preserves the hash.</summary>
    [TestMethod]
    public void Test_Serialization_SelfContained_RoundTrip_DeepCopiesChildren()
    {
        // Arrange
        var original = Composite(CompositeFunctionType.Average,
            (NormalChild("Day", 100d, 10d), 0.42d), (DeterministicChild("Night", 300d), 0.58d));

        // Act
        var restored = new CompositeConsequence(original.ToXElement());

        // Assert — full state, deep-copied children, identical hash.
        Assert.AreEqual(original.CompositeFunctionType, restored.CompositeFunctionType);
        Assert.AreEqual(2, restored.ConsequenceFunctions.Count);
        Assert.AreEqual(0.42d, restored.ConsequenceFunctions[0].Weight, 0d);
        Assert.AreEqual("Day", restored.ConsequenceFunctions[0].ConsequenceFunction!.Name);
        Assert.AreNotSame(original.ConsequenceFunctions[0].ConsequenceFunction, restored.ConsequenceFunctions[0].ConsequenceFunction);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());

        // Nested composites round-trip inline too.
        var nested = Composite(CompositeFunctionType.Mixture, (original, 1d));
        nested.Name = "Nested";
        var nestedRestored = new CompositeConsequence(nested.ToXElement());
        Assert.IsInstanceOfType<CompositeConsequence>(nestedRestored.ConsequenceFunctions[0].ConsequenceFunction);
        CollectionAssert.AreEqual(nested.CanonicalHash(), nestedRestored.CanonicalHash());
    }

    /// <summary>Verifies the by-reference round-trip reattaches the live stored instances.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_RoundTrip_ReattachesSameInstances()
    {
        // Arrange — a store of two functions and a resolver over it.
        var day = NormalChild("Day", 100d, 10d);
        var night = DeterministicChild("Night", 300d);
        var store = new IRiskFunction[] { day, night }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));
        var original = Composite(CompositeFunctionType.Mixture, (day, 0.42d), (night, 0.58d));

        // Act
        var form = original.ToXElement(RiskSerializationMode.ByReference);
        var restored = new CompositeConsequence(form, resolver);

        // Assert — the stored form carries no child content, and restoration reattaches the
        // live instances rather than copies.
        Assert.IsFalse(form.ToString().Contains("UncertainOrderedPairedData"), "A by-reference form must not embed child content.");
        Assert.AreSame(day, restored.ConsequenceFunctions[0].ConsequenceFunction);
        Assert.AreSame(night, restored.ConsequenceFunctions[1].ConsequenceFunction);
        Assert.AreEqual(0.42d, restored.ConsequenceFunctions[0].Weight, 0d);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>
    /// Verifies unresolvable references keep their weighted entries (weights survive the
    /// round-trip) and are reported by Validate — never silently dropped.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_ByReference_Unresolved_PreservesWeightEntry_AndValidateReports()
    {
        // Arrange
        var day = DeterministicChild("Day", 100d);
        var night = DeterministicChild("Night", 300d);
        var original = Composite(CompositeFunctionType.Mixture, (day, 0.42d), (night, 0.58d));
        var form = original.ToXElement(RiskSerializationMode.ByReference);

        // Act — no resolver at all: both entries survive with null functions.
        var noResolver = new CompositeConsequence(form);

        // Assert
        Assert.AreEqual(2, noResolver.ConsequenceFunctions.Count);
        Assert.IsNull(noResolver.ConsequenceFunctions[0].ConsequenceFunction);
        Assert.AreEqual(0.42d, noResolver.ConsequenceFunctions[0].Weight, 0d);
        var (isValid, messages) = noResolver.Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(2, messages.Count(m => m.Contains("which was not found")));
        Assert.IsFalse(messages.Any(m => m.Contains("No consequence functions have been defined")),
            "The precise unresolved-reference cause replaces the generic message.");

        // A name-only reference (no id) misses leniently through a resolver and is kept too.
        foreach (var marker in form.Element(nameof(CompositeConsequence.ConsequenceFunctions))!
                     .Elements(nameof(WeightedConsequenceFunction)).Elements("FunctionReference"))
        {
            marker.Attribute("Id")!.Remove();
        }
        var emptyResolver = new RiskFunctionResolver(_ => null, _ => null);
        var nameMiss = new CompositeConsequence(form, emptyResolver);
        Assert.AreEqual(2, nameMiss.ConsequenceFunctions.Count);
        Assert.AreEqual(0.58d, nameMiss.ConsequenceFunctions[1].Weight, 0d);
        Assert.IsFalse(nameMiss.Validate().IsValid);
    }

    /// <summary>Verifies a stale serialized id throws loudly (the resolver policy).</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_StaleId_Throws()
    {
        // Arrange
        var original = Composite(CompositeFunctionType.Average, (DeterministicChild("Day", 100d), 1d));
        var form = original.ToXElement(RiskSerializationMode.ByReference);
        var emptyResolver = new RiskFunctionResolver(_ => null, _ => null);

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeConsequence(form, emptyResolver));
    }

    /// <summary>
    /// Verifies the projected-identity hash is identical across serialization modes — the mode is
    /// persistence only and can never move a hash or a seed.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_IdenticalAcrossSerializationModes()
    {
        // Arrange
        var day = NormalChild("Day", 100d, 10d);
        var night = DeterministicChild("Night", 300d);
        var store = new IRiskFunction[] { day, night }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));
        var original = Composite(CompositeFunctionType.Mixture, (day, 0.42d), (night, 0.58d));

        // Act
        var fromSelfContained = new CompositeConsequence(original.ToXElement(RiskSerializationMode.SelfContained));
        var fromReference = new CompositeConsequence(original.ToXElement(RiskSerializationMode.ByReference), resolver);

        // Assert
        CollectionAssert.AreEqual(original.CanonicalHash(), fromSelfContained.CanonicalHash());
        CollectionAssert.AreEqual(original.CanonicalHash(), fromReference.CanonicalHash());
    }

    /// <summary>Verifies child metadata edits are hash-inert (child hashes are metadata-inert).</summary>
    [TestMethod]
    public void Test_CanonicalHash_ChildMetadataEdits_Inert()
    {
        // Arrange
        var day = NormalChild("Day", 100d, 10d);
        var composite = Composite(CompositeFunctionType.Average, (day, 0.4d), (DeterministicChild("Night", 300d), 0.6d));
        byte[] baseline = composite.CanonicalHash();

        // Act / Assert — child identity and label edits never move the composite hash.
        day.Name = "Day (renamed)";
        day.AssignNewId();
        day.SpecifiedConsequence = "Damages";
        day.ConsequenceUnit = "$";
        CollectionAssert.AreEqual(baseline, composite.CanonicalHash());

        // The composite's own metadata is inert too, and stripped attributes stay inert on the
        // persisted tree.
        HashInvariance.AssertMetadataInvariant(composite);
        HashInvariance.AssertStrippedAttributesInert(composite.ToXElement());
    }

    /// <summary>Verifies every compute-relevant edit moves the hash, including entry order.</summary>
    [TestMethod]
    public void Test_CanonicalHash_ComputeEdits_Move()
    {
        // Arrange
        var day = NormalChild("Day", 100d, 10d);
        var night = DeterministicChild("Night", 300d);
        CompositeConsequence Build() => Composite(CompositeFunctionType.Average, (day, 0.4d), (night, 0.6d));
        var composite = Build();

        // Act / Assert — weight edit (non-Additive), mode toggle, child content edit, entry
        // reorder, and entry add/remove each move the hash.
        HashInvariance.AssertComputeSensitive(composite.CanonicalHash, () => composite.ConsequenceFunctions[0].Weight = 0.5d);
        composite = Build();
        HashInvariance.AssertComputeSensitive(composite.CanonicalHash, () => composite.CompositeFunctionType = CompositeFunctionType.Mixture);
        composite = Build();
        HashInvariance.AssertComputeSensitive(composite.CanonicalHash, () => ((TabularConsequence)composite.ConsequenceFunctions[0].ConsequenceFunction!).HazardTransform = Transform.Logarithmic);
        ((TabularConsequence)day).HazardTransform = Transform.None;
        composite = Build();
        HashInvariance.AssertComputeSensitive(composite.CanonicalHash, () => composite.ConsequenceFunctions.Move(0, 1));
        composite = Build();
        HashInvariance.AssertComputeSensitive(composite.CanonicalHash, () => composite.ConsequenceFunctions.RemoveAt(1));
    }

    /// <summary>Verifies Additive weights are coerced in the identity form (weight edits inert).</summary>
    [TestMethod]
    public void Test_CanonicalHash_AdditiveWeights_CoercedInert()
    {
        // Arrange
        var composite = Composite(CompositeFunctionType.Additive,
            (DeterministicChild("Day", 100d), 0.4d), (DeterministicChild("Night", 300d), 0.6d));
        byte[] baseline = composite.CanonicalHash();

        // Act — weights are computationally inert under Additive.
        composite.ConsequenceFunctions[0].Weight = 0.9d;

        // Assert
        CollectionAssert.AreEqual(baseline, composite.CanonicalHash());
    }

    /// <summary>
    /// Verifies two independently built identical composites hash and sample identically —
    /// the content-based seed identity across instances.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_And_Draws_ReproduceAcrossInstances()
    {
        // Arrange
        CompositeConsequence Build() => Composite(CompositeFunctionType.Mixture,
            (NormalChild("Day", 100d, 10d), 0.42d), (NormalChild("Night", 40d, 5d), 0.58d));
        var first = Build();
        var second = Build();

        // Assert — identical hash, and identical realization streams at the same seed.
        CollectionAssert.AreEqual(first.CanonicalHash(), second.CanonicalHash());
        first.SetupSampler(64, 777, SamplingScheme.LatinHypercube);
        second.SetupSampler(64, 777, SamplingScheme.LatinHypercube);
        for (int i = 0; i < 64; i += 9)
        {
            Assert.AreEqual(first.SampleFunction(i).Function(10d), second.SampleFunction(i).Function(10d), 0d);
        }
    }

    /// <summary>
    /// Verifies the uncertainty summary is deterministic, content-seeded, exact for deterministic
    /// composites, and never disturbs the live engine sampler state.
    /// </summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_Deterministic_AndDoesNotDisturbLiveSampler()
    {
        // Arrange — the report's Average scenario: N(10,2), N(20,1), N(100,5) at weights .3/.2/.5.
        var composite = Composite(CompositeFunctionType.Average,
            (NormalChild("F1", 10d, 2d), 0.3d), (NormalChild("F2", 20d, 1d), 0.2d), (NormalChild("F3", 100d, 5d), 0.5d));
        composite.SetupSampler(128, 12345, SamplingScheme.LatinHypercube);
        double liveDraw = composite.SampleFunction(5).Function(10d);

        // Act
        var first = composite.ComputeUncertaintyResults(0.90d)!;
        var second = composite.ComputeUncertaintyResults(0.90d)!;

        // Assert — repeated calls agree bit-for-bit, and the live sampler stream is untouched.
        CollectionAssert.AreEqual(first.MeanCurve, second.MeanCurve);
        Assert.AreEqual(liveDraw, composite.SampleFunction(5).Function(10d), 0d,
            "The uncertainty summary must never disturb the live sampler state.");

        // Metadata edits cannot move the summary (content-based seed).
        composite.Name = "Renamed";
        composite.ConsequenceFunctions[0].ConsequenceFunction!.AssignNewId();
        CollectionAssert.AreEqual(first.MeanCurve, composite.ComputeUncertaintyResults(0.90d)!.MeanCurve);

        // Grid alignment: the union of the child knots {0, 10}; independent-child pooling at
        // stage 10 gives mean 57 with sd 2.58 — the 10k median-LHS estimate sits well within
        // ±0.5 of the exact mean.
        double[] grid = composite.UncertaintySummaryHazards();
        CollectionAssert.AreEqual(new[] { 0d, 10d }, grid);
        Assert.AreEqual(grid.Length, first.MeanCurve!.Length);
        Assert.AreEqual(57d, first.MeanCurve[1], 0.5d);
        Assert.IsTrue(first.ConfidenceIntervals![1, 0] < first.ConfidenceIntervals[1, 1]);

        // Deterministic composites summarize exactly with no simulation.
        var deterministic = Composite(CompositeFunctionType.Additive,
            (DeterministicChild("Day", 100d), 1d), (DeterministicChild("Night", 300d), 1d));
        var exact = deterministic.ComputeUncertaintyResults(0.90d)!;
        Assert.AreEqual(400d, exact.MeanCurve![1], 0d);
        Assert.AreEqual(400d, exact.ConfidenceIntervals![1, 0], 0d);

        // Width bounds are enforced; validation errors return null.
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => composite.ComputeUncertaintyResults(0d));
        Assert.IsNull(new CompositeConsequence().ComputeUncertaintyResults(0.90d));
    }

    /// <summary>Verifies entry- and child-level edits surface through the composite's notification.</summary>
    [TestMethod]
    public void Test_PropertyChange_ChildEditReachesComposite()
    {
        // Arrange
        var day = NormalChild("Day", 100d, 10d);
        var composite = Composite(CompositeFunctionType.Mixture, (day, 1d));
        var raised = new List<string>();
        composite.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act / Assert — a weight edit, a child content edit, and a membership change all surface
        // as ConsequenceFunctions.
        composite.ConsequenceFunctions[0].Weight = 0.9d;
        CollectionAssert.Contains(raised, nameof(CompositeConsequence.ConsequenceFunctions));

        raised.Clear();
        day.HazardTransform = Transform.Logarithmic;
        CollectionAssert.Contains(raised, nameof(CompositeConsequence.ConsequenceFunctions));

        raised.Clear();
        composite.ConsequenceFunctions.Add(new WeightedConsequenceFunction(DeterministicChild("Night", 300d), 0.1d));
        CollectionAssert.Contains(raised, nameof(CompositeConsequence.ConsequenceFunctions));

        // Clear() releases the entry subscriptions (the shadow-set reconciliation).
        raised.Clear();
        var entry = composite.ConsequenceFunctions[0];
        composite.ConsequenceFunctions.Clear();
        raised.Clear();
        entry.Weight = 0.123d;
        Assert.AreEqual(0, raised.Count, "A cleared entry must no longer notify the composite.");
    }
}
