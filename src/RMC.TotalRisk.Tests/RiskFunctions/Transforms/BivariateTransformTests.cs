using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Tests.RiskFunctions.Transforms;

/// <summary>
/// Unit tests for <see cref="BivariateTransform"/> — defaults, change notification, the
/// validation matrix, known-point bilinear evaluation with per-axis transforms, the pinned
/// native extrapolation policy, the fresh-interpolator discipline, the univariate-surface
/// rejection, shape-preserving serialization, factory round-trips, and hash identity.
/// </summary>
[TestClass]
public class BivariateTransformTests
{
    /// <summary>
    /// Builds the standard labeled 3×2 surface: X1 = {0, 10, 20}, X2 = {100, 200},
    /// Z = {{1, 2}, {3, 5}, {4, 8}}.
    /// </summary>
    private static BivariateTransform ConfiguredTransform()
    {
        return new BivariateTransform
        {
            Name = "Surge-Pool Stage",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            X1Values = new[] { 0d, 10d, 20d },
            X2Values = new[] { 100d, 200d },
            ZValues = new[,] { { 1d, 2d }, { 3d, 5d }, { 4d, 8d } },
        };
    }

    /// <summary>Collects the property names an instance raises.</summary>
    /// <param name="transform">The instance under observation.</param>
    /// <returns>The live list of raised names.</returns>
    private static List<string> Observe(BivariateTransform transform)
    {
        var raised = new List<string>();
        transform.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    /// <summary>Verifies the default construction state: the unit-square zero surface.</summary>
    [TestMethod]
    public void Test_Defaults_UnitSquareZeroSurface()
    {
        // Act
        var t = new BivariateTransform();

        // Assert
        CollectionAssert.AreEqual(new[] { 0d, 1d }, t.X1Values);
        CollectionAssert.AreEqual(new[] { 0d, 1d }, t.X2Values);
        Assert.AreEqual(2, t.ZValues.GetLength(0));
        Assert.AreEqual(2, t.ZValues.GetLength(1));
        Assert.AreEqual(0d, t.ZValues[0, 0], 0d);
        Assert.AreEqual(0d, t.ZValues[1, 1], 0d);
        Assert.AreEqual(Transform.None, t.HazardTransform);
        Assert.AreEqual(Transform.None, t.SecondaryHazardTransform);
        Assert.AreEqual(Transform.None, t.TransformTransform);
        Assert.AreEqual(string.Empty, t.SecondarySpecifiedHazard);
        Assert.AreEqual(string.Empty, t.SecondaryHazardUnit);
        Assert.AreEqual(TransformFunctionType.Bivariate, t.FunctionType);
        Assert.IsTrue(t.IsDeterministic);
        Assert.AreEqual(0, t.SamplingDimensions);
    }

    /// <summary>Verifies change notification for every property, and that null array assignments are ignored.</summary>
    [TestMethod]
    public void Test_PropertyChange_RaisesForEveryProperty()
    {
        // Arrange
        var t = new BivariateTransform();
        var raised = Observe(t);

        // Act
        t.X1Values = new[] { 0d, 5d };
        t.X2Values = new[] { 0d, 7d };
        t.ZValues = new[,] { { 1d, 2d }, { 3d, 4d } };
        t.HazardTransform = Transform.Logarithmic;
        t.SecondaryHazardTransform = Transform.NormalZ;
        t.TransformTransform = Transform.Logarithmic;
        t.SecondarySpecifiedHazard = "Pool";
        t.SecondaryHazardUnit = "ft";

        // Assert
        CollectionAssert.AreEqual(new[]
        {
            nameof(BivariateTransform.X1Values), nameof(BivariateTransform.X2Values),
            nameof(BivariateTransform.ZValues), nameof(BivariateTransform.HazardTransform),
            nameof(BivariateTransform.SecondaryHazardTransform), nameof(BivariateTransform.TransformTransform),
            nameof(BivariateTransform.SecondarySpecifiedHazard), nameof(BivariateTransform.SecondaryHazardUnit),
        }, raised);

        // A null array assignment is ignored: no raise, reference unchanged.
        var before = t.X1Values;
        raised.Clear();
        t.X1Values = null!;
        Assert.AreEqual(0, raised.Count);
        Assert.AreSame(before, t.X1Values);
    }

    /// <summary>Verifies a fully configured surface validates cleanly.</summary>
    [TestMethod]
    public void Test_Validate_ValidConfiguration_NoMessages()
    {
        // Act
        var (isValid, messages) = ConfiguredTransform().Validate();

        // Assert
        Assert.IsTrue(isValid);
        Assert.AreEqual(0, messages.Count);
    }

    /// <summary>Verifies every missing axis label reports its own error.</summary>
    [TestMethod]
    public void Test_Validate_MissingLabels_SixErrors()
    {
        // Act — the default instance is structurally usable but unlabeled.
        var (isValid, messages) = new BivariateTransform().Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.AreEqual(6, messages.Count(m => m.StartsWith("Error:", StringComparison.Ordinal)));
        Assert.IsTrue(messages.Any(m => m.Contains("secondary hazard type")));
        Assert.IsTrue(messages.Any(m => m.Contains("secondary hazard unit")));
        Assert.IsTrue(messages.Any(m => m.Contains("transformed hazard type")));
    }

    /// <summary>Verifies each axis reports the two-value minimum independently.</summary>
    [TestMethod]
    public void Test_Validate_ShortAxis_Error()
    {
        // Arrange
        var t = ConfiguredTransform();
        t.X1Values = new[] { 5d };
        t.ZValues = new[,] { { 1d, 2d } };

        // Act
        var (isValid, messages) = t.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("at least two primary hazard values")));

