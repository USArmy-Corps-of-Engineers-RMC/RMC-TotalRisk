using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Consequences;

namespace RMC.TotalRisk.Tests.RiskFunctions.Consequences;

/// <summary>
/// Unit tests for <see cref="BivariateConsequence"/> — defaults, change notification, the
/// validation matrix with the negative-cell advisory, known-point bilinear evaluation with
/// per-axis transforms, the pinned native extrapolation policy, the fresh-interpolator
/// discipline, the univariate-surface and exposure-branch rejections, shape-preserving
/// serialization, factory round-trips, and hash identity.
/// </summary>
[TestClass]
public class BivariateConsequenceTests
{
    /// <summary>
    /// Builds the standard labeled 3×2 surface: X1 = {0, 10, 20}, X2 = {100, 200},
    /// Z = {{1, 2}, {3, 5}, {4, 8}}.
    /// </summary>
    private static BivariateConsequence ConfiguredConsequence()
    {
        return new BivariateConsequence
        {
            Name = "Stage-Pool Life Loss",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            X1Values = new[] { 0d, 10d, 20d },
            X2Values = new[] { 100d, 200d },
            ZValues = new[,] { { 1d, 2d }, { 3d, 5d }, { 4d, 8d } },
        };
    }

    /// <summary>Collects the property names an instance raises.</summary>
    /// <param name="consequence">The instance under observation.</param>
    /// <returns>The live list of raised names.</returns>
    private static List<string> Observe(BivariateConsequence consequence)
    {
        var raised = new List<string>();
        consequence.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    /// <summary>Verifies the default construction state: the unit-square zero surface.</summary>
    [TestMethod]
    public void Test_Defaults_UnitSquareZeroSurface()
    {
        // Act
        var c = new BivariateConsequence();

        // Assert
        CollectionAssert.AreEqual(new[] { 0d, 1d }, c.X1Values);
        CollectionAssert.AreEqual(new[] { 0d, 1d }, c.X2Values);
        Assert.AreEqual(2, c.ZValues.GetLength(0));
        Assert.AreEqual(2, c.ZValues.GetLength(1));
        Assert.AreEqual(0d, c.ZValues[0, 0], 0d);
        Assert.AreEqual(Transform.None, c.HazardTransform);
        Assert.AreEqual(Transform.None, c.SecondaryHazardTransform);
        Assert.AreEqual(Transform.None, c.ConsequenceTransform);
        Assert.AreEqual(string.Empty, c.SecondarySpecifiedHazard);
        Assert.AreEqual(string.Empty, c.SecondaryHazardUnit);
        Assert.AreEqual(ConsequenceFunctionType.Bivariate, c.FunctionType);
        Assert.IsTrue(c.IsDeterministic);
        Assert.AreEqual(0, c.SamplingDimensions);
    }

    /// <summary>Verifies change notification for every property, and that null array assignments are ignored.</summary>
    [TestMethod]
    public void Test_PropertyChange_RaisesForEveryProperty()
    {
        // Arrange
        var c = new BivariateConsequence();
        var raised = Observe(c);

        // Act
        c.X1Values = new[] { 0d, 5d };
        c.X2Values = new[] { 0d, 7d };
        c.ZValues = new[,] { { 1d, 2d }, { 3d, 4d } };
        c.HazardTransform = Transform.Logarithmic;
        c.SecondaryHazardTransform = Transform.NormalZ;
        c.ConsequenceTransform = Transform.Logarithmic;
        c.SecondarySpecifiedHazard = "Pool";
        c.SecondaryHazardUnit = "ft";

        // Assert
        CollectionAssert.AreEqual(new[]
        {
            nameof(BivariateConsequence.X1Values), nameof(BivariateConsequence.X2Values),
            nameof(BivariateConsequence.ZValues), nameof(BivariateConsequence.HazardTransform),
            nameof(BivariateConsequence.SecondaryHazardTransform), nameof(BivariateConsequence.ConsequenceTransform),
            nameof(BivariateConsequence.SecondarySpecifiedHazard), nameof(BivariateConsequence.SecondaryHazardUnit),
        }, raised);

        // A null array assignment is ignored: no raise, reference unchanged.
        var before = c.ZValues;
        raised.Clear();
        c.ZValues = null!;
        Assert.AreEqual(0, raised.Count);
        Assert.AreSame(before, c.ZValues);
    }

    /// <summary>Verifies a fully configured surface validates cleanly.</summary>
    [TestMethod]
    public void Test_Validate_ValidConfiguration_NoMessages()
    {
        // Act
        var (isValid, messages) = ConfiguredConsequence().Validate();

        // Assert
        Assert.IsTrue(isValid);
        Assert.AreEqual(0, messages.Count);
    }

    /// <summary>Verifies every missing axis label reports its own error.</summary>
    [TestMethod]
    public void Test_Validate_MissingLabels_SixErrors()
    {
        // Act — the default instance is structurally usable but unlabeled.
        var (isValid, messages) = new BivariateConsequence().Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.AreEqual(6, messages.Count(m => m.StartsWith("Error:", StringComparison.Ordinal)));
        Assert.IsTrue(messages.Any(m => m.Contains("secondary hazard type")));
        Assert.IsTrue(messages.Any(m => m.Contains("secondary hazard unit")));
        Assert.IsTrue(messages.Any(m => m.Contains("consequence type")));
        Assert.IsTrue(messages.Any(m => m.Contains("consequence unit")));
    }

    /// <summary>Verifies the structural axis and surface rules with the consequence-cluster wording.</summary>
    [TestMethod]
    public void Test_Validate_StructuralRules_Errors()
    {
        // Arrange / Act / Assert — a short primary axis.
        var shortAxis = ConfiguredConsequence();
        shortAxis.X1Values = new[] { 5d };
        shortAxis.ZValues = new[,] { { 1d, 2d } };
        Assert.IsTrue(shortAxis.Validate().ValidationMessages.Any(
            m => m.Contains("bivariate consequence function must have at least two primary hazard values")));

        // A non-finite secondary axis reports finiteness alone.
        var nonFinite = ConfiguredConsequence();
        nonFinite.X2Values = new[] { 100d, double.PositiveInfinity };
        var nonFiniteMessages = nonFinite.Validate().ValidationMessages;
        Assert.IsTrue(nonFiniteMessages.Any(m => m.Contains("secondary hazard values must all be finite")));

        // A tied secondary axis reports the strictly ascending rule.
        var tied = ConfiguredConsequence();
        tied.X2Values = new[] { 100d, 100d };
        Assert.IsTrue(tied.Validate().ValidationMessages.Any(
            m => m.Contains("secondary hazard values must be strictly ascending")));

        // A surface whose dimensions disagree with the axes.
        var mismatched = ConfiguredConsequence();
        mismatched.ZValues = new[,] { { 1d, 2d }, { 3d, 5d } };
        Assert.IsTrue(mismatched.Validate().ValidationMessages.Any(
            m => m.Contains("one row of values per primary hazard value")));

        // A non-finite cell.
        var nanCell = ConfiguredConsequence();
        nanCell.ZValues = new[,] { { 1d, 2d }, { 3d, double.NaN }, { 4d, 8d } };
        Assert.IsTrue(nanCell.Validate().ValidationMessages.Any(
            m => m.Contains("surface values must all be finite")));
    }

    /// <summary>Verifies the logarithmic and normal-Z guards on each axis and on the output surface.</summary>
    [TestMethod]
    public void Test_Validate_TransformGuards_Errors()
    {
        // Arrange / Act / Assert — a negative primary axis under a log transform.
        var primaryLog = ConfiguredConsequence();
        primaryLog.X1Values = new[] { -1d, 10d, 20d };
        primaryLog.HazardTransform = Transform.Logarithmic;
        Assert.IsTrue(primaryLog.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The hazard interpolation transform cannot be logarithmic")));

        // A negative secondary axis under a log transform.
        var secondaryLog = ConfiguredConsequence();
        secondaryLog.X2Values = new[] { -100d, 200d };
        secondaryLog.SecondaryHazardTransform = Transform.Logarithmic;
        Assert.IsTrue(secondaryLog.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The secondary hazard interpolation transform cannot be logarithmic")));

        // A negative surface cell under a log output transform (the consequence wording).
        var outputLog = ConfiguredConsequence();
        outputLog.ZValues = new[,] { { -1d, 2d }, { 3d, 5d }, { 4d, 8d } };
        outputLog.ConsequenceTransform = Transform.Logarithmic;
        Assert.IsTrue(outputLog.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The consequence interpolation transform cannot be logarithmic")));

        // A normal-Z secondary axis outside [0, 1].
        var secondaryZ = ConfiguredConsequence();
        secondaryZ.X2Values = new[] { 0.5d, 1.5d };
        secondaryZ.SecondaryHazardTransform = Transform.NormalZ;
        Assert.IsTrue(secondaryZ.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The secondary hazard interpolation transform cannot be normal Z")));

        // A normal-Z output over cells outside [0, 1].
        var outputZ = ConfiguredConsequence();
        outputZ.ConsequenceTransform = Transform.NormalZ;
        Assert.IsTrue(outputZ.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The consequence interpolation transform cannot be normal Z")));
    }

    /// <summary>Verifies negative surface cells are advisory: a warning that never invalidates.</summary>
    [TestMethod]
    public void Test_Validate_NegativeCells_WarningOnly()
    {
        // Arrange
        var c = ConfiguredConsequence();
        c.ZValues = new[,] { { -1d, 2d }, { 3d, 5d }, { 4d, 8d } };

        // Act
        var (isValid, messages) = c.Validate();

        // Assert
        Assert.IsTrue(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)
            && m.Contains("negative consequence values")));
    }

    /// <summary>Verifies exact corner recovery, the interior bilinear value, and the pinned extrapolation policy.</summary>
    [TestMethod]
    public void Test_Evaluate_KnownValuesAndExtrapolation()
    {
        // Arrange
        var c = ConfiguredConsequence();

        // Act / Assert — grid nodes exactly; interior (5, 150) = (1 + 3 + 5 + 2)/4.
        Assert.AreEqual(1d, c.Evaluate(0d, 100d), 0d);
        Assert.AreEqual(8d, c.Evaluate(20d, 200d), 0d);
        Assert.AreEqual(2.75d, c.Evaluate(5d, 150d), 1e-12);

        // Both coordinates out of range clamp to the exact nearest corner.
        Assert.AreEqual(1d, c.Evaluate(-5d, 50d), 0d);
        Assert.AreEqual(8d, c.Evaluate(25d, 250d), 0d);

        // One coordinate out of range: edge row/column with 1-D interpolation along the in-range axis.
        Assert.AreEqual(1.5d, c.Evaluate(-5d, 150d), 1e-12);
        Assert.AreEqual(3.5d, c.Evaluate(5d, 250d), 1e-12);
    }

    /// <summary>Verifies a logarithmic secondary axis interpolates in log space.</summary>
    [TestMethod]
    public void Test_Evaluate_LogSecondaryAxis_KnownPoint()
    {
        // Arrange — z depends only on x2 so the log-axis fraction is isolated.
        var c = ConfiguredConsequence();
        c.X2Values = new[] { 1d, 100d };
        c.ZValues = new[,] { { 0d, 10d }, { 0d, 10d }, { 0d, 10d } };
        c.SecondaryHazardTransform = Transform.Logarithmic;

        // Act / Assert — x2 = 10 is the log-space midpoint of [1, 100].
        Assert.AreEqual(5d, c.Evaluate(5d, 10d), 1e-12);
    }

    /// <summary>Verifies a normal-Z output interpolates cells in z-score space.</summary>
    [TestMethod]
    public void Test_Evaluate_NormalZOutput_KnownPoint()
    {
        // Arrange
        var c = ConfiguredConsequence();
        c.ZValues = new[,] { { 0.1d, 0.2d }, { 0.3d, 0.5d }, { 0.4d, 0.8d } };
        c.ConsequenceTransform = Transform.NormalZ;

        // Act — interior (5, 150) carries equal quarter weights over the first cell.
        double zSpace = 0.25d * Normal.StandardZ(0.1d) + 0.25d * Normal.StandardZ(0.3d)
            + 0.25d * Normal.StandardZ(0.5d) + 0.25d * Normal.StandardZ(0.2d);
        double expected = Normal.StandardCDF(zSpace);

        // Assert
        Assert.AreEqual(expected, c.Evaluate(5d, 150d), 1e-12);
    }

    /// <summary>Verifies the convenience overload agrees bit-for-bit with the interpolator seam.</summary>
    [TestMethod]
    public void Test_Evaluate_MatchesInterpolator()
    {
        // Arrange
        var c = ConfiguredConsequence();
        var interpolator = c.CreateInterpolator();

        // Act / Assert — identical arithmetic path, so bit-equality.
        foreach (var (x, y) in new[] { (2d, 120d), (5d, 150d), (15d, 199d), (-3d, 50d), (30d, 300d) })
        {
            Assert.AreEqual(interpolator.Interpolate(x, y), c.Evaluate(x, y), 0d);
        }
    }

    /// <summary>
    /// Verifies the interpolator discipline: every call builds a fresh instance, configured with
    /// the function's transforms, sharing the function's arrays (no defensive copies — the
    /// share-the-arrays, rebuild-the-wrapper rule).
    /// </summary>
    [TestMethod]
    public void Test_CreateInterpolator_FreshPerCall_SharesArrays()
    {
        // Arrange
        var c = ConfiguredConsequence();
        c.HazardTransform = Transform.Logarithmic;
        c.X1Values = new[] { 1d, 10d, 20d };
        c.ConsequenceTransform = Transform.Logarithmic;

        // Act
        var first = c.CreateInterpolator();
        var second = c.CreateInterpolator();

        // Assert
        Assert.AreNotSame(first, second);
        Assert.AreSame(c.X1Values, first.X1Values);
        Assert.AreSame(c.X2Values, first.X2Values);
        Assert.AreSame(c.ZValues, first.YValues);
        Assert.AreEqual(Transform.Logarithmic, first.X1Transform);
        Assert.AreEqual(Transform.None, first.X2Transform);
        Assert.AreEqual(Transform.Logarithmic, first.YTransform);
    }

    /// <summary>Verifies the univariate sampling and exposure-branch surfaces throw, while the branch count stays structural.</summary>
    [TestMethod]
    public void Test_UnivariateSurface_Throws_CountStructural()
    {
        // Arrange
        var c = ConfiguredConsequence();

        // Act / Assert — the one-argument trio and both exposure-branch overloads.
        var thrown = Assert.ThrowsException<NotSupportedException>(() => c.SampleFunction());
        Assert.IsTrue(thrown.Message.Contains("Evaluate(x, y)"));
        Assert.ThrowsException<NotSupportedException>(() => c.SampleFunction(0.5d));
        Assert.ThrowsException<NotSupportedException>(() => c.SampleFunction(0));
        Assert.ThrowsException<NotSupportedException>(() => c.SampleExposureBranches());
        Assert.ThrowsException<NotSupportedException>(() => c.SampleExposureBranches(0.5d));

        // The structural branch count remains a single branch.
        Assert.AreEqual(1, c.CountExposureBranches());
    }

    /// <summary>Verifies the deterministic D = 0 sampler contract: size recorded, no matrix, still no univariate surface.</summary>
    [TestMethod]
    public void Test_SetupSampler_DeterministicNoMatrix()
    {
        // Arrange
        var c = ConfiguredConsequence();

        // Act
        c.SetupSampler(50, 67890, SamplingScheme.LatinHypercube);

        // Assert
        Assert.AreEqual(50, c.SampleSize);
        Assert.ThrowsException<NotSupportedException>(() => c.SampleFunction(3));
    }

    /// <summary>Verifies evaluation and bounds refuse a structurally unusable table, while Validate reports it.</summary>
    [TestMethod]
    public void Test_UnusableTable_Throws()
    {
        // Arrange — a single-value secondary axis cannot back interpolation.
        var c = ConfiguredConsequence();
        c.X2Values = new[] { 100d };

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => c.Evaluate(5d, 100d));
        Assert.ThrowsException<InvalidOperationException>(() => c.CreateInterpolator());
        Assert.ThrowsException<InvalidOperationException>(() => c.MinHazard());
        Assert.ThrowsException<InvalidOperationException>(() => c.MaxHazard());
        Assert.ThrowsException<InvalidOperationException>(() => c.MinSecondaryHazard());
        Assert.ThrowsException<InvalidOperationException>(() => c.MaxSecondaryHazard());

        // Validate never throws — it reports.
        Assert.IsFalse(c.Validate().IsValid);
    }

    /// <summary>Verifies the axis bounds and the null uncertainty representation.</summary>
    [TestMethod]
    public void Test_Bounds_And_UncertaintyResults()
    {
        // Arrange
        var c = ConfiguredConsequence();

        // Act / Assert
        Assert.AreEqual(0d, c.MinHazard(), 0d);
        Assert.AreEqual(20d, c.MaxHazard(), 0d);
        Assert.AreEqual(100d, c.MinSecondaryHazard(), 0d);
        Assert.AreEqual(200d, c.MaxSecondaryHazard(), 0d);
        Assert.IsNull(c.ComputeUncertaintyResults());
    }

    /// <summary>Verifies the serialized shape and a bit-equal round trip.</summary>
    [TestMethod]
    public void Test_ToXElement_RoundTrip()
    {
        // Arrange
        var c = ConfiguredConsequence();
        c.Description = "Two-way loss surface";
        c.SecondaryHazardTransform = Transform.Logarithmic;
        c.X2Values = new[] { 1d, 100d };
        c.ConsequenceTransform = Transform.Logarithmic;

        // Act
        var element = c.ToXElement();
        var restored = new BivariateConsequence(element);

        // Assert — the serialized shape: pipe-joined axes and one Row per primary value.
        Assert.AreEqual(nameof(BivariateConsequence), element.Name.LocalName);
        Assert.AreEqual("1|100", element.Element(nameof(BivariateConsequence.X2Values))!.Value);
        Assert.AreEqual(3, element.Element(nameof(BivariateConsequence.ZValues))!.Elements("Row").Count());

        // Every property survives.
        Assert.AreEqual(c.Id, restored.Id);
        Assert.AreEqual(c.Name, restored.Name);
        Assert.AreEqual(c.Description, restored.Description);
        Assert.AreEqual(c.SpecifiedHazard, restored.SpecifiedHazard);
        Assert.AreEqual(c.HazardUnit, restored.HazardUnit);
        Assert.AreEqual(c.SecondarySpecifiedHazard, restored.SecondarySpecifiedHazard);
        Assert.AreEqual(c.SecondaryHazardUnit, restored.SecondaryHazardUnit);
        Assert.AreEqual(c.SpecifiedConsequence, restored.SpecifiedConsequence);
        Assert.AreEqual(c.ConsequenceUnit, restored.ConsequenceUnit);
        Assert.AreEqual(c.HazardTransform, restored.HazardTransform);
        Assert.AreEqual(c.SecondaryHazardTransform, restored.SecondaryHazardTransform);
        Assert.AreEqual(c.ConsequenceTransform, restored.ConsequenceTransform);
        CollectionAssert.AreEqual(c.X1Values, restored.X1Values);
        CollectionAssert.AreEqual(c.X2Values, restored.X2Values);
        Assert.AreEqual(c.ZValues[2, 1], restored.ZValues[2, 1], 0d);
        Assert.IsTrue(restored.Validate().IsValid);

        // Re-serialization is bit-equal.
        Assert.AreEqual(element.ToString(), restored.ToXElement().ToString());
    }

    /// <summary>
    /// Verifies a ragged payload reconstructs to exactly the parsed shape — nothing truncated,
    /// absent cells NaN — and fails validation instead of silently computing.
    /// </summary>
    [TestMethod]
    public void Test_ToXElement_RaggedPayload_PreservedAndInvalid()
    {
        // Arrange — shorten the middle row's payload to a single cell.
        var element = ConfiguredConsequence().ToXElement();
        element.Element(nameof(BivariateConsequence.ZValues))!.Elements("Row").ElementAt(1).Value = "3";

        // Act
        var restored = new BivariateConsequence(element);

        // Assert — full parsed shape, the parsed cell kept, the absent cell NaN, and rejection.
        Assert.AreEqual(3, restored.ZValues.GetLength(0));
        Assert.AreEqual(2, restored.ZValues.GetLength(1));
        Assert.AreEqual(3d, restored.ZValues[1, 0], 0d);
        Assert.IsTrue(double.IsNaN(restored.ZValues[1, 1]));
        Assert.AreEqual(8d, restored.ZValues[2, 1], 0d);
        var (isValid, messages) = restored.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("surface values must all be finite")));
    }

    /// <summary>Verifies the factory reconstructs the type, preserves the hash, and filters by cluster.</summary>
    [TestMethod]
    public void Test_Factory_RoundTrip()
    {
        // Arrange
        var c = ConfiguredConsequence();
        var element = c.ToXElement();

        // Act
        var reconstructed = RiskFunctionFactory.CreateFromXElement(element);

        // Assert
        Assert.IsInstanceOfType<BivariateConsequence>(reconstructed);
        CollectionAssert.AreEqual(c.CanonicalHash(), reconstructed!.CanonicalHash());
        Assert.IsNotNull(RiskFunctionFactory.CreateConsequenceFunction(element));
        Assert.IsNull(RiskFunctionFactory.CreateTransformFunction(element));
    }

    /// <summary>Verifies hash identity: metadata and labels inert; every compute edit moves it; transposition moves it.</summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataInert_ComputeSensitive()
    {
        // Arrange
        var baseline = ConfiguredConsequence().CanonicalHash();

        // Metadata and axis labels are inert.
        var relabeled = ConfiguredConsequence();
        relabeled.Name = "Renamed";
        relabeled.Description = "Re-described";
        relabeled.AssignNewId();
        relabeled.SpecifiedHazard = "Different";
        relabeled.HazardUnit = "m";
        relabeled.SecondarySpecifiedHazard = "Other";
        relabeled.SecondaryHazardUnit = "m";
        relabeled.SpecifiedConsequence = "Damages";
        relabeled.ConsequenceUnit = "$";
        CollectionAssert.AreEqual(baseline, relabeled.CanonicalHash());

        // Every axis value, cell, and transform enum moves it.
        var axisEdit = ConfiguredConsequence();
        axisEdit.X1Values = new[] { 0d, 11d, 20d };
        CollectionAssert.AreNotEqual(baseline, axisEdit.CanonicalHash());

        var secondaryEdit = ConfiguredConsequence();
        secondaryEdit.X2Values = new[] { 101d, 200d };
        CollectionAssert.AreNotEqual(baseline, secondaryEdit.CanonicalHash());

        var cellEdit = ConfiguredConsequence();
        cellEdit.ZValues = new[,] { { 1d, 2d }, { 3d, 5.0001d }, { 4d, 8d } };
        CollectionAssert.AreNotEqual(baseline, cellEdit.CanonicalHash());

        var primaryTransformEdit = ConfiguredConsequence();
        primaryTransformEdit.HazardTransform = Transform.Logarithmic;
        CollectionAssert.AreNotEqual(baseline, primaryTransformEdit.CanonicalHash());

        var secondaryTransformEdit = ConfiguredConsequence();
        secondaryTransformEdit.SecondaryHazardTransform = Transform.NormalZ;
        CollectionAssert.AreNotEqual(baseline, secondaryTransformEdit.CanonicalHash());

        var outputTransformEdit = ConfiguredConsequence();
        outputTransformEdit.ConsequenceTransform = Transform.Logarithmic;
        CollectionAssert.AreNotEqual(baseline, outputTransformEdit.CanonicalHash());

        // Transposing a square asymmetric surface moves it — orientation is content.
        var original = new BivariateConsequence { ZValues = new[,] { { 1d, 2d }, { 3d, 4d } } };
        var transposed = new BivariateConsequence { ZValues = new[,] { { 1d, 3d }, { 2d, 4d } } };
        CollectionAssert.AreNotEqual(original.CanonicalHash(), transposed.CanonicalHash());
    }
}
