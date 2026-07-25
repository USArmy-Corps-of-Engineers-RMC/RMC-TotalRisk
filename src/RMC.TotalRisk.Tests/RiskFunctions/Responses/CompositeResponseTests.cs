using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Responses;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses;

/// <summary>
/// Unit tests for <see cref="CompositeResponse"/> — the v1.0 defaults and both combination rules
/// (aleatory mixture and competing-risks weakest link), the deliberate curve-sample refusal,
/// deterministic RNG-free sampling, content-derived child seeding, dual-mode serialization,
/// projected-identity hashing with its three coercions, and the validation matrix.
/// </summary>
[TestClass]
public class CompositeResponseTests
{
    /// <summary>The standard axis labels shared by composites and children in these tests.</summary>
    private static void Label(IResponseFunction function)
    {
        function.SpecifiedHazard = "Stage";
        function.HazardUnit = "ft";
    }

    /// <summary>
    /// Builds a labeled deterministic parametric fragility over the given Normal capacity — an
    /// exact analytic CDF, so combination rules can be checked in closed form.
    /// </summary>
    private static ParametricResponse NormalChild(string name, double mean, double sd)
    {
        var child = new ParametricResponse
        {
            Name = name,
            ParentDistribution = new Normal(mean, sd),
            IsUncertain = false,
        };
        Label(child);
        child.Estimate();
        return child;
    }