        // The secondary axis reports its own wording.
        var s = ConfiguredTransform();
        s.X2Values = new[] { 100d };
        s.ZValues = new[,] { { 1d }, { 3d }, { 4d } };
        Assert.IsTrue(s.Validate().ValidationMessages.Any(m => m.Contains("at least two secondary hazard values")));
    }

    /// <summary>Verifies a non-finite axis reports finiteness alone — ordering is meaningless against NaN.</summary>
    [TestMethod]
    public void Test_Validate_NonFiniteAxis_Error()
    {
        // Arrange
        var t = ConfiguredTransform();
        t.X1Values = new[] { 0d, double.NaN, 20d };

        // Act
        var (isValid, messages) = t.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("primary hazard values must all be finite")));
        Assert.IsFalse(messages.Any(m => m.Contains("primary hazard values must be strictly ascending")));
    }

    /// <summary>Verifies a tied axis reports the strictly ascending rule.</summary>
    [TestMethod]
    public void Test_Validate_NonAscendingAxis_Error()
    {
        // Arrange
        var t = ConfiguredTransform();
        t.X1Values = new[] { 0d, 10d, 10d };

        // Act
        var (isValid, messages) = t.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("primary hazard values must be strictly ascending")));
    }

    /// <summary>Verifies a surface whose dimensions disagree with the axes is rejected.</summary>
    [TestMethod]
    public void Test_Validate_SurfaceDimensionMismatch_Error()
    {
        // Arrange — three primary values but a 2×2 surface.
        var t = ConfiguredTransform();
        t.ZValues = new[,] { { 1d, 2d }, { 3d, 5d } };

        // Act
        var (isValid, messages) = t.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("one row of values per primary hazard value")));
    }

    /// <summary>Verifies a non-finite surface cell is rejected.</summary>
    [TestMethod]
    public void Test_Validate_NonFiniteCell_Error()
    {
        // Arrange
        var t = ConfiguredTransform();
        t.ZValues = new[,] { { 1d, 2d }, { 3d, double.NaN }, { 4d, 8d } };

        // Act
        var (isValid, messages) = t.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("surface values must all be finite")));
    }

    /// <summary>Verifies the logarithmic guard on each axis and on the output surface.</summary>
    [TestMethod]
    public void Test_Validate_LogGuards_Errors()
    {
        // Arrange / Act / Assert — a negative primary axis under a log transform.
        var primary = ConfiguredTransform();
        primary.X1Values = new[] { -1d, 10d, 20d };
        primary.HazardTransform = Transform.Logarithmic;
        Assert.IsTrue(primary.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The hazard interpolation transform cannot be logarithmic")));

        // A negative secondary axis under a log transform.
        var secondary = ConfiguredTransform();
        secondary.X2Values = new[] { -100d, 200d };
        secondary.SecondaryHazardTransform = Transform.Logarithmic;
        Assert.IsTrue(secondary.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The secondary hazard interpolation transform cannot be logarithmic")));

        // A negative surface cell under a log output transform.
        var output = ConfiguredTransform();
        output.ZValues = new[,] { { -1d, 2d }, { 3d, 5d }, { 4d, 8d } };
        output.TransformTransform = Transform.Logarithmic;
        Assert.IsTrue(output.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The transform interpolation transform cannot be logarithmic")));
    }

    /// <summary>Verifies the normal-Z guard on each axis and the output, with endpoints 0 and 1 legal.</summary>
    [TestMethod]
    public void Test_Validate_NormalZGuards_Errors()
    {
        // Arrange / Act / Assert — a primary axis outside [0, 1] under a normal-Z transform.
        var primary = ConfiguredTransform();
        primary.X1Values = new[] { -0.1d, 0.5d, 1d };
        primary.HazardTransform = Transform.NormalZ;
        Assert.IsTrue(primary.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The hazard interpolation transform cannot be normal Z")));

        // A secondary axis above one.
        var secondary = ConfiguredTransform();
        secondary.X2Values = new[] { 0.5d, 1.5d };
        secondary.SecondaryHazardTransform = Transform.NormalZ;
        Assert.IsTrue(secondary.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The secondary hazard interpolation transform cannot be normal Z")));

        // A surface cell above one under a normal-Z output.
        var output = ConfiguredTransform();
        output.ZValues = new[,] { { 0.1d, 0.2d }, { 0.3d, 1.5d }, { 0.4d, 0.8d } };
        output.TransformTransform = Transform.NormalZ;
        Assert.IsTrue(output.Validate().ValidationMessages.Any(
            m => m.Contains("Error: The transform interpolation transform cannot be normal Z")));

        // Exactly 0 and 1 are legal on a normal-Z axis (the ratified inclusive range).
        var endpoints = ConfiguredTransform();
        endpoints.X1Values = new[] { 0d, 0.5d, 1d };
        endpoints.HazardTransform = Transform.NormalZ;
        Assert.IsFalse(endpoints.Validate().ValidationMessages.Any(m => m.Contains("normal Z")));
    }

    /// <summary>Verifies exact corner recovery and the hand-computed interior bilinear value.</summary>
    [TestMethod]
    public void Test_Evaluate_CornersAndInterior_KnownValues()
    {
        // Arrange
        var t = ConfiguredTransform();

        // Act / Assert — grid nodes are recovered exactly.
        Assert.AreEqual(1d, t.Evaluate(0d, 100d), 0d);
        Assert.AreEqual(2d, t.Evaluate(0d, 200d), 0d);
        Assert.AreEqual(4d, t.Evaluate(20d, 100d), 0d);
        Assert.AreEqual(8d, t.Evaluate(20d, 200d), 0d);

        // Interior (5, 150): t = u = 0.5 over the {1, 2; 3, 5} cell → (1 + 3 + 5 + 2)/4.
        Assert.AreEqual(2.75d, t.Evaluate(5d, 150d), 1e-12);
    }

    /// <summary>Verifies the pinned policy: both coordinates out of range clamp to the exact nearest corner.</summary>
    [TestMethod]
    public void Test_Evaluate_ExtrapolationPolicy_CornerClamp()
    {
        // Arrange
        var t = ConfiguredTransform();

        // Act / Assert — all four corners, exactly.
        Assert.AreEqual(1d, t.Evaluate(-5d, 50d), 0d);
        Assert.AreEqual(2d, t.Evaluate(-5d, 250d), 0d);
        Assert.AreEqual(4d, t.Evaluate(25d, 50d), 0d);
        Assert.AreEqual(8d, t.Evaluate(25d, 250d), 0d);
    }

    /// <summary>
    /// Verifies the pinned policy: one coordinate out of range clamps to its edge row or column
    /// with one-dimensional linear interpolation along the in-range axis.
    /// </summary>
    [TestMethod]
    public void Test_Evaluate_ExtrapolationPolicy_EdgeInterpolation()
    {
        // Arrange
        var t = ConfiguredTransform();

        // Act / Assert — primary below range: first row {1, 2} at u = 0.5.
        Assert.AreEqual(1.5d, t.Evaluate(-5d, 150d), 1e-12);
        // Primary above range: last row {4, 8} at u = 0.5.
        Assert.AreEqual(6d, t.Evaluate(25d, 150d), 1e-12);
        // Secondary below range: first column {1, 3} at t = 0.5.
        Assert.AreEqual(2d, t.Evaluate(5d, 50d), 1e-12);
        // Secondary above range: second column {2, 5} at t = 0.5.
        Assert.AreEqual(3.5d, t.Evaluate(5d, 250d), 1e-12);
    }

    /// <summary>Verifies a logarithmic primary axis interpolates in log space.</summary>
    [TestMethod]
    public void Test_Evaluate_LogPrimaryAxis_KnownPoint()
    {
        // Arrange — z depends only on x1 so the log-axis fraction is isolated.
        var t = ConfiguredTransform();
        t.X1Values = new[] { 1d, 10d, 100d };
        t.ZValues = new[,] { { 0d, 0d }, { 10d, 10d }, { 20d, 20d } };
        t.HazardTransform = Transform.Logarithmic;

        // Act / Assert — x1 = √10 is the log-space midpoint of [1, 10].
        Assert.AreEqual(5d, t.Evaluate(Math.Sqrt(10d), 150d), 1e-12);
    }

    /// <summary>Verifies a logarithmic output interpolates cells in log space: the geometric form.</summary>
    [TestMethod]
    public void Test_Evaluate_LogOutput_KnownPoint()
    {
        // Arrange
        var t = ConfiguredTransform();
        t.ZValues = new[,] { { 1d, 2d }, { 10d, 20d }, { 100d, 200d } };
        t.TransformTransform = Transform.Logarithmic;

        // Act / Assert — equal quarter weights: 10^(log10(1·10·20·2)/4) = 400^0.25.
        Assert.AreEqual(Math.Pow(400d, 0.25d), t.Evaluate(5d, 150d), 1e-12);
    }

    /// <summary>Verifies a normal-Z output interpolates cells in z-score space.</summary>
    [TestMethod]
    public void Test_Evaluate_NormalZOutput_KnownPoint()
    {
        // Arrange
        var t = ConfiguredTransform();
        t.ZValues = new[,] { { 0.1d, 0.2d }, { 0.3d, 0.5d }, { 0.4d, 0.8d } };
        t.TransformTransform = Transform.NormalZ;

        // Act — interior (5, 150) carries equal quarter weights over the first cell.
        double zSpace = 0.25d * Normal.StandardZ(0.1d) + 0.25d * Normal.StandardZ(0.3d)
            + 0.25d * Normal.StandardZ(0.5d) + 0.25d * Normal.StandardZ(0.2d);
        double expected = Normal.StandardCDF(zSpace);

        // Assert
        Assert.AreEqual(expected, t.Evaluate(5d, 150d), 1e-12);
    }

    /// <summary>Verifies the convenience overload agrees bit-for-bit with the interpolator seam.</summary>
    [TestMethod]
    public void Test_Evaluate_MatchesInterpolator()
    {
        // Arrange
        var t = ConfiguredTransform();
        t.HazardTransform = Transform.Logarithmic;
        t.X1Values = new[] { 1d, 10d, 20d };
        var interpolator = t.CreateInterpolator();

        // Act / Assert — identical arithmetic path, so bit-equality.
        foreach (var (x, y) in new[] { (2d, 120d), (5d, 150d), (15d, 199d), (0.5d, 50d), (30d, 300d) })
        {
            Assert.AreEqual(interpolator.Interpolate(x, y), t.Evaluate(x, y), 0d);
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
        var t = ConfiguredTransform();
        t.HazardTransform = Transform.Logarithmic;
        t.X1Values = new[] { 1d, 10d, 20d };
        t.SecondaryHazardTransform = Transform.NormalZ;
        t.X2Values = new[] { 0.2d, 0.8d };

        // Act
        var first = t.CreateInterpolator();
        var second = t.CreateInterpolator();

        // Assert
        Assert.AreNotSame(first, second);
        Assert.AreSame(t.X1Values, first.X1Values);
        Assert.AreSame(t.X2Values, first.X2Values);
        Assert.AreSame(t.ZValues, first.YValues);
        Assert.AreEqual(Transform.Logarithmic, first.X1Transform);
        Assert.AreEqual(Transform.NormalZ, first.X2Transform);
        Assert.AreEqual(Transform.None, first.YTransform);
    }

    /// <summary>Verifies the univariate sampling surface throws, naming the joint evaluation seam.</summary>
    [TestMethod]
    public void Test_UnivariateSurface_Throws()
    {
        // Arrange
        var t = ConfiguredTransform();

        // Act / Assert
        var thrown = Assert.ThrowsException<NotSupportedException>(() => t.SampleFunction());
        Assert.IsTrue(thrown.Message.Contains("Evaluate(x, y)"));
        Assert.ThrowsException<NotSupportedException>(() => t.SampleFunction(0.5d));
        Assert.ThrowsException<NotSupportedException>(() => t.SampleFunction(0));
    }

    /// <summary>Verifies the deterministic D = 0 sampler contract: size recorded, no matrix, still no univariate surface.</summary>
    [TestMethod]
    public void Test_SetupSampler_DeterministicNoMatrix()
    {
        // Arrange
        var t = ConfiguredTransform();

        // Act
        t.SetupSampler(100, 12345, SamplingScheme.LatinHypercube);

        // Assert
        Assert.AreEqual(100, t.SampleSize);
        Assert.ThrowsException<NotSupportedException>(() => t.SampleFunction(3));
    }

    /// <summary>Verifies evaluation and bounds refuse a structurally unusable table, while Validate reports it.</summary>
    [TestMethod]
    public void Test_UnusableTable_Throws()
    {
        // Arrange — a single-value primary axis cannot back interpolation.
        var t = ConfiguredTransform();
        t.X1Values = new[] { 1d };

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => t.Evaluate(1d, 150d));
        Assert.ThrowsException<InvalidOperationException>(() => t.CreateInterpolator());
        Assert.ThrowsException<InvalidOperationException>(() => t.MinHazard());
        Assert.ThrowsException<InvalidOperationException>(() => t.MaxHazard());
        Assert.ThrowsException<InvalidOperationException>(() => t.MinSecondaryHazard());
        Assert.ThrowsException<InvalidOperationException>(() => t.MaxSecondaryHazard());
        Assert.ThrowsException<InvalidOperationException>(() => t.MinTransformedHazard(true));
        Assert.ThrowsException<InvalidOperationException>(() => t.MaxTransformedHazard(false));

        // Validate never throws — it reports.
        Assert.IsFalse(t.Validate().IsValid);
    }

    /// <summary>Verifies the axis and surface bounds.</summary>
    [TestMethod]
    public void Test_Bounds_KnownValues()
    {
        // Arrange
        var t = ConfiguredTransform();

        // Act / Assert
        Assert.AreEqual(0d, t.MinHazard(), 0d);
        Assert.AreEqual(20d, t.MaxHazard(), 0d);
        Assert.AreEqual(100d, t.MinSecondaryHazard(), 0d);
        Assert.AreEqual(200d, t.MaxSecondaryHazard(), 0d);
        Assert.AreEqual(1d, t.MinTransformedHazard(true), 0d);
        Assert.AreEqual(1d, t.MinTransformedHazard(false), 0d);
        Assert.AreEqual(8d, t.MaxTransformedHazard(true), 0d);
        Assert.AreEqual(8d, t.MaxTransformedHazard(false), 0d);
    }

    /// <summary>Verifies the deterministic surface has no uncertainty representation.</summary>
    [TestMethod]
    public void Test_ComputeUncertaintyResults_ReturnsNull()
    {
        // Act / Assert
        Assert.IsNull(ConfiguredTransform().ComputeUncertaintyResults());
    }

    /// <summary>Verifies the serialized shape and a bit-equal round trip.</summary>
    [TestMethod]
    public void Test_ToXElement_RoundTrip()
    {
        // Arrange
        var t = ConfiguredTransform();
        t.Description = "Two-way rating";
        t.HazardTransform = Transform.Logarithmic;
        t.X1Values = new[] { 1d, 10d, 20d };
        t.TransformTransform = Transform.Logarithmic;

        // Act
        var element = t.ToXElement();
        var restored = new BivariateTransform(element);

        // Assert — the serialized shape: pipe-joined axes and one Row per primary value.
        Assert.AreEqual(nameof(BivariateTransform), element.Name.LocalName);
        Assert.AreEqual("1|10|20", element.Element(nameof(BivariateTransform.X1Values))!.Value);
        Assert.AreEqual(3, element.Element(nameof(BivariateTransform.ZValues))!.Elements("Row").Count());
        Assert.AreEqual("1|2", element.Element(nameof(BivariateTransform.ZValues))!.Elements("Row").First().Value);

        // Every property survives.
        Assert.AreEqual(t.Id, restored.Id);
        Assert.AreEqual(t.Name, restored.Name);
        Assert.AreEqual(t.Description, restored.Description);
        Assert.AreEqual(t.SpecifiedHazard, restored.SpecifiedHazard);
        Assert.AreEqual(t.HazardUnit, restored.HazardUnit);
        Assert.AreEqual(t.SecondarySpecifiedHazard, restored.SecondarySpecifiedHazard);
        Assert.AreEqual(t.SecondaryHazardUnit, restored.SecondaryHazardUnit);
        Assert.AreEqual(t.TransformedHazard, restored.TransformedHazard);
        Assert.AreEqual(t.TransformedHazardUnit, restored.TransformedHazardUnit);
        Assert.AreEqual(t.HazardTransform, restored.HazardTransform);
        Assert.AreEqual(t.SecondaryHazardTransform, restored.SecondaryHazardTransform);
        Assert.AreEqual(t.TransformTransform, restored.TransformTransform);
        CollectionAssert.AreEqual(t.X1Values, restored.X1Values);
        CollectionAssert.AreEqual(t.X2Values, restored.X2Values);
        Assert.AreEqual(t.ZValues[2, 1], restored.ZValues[2, 1], 0d);
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
        // Arrange — drop the last row entirely.
        var dropped = ConfiguredTransform().ToXElement();
        dropped.Element(nameof(BivariateTransform.ZValues))!.Elements("Row").Last().Remove();

        // Act
        var missingRow = new BivariateTransform(dropped);

        // Assert — two parsed rows survive verbatim; the dimension rule rejects.
        Assert.AreEqual(2, missingRow.ZValues.GetLength(0));
        Assert.AreEqual(2, missingRow.ZValues.GetLength(1));
        Assert.AreEqual(1d, missingRow.ZValues[0, 0], 0d);
        Assert.AreEqual(5d, missingRow.ZValues[1, 1], 0d);
        var (droppedValid, droppedMessages) = missingRow.Validate();
        Assert.IsFalse(droppedValid);
        Assert.IsTrue(droppedMessages.Any(m => m.Contains("one row of values per primary hazard value")));

        // Arrange — shorten one row's payload to a single cell.
        var shortened = ConfiguredTransform().ToXElement();
        shortened.Element(nameof(BivariateTransform.ZValues))!.Elements("Row").First().Value = "1";

        // Act
        var shortRow = new BivariateTransform(shortened);

        // Assert — full parsed shape, the parsed cell kept, the absent cell NaN, and rejection.
        Assert.AreEqual(3, shortRow.ZValues.GetLength(0));
        Assert.AreEqual(2, shortRow.ZValues.GetLength(1));
        Assert.AreEqual(1d, shortRow.ZValues[0, 0], 0d);
        Assert.IsTrue(double.IsNaN(shortRow.ZValues[0, 1]));
        Assert.AreEqual(8d, shortRow.ZValues[2, 1], 0d);
        var (shortValid, shortMessages) = shortRow.Validate();
        Assert.IsFalse(shortValid);
        Assert.IsTrue(shortMessages.Any(m => m.Contains("surface values must all be finite")));
    }

    /// <summary>Verifies the factory reconstructs the type, preserves the hash, and filters by cluster.</summary>
    [TestMethod]
    public void Test_Factory_RoundTrip()
    {
        // Arrange
        var t = ConfiguredTransform();
        var element = t.ToXElement();

        // Act
        var reconstructed = RiskFunctionFactory.CreateFromXElement(element);

        // Assert
        Assert.IsInstanceOfType<BivariateTransform>(reconstructed);
        CollectionAssert.AreEqual(t.CanonicalHash(), reconstructed!.CanonicalHash());
        Assert.IsNotNull(RiskFunctionFactory.CreateTransformFunction(element));
        Assert.IsNull(RiskFunctionFactory.CreateConsequenceFunction(element));
    }

    /// <summary>Verifies hash identity: metadata and labels inert; every compute edit moves it.</summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataInert_ComputeSensitive()
    {
        // Arrange
        var baseline = ConfiguredTransform().CanonicalHash();

        // Metadata and axis labels are inert.
        var relabeled = ConfiguredTransform();
        relabeled.Name = "Renamed";
        relabeled.Description = "Re-described";
        relabeled.AssignNewId();
        relabeled.SpecifiedHazard = "Different";
        relabeled.HazardUnit = "m";
        relabeled.SecondarySpecifiedHazard = "Other";
        relabeled.SecondaryHazardUnit = "m";
        relabeled.TransformedHazard = "Other";
        relabeled.TransformedHazardUnit = "m";
        CollectionAssert.AreEqual(baseline, relabeled.CanonicalHash());

        // Every axis value, cell, and transform enum moves it.
        var axisEdit = ConfiguredTransform();
        axisEdit.X1Values = new[] { 0d, 11d, 20d };
        CollectionAssert.AreNotEqual(baseline, axisEdit.CanonicalHash());

        var secondaryEdit = ConfiguredTransform();
        secondaryEdit.X2Values = new[] { 101d, 200d };
        CollectionAssert.AreNotEqual(baseline, secondaryEdit.CanonicalHash());

        var cellEdit = ConfiguredTransform();
        cellEdit.ZValues = new[,] { { 1d, 2d }, { 3d, 5.0001d }, { 4d, 8d } };
        CollectionAssert.AreNotEqual(baseline, cellEdit.CanonicalHash());

        var primaryTransformEdit = ConfiguredTransform();
        primaryTransformEdit.HazardTransform = Transform.Logarithmic;
        CollectionAssert.AreNotEqual(baseline, primaryTransformEdit.CanonicalHash());

        var secondaryTransformEdit = ConfiguredTransform();
        secondaryTransformEdit.SecondaryHazardTransform = Transform.NormalZ;
        CollectionAssert.AreNotEqual(baseline, secondaryTransformEdit.CanonicalHash());

        var outputTransformEdit = ConfiguredTransform();
        outputTransformEdit.TransformTransform = Transform.Logarithmic;
        CollectionAssert.AreNotEqual(baseline, outputTransformEdit.CanonicalHash());
    }

    /// <summary>Verifies transposing a square surface moves the hash — orientation is content.</summary>
    [TestMethod]
    public void Test_CanonicalHash_Transpose_Moves()
    {
        // Arrange — a square surface over equal axes, asymmetric across the diagonal.
        var original = new BivariateTransform
        {
            X1Values = new[] { 0d, 1d },
            X2Values = new[] { 0d, 1d },
            ZValues = new[,] { { 1d, 2d }, { 3d, 4d } },
        };
        var transposed = new BivariateTransform
        {
            X1Values = new[] { 0d, 1d },
            X2Values = new[] { 0d, 1d },
            ZValues = new[,] { { 1d, 3d }, { 2d, 4d } },
        };

        // Act / Assert
        CollectionAssert.AreNotEqual(original.CanonicalHash(), transposed.CanonicalHash());
    }
}
