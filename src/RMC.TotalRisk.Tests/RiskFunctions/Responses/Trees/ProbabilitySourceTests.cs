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
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Tests tree probability-source discriminators, owned values, hazard-transform chains, and the bivariate surface axis.</summary>
[TestClass]
public class ProbabilitySourceTests
{
    /// <summary>Verifies scalar, table, and response source construction.</summary>
    [TestMethod]
    public void Test_Constructors_SelectExpectedKinds()
    {
        var table = new UncertainOrderedPairedData(
            new[]
            {
                new UncertainOrdinate(0d, new Uniform(0.1d, 0.3d)),
                new UncertainOrdinate(1d, new Uniform(0.2d, 0.4d)),
            }, true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Uniform);
        var response = new TabularResponse();

        var scalarSource = new ProbabilitySource(0.25d);
        var tableSource = new ProbabilitySource(table);
        var responseSource = new ProbabilitySource(response);

        Assert.AreEqual(ProbabilitySourceKind.DeterministicScalar, scalarSource.Kind);
        Assert.AreEqual(0.25d, scalarSource.ScalarProbability);
        Assert.AreEqual(ProbabilitySourceKind.UncertainTabular, tableSource.Kind);
        Assert.AreSame(table, tableSource.Table);
        Assert.AreEqual(ProbabilitySourceKind.ResponseFunctionReference, responseSource.Kind);
        Assert.AreSame(response, responseSource.ResponseFunction);
        Assert.AreEqual(0, scalarSource.HazardTransforms.Count);
        Assert.IsNull(tableSource.BivariateAxis);
    }

    /// <summary>
    /// Verifies the bivariate scope guard: a source referencing a bivariate response without a
    /// declared surface axis reports the original validation error verbatim (evaluating it at a
    /// single tree hazard would silently collapse the secondary hazard through the stored
    /// weights).
    /// </summary>
    [TestMethod]
    public void Test_Validate_BivariateReferencedResponse_Error()
    {
        // Arrange
        var source = new ProbabilitySource(new BivariateResponse { Name = "Surface" });

        // Act
        var messages = source.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");

        // Assert
        Assert.IsTrue(messages.Any(m =>
            m.StartsWith("Error:") && m.Contains("bivariate response function")));
    }

    /// <summary>Verifies the chain constructors store an ordered defensive copy and the axis.</summary>
    [TestMethod]
    public void Test_ChainConstructors_StoreOrderedChainAndAxis()
    {
        // Arrange
        var first = Linear("First", 2d, 0.5d);
        var second = Linear("Second", 0d, 3d);
        var chain = new List<ITransformFunction> { first, second };

        // Act
        var tableSource = new ProbabilitySource(TransformedTable(), chain);
        var responseSource = new ProbabilitySource(DurationResponse(), chain);
        var surfaceSource = new ProbabilitySource(Surface(), BivariateSourceAxis.Secondary, chain);
        chain.Clear();

        // Assert
        Assert.AreEqual(2, tableSource.HazardTransforms.Count);
        Assert.AreSame(first, tableSource.HazardTransforms[0]);
        Assert.AreSame(second, tableSource.HazardTransforms[1]);
        Assert.AreEqual(2, responseSource.HazardTransforms.Count);
        Assert.IsNull(responseSource.BivariateAxis);
        Assert.AreEqual(BivariateSourceAxis.Secondary, surfaceSource.BivariateAxis);
        Assert.AreEqual(2, surfaceSource.HazardTransforms.Count);
    }

    /// <summary>Verifies chain constructor guards: a null list and a null entry both throw.</summary>
    [TestMethod]
    public void Test_ChainConstructors_NullListOrEntry_Throw()
    {
        // Arrange
        var table = TransformedTable();

        // Act / Assert
        try
        {
            _ = new ProbabilitySource(table, null!);
            Assert.Fail("A null transform list must throw.");
        }
        catch (ArgumentNullException)
        {
        }
        try
        {
            _ = new ProbabilitySource(table, new ITransformFunction[] { null! });
            Assert.Fail("A null transform entry must throw.");
        }
        catch (ArgumentException)
        {
        }
    }

