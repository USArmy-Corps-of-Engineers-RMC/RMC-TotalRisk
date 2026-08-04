using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Functions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Tests.RiskFunctions.Transforms;

/// <summary>
/// Unit tests for <see cref="CompositeTransform"/> — the Average-only combine and its rejection of
/// the other modes, closed-form weighted averaging, the intersection domain rule, deterministic
/// RNG-free sampling, content-derived child seeding, dual-mode serialization, projected-identity
/// hashing, and the validation matrix.
/// </summary>
[TestClass]
public class CompositeTransformTests
{
    /// <summary>The standard axis labels shared by composites and children in these tests.</summary>
    private static void Label(ITransformFunction function)
    {
        function.SpecifiedHazard = "Flow";
        function.HazardUnit = "cfs";
        function.TransformedHazard = "Stage";
        function.TransformedHazardUnit = "ft";
    }

    /// <summary>Builds a labeled deterministic linear child: Y = α + β·X over [0, 100].</summary>
    private static LinearTransform LinearChild(string name, double alpha, double beta)
    {
        var child = new LinearTransform
        {
            Name = name,
            Alpha = alpha,
            Beta = beta,
            IsUncertain = false,
        };
        Label(child);
        return child;
    }

    /// <summary>Builds a labeled uncertain linear child: Y = α + β·X + ε, ε ~ N(0, σ).</summary>
    private static LinearTransform UncertainChild(string name, double alpha, double beta, double sigma)
    {
        var child = new LinearTransform
        {
            Name = name,
            Alpha = alpha,
            Beta = beta,
            Sigma = sigma,
            IsUncertain = true,
        };
        Label(child);
        return child;
    }

    /// <summary>Builds a labeled composite over the given (child, weight) pairs.</summary>
    private static CompositeTransform Composite(params (ITransformFunction Function, double Weight)[] entries)
    {
        var composite = new CompositeTransform(entries.Select(e => new WeightedTransformFunction(e.Function, e.Weight)))
        {
            Name = "Composite",
        };
        Label(composite);
        return composite;
    }

