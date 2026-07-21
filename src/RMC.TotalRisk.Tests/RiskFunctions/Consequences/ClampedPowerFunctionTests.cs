using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Consequences;

namespace RMC.TotalRisk.Tests.RiskFunctions.Consequences;

/// <summary>
/// Unit tests for <see cref="ClampedPowerFunction"/> — exact closed-form evaluation across the
/// zero segment, the power segment, and the saturation cap; the inverse; the derived support
/// bounds; and the four-parameter layout.
/// </summary>
[TestClass]
public class ClampedPowerFunctionTests
{
    /// <summary>The reference saturation crossing for (a=10, b=1.5, h₀=2, U=500): 2 + 50^(2/3).</summary>
    private static readonly double Crossing = 2d + Math.Pow(50d, 2d / 3d);

    /// <summary>Verifies exact evaluation at the threshold, unit offset, power segment, and cap.</summary>
    [TestMethod]
    public void Test_Function_KnownPoints_ThresholdUnitOffsetCapAndBeyond()
    {
        // Arrange
        var f = new ClampedPowerFunction(10d, 1.5d, 2d, 500d);

        // Assert — zero at and below the threshold (exact, not epsilon-offset).
        Assert.AreEqual(0d, f.Function(1d), 0d);
        Assert.AreEqual(0d, f.Function(2d), 0d);

        // Power segment: a·(h − h₀)^b.
        Assert.AreEqual(10d, f.Function(3d), 1e-12, "Unit offset must evaluate to the scale coefficient.");
        Assert.AreEqual(80d, f.Function(6d), 1e-12, "10·4^1.5 = 80.");

        // At and beyond the saturation crossing the curve is flat at U.
        Assert.AreEqual(500d, f.Function(Crossing), 1e-9);
        Assert.AreEqual(500d, f.Function(Crossing + 10d), 0d);

        // No cap: the power segment continues unbounded.
        var uncapped = new ClampedPowerFunction(10d, 1.5d, 2d, double.PositiveInfinity);
        Assert.AreEqual(10d * Math.Pow(98d, 1.5d), uncapped.Function(100d), 1e-9);
    }

    /// <summary>Verifies the closed-form inverse and forward/inverse round-trips.</summary>
    [TestMethod]
    public void Test_InverseFunction_ClosedForm_RoundTrips()
    {
        // Arrange
        var f = new ClampedPowerFunction(10d, 1.5d, 2d, 500d);

        // Assert — the zero segment inverts to the threshold.
        Assert.AreEqual(2d, f.InverseFunction(0d), 0d);
        Assert.AreEqual(2d, f.InverseFunction(-5d), 0d);

        // The power segment inverts exactly, and round-trips.
        Assert.AreEqual(6d, f.InverseFunction(80d), 1e-12);
        Assert.AreEqual(80d, f.Function(f.InverseFunction(80d)), 1e-9);

        // At and above the cap the inverse is the saturation crossing.
        Assert.AreEqual(Crossing, f.InverseFunction(500d), 1e-12);
        Assert.AreEqual(Crossing, f.InverseFunction(600d), 1e-12);

        // Without a cap the power-segment formula applies everywhere above zero.
        var uncapped = new ClampedPowerFunction(10d, 1.5d, 2d, double.PositiveInfinity);
        Assert.AreEqual(2d + Math.Pow(60d, 2d / 3d), uncapped.InverseFunction(600d), 1e-12);
    }

    /// <summary>Verifies the derived support bounds and the deterministic contract.</summary>
    [TestMethod]
    public void Test_Minimum_SetterThrows_MaximumIsCrossing()
    {
        // Arrange
        var f = new ClampedPowerFunction(10d, 1.5d, 2d, 500d);

        // Assert — Minimum is the threshold; Maximum is the saturation crossing.
        Assert.AreEqual(2d, f.Minimum, 0d);
        Assert.AreEqual(Crossing, f.Maximum, 1e-12);
        Assert.ThrowsException<NotSupportedException>(() => f.Minimum = 0d);
        Assert.ThrowsException<NotSupportedException>(() => f.Maximum = 100d);

        // Without a cap, Maximum is the interface's unbounded convention.
        var uncapped = new ClampedPowerFunction(10d, 1.5d, 2d, double.PositiveInfinity);
        Assert.AreEqual(double.MaxValue, uncapped.Maximum, 0d);

        // Always deterministic: true is accepted, false is rejected loudly.
        Assert.IsTrue(f.IsDeterministic);
        f.IsDeterministic = true;
        Assert.ThrowsException<NotSupportedException>(() => f.IsDeterministic = false);
    }

    /// <summary>Verifies the [a, b, h₀, U] parameter layout, validation, and the throw-on-use gate.</summary>
    [TestMethod]
    public void Test_SetParameters_FourParameterLayout()
    {
        // Arrange
        var f = new ClampedPowerFunction();
        Assert.AreEqual(4, f.NumberOfParameters);

        // Act — re-parameterize through the interface layout.
        f.SetParameters(new[] { 10d, 1.5d, 2d, 500d });

        // Assert
        Assert.IsTrue(f.ParametersValid);
        Assert.AreEqual(10d, f.Alpha, 0d);
        Assert.AreEqual(1.5d, f.Beta, 0d);
        Assert.AreEqual(2d, f.Threshold, 0d);
        Assert.AreEqual(500d, f.UpperBound, 0d);
        Assert.AreEqual(80d, f.Function(6d), 1e-12);

        // A wrong-arity list throws immediately; invalid values defer to first use.
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => f.SetParameters(new[] { 1d, 2d }));
        var invalid = new ClampedPowerFunction(-1d, 1.5d, 2d, 500d);
        Assert.IsFalse(invalid.ParametersValid);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => invalid.Function(6d));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => invalid.InverseFunction(80d));

        // ValidateParameters reports without throwing when asked.
        Assert.IsNotNull(invalid.ValidateParameters(new[] { -1d, 1.5d, 2d, 500d }, throwException: false));
        Assert.IsNull(invalid.ValidateParameters(new[] { 1d, 1.5d, 2d, 500d }, throwException: false));

        // Parameter bounds arrays follow the layout.
        Assert.AreEqual(0d, f.MinimumOfParameters[0], 0d);
        Assert.AreEqual(double.MinValue, f.MinimumOfParameters[2], 0d);
        Assert.AreEqual(double.PositiveInfinity, f.MaximumOfParameters[3], 0d);
    }
}