    /// <summary>Verifies sampler dimensions and determinism fold the chain entries.</summary>
    [TestMethod]
    public void Test_SamplingDimensionsAndDeterminism_FoldChain()
    {
        // Arrange
        var deterministic = new ProbabilitySource(DeterministicTable(new[] { 2d, 3d }, new[] { 0.2d, 0.6d }),
            new[] { Linear("Map", 2d, 0.5d) });
        var uncertain = new ProbabilitySource(DeterministicTable(new[] { 2d, 3d }, new[] { 0.2d, 0.6d }),
            new[] { Linear("Map", 2d, 0.5d, sigma: 1d, uncertain: true) });

        // Assert: the deterministic-table dimension plus zero or one chain dimensions.
        Assert.AreEqual(1, deterministic.SamplingDimensions);
        Assert.IsTrue(deterministic.IsDeterministic);
        Assert.AreEqual(2, uncertain.SamplingDimensions);
        Assert.IsFalse(uncertain.IsDeterministic);
    }

    /// <summary>Verifies a chainless source serializes with no chain child and no axis attribute.</summary>
    [TestMethod]
    public void Test_Serialization_ChainlessSource_HasNoChainOrAxisMarkup()
    {
        // Arrange
        var scalar = new ProbabilitySource(0.25d);
        var reference = new ProbabilitySource(DurationResponse());

        // Act
        var scalarElement = scalar.ToXElement(RiskSerializationMode.SelfContained);
        var referenceElement = reference.ToXElement(RiskSerializationMode.SelfContained);

        // Assert: conditional presence — every existing form stays byte-identical.
        Assert.IsNull(scalarElement.Element("HazardTransforms"));
        Assert.IsNull(scalarElement.Attribute("BivariateAxis"));
        Assert.IsNull(referenceElement.Element("HazardTransforms"));
        Assert.IsNull(referenceElement.Attribute("BivariateAxis"));
        Assert.IsNull(scalar.ToIdentityXElement().Element("HazardTransforms"));
        Assert.IsNull(reference.ToIdentityXElement().Attribute("BivariateAxis"));
    }

    /// <summary>Verifies a chained tabular source round-trips self-contained with evaluation parity.</summary>
    [TestMethod]
    public void Test_Serialization_ChainedTabularSource_RoundTripsSelfContained()
    {
        // Arrange
        var source = new ProbabilitySource(TransformedTable(), new[] { Linear("Map", 2d, 0.5d) });

        // Act
        var restored = new ProbabilitySource(source.ToXElement(RiskSerializationMode.SelfContained),
            null, "Owner", "event-tree");

        // Assert
        Assert.AreEqual(1, restored.HazardTransforms.Count);
        Assert.AreEqual("Map", restored.HazardTransforms[0]!.Name);
        Assert.AreEqual(source.EvaluateMeanAtHazard(1d), restored.EvaluateMeanAtHazard(1d));
        Assert.AreEqual(source.CanonicalToken(), restored.CanonicalToken());
    }

    /// <summary>Verifies a bivariate-axis source round-trips in both modes through a resolver.</summary>
    [TestMethod]
    public void Test_Serialization_BivariateAxisSource_RoundTripsBothModes()
    {
        // Arrange
        var surface = Surface();
        var transform = Linear("Duration map", 1d, 2d);
        var source = new ProbabilitySource(surface, BivariateSourceAxis.Primary, new[] { transform });
        var store = new IRiskFunction[] { surface, transform };
        var resolver = new RiskFunctionResolver(
            id => store.FirstOrDefault(f => f.Id == id),
            name => store.FirstOrDefault(f => f.Name == name));

        // Act
        var selfContained = new ProbabilitySource(source.ToXElement(RiskSerializationMode.SelfContained),
            null, "Owner", "event-tree");
        var referenceElement = source.ToXElement(RiskSerializationMode.ByReference);
        var byReference = new ProbabilitySource(referenceElement, resolver, "Owner", "event-tree");

        // Assert: the by-reference form writes markers and re-attaches the live instances.
        Assert.AreEqual(BivariateSourceAxis.Primary, selfContained.BivariateAxis);
        Assert.AreEqual(BivariateSourceAxis.Primary, byReference.BivariateAxis);
        Assert.IsNotNull(referenceElement.Element("Function")?.Element("FunctionReference"));
        Assert.IsNotNull(referenceElement.Element("HazardTransforms")?.Element("FunctionReference"));
        Assert.AreSame(surface, byReference.ResponseFunction);
        Assert.AreSame(transform, byReference.HazardTransforms[0]);
        Assert.AreEqual(source.EvaluateMeanAtHazard(1d), byReference.EvaluateMeanAtHazard(1d));
        Assert.AreEqual(source.CanonicalToken(), selfContained.CanonicalToken());
        Assert.AreEqual(source.CanonicalToken(), byReference.CanonicalToken());
    }