    /// <summary>
    /// Builds a labeled sampler-driven child: a tabular fragility (D = 1) whose draws come from its
    /// own content-seeded sampler, so ordinal-in-seed independence is observable.
    /// </summary>
    /// <remarks>
    /// Ordinate distributions are <see cref="Uniform"/> rather than Normal because a tabular
    /// response gates on each ordinate's support lying inside [0, 1] — an unbounded distribution
    /// makes the table unusable.
    /// </remarks>
    private static TabularResponse TabularChild(string name, double lowMean, double highMean, double halfWidth)
    {
        var child = new TabularResponse
        {
            Name = name,
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(100d, new Uniform(lowMean - halfWidth, lowMean + halfWidth)),
                    new UncertainOrdinate(200d, new Uniform(highMean - halfWidth, highMean + halfWidth)),
                },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Uniform),
        };
        Label(child);
        return child;
    }

    /// <summary>Builds a labeled composite over the given (child, weight) pairs.</summary>
    private static CompositeResponse Composite(CompositeCombinationType type, params (IResponseFunction Function, double Weight)[] entries)
    {
        var composite = new CompositeResponse(entries.Select(e => new WeightedResponseFunction(e.Function, e.Weight)))
        {
            Name = "Composite",
            CompositeCombinationType = type,
        };
        Label(composite);
        return composite;
    }

    /// <summary>Verifies the v1.0 default construction state (Mixture, independent, empty).</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var r = new CompositeResponse();

        // Assert
        Assert.AreEqual(CompositeCombinationType.Mixture, r.CompositeCombinationType);
        Assert.AreEqual(DependencyType.Independent, r.Dependency);
        Assert.IsNull(r.CorrelationMatrix);
        Assert.AreEqual(Transform.None, r.HazardTransform);
        Assert.AreEqual(Transform.None, r.ProbabilityTransform, "The v1.0 response default is None, unlike the hazard composite's NormalZ.");
        Assert.AreEqual(0, r.ResponseFunctions.Count);
        Assert.AreEqual(ResponseFunctionType.Composite, r.FunctionType);
        Assert.AreEqual(0, r.SamplingDimensions);
        Assert.IsTrue(r.IsDeterministic);
        Assert.IsTrue(r.IsMonotonic(), "An empty composite is trivially monotonic (v1.0 parity).");
    }

    /// <summary>Verifies the convenience constructor wires entries and their subscriptions.</summary>
    [TestMethod]
    public void Test_Ctor_FromEnumerable_WiresEntries()
    {
        // Arrange
        var child = NormalChild("Overtopping", 140d, 30d);
        var composite = Composite(CompositeCombinationType.Mixture, (child, 1d));
        var raised = new List<string>();
        composite.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        child.EffectiveRecordLength = 75;

        // Assert
        Assert.AreEqual(1, composite.ResponseFunctions.Count);
        CollectionAssert.Contains(raised, nameof(CompositeResponse.ResponseFunctions));
        Assert.ThrowsException<ArgumentNullException>(() => new CompositeResponse((IEnumerable<WeightedResponseFunction>)null!));
    }

    /// <summary>Verifies the validation matrix: labels, children, weights, and the warning downgrades.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // A labeled Mixture composite with matching children and weights summing to one is clean.
        var valid = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Overtopping", 140d, 30d), 0.45d), (NormalChild("Piping", 160d, 10d), 0.55d));
        var (ok, okMessages) = valid.Validate();
        Assert.IsTrue(ok);
        Assert.AreEqual(0, okMessages.Count);

        // Unlabeled and empty: 2 label errors + the no-functions error.
        var (isValid, messages) = new CompositeResponse().Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(3, messages.Count);

        // A null child entry is an error.
        var nullChild = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 140d, 30d), 1d));
        nullChild.ResponseFunctions.Add(new WeightedResponseFunction(null, 0d));
        Assert.IsFalse(nullChild.Validate().IsValid);

        // Mixture weights must lie in [0, 1] and sum to one.
        Assert.IsFalse(Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.4d), (NormalChild("B", 160d, 10d), 0.4d)).Validate().IsValid);

        // A child whose labels differ is a warning, not an error.
        var mismatched = NormalChild("Odd", 140d, 30d);
        mismatched.HazardUnit = "m";
        var (warnValid, warnMessages) = Composite(CompositeCombinationType.Mixture, (mismatched, 1d)).Validate();
        Assert.IsTrue(warnValid);
        Assert.IsTrue(warnMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)));

        // A single-entry competing-risks combination warns that it degenerates.
        var (singleValid, singleMessages) = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("Only", 140d, 30d), 1d)).Validate();
        Assert.IsTrue(singleValid);
        Assert.IsTrue(singleMessages.Any(m => m.Contains("degenerates")));
    }

    /// <summary>
    /// Verifies a <c>NonFailResponse</c> child is rejected: it emits no distribution, so it would
    /// otherwise surface as a null reference inside the combination kernel.
    /// </summary>
    [TestMethod]
    public void Test_Validate_NonFailChild_IsError()
    {
        // Arrange
        var nonFail = new NonFailResponse();
        Label(nonFail);
        var composite = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Overtopping", 140d, 30d), 0.5d), (nonFail, 0.5d));

        // Assert
        var (isValid, messages) = composite.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("non-failure response")));
        Assert.ThrowsException<InvalidOperationException>(() => composite.SampleFunction());
    }

    /// <summary>Verifies weights are inert under competing risks.</summary>
    [TestMethod]
    public void Test_Validate_CompetingRisksWeights_AreInert()
    {
        // Arrange — weights that would fail hard under Mixture.
        var competing = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 140d, 30d), 7d), (NormalChild("B", 160d, 10d), -3d));

        // Assert
        Assert.IsTrue(competing.Validate().IsValid);
        competing.CompositeCombinationType = CompositeCombinationType.Mixture;
        Assert.IsFalse(competing.Validate().IsValid);
    }

    /// <summary>Verifies the correlation-matrix gate: required dimension and positive definiteness.</summary>
    [TestMethod]
    public void Test_Validate_CorrelationMatrix_DimensionAndPositiveDefiniteness()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 140d, 30d), 0.5d), (NormalChild("B", 160d, 10d), 0.5d));

        // Assert
        Assert.IsTrue(composite.IsCorrelationMatrixValid());
        composite.Dependency = DependencyType.CorrelationMatrix;
        Assert.IsFalse(composite.IsCorrelationMatrixValid());
        composite.CorrelationMatrix = new double[,] { { 1d } };
        Assert.IsFalse(composite.IsCorrelationMatrixValid());
        composite.CorrelationMatrix = new double[,] { { 1d, 1d }, { 1d, 1d } };
        Assert.IsFalse(composite.IsCorrelationMatrixValid());
        composite.CorrelationMatrix = new double[,] { { 1d, 0.5d }, { 0.5d, 1d } };
        Assert.IsTrue(composite.IsCorrelationMatrixValid());
        Assert.IsTrue(composite.Validate().IsValid);
    }

    /// <summary>Verifies circular-reference detection, direct and nested.</summary>
    [TestMethod]
    public void Test_Validate_CircularReference_DirectAndNested()
    {
        // Arrange — direct self-reference.
        var direct = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 140d, 30d), 1d));
        direct.ResponseFunctions[0].ResponseFunction = direct;
        var (directValid, directMessages) = direct.Validate();
        Assert.IsFalse(directValid);
        Assert.IsTrue(directMessages.Any(m => m.Contains("Circular reference")));

        // Nested: outer → inner → outer.
        var outer = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 140d, 30d), 1d));
        var inner = Composite(CompositeCombinationType.Mixture, (NormalChild("B", 160d, 10d), 1d));
        outer.ResponseFunctions[0].ResponseFunction = inner;
        inner.ResponseFunctions[0].ResponseFunction = outer;
        Assert.IsFalse(outer.Validate().IsValid);
        Assert.IsTrue(outer.ContainsComposite(inner));
        Assert.ThrowsException<ArgumentNullException>(() => outer.ContainsComposite(null!));
    }

    /// <summary>
    /// Verifies the aleatory mixture rule in closed form: the conditional failure probability at a
    /// hazard level is the weighted sum of the child fragilities.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Mean_Mixture_EqualsWeightedChildFragilities()
    {
        // Arrange
        var a = new Normal(140d, 30d);
        var b = new Normal(160d, 10d);
        var composite = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.45d), (NormalChild("B", 160d, 10d), 0.55d));

        // Act
        var combined = composite.SampleFunction();

        // Assert
        Assert.IsInstanceOfType<Mixture>(combined);
        foreach (double h in new[] { 100d, 130d, 150d, 170d, 200d })
        {
            Assert.AreEqual(0.45d * a.CDF(h) + 0.55d * b.CDF(h), combined.CDF(h), 1e-12);
        }
    }

    /// <summary>
    /// Verifies the competing-risks weakest-link rule in closed form under independence:
    /// <c>p(h) = 1 − ∏(1 − pᵢ(h))</c> — the one substantive divergence from the hazard composite,
    /// which takes the maximum instead.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Mean_CompetingRisks_MinRule_EqualsWeakestLink()
    {
        // Arrange
        var a = new Normal(140d, 30d);
        var b = new Normal(160d, 10d);
        var composite = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 140d, 30d), 0.45d), (NormalChild("B", 160d, 10d), 0.55d));

        // Act
        var combined = composite.SampleFunction();

        // Assert
        Assert.IsInstanceOfType<CompetingRisks>(combined);
        foreach (double h in new[] { 100d, 130d, 150d, 170d, 200d })
        {
            Assert.AreEqual(1d - ((1d - a.CDF(h)) * (1d - b.CDF(h))), combined.CDF(h), 1e-10);
        }
    }

    /// <summary>
    /// Verifies the weakest link is at least as likely to fail as any single mechanism, and the
    /// mixture always lies between the child fragilities.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Bracketing_WeakestLinkAboveMixtureBetween()
    {
        // Arrange
        var a = new Normal(140d, 30d);
        var b = new Normal(160d, 10d);
        var mixture = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.45d), (NormalChild("B", 160d, 10d), 0.55d)).SampleFunction();
        var competing = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 140d, 30d), 0.45d), (NormalChild("B", 160d, 10d), 0.55d)).SampleFunction();

        // Assert
        foreach (double h in new[] { 110d, 140d, 165d, 195d })
        {
            double lo = Math.Min(a.CDF(h), b.CDF(h));
            double hi = Math.Max(a.CDF(h), b.CDF(h));
            Assert.IsTrue(competing.CDF(h) >= hi - 1e-10, $"The weakest link must fail at least as readily as any child at {h}.");
            Assert.IsTrue(mixture.CDF(h) >= lo - 1e-10 && mixture.CDF(h) <= hi + 1e-10, $"Mixture must lie between the child fragilities at {h}.");
        }
    }

    /// <summary>
    /// Verifies all three ordered-pair curve overloads throw — the ratified v1.0-parity decision
    /// (the engine consumes the distribution form exclusively, and a union-knot re-tabulation would
    /// be wrong between knots under the weakest-link rule).
    /// </summary>
    [TestMethod]
    public void Test_SampleResponseFunction_AllOverloads_Throw()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 140d, 30d), 1d));

        // Assert
        Assert.ThrowsException<NotImplementedException>(() => composite.SampleResponseFunction());
        Assert.ThrowsException<NotImplementedException>(() => composite.SampleResponseFunction(0.5d));
        Assert.ThrowsException<NotImplementedException>(() => composite.SampleResponseFunction(0));
    }

    /// <summary>
    /// Verifies the monotonicity theorem: monotone children imply a monotone combination under both
    /// rules, and a non-monotone child breaks it.
    /// </summary>
    [TestMethod]
    public void Test_IsMonotonic_AndOverChildren()
    {
        // Arrange — parametric fragilities are always monotonic.
        var monotone = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.5d), (NormalChild("B", 160d, 10d), 0.5d));
        Assert.IsTrue(monotone.IsMonotonic());
        monotone.CompositeCombinationType = CompositeCombinationType.CompetingRisks;
        Assert.IsTrue(monotone.IsMonotonic());

        // A tabular child whose probabilities decrease with hazard is not monotonic.
        var decreasing = new TabularResponse
        {
            Name = "Decreasing",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(100d, new Deterministic(0.9d)),
                    new UncertainOrdinate(200d, new Deterministic(0.1d)),
                },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
        Label(decreasing);
        Assert.IsFalse(Composite(CompositeCombinationType.Mixture, (decreasing, 1d)).IsMonotonic());
    }

    /// <summary>Verifies the probability envelope and the empty-composite throw.</summary>
    [TestMethod]
    public void Test_MinMaxProbability_ChildEnvelope_EmptyThrows()
    {
        // Arrange
        var a = TabularChild("A", 0.1d, 0.8d, 0.02d);
        var b = TabularChild("B", 0.2d, 0.9d, 0.02d);
        var composite = Composite(CompositeCombinationType.Mixture, (a, 0.5d), (b, 0.5d));

        // Assert — the child envelope (v1.0 parity: it bounds rather than equals the combined curve).
        Assert.AreEqual(Math.Min(a.MinProbability(), b.MinProbability()), composite.MinProbability(), 0d);
        Assert.AreEqual(Math.Max(a.MaxProbability(), b.MaxProbability()), composite.MaxProbability(), 0d);
        Assert.AreEqual(Math.Min(a.MinHazard(), b.MinHazard()), composite.MinHazard(), 0d);
        Assert.AreEqual(Math.Max(a.MaxHazard(), b.MaxHazard()), composite.MaxHazard(), 0d);

        Assert.ThrowsException<InvalidOperationException>(() => new CompositeResponse().MinHazard());
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeResponse().MaxHazard());
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeResponse().MinProbability());
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeResponse().MaxProbability());
    }

    /// <summary>Verifies the percentile overload is deterministic, RNG-free, and co-monotonic.</summary>
    [TestMethod]
    public void Test_SampleFunction_Percentile_DeterministicAndCoMonotonic()
    {
        // Arrange
        var a = TabularChild("A", 0.1d, 0.8d, 0.02d);
        var b = TabularChild("B", 0.2d, 0.9d, 0.02d);
        var composite = Composite(CompositeCombinationType.Mixture, (a, 0.45d), (b, 0.55d));

        // Act
        double first = composite.SampleFunction(0.3d).CDF(150d);
        double second = composite.SampleFunction(0.3d).CDF(150d);

        // Assert
        Assert.AreEqual(first, second, 0d, "Percentile sampling must be deterministic and RNG-free.");
        Assert.AreEqual(0.45d * a.SampleFunction(0.3d).CDF(150d) + 0.55d * b.SampleFunction(0.3d).CDF(150d), first, 1e-12);
    }

    /// <summary>Verifies §5.8.5 child seeding: ordinal-in-seed independence and metadata inertness.</summary>
    [TestMethod]
    public void Test_SetupSampler_RecursesWithContentDerivedChildSeeds()
    {
        // Arrange — two identical-content sampler-driven children.
        const int Seed = 20260725;
        var composite = Composite(CompositeCombinationType.Mixture,
            (TabularChild("First", 0.1d, 0.8d, 0.05d), 0.5d), (TabularChild("Second", 0.1d, 0.8d, 0.05d), 0.5d));
        composite.SetupSampler(32, Seed, SamplingScheme.LatinHypercube);

        var replica0 = TabularChild("Replica0", 0.1d, 0.8d, 0.05d);
        var replica1 = TabularChild("Replica1", 0.1d, 0.8d, 0.05d);
        replica0.SetupSampler(32, SeedHelpers.HashCombine(Seed, replica0.CanonicalHash(), 0), SamplingScheme.LatinHypercube);
        replica1.SetupSampler(32, SeedHelpers.HashCombine(Seed, replica1.CanonicalHash(), 1), SamplingScheme.LatinHypercube);

        bool siblingsDiffer = false;
        for (int i = 0; i < 32; i++)
        {
            double expected = 0.5d * replica0.SampleFunction(i).CDF(150d) + 0.5d * replica1.SampleFunction(i).CDF(150d);
            Assert.AreEqual(expected, composite.SampleFunction(i).CDF(150d), 1e-12,
                "Child draws must follow the documented HashCombine(seed, childHash, ordinal) recipe.");
            if (Math.Abs(replica0.SampleFunction(i).CDF(150d) - replica1.SampleFunction(i).CDF(150d)) > 1e-9)
                siblingsDiffer = true;
        }
        Assert.IsTrue(siblingsDiffer, "Identical-content siblings must draw independently (ordinal in the seed).");

        // Metadata edits are hash-inert, so re-setup reproduces the identical stream.
        double before = composite.SampleFunction(7).CDF(150d);
        composite.ResponseFunctions[0].ResponseFunction!.Name = "First (renamed)";
        composite.ResponseFunctions[1].ResponseFunction!.AssignNewId();
        composite.SetupSampler(32, Seed, SamplingScheme.LatinHypercube);
        Assert.AreEqual(before, composite.SampleFunction(7).CDF(150d), 0d);
    }

    /// <summary>Verifies the posterior-capacity guard rejects a shallow parametric child at setup.</summary>
    [TestMethod]
    public void Test_SetupSampler_PosteriorCapacityTooSmall_Throws()
    {
        // Arrange — a 200-realization posterior asked to serve 1,000 realizations.
        var shallow = new ParametricResponse
        {
            Name = "Shallow",
            ParentDistribution = new Normal(140d, 30d),
            EffectiveRecordLength = 50,
            Realizations = 200,
        };
        Label(shallow);
        shallow.Estimate();
        var composite = Composite(CompositeCombinationType.Mixture, (shallow, 1d));

        // Act
        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => composite.SetupSampler(1000, 12345, SamplingScheme.LatinHypercube));

        // Assert
        StringAssert.Contains(ex.Message, "1000");
        StringAssert.Contains(ex.Message, "200");
        StringAssert.Contains(ex.Message, "Shallow");
        composite.SetupSampler(200, 12345, SamplingScheme.LatinHypercube);
    }

    /// <summary>Verifies sampling dimensions are zero in both modes.</summary>
    [TestMethod]
    public void Test_SamplingDimensions_ZeroInBothModes()
    {
        // Arrange
        var r = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 140d, 30d), 1d));

        // Assert
        Assert.AreEqual(0, r.SamplingDimensions);
        r.CompositeCombinationType = CompositeCombinationType.CompetingRisks;
        Assert.AreEqual(0, r.SamplingDimensions);
    }

    /// <summary>
    /// Verifies the determinism answer, which diverges from <c>CompositeConsequence</c>: a mixture
    /// of deterministic children is itself deterministic.
    /// </summary>
    [TestMethod]
    public void Test_IsDeterministic_MixtureWithDeterministicChildren_IsTrue()
    {
        // Assert
        Assert.IsTrue(Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.45d), (NormalChild("B", 160d, 10d), 0.55d)).IsDeterministic);
        Assert.IsFalse(Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.5d), (TabularChild("B", 0.1d, 0.8d, 0.02d), 0.5d)).IsDeterministic);
    }

    /// <summary>Verifies the self-contained round-trip deep-copies children and preserves the hash.</summary>
    [TestMethod]
    public void Test_Serialization_SelfContained_RoundTrip_DeepCopiesChildren()
    {
        // Arrange
        var original = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Overtopping", 140d, 30d), 0.45d), (NormalChild("Piping", 160d, 10d), 0.55d));
        original.ProbabilityTransform = Transform.NormalZ;

        // Act
        var restored = new CompositeResponse(original.ToXElement());

        // Assert
        Assert.AreEqual(original.CompositeCombinationType, restored.CompositeCombinationType);
        Assert.AreEqual(Transform.NormalZ, restored.ProbabilityTransform);
        Assert.AreEqual(2, restored.ResponseFunctions.Count);
        Assert.AreEqual(0.45d, restored.ResponseFunctions[0].Weight, 0d);
        Assert.AreNotSame(original.ResponseFunctions[0].ResponseFunction, restored.ResponseFunctions[0].ResponseFunction);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());

        // Nested composites round-trip inline too.
        var nested = Composite(CompositeCombinationType.Mixture, (original, 1d));
        nested.Name = "Nested";
        var nestedRestored = new CompositeResponse(nested.ToXElement());
        Assert.IsInstanceOfType<CompositeResponse>(nestedRestored.ResponseFunctions[0].ResponseFunction);
        CollectionAssert.AreEqual(nested.CanonicalHash(), nestedRestored.CanonicalHash());

        Assert.ThrowsException<ArgumentNullException>(() => new CompositeResponse((XElement)null!));
    }

    /// <summary>Verifies the by-reference round-trip reattaches the live stored instances.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_RoundTrip_ReattachesSameInstances()
    {
        // Arrange
        var overtopping = NormalChild("Overtopping", 140d, 30d);
        var piping = NormalChild("Piping", 160d, 10d);
        var store = new IRiskFunction[] { overtopping, piping }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));
        var original = Composite(CompositeCombinationType.Mixture, (overtopping, 0.45d), (piping, 0.55d));

        // Act
        var restored = new CompositeResponse(original.ToXElement(RiskSerializationMode.ByReference), resolver);

        // Assert
        Assert.AreSame(overtopping, restored.ResponseFunctions[0].ResponseFunction);
        Assert.AreSame(piping, restored.ResponseFunctions[1].ResponseFunction);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>Verifies an unresolvable reference keeps its weighted entry and is reported.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_Unresolved_PreservesWeightEntry_AndValidateReports()
    {
        // Arrange
        var original = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Overtopping", 140d, 30d), 0.45d), (NormalChild("Piping", 160d, 10d), 0.55d));
        var empty = new RiskFunctionResolver(_ => null, _ => null);

        // Act — strip the ids to exercise the lenient name path, which records instead of throwing.
        var form = original.ToXElement(RiskSerializationMode.ByReference);
        foreach (var marker in form.Descendants("FunctionReference")) marker.Attribute("Id")!.Remove();
        var restored = new CompositeResponse(form, empty);

        // Assert
        Assert.AreEqual(2, restored.ResponseFunctions.Count);
        Assert.AreEqual(0.45d, restored.ResponseFunctions[0].Weight, 0d);
        Assert.IsNull(restored.ResponseFunctions[0].ResponseFunction);
        var (isValid, messages) = restored.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("Overtopping") && m.Contains("not found")));
    }

    /// <summary>Verifies a stale serialized reference id throws rather than resolving silently.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_StaleId_Throws()
    {
        // Arrange
        var original = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 140d, 30d), 1d));
        var empty = new RiskFunctionResolver(_ => null, _ => null);

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(
            () => new CompositeResponse(original.ToXElement(RiskSerializationMode.ByReference), empty));
    }

    /// <summary>Verifies the hash is identical across serialization modes.</summary>
    [TestMethod]
    public void Test_CanonicalHash_IdenticalAcrossSerializationModes()
    {
        // Arrange
        var a = NormalChild("A", 140d, 30d);
        var b = NormalChild("B", 160d, 10d);
        var store = new IRiskFunction[] { a, b }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));
        var original = Composite(CompositeCombinationType.Mixture, (a, 0.45d), (b, 0.55d));

        // Assert
        CollectionAssert.AreEqual(original.CanonicalHash(),
            new CompositeResponse(original.ToXElement(RiskSerializationMode.SelfContained)).CanonicalHash());
        CollectionAssert.AreEqual(original.CanonicalHash(),
            new CompositeResponse(original.ToXElement(RiskSerializationMode.ByReference), resolver).CanonicalHash());
    }

    /// <summary>Verifies metadata edits are hash-inert and compute edits move the hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataInert_ComputeEditsMove()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.45d), (NormalChild("B", 160d, 10d), 0.55d));
        byte[] baseline = composite.CanonicalHash();

        // Metadata is inert.
        composite.Name = "Renamed";
        composite.Description = "A description";
        composite.SpecifiedHazard = "Pool";
        composite.AssignNewId();
        composite.ResponseFunctions[0].ResponseFunction!.Name = "A (renamed)";
        CollectionAssert.AreEqual(baseline, composite.CanonicalHash());

        // The combination mode moves it.
        composite.CompositeCombinationType = CompositeCombinationType.CompetingRisks;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.CompositeCombinationType = CompositeCombinationType.Mixture;

        // A mixture weight moves it.
        composite.ResponseFunctions[0].Weight = 0.5d;
        composite.ResponseFunctions[1].Weight = 0.5d;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.ResponseFunctions[0].Weight = 0.45d;
        composite.ResponseFunctions[1].Weight = 0.55d;
        CollectionAssert.AreEqual(baseline, composite.CanonicalHash());

        // The interpolation transforms move it.
        composite.ProbabilityTransform = Transform.NormalZ;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.ProbabilityTransform = Transform.None;

        // Entry order is semantic.
        CollectionAssert.AreNotEqual(baseline, Composite(CompositeCombinationType.Mixture,
            (NormalChild("B", 160d, 10d), 0.55d), (NormalChild("A", 140d, 30d), 0.45d)).CanonicalHash());
    }

    /// <summary>Verifies the three hash coercions keep inert edits from re-rolling seeds.</summary>
    [TestMethod]
    public void Test_CanonicalHash_InertEdits_Coerced()
    {
        // Arrange — competing risks, where weights do not participate.
        var competing = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 140d, 30d), 0.45d), (NormalChild("B", 160d, 10d), 0.55d));
        byte[] competingBaseline = competing.CanonicalHash();
        competing.ResponseFunctions[0].Weight = 0.9d;
        CollectionAssert.AreEqual(competingBaseline, competing.CanonicalHash());

        // Mixture, where the dependence and matrix do not participate.
        var mixture = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.45d), (NormalChild("B", 160d, 10d), 0.55d));
        byte[] mixtureBaseline = mixture.CanonicalHash();
        mixture.Dependency = DependencyType.PerfectlyNegative;
        mixture.CorrelationMatrix = new double[,] { { 1d, 0.3d }, { 0.3d, 1d } };
        CollectionAssert.AreEqual(mixtureBaseline, mixture.CanonicalHash());

        // But under competing risks the dependence is live content.
        competing.Dependency = DependencyType.PerfectlyPositive;
        CollectionAssert.AreNotEqual(competingBaseline, competing.CanonicalHash());
    }

    /// <summary>Verifies the uncertainty summary is deterministic and leaves the live sampler untouched.</summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_DeterministicAndDoesNotDisturbLiveSampler()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture,
            (TabularChild("A", 0.1d, 0.8d, 0.05d), 0.45d), (TabularChild("B", 0.2d, 0.9d, 0.05d), 0.55d));
        composite.SetupSampler(64, 777, SamplingScheme.LatinHypercube);
        double beforeDraw = composite.SampleFunction(5).CDF(150d);

        // Act
        var results = composite.ComputeUncertaintyResults();
        var repeat = composite.ComputeUncertaintyResults();

        // Assert
        Assert.IsNotNull(results);
        double[] hazards = composite.UncertaintySummaryHazards();
        Assert.AreEqual(hazards.Length, results!.MeanCurve!.Length);
        for (int i = 0; i < hazards.Length; i++)
        {
            Assert.AreEqual(results.MeanCurve[i], repeat!.MeanCurve![i], 0d);
            Assert.IsTrue(results.ConfidenceIntervals![i, 0] <= results.ConfidenceIntervals[i, 1]);
        }

        Assert.AreEqual(beforeDraw, composite.SampleFunction(5).CDF(150d), 0d);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => composite.ComputeUncertaintyResults(0d));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => composite.ComputeUncertaintyResults(1d));
        Assert.IsNull(new CompositeResponse().ComputeUncertaintyResults());
    }

    /// <summary>Verifies a nested composite samples and hashes through the recursion.</summary>
    [TestMethod]
    public void Test_NestedComposite_SamplesAndHashes()
    {
        // Arrange
        var inner = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.5d), (NormalChild("B", 160d, 10d), 0.5d));
        inner.Name = "Inner";
        var outer = Composite(CompositeCombinationType.Mixture, (inner, 0.5d), (NormalChild("C", 180d, 20d), 0.5d));

        // Act
        var combined = outer.SampleFunction();

        // Assert — the flattened mixture: 0.25 A + 0.25 B + 0.5 C.
        var a = new Normal(140d, 30d);
        var b = new Normal(160d, 10d);
        var c = new Normal(180d, 20d);
        foreach (double h in new[] { 120d, 160d, 200d })
        {
            Assert.AreEqual(0.25d * a.CDF(h) + 0.25d * b.CDF(h) + 0.5d * c.CDF(h), combined.CDF(h), 1e-9);
        }

        CollectionAssert.AreEqual(outer.CanonicalHash(), new CompositeResponse(outer.ToXElement()).CanonicalHash());
    }

    /// <summary>Verifies the factory reconstructs the composite by element name.</summary>
    [TestMethod]
    public void Test_Factory_RoundTripsByElementName()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 140d, 30d), 1d));

        // Act
        var viaFactory = RiskFunctionFactory.CreateResponseFunction(composite.ToXElement());

        // Assert
        Assert.IsInstanceOfType<CompositeResponse>(viaFactory);
        Assert.IsNull(RiskFunctionFactory.CreateHazardFunction(composite.ToXElement()),
            "A composite response must not satisfy the hazard cluster filter.");
        CollectionAssert.AreEqual(composite.CanonicalHash(), viaFactory!.CanonicalHash());
    }

    /// <summary>Verifies the composite refuses to sample an invalid configuration.</summary>
    [TestMethod]
    public void Test_SampleFunction_InvalidComposite_Throws()
    {
        // Assert
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeResponse().SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(() => Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 140d, 30d), 0.4d), (NormalChild("B", 160d, 10d), 0.4d)).SampleFunction());
        Assert.ThrowsException<InvalidOperationException>(() => Composite(CompositeCombinationType.Mixture, (null!, 1d)).SampleFunction(0.5d));
    }

    /// <summary>Verifies entry membership changes reconcile subscriptions without leaking.</summary>
    [TestMethod]
    public void Test_PropertyChange_MembershipAndEntryEdits()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 140d, 30d), 1d));
        var entry = composite.ResponseFunctions[0];
        var raised = new List<string>();
        composite.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.Weight = 0.5d;
        Assert.IsTrue(raised.Contains(nameof(CompositeResponse.ResponseFunctions)));

        raised.Clear();
        composite.ResponseFunctions.Clear();
        Assert.IsTrue(raised.Contains(nameof(CompositeResponse.ResponseFunctions)));

        raised.Clear();
        entry.Weight = 0.25d;
        Assert.AreEqual(0, raised.Count, "A removed entry must no longer notify the composite.");
    }
}
