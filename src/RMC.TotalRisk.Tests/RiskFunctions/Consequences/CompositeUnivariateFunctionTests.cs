using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Functions;
using RMC.TotalRisk.RiskFunctions.Consequences;

namespace RMC.TotalRisk.Tests.RiskFunctions.Consequences;

/// <summary>
/// Unit tests for <see cref="CompositeUnivariateFunction"/> — the pointwise weighted combine:
/// exact evaluation, the zero clamp, the non-monotonic inverse rejection, the child envelope, and
/// the constructor guards.
/// </summary>
[TestClass]
public class CompositeUnivariateFunctionTests
{
    /// <summary>Verifies the weighted pointwise combine is exact at known points.</summary>
    [TestMethod]
    public void Test_Function_WeightedCombine_ExactAtKnownPoints()
    {
        // Arrange — two exact power curves: 10·(x−2)^1.5 and 5·x^2 (β=2, no threshold).
        var a = new ClampedPowerFunction(10d, 1.5d, 2d, double.PositiveInfinity);
        var b = new ClampedPowerFunction(5d, 2d, 0d, double.PositiveInfinity);
        var combined = new CompositeUnivariateFunction(new IUnivariateFunction[] { a, b }, new[] { 0.25d, 0.75d });

        // Assert — 0.25·(10·4^1.5) + 0.75·(5·36) = 20 + 135 at x = 6.
        Assert.AreEqual(0.25d * 80d + 0.75d * 180d, combined.Function(6d), 1e-12);

        // Below the first child's threshold only the second contributes.
        Assert.AreEqual(0.75d * 5d, combined.Function(1d), 1e-12);

        // A single-child combine (the Mixture realization shape) is the child itself.
        var single = new CompositeUnivariateFunction(new IUnivariateFunction[] { a }, new[] { 1d });
        Assert.AreEqual(80d, single.Function(6d), 1e-12);
    }

    /// <summary>Verifies a negative combined value clamps to zero (the legacy composite clamp).</summary>
    [TestMethod]
    public void Test_Function_NegativeCombine_ClampsToZero()
    {
        // Arrange — a constant −10 line (Numerics LinearFunction: y = α + βx).
        var negative = new LinearFunction(-10d, 0d);
        var combined = new CompositeUnivariateFunction(new IUnivariateFunction[] { negative }, new[] { 1d });

        // Assert
        Assert.AreEqual(0d, combined.Function(5d), 0d);
    }

    /// <summary>Verifies the inverse and parameter surfaces reject loudly (no parameters of its own).</summary>
    [TestMethod]
    public void Test_InverseFunction_And_Parameters_Rejected()
    {
        // Arrange
        var combined = new CompositeUnivariateFunction(
            new IUnivariateFunction[] { new ClampedPowerFunction() }, new[] { 1d });

        // Assert
        Assert.ThrowsException<NotSupportedException>(() => combined.InverseFunction(10d));
        Assert.ThrowsException<NotSupportedException>(() => combined.SetParameters(new[] { 1d }));
        Assert.ThrowsException<NotSupportedException>(() => combined.IsDeterministic = false);
        Assert.IsNull(combined.ValidateParameters(null!, throwException: true));
        Assert.AreEqual(0, combined.NumberOfParameters);
        Assert.IsTrue(combined.ParametersValid);
        Assert.IsTrue(combined.IsDeterministic);
    }

    /// <summary>Verifies the support envelope and the constructor guards.</summary>
    [TestMethod]
    public void Test_MinimumMaximum_ChildEnvelope_AndCtorGuards()
    {
        // Arrange — thresholds 2 and 0; caps 500 (crossing ≈ 15.57) and none.
        var a = new ClampedPowerFunction(10d, 1.5d, 2d, 500d);
        var b = new ClampedPowerFunction(5d, 2d, 0d, double.PositiveInfinity);
        var combined = new CompositeUnivariateFunction(new IUnivariateFunction[] { a, b }, new[] { 0.5d, 0.5d });

        // Assert — envelope: the smallest minimum and the largest maximum.
        Assert.AreEqual(0d, combined.Minimum, 0d);
        Assert.AreEqual(double.MaxValue, combined.Maximum, 0d);
        Assert.ThrowsException<NotSupportedException>(() => combined.Minimum = 1d);
        Assert.ThrowsException<NotSupportedException>(() => combined.Maximum = 1d);

        // Constructor guards.
        Assert.ThrowsException<ArgumentNullException>(() => new CompositeUnivariateFunction(null!, new[] { 1d }));
        Assert.ThrowsException<ArgumentNullException>(() => new CompositeUnivariateFunction(new IUnivariateFunction[] { a }, null!));
        Assert.ThrowsException<ArgumentException>(() => new CompositeUnivariateFunction(Array.Empty<IUnivariateFunction>(), Array.Empty<double>()));
        Assert.ThrowsException<ArgumentException>(() => new CompositeUnivariateFunction(new IUnivariateFunction[] { a }, new[] { 0.5d, 0.5d }));
        Assert.ThrowsException<ArgumentException>(() => new CompositeUnivariateFunction(new IUnivariateFunction[] { a, null! }, new[] { 0.5d, 0.5d }));
    }
}