    /// <summary>Verifies an unresolvable by-reference chain records the reference and fails validation.</summary>
    [TestMethod]
    public void Test_Serialization_ByReferenceChainWithoutResolver_RecordsUnresolved()
    {
        // Arrange
        var source = new ProbabilitySource(DurationResponse(), new[] { Linear("Map", 2d, 0.5d) });

        // Act
        var restored = new ProbabilitySource(source.ToXElement(RiskSerializationMode.ByReference),
            null, "Owner", "event-tree");
        var messages = restored.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");

        // Assert
        Assert.IsNull(restored.ResponseFunction);
        Assert.IsNull(restored.HazardTransforms[0]);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("was not found")));
    }

    /// <summary>Verifies chain content, chain order, and the axis all move the identity token.</summary>
    [TestMethod]
    public void Test_Identity_ChainContentOrderAndAxis_Move()
    {
        // Arrange
        var table = DeterministicTable(new[] { 2d, 3d }, new[] { 0.2d, 0.6d });
        var chainless = new ProbabilitySource(table);
        var chained = new ProbabilitySource(table, new[] { Linear("Map", 2d, 0.5d) });
        var edited = new ProbabilitySource(table, new[] { Linear("Map", 2d, 0.75d) });
        var swapped = new ProbabilitySource(table,
            new[] { Linear("Second", 0d, 3d), Linear("Map", 2d, 0.5d) });
        var ordered = new ProbabilitySource(table,
            new[] { Linear("Map", 2d, 0.5d), Linear("Second", 0d, 3d) });
        var primary = new ProbabilitySource(Surface(), BivariateSourceAxis.Primary, new[] { Linear("Map", 2d, 0.5d) });
        var secondary = new ProbabilitySource(Surface(), BivariateSourceAxis.Secondary, new[] { Linear("Map", 2d, 0.5d) });

        // Assert
        Assert.AreNotEqual(chainless.CanonicalToken(), chained.CanonicalToken());
        Assert.AreNotEqual(chained.CanonicalToken(), edited.CanonicalToken());
        Assert.AreNotEqual(ordered.CanonicalToken(), swapped.CanonicalToken());
        Assert.AreNotEqual(primary.CanonicalToken(), secondary.CanonicalToken());
    }

    /// <summary>Verifies renaming a chain transform never moves the identity token.</summary>
    [TestMethod]
    public void Test_Identity_ChainTransformRename_Inert()
    {
        // Arrange
        var table = DeterministicTable(new[] { 2d, 3d }, new[] { 0.2d, 0.6d });
        var first = new ProbabilitySource(table, new[] { Linear("Original", 2d, 0.5d) });
        var second = new ProbabilitySource(table, new[] { Linear("Renamed entirely", 2d, 0.5d) });

        // Assert
        Assert.AreEqual(first.CanonicalToken(), second.CanonicalToken());
    }

    /// <summary>Verifies the scalar-with-chain and misplaced-axis validation errors.</summary>
    [TestMethod]
    public void Test_Validate_ScalarChainAndMisplacedAxis_Error()
    {
        // Arrange: a scalar source with a chain is only constructible through serialization.
        var scalarElement = new ProbabilitySource(0.25d).ToXElement(RiskSerializationMode.SelfContained);
        scalarElement.Add(new XElement("HazardTransforms",
            Linear("Map", 2d, 0.5d).ToXElement()));
        scalarElement.SetAttributeValue("BivariateAxis", nameof(BivariateSourceAxis.Primary));
        var scalar = new ProbabilitySource(scalarElement, null, "Owner", "event-tree");
        var tabular = new ProbabilitySource(TransformedTable(), new[] { Linear("Map", 2d, 0.5d) });
        var tabularElement = tabular.ToXElement(RiskSerializationMode.SelfContained);
        tabularElement.SetAttributeValue("BivariateAxis", nameof(BivariateSourceAxis.Primary));
        var tabularWithAxis = new ProbabilitySource(tabularElement, null, "Owner", "event-tree");

        // Act
        var scalarMessages = scalar.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");
        var tabularMessages = tabularWithAxis.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");

        // Assert
        Assert.IsTrue(scalarMessages.Any(m => m.StartsWith("Error:") && m.Contains("scalar probability source cannot use")));
        Assert.IsTrue(scalarMessages.Any(m => m.StartsWith("Error:") && m.Contains("bivariate surface axis")));
        Assert.IsTrue(tabularMessages.Any(m => m.StartsWith("Error:") && m.Contains("bivariate surface axis")));
    }

    /// <summary>Verifies a transformed table is freed from tree-axis alignment but keeps the probability range rule.</summary>
    [TestMethod]
    public void Test_Validate_TransformedTable_SkipsAlignmentKeepsRange()
    {
        // Arrange: three t-space ordinates against a two-level tree axis.
        var freeTable = DeterministicTable(new[] { 2d, 2.5d, 3d }, new[] { 0.2d, 0.4d, 0.6d });
        var aligned = new ProbabilitySource(freeTable, new[] { Linear("Map", 2d, 0.5d) });
        var outOfRange = new ProbabilitySource(
            DeterministicTable(new[] { 2d, 3d }, new[] { 0.2d, 1.5d }),
            new[] { Linear("Map", 2d, 0.5d) });

        // Act
        var alignedMessages = aligned.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");
        var rangeMessages = outOfRange.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");

        // Assert
        Assert.IsFalse(alignedMessages.Any(m => m.StartsWith("Error:")),
            string.Join(" | ", alignedMessages));
        Assert.IsTrue(rangeMessages.Any(m => m.StartsWith("Error:") && m.Contains("within [0, 1]")));
    }

    /// <summary>Verifies the bivariate-axis validation matrix.</summary>
    [TestMethod]
    public void Test_Validate_BivariateAxisMatrix()
    {
        // Arrange
        var legal = new ProbabilitySource(Surface(), BivariateSourceAxis.Primary, new[] { Linear("Map", 2d, 0.5d) });
        var axisOnUnivariate = new ProbabilitySource(DurationResponse().ToWithAxis(), null, "Owner", "event-tree");
        var noChainElement = new ProbabilitySource(Surface()).ToXElement(RiskSerializationMode.SelfContained);
        noChainElement.SetAttributeValue("BivariateAxis", nameof(BivariateSourceAxis.Primary));
        var axisWithoutChain = new ProbabilitySource(noChainElement, null, "Owner", "event-tree");

        // Act
        var legalMessages = legal.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");
        var univariateMessages = axisOnUnivariate.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");
        var chainlessMessages = axisWithoutChain.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");

        // Assert
        Assert.IsFalse(legalMessages.Any(m => m.StartsWith("Error:")), string.Join(" | ", legalMessages));
        Assert.IsTrue(univariateMessages.Any(m => m.StartsWith("Error:") && m.Contains("univariate response function")));
        Assert.IsTrue(chainlessMessages.Any(m => m.StartsWith("Error:") && m.Contains("no hazard transforms")));
    }

    /// <summary>Verifies a bivariate transform in the chain is rejected.</summary>
    [TestMethod]
    public void Test_Validate_BivariateTransformInChain_Error()
    {
        // Arrange
        var source = new ProbabilitySource(TransformedTable(),
            new ITransformFunction[] { new BivariateTransform { Name = "Surface map" } });

        // Act
        var messages = source.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");

        // Assert
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:") && m.Contains("univariate transforms only")));
    }

    /// <summary>Verifies axis-label continuity warnings across the owner, the chain, and the target.</summary>
    [TestMethod]
    public void Test_Validate_LabelContinuity_Warnings()
    {
        // Arrange: the transform expects "Stage" but the owner supplies "Flow", and it produces
        // "Duration" while the referenced response expects "Depth".
        var response = DurationResponse();
        response.SpecifiedHazard = "Depth";
        var mismatched = new ProbabilitySource(response, new[] { Linear("Map", 2d, 0.5d) });
        var continuous = new ProbabilitySource(DurationResponse(), new[] { Linear("Map", 2d, 0.5d) });

        // Act
        var mismatchedMessages = mismatched.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree", "Flow");
        var continuousMessages = continuous.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree", "Stage");

        // Assert
        Assert.IsTrue(mismatchedMessages.Any(m => m.StartsWith("Warning:") && m.Contains("receives 'Flow'")));
        Assert.IsTrue(mismatchedMessages.Any(m => m.StartsWith("Warning:") && m.Contains("the source expects 'Duration'")));
        Assert.IsFalse(continuousMessages.Any(m => m.StartsWith("Warning:")), string.Join(" | ", continuousMessages));
    }

    /// <summary>Verifies a transformed table evaluates by interpolation at the transformed hazard on every mean path.</summary>
    [TestMethod]
    public void Test_EvaluateMean_TransformedTable_InterpolatesAtTransformedHazard()
    {
        // Arrange: t(h) = 2 + 0.5·h, so h = 1 lands midway between the t-space knots 2 and 3.
        var source = new ProbabilitySource(DeterministicTable(new[] { 2d, 3d }, new[] { 0.2d, 0.6d }),
            new[] { Linear("Map", 2d, 0.5d) });

        // Act / Assert: the aligned entry ignores its index and matches the off-axis entry.
        Assert.AreEqual(0.4d, source.EvaluateMeanAtHazard(1d), 1e-14d);
        Assert.AreEqual(source.EvaluateMeanAtHazard(1d), source.EvaluateMean(1d, 0));
        Assert.AreEqual(source.EvaluateMeanAtHazard(1d), source.EvaluatePercentile(1d, 0, 0.75d));
    }

    /// <summary>Verifies a transformed reference applies one consistent percentile to the chain and the target.</summary>
    [TestMethod]
    public void Test_EvaluatePercentile_TransformedReference_MatchesManualComposition()
    {
        // Arrange
        var transform = Linear("Map", 2d, 0.5d, sigma: 1d, uncertain: true);
        var response = DurationResponse();
        var source = new ProbabilitySource(response, new[] { transform });
        double percentile = 0.8d;

        // Act
        double expectedHazard = transform.SampleFunction(percentile).Function(1d);
        double expected = response.SampleFunction(percentile).CDF(expectedHazard);

        // Assert: bit-equal to the manual composition of the same live calls.
        Assert.AreEqual(expected, source.EvaluatePercentileAtHazard(1d, percentile));
        Assert.AreEqual(expected, source.EvaluatePercentile(1d, 0, percentile));
    }

    /// <summary>Verifies the bivariate axis orders the surface coordinates on both settings.</summary>
    [TestMethod]
    public void Test_EvaluateMean_BivariateAxis_MatchesSurfaceProbability()
    {
        // Arrange
        var surface = Surface();
        var transform = Linear("Map", 0.5d, 1d);
        var primary = new ProbabilitySource(surface, BivariateSourceAxis.Primary, new[] { transform });
        var secondary = new ProbabilitySource(surface, BivariateSourceAxis.Secondary, new[] { transform });
        double hazard = 0.75d;
        double transformed = transform.SampleFunction().Function(hazard);

        // Assert: bit-equal to the clamped surface evaluation with the declared coordinate order.
        Assert.AreEqual(surface.SurfaceProbability(hazard, transformed), primary.EvaluateMeanAtHazard(hazard));
        Assert.AreEqual(surface.SurfaceProbability(transformed, hazard), secondary.EvaluateMeanAtHazard(hazard));
        Assert.AreNotEqual(primary.EvaluateMeanAtHazard(hazard), secondary.EvaluateMeanAtHazard(hazard));
    }

    /// <summary>Verifies the aligned realization entry refuses transformed sources.</summary>
    [TestMethod]
    public void Test_EvaluateRealization_TransformedSource_Throws()
    {
        // Arrange
        var source = new ProbabilitySource(TransformedTable(), new[] { Linear("Map", 2d, 0.5d) });

        // Act / Assert
        try
        {
            _ = source.EvaluateRealization(1d, 0, 0, 0.5d);
            Assert.Fail("The aligned realization lookup must refuse a transformed source.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("EvaluateRealizationAtHazard"));
        }
    }

    /// <summary>Verifies fragment snapshots carry the chain as live references and keep the axis.</summary>
    [TestMethod]
    public void Test_CloneForFragment_CarriesChainAndAxis()
    {
        // Arrange
        var transform = Linear("Map", 2d, 0.5d);
        var tabular = new ProbabilitySource(TransformedTable(), new[] { transform });
        var surfaceSource = new ProbabilitySource(Surface(), BivariateSourceAxis.Secondary, new[] { transform });

        // Act
        var tabularClone = tabular.CloneForFragment();
        var surfaceClone = surfaceSource.CloneForFragment();

        // Assert
        Assert.AreNotSame(tabular.Table, tabularClone.Table);
        Assert.AreSame(transform, tabularClone.HazardTransforms[0]);
        Assert.AreEqual(BivariateSourceAxis.Secondary, surfaceClone.BivariateAxis);
        Assert.AreSame(transform, surfaceClone.HazardTransforms[0]);
    }

    /// <summary>Verifies the shared realization chain application composes sampler-bound clone curves in order.</summary>
    [TestMethod]
    public void Test_ApplyTransformRealizations_ComposesInOrder()
    {
        // Arrange
        var first = Linear("First", 2d, 0.5d);
        var second = Linear("Second", 1d, 3d);

        // Act: deterministic transforms need no sampler; realization reads fall through to the mean.
        double result = ProbabilitySource.ApplyTransformRealizations(
            new ITransformFunction[] { first, second }, 1d, 0);

        // Assert: 1 → 2.5 → 8.5.
        Assert.AreEqual(8.5d, result, 1e-14d);
    }

    /// <summary>Builds a valid deterministic linear transform mapping Stage onto Duration.</summary>
    /// <param name="name">The transform name.</param>
    /// <param name="alpha">The intercept.</param>
    /// <param name="beta">The slope.</param>
    /// <param name="sigma">The standard error while uncertain.</param>
    /// <param name="uncertain">Whether the transform carries uncertainty.</param>
    /// <returns>The transform.</returns>
    private static LinearTransform Linear(string name, double alpha, double beta,
        double sigma = 0d, bool uncertain = false)
    {
        return new LinearTransform
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            TransformedHazard = "Duration",
            TransformedHazardUnit = "hr",
            Minimum = -100d,
            Maximum = 100d,
            Alpha = alpha,
            Beta = beta,
            Sigma = sigma,
            IsUncertain = uncertain,
        };
    }

    /// <summary>Builds a deterministic probability table at transformed-axis ordinates.</summary>
    /// <returns>The table.</returns>
    private static UncertainOrderedPairedData TransformedTable()
    {
        return DeterministicTable(new[] { 2d, 3d }, new[] { 0.2d, 0.6d });
    }

    /// <summary>Builds a deterministic uncertainty table.</summary>
    /// <param name="hazards">The hazards.</param>
    /// <param name="probabilities">The probabilities.</param>
    /// <returns>The table.</returns>
    private static UncertainOrderedPairedData DeterministicTable(double[] hazards, double[] probabilities)
    {
        return new UncertainOrderedPairedData(
            hazards.Select((hazard, index) => new UncertainOrdinate(hazard, new Deterministic(probabilities[index]))).ToArray(),
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
    }

    /// <summary>Builds a valid tabular response on the transformed Duration axis.</summary>
    /// <returns>The response.</returns>
    private static TabularResponse DurationResponse()
    {
        return new TabularResponse
        {
            Name = "Duration fragility",
            SpecifiedHazard = "Duration",
            HazardUnit = "hr",
            UncertainOrderedPairedData = DeterministicTable(new[] { 2d, 3d }, new[] { 0.2d, 0.6d }),
        };
    }

    /// <summary>Builds a valid 2×2 bivariate response surface.</summary>
    /// <returns>The surface.</returns>
    private static BivariateResponse Surface()
    {
        var surface = new BivariateResponse
        {
            Name = "Surface",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Duration",
            SecondaryHazardUnit = "hr",
        };
        surface.PrimaryHazardLevels.Clear();
        surface.PrimaryHazardLevels.Add(0d);
        surface.PrimaryHazardLevels.Add(1d);
        surface.SecondaryHazardLevels.Clear();
        surface.SecondaryHazardLevels.Add(new WeightedHazardLevel { Level = 0d, Weight = 0.5d });
        surface.SecondaryHazardLevels.Add(new WeightedHazardLevel { Level = 2d, Weight = 0.5d });
        surface.ProbabilityValues = new[,] { { 0.1d, 0.3d }, { 0.5d, 0.9d } };
        return surface;
    }
}

/// <summary>Test-only helpers for composing probability-source fixtures.</summary>
internal static class ProbabilitySourceTestExtensions
{
    /// <summary>Serializes a univariate reference source and stamps a bivariate axis onto it.</summary>
    /// <param name="response">The univariate response.</param>
    /// <returns>The malformed serialized source.</returns>
    internal static XElement ToWithAxis(this TabularResponse response)
    {
        var element = new ProbabilitySource(response)
            .ToXElement(RiskSerializationMode.SelfContained);
        element.SetAttributeValue("BivariateAxis", nameof(BivariateSourceAxis.Primary));
        element.Add(new XElement("HazardTransforms",
            new LinearTransform
            {
                Name = "Map",
                SpecifiedHazard = "Stage",
                HazardUnit = "ft",
                TransformedHazard = "Duration",
                TransformedHazardUnit = "hr",
                Minimum = -100d,
                Maximum = 100d,
                Alpha = 2d,
                Beta = 0.5d,
            }.ToXElement()));
        return element;
    }
}
