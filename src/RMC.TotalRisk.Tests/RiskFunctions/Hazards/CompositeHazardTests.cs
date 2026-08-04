using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Hazards;

namespace RMC.TotalRisk.Tests.RiskFunctions.Hazards;

/// <summary>
/// Unit tests for <see cref="CompositeHazard"/> — the v1.0 defaults and both combination rules
/// (aleatory mixture and competing-risks maximum), deterministic RNG-free sampling, content-derived
/// child seeding, dual-mode serialization over the shared function-entry contract, projected-identity
/// hashing with its three coercions, the validation matrix, and the observable weighted-entry wiring.
/// </summary>
[TestClass]
public class CompositeHazardTests
{
    /// <summary>The standard axis labels shared by composites and children in these tests.</summary>
    private static void Label(IHazardFunction function)
    {
        function.SpecifiedHazard = "Peak Flow";
        function.HazardUnit = "cfs";
    }

    /// <summary>
    /// Builds a labeled deterministic parametric child over the given Normal parent — an exact
    /// analytic CDF, so combination rules can be checked in closed form.
    /// </summary>
    private static ParametricUnivariateHazard NormalChild(string name, double mean, double sd)
    {
        var child = new ParametricUnivariateHazard
        {
            Name = name,
            ParentDistribution = new Normal(mean, sd),
            IsUncertain = false,
        };
        Label(child);
        child.Estimate();
        return child;
    }

    /// <summary>Builds a labeled uncertain parametric child with a small, fast posterior.</summary>
    /// <remarks>
    /// Posterior-indexed (D = 0): its draws come from the posterior fixed by <c>PRNGSeed</c> at
    /// <c>Estimate()</c>, not from <c>SetupSampler</c>. Use <see cref="TabularChild"/> wherever a
    /// test needs a genuinely sampler-driven child.
    /// </remarks>
    private static ParametricUnivariateHazard UncertainChild(string name, double mean, double sd, int realizations = 200)
    {
        var child = new ParametricUnivariateHazard
        {
            Name = name,
            ParentDistribution = new Normal(mean, sd),
            EffectiveRecordLength = 50,
            Realizations = realizations,
        };
        Label(child);
        child.Estimate();
        return child;
    }