    /// <summary>Verifies the bivariate scope guard: a bivariate child is rejected loudly by validation and by the sampling gate.</summary>
    [TestMethod]
    public void Test_Validate_BivariateChild_Error()
    {
        // Arrange — an otherwise valid composite carrying a bivariate child.
        var composite = Composite((LinearChild("Rating", 2d, 3d), 0.5d), (new BivariateTransform { Name = "Surface" }, 0.5d));

        // Act
        var (isValid, messages) = composite.Validate();

        // Assert — the loud scope guard, and sampling refuses too.
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("'Surface' is bivariate")));
        Assert.ThrowsException<InvalidOperationException>(() => composite.SampleFunction());
    }

    /// <summary>Verifies the default construction state — Average, not Mixture.</summary>
    [TestMethod]
    public void Test_Defaults_Average_NotMixture()
    {
        // Act
        var t = new CompositeTransform();

        // Assert — greenfield: Average is the default so a new instance is valid out of the box,
        // deliberately unlike CompositeConsequence, whose v1.0 default is Mixture.
        Assert.AreEqual(CompositeFunctionType.Average, t.CompositeFunctionType);
        Assert.AreEqual(0, t.TransformFunctions.Count);
        Assert.AreEqual(TransformFunctionType.Composite, t.FunctionType);
        Assert.AreEqual(0, t.SamplingDimensions);
        Assert.IsTrue(t.IsDeterministic);
    }

    /// <summary>Verifies the convenience constructor wires entries and their subscriptions.</summary>
    [TestMethod]
    public void Test_Ctor_FromEnumerable_WiresEntries()
    {
        // Arrange
        var child = LinearChild("Rating A", 0d, 0.5d);
        var composite = Composite((child, 1d));
        var raised = new List<string>();
        composite.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        child.Beta = 0.6d;

        // Assert
        Assert.AreEqual(1, composite.TransformFunctions.Count);
        CollectionAssert.Contains(raised, nameof(CompositeTransform.TransformFunctions));
        Assert.ThrowsException<ArgumentNullException>(() => new CompositeTransform((IEnumerable<WeightedTransformFunction>)null!));
    }

    /// <summary>
    /// Verifies the deliberate mode restriction: only Average is supported, and the other two members
    /// are errors carrying the reason.
    /// </summary>
    [TestMethod]
    public void Test_Validate_AdditiveAndMixture_AreErrors()
    {
        // Arrange
        var composite = Composite((LinearChild("A", 0d, 0.5d), 0.5d), (LinearChild("B", 1d, 0.4d), 0.5d));
        Assert.IsTrue(composite.Validate().IsValid);

        // Mixture is rejected: there is no transform exposure-branch surface, so a mean-only run
        // would disagree with the mean of the full-uncertainty ensemble.
        composite.CompositeFunctionType = CompositeFunctionType.Mixture;
        var (mixtureValid, mixtureMessages) = composite.Validate();
        Assert.IsFalse(mixtureValid);
        Assert.IsTrue(mixtureMessages.Any(m => m.Contains("Mixture") && m.Contains("branch")));
        Assert.ThrowsException<InvalidOperationException>(() => composite.SampleFunction());

        // Additive is rejected: summing transforms has no physical reading.
        composite.CompositeFunctionType = CompositeFunctionType.Additive;
        Assert.IsFalse(composite.Validate().IsValid);
        Assert.ThrowsException<InvalidOperationException>(() => composite.SampleFunction());
    }

    /// <summary>Verifies the validation matrix: labels, children, weights, and the warning downgrade.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // A labeled composite with matching children and weights summing to one is clean.
        var (ok, okMessages) = Composite((LinearChild("A", 0d, 0.5d), 0.4d), (LinearChild("B", 1d, 0.4d), 0.6d)).Validate();
        Assert.IsTrue(ok);
        Assert.AreEqual(0, okMessages.Count);

        // Unlabeled and empty: 4 label errors + the no-functions error.
        var (isValid, messages) = new CompositeTransform().Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(5, messages.Count);

        // A null child entry is an error.
        var nullChild = Composite((LinearChild("A", 0d, 0.5d), 1d));
        nullChild.TransformFunctions.Add(new WeightedTransformFunction(null, 0d));
        Assert.IsFalse(nullChild.Validate().IsValid);

        // Weights must lie in [0, 1] and sum to one.
        Assert.IsFalse(Composite((LinearChild("A", 0d, 0.5d), 0.4d), (LinearChild("B", 1d, 0.4d), 0.4d)).Validate().IsValid);
        Assert.IsFalse(Composite((LinearChild("A", 0d, 0.5d), 1.4d), (LinearChild("B", 1d, 0.4d), -0.4d)).Validate().IsValid);

        // A child whose labels differ is a warning, not an error.
        var mismatched = LinearChild("Odd", 0d, 0.5d);
        mismatched.TransformedHazardUnit = "m";
        var (warnValid, warnMessages) = Composite((mismatched, 1d)).Validate();
        Assert.IsTrue(warnValid);
        Assert.IsTrue(warnMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Verifies the intersection domain rule: differing child domains warn, and a disjoint pair is
    /// an error, because a weighted average needs every child evaluable at every input.
    /// </summary>
    [TestMethod]
    public void Test_Validate_ChildDomains_DifferWarns_DisjointErrors()
    {
        // Arrange — identical default domains [0, 100] are clean.
        Assert.IsTrue(Composite((LinearChild("A", 0d, 0.5d), 0.5d), (LinearChild("B", 1d, 0.4d), 0.5d)).Validate().IsValid);

        // Overlapping but different domains warn, and the composite spans the intersection.
        var narrow = LinearChild("Narrow", 0d, 0.5d);
        narrow.Minimum = 20d;
        narrow.Maximum = 80d;
        var overlapping = Composite((LinearChild("Wide", 1d, 0.4d), 0.5d), (narrow, 0.5d));
        var (overlapValid, overlapMessages) = overlapping.Validate();
        Assert.IsTrue(overlapValid);
        Assert.IsTrue(overlapMessages.Any(m => m.Contains("domains differ")));
        Assert.AreEqual(20d, overlapping.MinHazard(), 0d, "The domain is the intersection: max of the child minima.");
        Assert.AreEqual(80d, overlapping.MaxHazard(), 0d, "The domain is the intersection: min of the child maxima.");

        // Disjoint domains are an error.
        var high = LinearChild("High", 0d, 0.5d);
        high.Minimum = 200d;
        high.Maximum = 300d;
        var disjoint = Composite((LinearChild("Low", 1d, 0.4d), 0.5d), (high, 0.5d));
        var (disjointValid, disjointMessages) = disjoint.Validate();
        Assert.IsFalse(disjointValid);
        Assert.IsTrue(disjointMessages.Any(m => m.Contains("do not overlap")));
        Assert.ThrowsException<InvalidOperationException>(() => disjoint.SampleFunction());
    }

    /// <summary>Verifies circular-reference detection, direct and nested.</summary>
    [TestMethod]
    public void Test_Validate_CircularReference_DirectAndNested()
    {
        // Arrange — direct self-reference.
        var direct = Composite((LinearChild("A", 0d, 0.5d), 1d));
        direct.TransformFunctions[0].TransformFunction = direct;
        var (directValid, directMessages) = direct.Validate();
        Assert.IsFalse(directValid);
        Assert.IsTrue(directMessages.Any(m => m.Contains("Circular reference")));

        // Nested: outer → inner → outer.
        var outer = Composite((LinearChild("A", 0d, 0.5d), 1d));
        var inner = Composite((LinearChild("B", 1d, 0.4d), 1d));
        outer.TransformFunctions[0].TransformFunction = inner;
        inner.TransformFunctions[0].TransformFunction = outer;
        Assert.IsFalse(outer.Validate().IsValid);
        Assert.IsTrue(outer.ContainsComposite(inner));
        Assert.ThrowsException<ArgumentNullException>(() => outer.ContainsComposite(null!));
    }

    /// <summary>
    /// Verifies the weighted average in closed form: linear children combine to the linear function
    /// with α = Σωᵢαᵢ and β = Σωᵢβᵢ.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_LinearChildren_CombineToClosedFormLinear()
    {
        // Arrange — 0.25·(2 + 3X) + 0.75·(10 + 4X) = 8 + 3.75X.
        var composite = Composite((LinearChild("A", 2d, 3d), 0.25d), (LinearChild("B", 10d, 4d), 0.75d));

        // Act
        var combined = composite.SampleFunction();

        // Assert
        foreach (double x in new[] { 0d, 10d, 42.5d, 100d })
        {
            Assert.AreEqual(8d + (3.75d * x), combined.Function(x), 1e-12);
        }
    }

    /// <summary>
    /// Verifies the combine rides the Numerics <see cref="CompositeFunction"/> in weighted-average
    /// mode, with <c>ConfidenceLevel</c> left at −1 so the mean-convention branch evaluates children
    /// in the state the wrapper already sampled them into.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_ReturnsCompositeFunction_WithConfidenceLevelNegativeOne()
    {
        // Arrange
        var composite = Composite((LinearChild("A", 2d, 3d), 0.5d), (LinearChild("B", 10d, 4d), 0.5d));

        // Act
        var combined = composite.SampleFunction();

        // Assert
        Assert.IsInstanceOfType<CompositeFunction>(combined);
        var typed = (CompositeFunction)combined;
        Assert.AreEqual(CompositeFunctionMode.WeightedAverage, typed.Mode);
        Assert.AreEqual(-1d, typed.ConfidenceLevel, 0d,
            "The wrapper bakes each child's percentile in, so the combine must take the mean-convention branch.");
        CollectionAssert.AreEqual(new[] { 0.5d, 0.5d }, typed.Weights.ToArray());
    }

    /// <summary>
    /// Verifies the Numerics constraint that the combined curve's bounds are read-only — pinned so
    /// a future change upstream surfaces here rather than in an engine run.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_MinimumMaximumSetters_Throw()
    {
        // Arrange
        var combined = Composite((LinearChild("A", 2d, 3d), 1d)).SampleFunction();

        // Assert
        Assert.ThrowsException<NotSupportedException>(() => combined.Minimum = 5d);
        Assert.ThrowsException<NotSupportedException>(() => combined.Maximum = 50d);
    }

    /// <summary>Verifies the combined curve inverts for monotone children.</summary>
    [TestMethod]
    public void Test_SampleFunction_InverseFunction_RoundTripsForMonotoneChildren()
    {
        // Arrange — 0.25·(2 + 3X) + 0.75·(10 + 4X) = 8 + 3.75X, strictly increasing.
        var combined = Composite((LinearChild("A", 2d, 3d), 0.25d), (LinearChild("B", 10d, 4d), 0.75d)).SampleFunction();

        // Act / Assert
        foreach (double x in new[] { 5d, 30d, 75d })
        {
            Assert.AreEqual(x, combined.InverseFunction(combined.Function(x)), 1e-6);
        }
    }

    /// <summary>Verifies the percentile overload is deterministic, RNG-free, and co-monotonic.</summary>
    [TestMethod]
    public void Test_SampleFunction_Percentile_DeterministicAndCoMonotonic()
    {
        // Arrange
        var a = UncertainChild("A", 2d, 3d, 1.5d);
        var b = UncertainChild("B", 10d, 4d, 2.5d);
        var composite = Composite((a, 0.4d), (b, 0.6d));

        // Act
        double first = composite.SampleFunction(0.3d).Function(50d);
        double second = composite.SampleFunction(0.3d).Function(50d);

        // Assert
        Assert.AreEqual(first, second, 0d, "Percentile sampling must be deterministic and RNG-free.");
        Assert.AreEqual(0.4d * a.SampleFunction(0.3d).Function(50d) + 0.6d * b.SampleFunction(0.3d).Function(50d),
            first, 1e-12, "Every child must be driven at the same percentile.");
    }

    /// <summary>Verifies §5.8.5 child seeding: ordinal-in-seed independence and metadata inertness.</summary>
    [TestMethod]
    public void Test_SetupSampler_RecursesWithContentDerivedChildSeeds()
    {
        // Arrange — two identical-content uncertain children.
        const int Seed = 20260725;
        var composite = Composite((UncertainChild("First", 2d, 3d, 1.5d), 0.5d), (UncertainChild("Second", 2d, 3d, 1.5d), 0.5d));
        composite.SetupSampler(32, Seed, SamplingScheme.LatinHypercube);

        var replica0 = UncertainChild("Replica0", 2d, 3d, 1.5d);
        var replica1 = UncertainChild("Replica1", 2d, 3d, 1.5d);
        replica0.SetupSampler(32, SeedHelpers.HashCombine(Seed, replica0.CanonicalHash(), 0), SamplingScheme.LatinHypercube);
        replica1.SetupSampler(32, SeedHelpers.HashCombine(Seed, replica1.CanonicalHash(), 1), SamplingScheme.LatinHypercube);

        bool siblingsDiffer = false;
        for (int i = 0; i < 32; i++)
        {
            double expected = 0.5d * replica0.SampleFunction(i).Function(50d) + 0.5d * replica1.SampleFunction(i).Function(50d);
            Assert.AreEqual(expected, composite.SampleFunction(i).Function(50d), 1e-12,
                "Child draws must follow the documented HashCombine(seed, childHash, ordinal) recipe.");
            if (Math.Abs(replica0.SampleFunction(i).Function(50d) - replica1.SampleFunction(i).Function(50d)) > 1e-9)
                siblingsDiffer = true;
        }
        Assert.IsTrue(siblingsDiffer, "Identical-content siblings must draw independently (ordinal in the seed).");

        // Metadata edits are hash-inert, so re-setup reproduces the identical stream.
        double before = composite.SampleFunction(7).Function(50d);
        composite.TransformFunctions[0].TransformFunction!.Name = "First (renamed)";
        composite.TransformFunctions[1].TransformFunction!.AssignNewId();
        composite.SetupSampler(32, Seed, SamplingScheme.LatinHypercube);
        Assert.AreEqual(before, composite.SampleFunction(7).Function(50d), 0d);
    }

    /// <summary>Verifies the transformed-hazard bounds evaluate the combine at its own domain bounds.</summary>
    [TestMethod]
    public void Test_MinMaxTransformedHazard_CombineAtDomainBounds()
    {
        // Arrange — 0.25·(2 + 3X) + 0.75·(10 + 4X) = 8 + 3.75X over [0, 100].
        var composite = Composite((LinearChild("A", 2d, 3d), 0.25d), (LinearChild("B", 10d, 4d), 0.75d));

        // Assert — deterministic children, so the mean-only and full-uncertainty bounds agree.
        Assert.AreEqual(8d, composite.MinTransformedHazard(true), 1e-12);
        Assert.AreEqual(8d + 375d, composite.MaxTransformedHazard(true), 1e-12);

        // Empty composites throw rather than returning sentinels.
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeTransform().MinHazard());
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeTransform().MaxHazard());
    }

    /// <summary>Verifies the self-contained round-trip deep-copies children and preserves the hash.</summary>
    [TestMethod]
    public void Test_Serialization_SelfContained_RoundTrip_DeepCopiesChildren()
    {
        // Arrange
        var original = Composite((LinearChild("Rating A", 2d, 3d), 0.4d), (LinearChild("Rating B", 10d, 4d), 0.6d));

        // Act
        var restored = new CompositeTransform(original.ToXElement());

        // Assert
        Assert.AreEqual(CompositeFunctionType.Average, restored.CompositeFunctionType);
        Assert.AreEqual(2, restored.TransformFunctions.Count);
        Assert.AreEqual(0.4d, restored.TransformFunctions[0].Weight, 0d);
        Assert.AreEqual("Rating A", restored.TransformFunctions[0].TransformFunction!.Name);
        Assert.AreNotSame(original.TransformFunctions[0].TransformFunction, restored.TransformFunctions[0].TransformFunction);
        Assert.AreEqual("Stage", restored.TransformedHazard);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());

        // Nested composites round-trip inline too.
        var nested = Composite((original, 1d));
        nested.Name = "Nested";
        var nestedRestored = new CompositeTransform(nested.ToXElement());
        Assert.IsInstanceOfType<CompositeTransform>(nestedRestored.TransformFunctions[0].TransformFunction);
        CollectionAssert.AreEqual(nested.CanonicalHash(), nestedRestored.CanonicalHash());

        Assert.ThrowsException<ArgumentNullException>(() => new CompositeTransform((XElement)null!));
    }

    /// <summary>Verifies the by-reference round-trip reattaches the live stored instances.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_RoundTrip_ReattachesSameInstances()
    {
        // Arrange
        var a = LinearChild("Rating A", 2d, 3d);
        var b = LinearChild("Rating B", 10d, 4d);
        var store = new IRiskFunction[] { a, b }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));
        var original = Composite((a, 0.4d), (b, 0.6d));

        // Act
        var restored = new CompositeTransform(original.ToXElement(RiskSerializationMode.ByReference), resolver);

        // Assert
        Assert.AreSame(a, restored.TransformFunctions[0].TransformFunction);
        Assert.AreSame(b, restored.TransformFunctions[1].TransformFunction);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>Verifies an unresolvable reference keeps its weighted entry and is reported.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_Unresolved_PreservesWeightEntry_AndValidateReports()
    {
        // Arrange
        var original = Composite((LinearChild("Rating A", 2d, 3d), 0.4d), (LinearChild("Rating B", 10d, 4d), 0.6d));
        var empty = new RiskFunctionResolver(_ => null, _ => null);

        // Act — strip the ids to exercise the lenient name path, which records instead of throwing.
        var form = original.ToXElement(RiskSerializationMode.ByReference);
        foreach (var marker in form.Descendants("FunctionReference")) marker.Attribute("Id")!.Remove();
        var restored = new CompositeTransform(form, empty);

        // Assert
        Assert.AreEqual(2, restored.TransformFunctions.Count);
        Assert.AreEqual(0.4d, restored.TransformFunctions[0].Weight, 0d);
        Assert.IsNull(restored.TransformFunctions[0].TransformFunction);
        var (isValid, messages) = restored.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("Rating A") && m.Contains("not found")));
    }

    /// <summary>Verifies a stale serialized reference id throws rather than resolving silently.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_StaleId_Throws()
    {
        // Arrange
        var original = Composite((LinearChild("Rating A", 2d, 3d), 1d));
        var empty = new RiskFunctionResolver(_ => null, _ => null);

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(
            () => new CompositeTransform(original.ToXElement(RiskSerializationMode.ByReference), empty));
    }

    /// <summary>Verifies the hash is identical across serialization modes.</summary>
    [TestMethod]
    public void Test_CanonicalHash_IdenticalAcrossSerializationModes()
    {
        // Arrange
        var a = LinearChild("Rating A", 2d, 3d);
        var b = LinearChild("Rating B", 10d, 4d);
        var store = new IRiskFunction[] { a, b }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));
        var original = Composite((a, 0.4d), (b, 0.6d));

        // Assert
        CollectionAssert.AreEqual(original.CanonicalHash(),
            new CompositeTransform(original.ToXElement(RiskSerializationMode.SelfContained)).CanonicalHash());
        CollectionAssert.AreEqual(original.CanonicalHash(),
            new CompositeTransform(original.ToXElement(RiskSerializationMode.ByReference), resolver).CanonicalHash());
    }

    /// <summary>Verifies metadata edits are hash-inert and compute edits move the hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataInert_ComputeEditsMove()
    {
        // Arrange
        var composite = Composite((LinearChild("Rating A", 2d, 3d), 0.4d), (LinearChild("Rating B", 10d, 4d), 0.6d));
        byte[] baseline = composite.CanonicalHash();

        // Metadata is inert.
        composite.Name = "Renamed";
        composite.Description = "A description";
        composite.TransformedHazard = "Pool";
        composite.AssignNewId();
        composite.TransformFunctions[0].TransformFunction!.Name = "Rating A (renamed)";
        CollectionAssert.AreEqual(baseline, composite.CanonicalHash());

        // A weight moves it.
        composite.TransformFunctions[0].Weight = 0.5d;
        composite.TransformFunctions[1].Weight = 0.5d;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.TransformFunctions[0].Weight = 0.4d;
        composite.TransformFunctions[1].Weight = 0.6d;
        CollectionAssert.AreEqual(baseline, composite.CanonicalHash());

        // The combine mode moves it (even though the other modes are validation errors, the field
        // is hashed so enabling one later cannot silently reinterpret a stored model).
        composite.CompositeFunctionType = CompositeFunctionType.Mixture;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.CompositeFunctionType = CompositeFunctionType.Average;

        // Entry order is semantic.
        CollectionAssert.AreNotEqual(baseline,
            Composite((LinearChild("Rating B", 10d, 4d), 0.6d), (LinearChild("Rating A", 2d, 3d), 0.4d)).CanonicalHash());

        // Child content moves it.
        CollectionAssert.AreNotEqual(baseline,
            Composite((LinearChild("Rating A", 2.5d, 3d), 0.4d), (LinearChild("Rating B", 10d, 4d), 0.6d)).CanonicalHash());
    }

    /// <summary>
    /// Verifies a null child projects an empty hash token, so an unresolved reference cannot alias
    /// a resolved one.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_NullChild_DoesNotAliasResolved()
    {
        // Assert
        CollectionAssert.AreNotEqual(
            Composite((LinearChild("Rating A", 2d, 3d), 1d)).CanonicalHash(),
            Composite((null!, 1d)).CanonicalHash());
    }

    /// <summary>
    /// Verifies a deterministic composite's uncertainty summary collapses to the exact mean curve
    /// with no simulation.
    /// </summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_Deterministic_CollapsesToMeanCurve()
    {
        // Arrange
        var composite = Composite((LinearChild("A", 2d, 3d), 0.25d), (LinearChild("B", 10d, 4d), 0.75d));

        // Act
        var results = composite.ComputeUncertaintyResults();
        double[] hazards = composite.UncertaintySummaryHazards();

        // Assert — every band equals the closed-form combine 8 + 3.75X.
        Assert.IsNotNull(results);
        for (int i = 0; i < hazards.Length; i++)
        {
            Assert.AreEqual(8d + (3.75d * hazards[i]), results!.MeanCurve![i], 1e-12);
            Assert.AreEqual(results.MeanCurve[i], results.ConfidenceIntervals![i, 0], 0d);
            Assert.AreEqual(results.MeanCurve[i], results.ConfidenceIntervals[i, 1], 0d);
        }
    }

    /// <summary>Verifies a nested composite samples and hashes through the recursion.</summary>
    [TestMethod]
    public void Test_NestedComposite_SamplesAndHashes()
    {
        // Arrange — an inner average used as one entry of an outer average.
        var inner = Composite((LinearChild("A", 2d, 3d), 0.5d), (LinearChild("B", 10d, 4d), 0.5d));
        inner.Name = "Inner";
        var outer = Composite((inner, 0.5d), (LinearChild("C", 0d, 1d), 0.5d));

        // Act — inner = 6 + 3.5X, so outer = 0.5·(6 + 3.5X) + 0.5·X = 3 + 2.25X.
        var combined = outer.SampleFunction();

        // Assert
        foreach (double x in new[] { 0d, 20d, 100d })
        {
            Assert.AreEqual(3d + (2.25d * x), combined.Function(x), 1e-12);
        }

        CollectionAssert.AreEqual(outer.CanonicalHash(), new CompositeTransform(outer.ToXElement()).CanonicalHash());
    }

    /// <summary>Verifies the factory reconstructs the composite by element name.</summary>
    [TestMethod]
    public void Test_Factory_RoundTripsByElementName()
    {
        // Arrange
        var composite = Composite((LinearChild("Rating A", 2d, 3d), 1d));

        // Act
        var viaFactory = RiskFunctionFactory.CreateTransformFunction(composite.ToXElement());

        // Assert
        Assert.IsInstanceOfType<CompositeTransform>(viaFactory);
        Assert.IsNull(RiskFunctionFactory.CreateHazardFunction(composite.ToXElement()),
            "A composite transform must not satisfy the hazard cluster filter.");
        CollectionAssert.AreEqual(composite.CanonicalHash(), viaFactory!.CanonicalHash());
    }

    /// <summary>Verifies the composite refuses to sample an invalid configuration.</summary>
    [TestMethod]
    public void Test_SampleFunction_InvalidComposite_Throws()
    {
        // Assert
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeTransform().SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(
            () => Composite((LinearChild("A", 2d, 3d), 0.4d), (LinearChild("B", 10d, 4d), 0.4d)).SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(() => Composite((null!, 1d)).SampleFunction(0.5d));
    }

    /// <summary>Verifies entry membership changes reconcile subscriptions without leaking.</summary>
    [TestMethod]
    public void Test_PropertyChange_MembershipAndEntryEdits()
    {
        // Arrange
        var composite = Composite((LinearChild("A", 2d, 3d), 1d));
        var entry = composite.TransformFunctions[0];
        var raised = new List<string>();
        composite.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.Weight = 0.5d;
        Assert.IsTrue(raised.Contains(nameof(CompositeTransform.TransformFunctions)));

        raised.Clear();
        composite.TransformFunctions.Clear();
        Assert.IsTrue(raised.Contains(nameof(CompositeTransform.TransformFunctions)));

        raised.Clear();
        entry.Weight = 0.25d;
        Assert.AreEqual(0, raised.Count, "A removed entry must no longer notify the composite.");
    }
}