    /// <summary>
    /// Builds a labeled sampler-driven child: a hazard-uncertain tabular curve (D = 1) whose draws
    /// come from its own content-seeded sampler, so ordinal-in-seed independence is observable.
    /// </summary>
    private static TabularHazard TabularChild(string name, double lowMean, double highMean, double sd)
    {
        var child = new TabularHazard
        {
            Name = name,
            UncertaintyValue = FunctionUncertainty.Hazard,
            HazardUncertainFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Normal(lowMean, sd)),
                    new UncertainOrdinate(0.001d, new Normal(highMean, sd)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Normal),
        };
        Label(child);
        return child;
    }

    /// <summary>Builds a labeled composite over the given (child, weight) pairs.</summary>
    private static CompositeHazard Composite(CompositeCombinationType type, params (IHazardFunction Function, double Weight)[] entries)
    {
        var composite = new CompositeHazard(entries.Select(e => new WeightedHazardFunction(e.Function, e.Weight)))
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
        var h = new CompositeHazard();

        // Assert
        Assert.AreEqual(CompositeCombinationType.Mixture, h.CompositeCombinationType);
        Assert.AreEqual(DependencyType.Independent, h.Dependency);
        Assert.IsNull(h.CorrelationMatrix);
        Assert.AreEqual(Transform.None, h.HazardTransform);
        Assert.AreEqual(Transform.NormalZ, h.ProbabilityTransform);
        Assert.AreEqual(0, h.HazardFunctions.Count);
        Assert.AreEqual(HazardFunctionType.Composite, h.FunctionType);
        Assert.AreEqual(0, h.SamplingDimensions, "The combination is aleatory and consumes no knowledge draw of its own.");
        Assert.IsTrue(h.IsDeterministic);
    }

    /// <summary>Verifies the convenience constructor wires entries and their subscriptions.</summary>
    [TestMethod]
    public void Test_Ctor_FromEnumerable_WiresEntries()
    {
        // Arrange
        var child = NormalChild("Rain", 100d, 20d);
        var composite = Composite(CompositeCombinationType.Mixture, (child, 1d));
        var raised = new List<string>();
        composite.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act — a child content edit must reach the composite through the entry subscription.
        child.EffectiveRecordLength = 75;

        // Assert
        Assert.AreEqual(1, composite.HazardFunctions.Count);
        CollectionAssert.Contains(raised, nameof(CompositeHazard.HazardFunctions));
        Assert.ThrowsException<ArgumentNullException>(() => new CompositeHazard((IEnumerable<WeightedHazardFunction>)null!));
    }

    /// <summary>Verifies the validation matrix: labels, children, weights, and the warning downgrades.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // A labeled Mixture composite with matching children and weights summing to one is clean.
        var valid = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Rain", 100d, 20d), 0.45d), (NormalChild("Snow", 60d, 15d), 0.55d));
        var (ok, okMessages) = valid.Validate();
        Assert.IsTrue(ok);
        Assert.AreEqual(0, okMessages.Count);

        // Unlabeled and empty: 2 label errors + the no-functions error.
        var (isValid, messages) = new CompositeHazard().Validate();
        Assert.IsFalse(isValid);
        Assert.AreEqual(3, messages.Count);
        Assert.IsTrue(messages.All(m => m.StartsWith("Error:", StringComparison.Ordinal)));

        // A null child entry is an error.
        var nullChild = Composite(CompositeCombinationType.Mixture, (NormalChild("Rain", 100d, 20d), 1d));
        nullChild.HazardFunctions.Add(new WeightedHazardFunction(null, 0d));
        Assert.IsFalse(nullChild.Validate().IsValid);

        // Mixture weights must lie in [0, 1] and sum to one.
        Assert.IsFalse(Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 1.4d), (NormalChild("B", 60d, 15d), -0.4d)).Validate().IsValid);
        Assert.IsFalse(Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.4d), (NormalChild("B", 60d, 15d), 0.4d)).Validate().IsValid);

        // A child whose labels differ is a warning, not an error (a deliberate downgrade).
        var mismatched = NormalChild("Odd", 100d, 20d);
        mismatched.HazardUnit = "m3/s";
        var warned = Composite(CompositeCombinationType.Mixture, (mismatched, 1d));
        var (warnValid, warnMessages) = warned.Validate();
        Assert.IsTrue(warnValid);
        Assert.IsTrue(warnMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)));

        // A single-entry competing-risks combination warns that it degenerates.
        var single = Composite(CompositeCombinationType.CompetingRisks, (NormalChild("Only", 100d, 20d), 1d));
        var (singleValid, singleMessages) = single.Validate();
        Assert.IsTrue(singleValid);
        Assert.IsTrue(singleMessages.Any(m => m.Contains("degenerates")));

        // An invalid child is reported as a summary line.
        var unestimated = new ParametricUnivariateHazard { Name = "Unestimated", ParentDistribution = new Normal(100d, 20d) };
        Label(unestimated);
        Assert.IsFalse(Composite(CompositeCombinationType.Mixture, (unestimated, 1d)).Validate().IsValid);
    }

    /// <summary>
    /// Verifies that weights are inert under competing risks: any weight passes validation, since
    /// the combination is governed by the maximum rule and the dependence rather than by weights.
    /// </summary>
    [TestMethod]
    public void Test_Validate_CompetingRisksWeights_AreInert()
    {
        // Arrange — weights that would fail hard under Mixture.
        var competing = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 7d), (NormalChild("B", 60d, 15d), -3d));

        // Assert
        Assert.IsTrue(competing.Validate().IsValid);

        // The same entries under Mixture fail.
        competing.CompositeCombinationType = CompositeCombinationType.Mixture;
        Assert.IsFalse(competing.Validate().IsValid);
    }

    /// <summary>Verifies the correlation-matrix gate: required dimension and positive definiteness.</summary>
    [TestMethod]
    public void Test_Validate_CorrelationMatrix_DimensionAndPositiveDefiniteness()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 0.5d), (NormalChild("B", 60d, 15d), 0.5d));

        // A matrix is only required in the correlation-matrix dependence.
        Assert.IsTrue(composite.IsCorrelationMatrixValid());
        composite.Dependency = DependencyType.PerfectlyPositive;
        Assert.IsTrue(composite.IsCorrelationMatrixValid());

        // Missing, in the mode that reads it.
        composite.Dependency = DependencyType.CorrelationMatrix;
        Assert.IsFalse(composite.IsCorrelationMatrixValid());
        Assert.IsFalse(composite.Validate().IsValid);

        // Wrong dimension.
        composite.CorrelationMatrix = new double[,] { { 1d } };
        Assert.IsFalse(composite.IsCorrelationMatrixValid());

        // Not positive definite (perfectly correlated off-diagonals of magnitude one).
        composite.CorrelationMatrix = new double[,] { { 1d, 1d }, { 1d, 1d } };
        Assert.IsFalse(composite.IsCorrelationMatrixValid());

        // Valid.
        composite.CorrelationMatrix = new double[,] { { 1d, 0.5d }, { 0.5d, 1d } };
        Assert.IsTrue(composite.IsCorrelationMatrixValid());
        Assert.IsTrue(composite.Validate().IsValid);

        // Mixture ignores the matrix entirely.
        composite.CompositeCombinationType = CompositeCombinationType.Mixture;
        composite.CorrelationMatrix = null;
        Assert.IsTrue(composite.IsCorrelationMatrixValid());
    }

    /// <summary>Verifies circular-reference detection, direct and nested.</summary>
    [TestMethod]
    public void Test_Validate_CircularReference_DirectAndNested()
    {
        // Arrange — direct self-reference.
        var direct = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 100d, 20d), 1d));
        direct.HazardFunctions[0].HazardFunction = direct;
        var (directValid, directMessages) = direct.Validate();
        Assert.IsFalse(directValid);
        Assert.IsTrue(directMessages.Any(m => m.Contains("Circular reference")));
        Assert.ThrowsException<InvalidOperationException>(() => direct.SampleFunction());

        // Nested: outer → inner → outer.
        var outer = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 100d, 20d), 1d));
        var inner = Composite(CompositeCombinationType.Mixture, (NormalChild("B", 60d, 15d), 1d));
        outer.HazardFunctions[0].HazardFunction = inner;
        inner.HazardFunctions[0].HazardFunction = outer;
        Assert.IsFalse(outer.Validate().IsValid);
        Assert.IsTrue(outer.ContainsComposite(inner));
        Assert.ThrowsException<ArgumentNullException>(() => outer.ContainsComposite(null!));
    }

    /// <summary>
    /// A bivariate child is rejected loudly, at validation and at sampling: a composite has no
    /// univariate collapse for it and would otherwise silently combine its X marginal alone.
    /// </summary>
    [TestMethod]
    public void Test_Validate_BivariateChild_Error()
    {
        // Arrange — a valid composite whose second child is a bivariate hazard.
        var bivariate = new BivariateHazard(
            NormalChild("PGA", 1d, 0.2d), NormalChild("Pool", 15d, 3d))
        {
            Name = "Coupled",
            SpecifiedHazard = "Peak Flow",
            HazardUnit = "cfs",
            SecondarySpecifiedHazard = "Pool Duration",
            SecondaryHazardUnit = "days",
        };
        var composite = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.5d), (bivariate, 0.5d));

        // Act
        var (isValid, messages) = composite.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Contains(
            "Error: The hazard function 'Coupled' is bivariate; a composite hazard function cannot combine bivariate hazard functions."));
        Assert.ThrowsException<InvalidOperationException>(() => composite.SampleFunction());
    }

    /// <summary>
    /// Verifies the aleatory mixture rule in closed form: the combined CDF is exactly the
    /// weighted sum of the child CDFs — report Equation 49.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Mean_Mixture_CdfEqualsWeightedChildCdfs()
    {
        // Arrange
        var a = new Normal(100d, 20d);
        var b = new Normal(60d, 15d);
        var composite = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.45d), (NormalChild("B", 60d, 15d), 0.55d));

        // Act
        var combined = composite.SampleFunction();

        // Assert
        Assert.IsInstanceOfType<Mixture>(combined);
        foreach (double x in new[] { 20d, 50d, 80d, 100d, 130d, 160d })
        {
            Assert.AreEqual(0.45d * a.CDF(x) + 0.55d * b.CDF(x), combined.CDF(x), 1e-12);
        }
    }

    /// <summary>
    /// Verifies the competing-risks maximum rule in closed form under independence: the combined
    /// CDF is the product of the child CDFs (all mechanisms act; the most severe controls).
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Mean_CompetingRisks_MaxRule_CdfEqualsProductOfChildCdfs()
    {
        // Arrange
        var a = new Normal(100d, 20d);
        var b = new Normal(60d, 15d);
        var composite = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 0.45d), (NormalChild("B", 60d, 15d), 0.55d));

        // Act
        var combined = composite.SampleFunction();

        // Assert
        Assert.IsInstanceOfType<CompetingRisks>(combined);
        foreach (double x in new[] { 20d, 50d, 80d, 100d, 130d, 160d })
        {
            Assert.AreEqual(a.CDF(x) * b.CDF(x), combined.CDF(x), 1e-10);
        }
    }

    /// <summary>
    /// Verifies the perfectly positive dependence: the maximum-rule joint probability of
    /// comonotonic variables is the minimum of the marginals.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_CompetingRisks_PerfectlyPositive_EqualsMinimumOfChildCdfs()
    {
        // Arrange
        var a = new Normal(100d, 20d);
        var b = new Normal(60d, 15d);
        var composite = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 0.5d), (NormalChild("B", 60d, 15d), 0.5d));
        composite.Dependency = DependencyType.PerfectlyPositive;

        // Act
        var combined = composite.SampleFunction();

        // Assert
        foreach (double x in new[] { 40d, 70d, 100d, 140d })
        {
            Assert.AreEqual(Math.Min(a.CDF(x), b.CDF(x)), combined.CDF(x), 1e-6);
        }
    }

    /// <summary>
    /// Verifies the combination is bracketed as theory requires: the maximum-rule combined CDF
    /// never exceeds the smallest child CDF, and the mixture always lies between the child CDFs.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Bracketing_CompetingBelowMixtureBetween()
    {
        // Arrange
        var a = new Normal(100d, 20d);
        var b = new Normal(60d, 15d);
        var mixture = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.45d), (NormalChild("B", 60d, 15d), 0.55d)).SampleFunction();
        var competing = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 0.45d), (NormalChild("B", 60d, 15d), 0.55d)).SampleFunction();

        // Assert
        foreach (double x in new[] { 40d, 70d, 100d, 140d })
        {
            double lo = Math.Min(a.CDF(x), b.CDF(x));
            double hi = Math.Max(a.CDF(x), b.CDF(x));
            Assert.IsTrue(competing.CDF(x) <= lo + 1e-10, $"Maximum rule must not exceed the smallest child CDF at {x}.");
            Assert.IsTrue(mixture.CDF(x) >= lo - 1e-10 && mixture.CDF(x) <= hi + 1e-10, $"Mixture must lie between the child CDFs at {x}.");
        }
    }

    /// <summary>
    /// Verifies the percentile overload is deterministic and RNG-free (the v1.0 percentile-reseeded
    /// <c>Random</c> is gone) and drives every child co-monotonically.
    /// </summary>
    [TestMethod]
    public void Test_SampleFunction_Percentile_DeterministicAndCoMonotonic()
    {
        // Arrange
        var a = TabularChild("A", 10d, 100d, 5d);
        var b = TabularChild("B", 5d, 60d, 4d);
        var composite = Composite(CompositeCombinationType.Mixture, (a, 0.45d), (b, 0.55d));

        // Act — repeated calls at the same percentile must agree bit for bit.
        double first = composite.SampleFunction(0.3d).CDF(80d);
        double second = composite.SampleFunction(0.3d).CDF(80d);

        // Assert
        Assert.AreEqual(first, second, 0d, "Percentile sampling must be deterministic and RNG-free.");

        // Every child is driven at the same percentile: the combined CDF is the weighted sum of
        // the children's own percentile samples.
        Assert.AreEqual(0.45d * a.SampleFunction(0.3d).CDF(80d) + 0.55d * b.SampleFunction(0.3d).CDF(80d),
            first, 1e-12);

        // Percentiles a fraction apart resolve distinctly (v1.0 collided them onto one seed).
        Assert.AreNotEqual(composite.SampleFunction(0.300001d).CDF(80d), composite.SampleFunction(0.7d).CDF(80d));
    }

    /// <summary>
    /// Verifies §5.8.5 child seeding: identical-content siblings draw independently (the ordinal in
    /// the seed), the recursion reproduces exactly from the documented recipe, and child metadata
    /// edits cannot move the draws.
    /// </summary>
    [TestMethod]
    public void Test_SetupSampler_RecursesWithContentDerivedChildSeeds()
    {
        // Arrange — two identical-content sampler-driven children.
        const int Seed = 20260725;
        var composite = Composite(CompositeCombinationType.Mixture,
            (TabularChild("First", 10d, 100d, 5d), 0.5d), (TabularChild("Second", 10d, 100d, 5d), 0.5d));
        composite.SetupSampler(32, Seed, SamplingScheme.LatinHypercube);

        // Independently reproduce each child's stream from the documented recipe.
        var replica0 = TabularChild("Replica0", 10d, 100d, 5d);
        var replica1 = TabularChild("Replica1", 10d, 100d, 5d);
        replica0.SetupSampler(32, SeedHelpers.HashCombine(Seed, replica0.CanonicalHash(), 0), SamplingScheme.LatinHypercube);
        replica1.SetupSampler(32, SeedHelpers.HashCombine(Seed, replica1.CanonicalHash(), 1), SamplingScheme.LatinHypercube);

        bool siblingsDiffer = false;
        for (int i = 0; i < 32; i++)
        {
            double expected = 0.5d * replica0.SampleFunction(i).CDF(90d) + 0.5d * replica1.SampleFunction(i).CDF(90d);
            Assert.AreEqual(expected, composite.SampleFunction(i).CDF(90d), 1e-12,
                "Child draws must follow the documented HashCombine(seed, childHash, ordinal) recipe.");
            if (Math.Abs(replica0.SampleFunction(i).CDF(90d) - replica1.SampleFunction(i).CDF(90d)) > 1e-9)
                siblingsDiffer = true;
        }
        Assert.IsTrue(siblingsDiffer, "Identical-content siblings must draw independently (ordinal in the seed).");

        // Metadata edits are hash-inert, so re-setup reproduces the identical stream.
        double before = composite.SampleFunction(7).CDF(90d);
        composite.HazardFunctions[0].HazardFunction!.Name = "First (renamed)";
        composite.HazardFunctions[1].HazardFunction!.AssignNewId();
        composite.SetupSampler(32, Seed, SamplingScheme.LatinHypercube);
        Assert.AreEqual(before, composite.SampleFunction(7).CDF(90d), 0d, "Metadata edits must never move Monte Carlo draws.");
    }

    /// <summary>
    /// Verifies the posterior-capacity guard: a child whose posterior is shallower than the
    /// requested sample size is rejected at setup, naming both counts, rather than throwing deep
    /// inside a realization loop (the v1.1 improvement).
    /// </summary>
    [TestMethod]
    public void Test_SetupSampler_PosteriorCapacityTooSmall_Throws()
    {
        // Arrange — a 200-realization posterior asked to serve 1,000 realizations.
        var composite = Composite(CompositeCombinationType.Mixture, (UncertainChild("Shallow", 100d, 20d, 200), 1d));

        // Act
        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => composite.SetupSampler(1000, 12345, SamplingScheme.LatinHypercube));

        // Assert — both counts and the child name appear in the message.
        StringAssert.Contains(ex.Message, "1000");
        StringAssert.Contains(ex.Message, "200");
        StringAssert.Contains(ex.Message, "Shallow");

        // At or below the posterior depth it sets up cleanly.
        composite.SetupSampler(200, 12345, SamplingScheme.LatinHypercube);
    }

    /// <summary>Verifies the composite's own sampling dimensions are zero in both modes.</summary>
    [TestMethod]
    public void Test_SamplingDimensions_ZeroInBothModes()
    {
        // Arrange
        var c = Composite(CompositeCombinationType.Mixture, (NormalChild("A", 100d, 20d), 1d));

        // Assert
        Assert.AreEqual(0, c.SamplingDimensions);
        c.CompositeCombinationType = CompositeCombinationType.CompetingRisks;
        Assert.AreEqual(0, c.SamplingDimensions);
    }

    /// <summary>
    /// Verifies the determinism answer, which deliberately diverges from
    /// <c>CompositeConsequence</c>: a mixture of deterministic children is itself deterministic,
    /// because the mixture is aleatory and no branch is drawn per realization.
    /// </summary>
    [TestMethod]
    public void Test_IsDeterministic_MixtureWithDeterministicChildren_IsTrue()
    {
        // Assert — the divergence from the consequence composite.
        Assert.IsTrue(Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.45d), (NormalChild("B", 60d, 15d), 0.55d)).IsDeterministic);
        Assert.IsTrue(Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 0.5d), (NormalChild("B", 60d, 15d), 0.5d)).IsDeterministic);

        // An uncertain child makes the composite uncertain.
        Assert.IsFalse(Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.5d), (UncertainChild("B", 60d, 15d), 0.5d)).IsDeterministic);
    }

    /// <summary>Verifies the hazard envelope and the empty-composite throw (no legacy sentinels).</summary>
    [TestMethod]
    public void Test_MinMaxHazard_UnionOfChildren_EmptyThrows()
    {
        // Arrange — children with different supports.
        var narrow = NormalChild("Narrow", 100d, 5d);
        var wide = NormalChild("Wide", 100d, 40d);
        var composite = Composite(CompositeCombinationType.Mixture, (narrow, 0.5d), (wide, 0.5d));

        // Assert — the union: the smallest minimum and the largest maximum.
        Assert.AreEqual(Math.Min(narrow.MinHazard(true), wide.MinHazard(true)), composite.MinHazard(true), 0d);
        Assert.AreEqual(Math.Max(narrow.MaxHazard(true), wide.MaxHazard(true)), composite.MaxHazard(true), 0d);

        // Improved over v1.0: an empty composite throws rather than returning a sentinel.
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeHazard().MinHazard(true));
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeHazard().MaxHazard(true));
    }

    /// <summary>Verifies the self-contained round-trip deep-copies children and preserves the hash.</summary>
    [TestMethod]
    public void Test_Serialization_SelfContained_RoundTrip_DeepCopiesChildren()
    {
        // Arrange
        var original = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Rain", 100d, 20d), 0.45d), (NormalChild("Snow", 60d, 15d), 0.55d));
        original.HazardTransform = Transform.Logarithmic;

        // Act
        var restored = new CompositeHazard(original.ToXElement());

        // Assert — full state, deep-copied children, identical hash.
        Assert.AreEqual(original.CompositeCombinationType, restored.CompositeCombinationType);
        Assert.AreEqual(Transform.Logarithmic, restored.HazardTransform);
        Assert.AreEqual(Transform.NormalZ, restored.ProbabilityTransform);
        Assert.AreEqual(2, restored.HazardFunctions.Count);
        Assert.AreEqual(0.45d, restored.HazardFunctions[0].Weight, 0d);
        Assert.AreEqual("Rain", restored.HazardFunctions[0].HazardFunction!.Name);
        Assert.AreNotSame(original.HazardFunctions[0].HazardFunction, restored.HazardFunctions[0].HazardFunction);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());

        // Nested composites round-trip inline too.
        var nested = Composite(CompositeCombinationType.Mixture, (original, 1d));
        nested.Name = "Nested";
        var nestedRestored = new CompositeHazard(nested.ToXElement());
        Assert.IsInstanceOfType<CompositeHazard>(nestedRestored.HazardFunctions[0].HazardFunction);
        CollectionAssert.AreEqual(nested.CanonicalHash(), nestedRestored.CanonicalHash());

        Assert.ThrowsException<ArgumentNullException>(() => new CompositeHazard((System.Xml.Linq.XElement)null!));
    }

    /// <summary>Verifies the by-reference round-trip reattaches the live stored instances.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_RoundTrip_ReattachesSameInstances()
    {
        // Arrange — a store of two functions and a resolver over it.
        var rain = NormalChild("Rain", 100d, 20d);
        var snow = NormalChild("Snow", 60d, 15d);
        var store = new IRiskFunction[] { rain, snow }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));
        var original = Composite(CompositeCombinationType.Mixture, (rain, 0.45d), (snow, 0.55d));

        // Act
        var restored = new CompositeHazard(original.ToXElement(RiskSerializationMode.ByReference), resolver);

        // Assert — the same live instances, not copies.
        Assert.AreSame(rain, restored.HazardFunctions[0].HazardFunction);
        Assert.AreSame(snow, restored.HazardFunctions[1].HazardFunction);
        Assert.AreEqual(0.45d, restored.HazardFunctions[0].Weight, 0d);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>
    /// Verifies an unresolvable reference keeps its weighted entry (so the weight list, the hash,
    /// and the weight-sum validation survive the round-trip) and is reported precisely.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_ByReference_Unresolved_PreservesWeightEntry_AndValidateReports()
    {
        // Arrange — a resolver that knows nothing.
        var original = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Rain", 100d, 20d), 0.45d), (NormalChild("Snow", 60d, 15d), 0.55d));
        var empty = new RiskFunctionResolver(_ => null, _ => null);

        // Act — an id-bearing marker whose id is unknown is loud; strip the id to exercise the
        // lenient name path, which records instead of throwing.
        var form = original.ToXElement(RiskSerializationMode.ByReference);
        foreach (var marker in form.Descendants("FunctionReference")) marker.Attribute("Id")!.Remove();
        var restored = new CompositeHazard(form, empty);

        // Assert
        Assert.AreEqual(2, restored.HazardFunctions.Count);
        Assert.AreEqual(0.45d, restored.HazardFunctions[0].Weight, 0d);
        Assert.IsNull(restored.HazardFunctions[0].HazardFunction);
        var (isValid, messages) = restored.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("Rain") && m.Contains("not found")));
    }

    /// <summary>Verifies a stale serialized reference id throws rather than resolving silently.</summary>
    [TestMethod]
    public void Test_Serialization_ByReference_StaleId_Throws()
    {
        // Arrange
        var original = Composite(CompositeCombinationType.Mixture, (NormalChild("Rain", 100d, 20d), 1d));
        var empty = new RiskFunctionResolver(_ => null, _ => null);

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(
            () => new CompositeHazard(original.ToXElement(RiskSerializationMode.ByReference), empty));
    }

    /// <summary>
    /// Verifies the correlation matrix is written only in the one configuration that reads it, so
    /// a stale matrix cannot ride along in a mode that ignores it.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_CorrelationMatrix_WrittenOnlyUnderMatrixDependency()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 0.5d), (NormalChild("B", 60d, 15d), 0.5d));
        composite.CorrelationMatrix = new double[,] { { 1d, 0.5d }, { 0.5d, 1d } };

        // Independent dependence: the matrix is not written.
        Assert.AreEqual(string.Empty, composite.ToXElement().Attribute("CorrelationMatrix")!.Value);

        // The correlation-matrix dependence writes and round-trips it.
        composite.Dependency = DependencyType.CorrelationMatrix;
        var restored = new CompositeHazard(composite.ToXElement());
        Assert.IsNotNull(restored.CorrelationMatrix);
        Assert.AreEqual(0.5d, restored.CorrelationMatrix![0, 1], 0d);
        CollectionAssert.AreEqual(composite.CanonicalHash(), restored.CanonicalHash());
    }

    /// <summary>
    /// Verifies the hash is identical across serialization modes — the invariant that keeps Monte
    /// Carlo results independent of how a project was stored.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_IdenticalAcrossSerializationModes()
    {
        // Arrange
        var rain = NormalChild("Rain", 100d, 20d);
        var snow = NormalChild("Snow", 60d, 15d);
        var store = new IRiskFunction[] { rain, snow }.ToDictionary(f => f.Id);
        var resolver = new RiskFunctionResolver(
            id => store.TryGetValue(id, out var f) ? f : null,
            name => store.Values.FirstOrDefault(f => f.Name == name));
        var original = Composite(CompositeCombinationType.Mixture, (rain, 0.45d), (snow, 0.55d));

        // Act
        var selfContained = new CompositeHazard(original.ToXElement(RiskSerializationMode.SelfContained));
        var byReference = new CompositeHazard(original.ToXElement(RiskSerializationMode.ByReference), resolver);

        // Assert
        CollectionAssert.AreEqual(original.CanonicalHash(), selfContained.CanonicalHash());
        CollectionAssert.AreEqual(original.CanonicalHash(), byReference.CanonicalHash());
    }

    /// <summary>Verifies metadata edits — on the composite and on its children — are hash-inert.</summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataEdits_Inert()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Rain", 100d, 20d), 0.45d), (NormalChild("Snow", 60d, 15d), 0.55d));
        byte[] before = composite.CanonicalHash();

        // Act
        composite.Name = "Renamed";
        composite.Description = "A description";
        composite.SpecifiedHazard = "Discharge";
        composite.AssignNewId();
        composite.HazardFunctions[0].HazardFunction!.Name = "Rain (renamed)";
        composite.HazardFunctions[1].HazardFunction!.AssignNewId();

        // Assert
        CollectionAssert.AreEqual(before, composite.CanonicalHash());
    }

    /// <summary>Verifies every compute-relevant edit moves the hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_ComputeEdits_Move()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Rain", 100d, 20d), 0.45d), (NormalChild("Snow", 60d, 15d), 0.55d));
        byte[] baseline = composite.CanonicalHash();

        // The combination mode.
        composite.CompositeCombinationType = CompositeCombinationType.CompetingRisks;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.CompositeCombinationType = CompositeCombinationType.Mixture;

        // A mixture weight.
        composite.HazardFunctions[0].Weight = 0.5d;
        composite.HazardFunctions[1].Weight = 0.5d;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.HazardFunctions[0].Weight = 0.45d;
        composite.HazardFunctions[1].Weight = 0.55d;
        CollectionAssert.AreEqual(baseline, composite.CanonicalHash());

        // The interpolation transforms.
        composite.HazardTransform = Transform.Logarithmic;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.HazardTransform = Transform.None;
        composite.ProbabilityTransform = Transform.None;
        CollectionAssert.AreNotEqual(baseline, composite.CanonicalHash());
        composite.ProbabilityTransform = Transform.NormalZ;

        // Entry order is semantic.
        var reordered = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Snow", 60d, 15d), 0.55d), (NormalChild("Rain", 100d, 20d), 0.45d));
        CollectionAssert.AreNotEqual(baseline, reordered.CanonicalHash());

        // Child content.
        var edited = Composite(CompositeCombinationType.Mixture,
            (NormalChild("Rain", 101d, 20d), 0.45d), (NormalChild("Snow", 60d, 15d), 0.55d));
        CollectionAssert.AreNotEqual(baseline, edited.CanonicalHash());
    }

    /// <summary>
    /// Verifies the three hash coercions: weights are inert under competing risks, the dependence
    /// and the correlation matrix are inert under mixture — so an edit that cannot change results
    /// cannot re-roll seeds.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_InertEdits_Coerced()
    {
        // Arrange — competing risks, where weights do not participate.
        var competing = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 0.45d), (NormalChild("B", 60d, 15d), 0.55d));
        byte[] competingBaseline = competing.CanonicalHash();

        // Act / Assert — weight edits are inert.
        competing.HazardFunctions[0].Weight = 0.9d;
        competing.HazardFunctions[1].Weight = 0.1d;
        CollectionAssert.AreEqual(competingBaseline, competing.CanonicalHash());

        // Mixture, where the dependence and matrix do not participate.
        var mixture = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.45d), (NormalChild("B", 60d, 15d), 0.55d));
        byte[] mixtureBaseline = mixture.CanonicalHash();
        mixture.Dependency = DependencyType.PerfectlyNegative;
        mixture.CorrelationMatrix = new double[,] { { 1d, 0.3d }, { 0.3d, 1d } };
        CollectionAssert.AreEqual(mixtureBaseline, mixture.CanonicalHash());

        // But under competing risks the dependence is live content.
        competing.Dependency = DependencyType.PerfectlyPositive;
        CollectionAssert.AreNotEqual(competingBaseline, competing.CanonicalHash());
    }

    /// <summary>
    /// Verifies a null child projects an empty hash token, so an unresolved reference cannot alias
    /// a resolved one.
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_NullChild_DoesNotAliasResolved()
    {
        // Arrange
        var resolved = Composite(CompositeCombinationType.Mixture, (NormalChild("Rain", 100d, 20d), 1d));
        var unresolved = Composite(CompositeCombinationType.Mixture, (null!, 1d));

        // Assert
        CollectionAssert.AreNotEqual(resolved.CanonicalHash(), unresolved.CanonicalHash());
    }

    /// <summary>Verifies two independently built composites with identical content hash and draw identically.</summary>
    [TestMethod]
    public void Test_CanonicalHash_AndDraws_ReproduceAcrossInstances()
    {
        // Arrange
        var first = Composite(CompositeCombinationType.Mixture,
            (TabularChild("A", 10d, 100d, 5d), 0.45d), (TabularChild("B", 5d, 60d, 4d), 0.55d));
        var second = Composite(CompositeCombinationType.Mixture,
            (TabularChild("X", 10d, 100d, 5d), 0.45d), (TabularChild("Y", 5d, 60d, 4d), 0.55d));
        second.Name = "Different name entirely";

        // Assert — identical compute content hashes identically despite different names and ids.
        CollectionAssert.AreEqual(first.CanonicalHash(), second.CanonicalHash());

        first.SetupSampler(32, 4242, SamplingScheme.LatinHypercube);
        second.SetupSampler(32, 4242, SamplingScheme.LatinHypercube);
        for (int i = 0; i < 32; i += 5)
        {
            Assert.AreEqual(first.SampleFunction(i).CDF(90d), second.SampleFunction(i).CDF(90d), 0d);
        }
    }

    /// <summary>
    /// Verifies a deterministic composite's uncertainty summary collapses to the exact mean curve
    /// with no simulation — every band equals the mean.
    /// </summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_Deterministic_CollapsesToMeanCurve()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.45d), (NormalChild("B", 60d, 15d), 0.55d));

        // Act
        var results = composite.ComputeUncertaintyResults();

        // Assert
        Assert.IsNotNull(results);
        for (int i = 0; i < results!.MeanCurve!.Length; i++)
        {
            Assert.AreEqual(results.MeanCurve[i], results.ConfidenceIntervals![i, 0], 0d);
            Assert.AreEqual(results.MeanCurve[i], results.ConfidenceIntervals[i, 1], 0d);
            Assert.AreEqual(results.MeanCurve[i], results.ModeCurve![i], 0d);
        }
    }

    /// <summary>Verifies the composite refuses to sample an invalid configuration.</summary>
    [TestMethod]
    public void Test_SampleFunction_InvalidComposite_Throws()
    {
        // Assert — empty.
        Assert.ThrowsException<InvalidOperationException>(() => new CompositeHazard().SampleFunction());

        // Weights that do not sum to one.
        var badWeights = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.4d), (NormalChild("B", 60d, 15d), 0.4d));
        Assert.ThrowsException<InvalidOperationException>(() => badWeights.SampleFunction());

        // A null child.
        var nullChild = Composite(CompositeCombinationType.Mixture, (null!, 1d));
        Assert.ThrowsException<InvalidOperationException>(() => nullChild.SampleFunction(0.5d));

        // A competing-risks composite whose required correlation matrix is missing.
        var noMatrix = Composite(CompositeCombinationType.CompetingRisks,
            (NormalChild("A", 100d, 20d), 0.5d), (NormalChild("B", 60d, 15d), 0.5d));
        noMatrix.Dependency = DependencyType.CorrelationMatrix;
        Assert.ThrowsException<InvalidOperationException>(() => noMatrix.SampleFunction());
    }

    /// <summary>Verifies a nested composite samples and hashes through the recursion.</summary>
    [TestMethod]
    public void Test_NestedComposite_SamplesAndHashes()
    {
        // Arrange — an inner mixture used as one branch of an outer mixture.
        var inner = Composite(CompositeCombinationType.Mixture,
            (NormalChild("A", 100d, 20d), 0.5d), (NormalChild("B", 60d, 15d), 0.5d));
        inner.Name = "Inner";
        var outer = Composite(CompositeCombinationType.Mixture, (inner, 0.5d), (NormalChild("C", 140d, 10d), 0.5d));

        // Act
        var combined = outer.SampleFunction();

        // Assert — the flattened mixture: 0.25 A + 0.25 B + 0.5 C.
        var a = new Normal(100d, 20d);
        var b = new Normal(60d, 15d);
        var c = new Normal(140d, 10d);
        foreach (double x in new[] { 50d, 90d, 130d })
        {
            Assert.AreEqual(0.25d * a.CDF(x) + 0.25d * b.CDF(x) + 0.5d * c.CDF(x), combined.CDF(x), 1e-9);
        }

        // The nested hash is content-derived and stable across a round-trip.
        CollectionAssert.AreEqual(outer.CanonicalHash(), new CompositeHazard(outer.ToXElement()).CanonicalHash());
    }

    /// <summary>Verifies the factory reconstructs the composite by element name.</summary>
    [TestMethod]
    public void Test_Factory_RoundTripsByElementName()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture, (NormalChild("Rain", 100d, 20d), 1d));

        // Act
        var viaFactory = RiskFunctionFactory.CreateHazardFunction(composite.ToXElement());

        // Assert
        Assert.IsInstanceOfType<CompositeHazard>(viaFactory);
        Assert.IsNull(RiskFunctionFactory.CreateTransformFunction(composite.ToXElement()),
            "A composite hazard must not satisfy the transform cluster filter.");
        CollectionAssert.AreEqual(composite.CanonicalHash(), viaFactory!.CanonicalHash());
    }

    /// <summary>Verifies entry membership changes reconcile subscriptions without leaking.</summary>
    [TestMethod]
    public void Test_PropertyChange_MembershipAndEntryEdits()
    {
        // Arrange
        var composite = Composite(CompositeCombinationType.Mixture, (NormalChild("Rain", 100d, 20d), 1d));
        var entry = composite.HazardFunctions[0];
        var raised = new List<string>();
        composite.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act — a weight edit reaches the composite.
        entry.Weight = 0.5d;
        Assert.IsTrue(raised.Contains(nameof(CompositeHazard.HazardFunctions)));

        // Clearing raises Reset (no OldItems) and must still detach the subscription.
        raised.Clear();
        composite.HazardFunctions.Clear();
        Assert.IsTrue(raised.Contains(nameof(CompositeHazard.HazardFunctions)));

        raised.Clear();
        entry.Weight = 0.25d;
        Assert.AreEqual(0, raised.Count, "A removed entry must no longer notify the composite.");
    }
}
