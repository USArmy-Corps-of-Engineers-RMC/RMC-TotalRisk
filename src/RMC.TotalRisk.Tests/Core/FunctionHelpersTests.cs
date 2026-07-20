using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>
/// Unit tests for <see cref="FunctionHelpers.ForceMonotonic(OrderedPairedData)"/> — the four
/// sort-order branches of the strict-monotonicity repair.
/// </summary>
/// <remarks>
/// v1.1 improves on the v1.0 repair (absolute machine-epsilon nudge — unrepresentable at
/// magnitudes ≥ 1 and swallowed by the tolerance-equal ordinate setter): the nudge is now
/// scale-aware, so ties and crossings repair at every magnitude. The large-magnitude and
/// exact-tie tests below pin the improvement.
/// </remarks>
[TestClass]
public class FunctionHelpersTests
{
    /// <summary>Verifies the ascending-X / ascending-Y branch repairs a crossing.</summary>
    [TestMethod]
    public void Test_ForceMonotonic_AscendingAscending_RepairsCrossing()
    {
        // Arrange — index 1 crosses below index 0 on both axes.
        var opd = new OrderedPairedData(
            new[] { 0.1, 0.05, 0.3 }, new[] { 0.2, 0.15, 0.4 },
            true, SortOrder.Ascending, true, SortOrder.Ascending);
        Assert.IsFalse(opd.IsValid);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert — strictly increasing on both axes; untouched ordinates unchanged.
        Assert.IsTrue(opd[1].X > opd[0].X);
        Assert.IsTrue(opd[1].Y > opd[0].Y);
        Assert.IsTrue(opd[2].X > opd[1].X);
        Assert.IsTrue(opd[2].Y > opd[1].Y);
        Assert.AreEqual(0.3, opd[2].X, 0d);
    }

    /// <summary>Verifies the ascending-X / descending-Y branch nudges X upward and Y downward.</summary>
    [TestMethod]
    public void Test_ForceMonotonic_AscendingDescending_RepairsCrossing()
    {
        // Arrange
        var opd = new OrderedPairedData(
            new[] { 0.1, 0.05, 0.3 }, new[] { 0.4, 0.45, 0.2 },
            true, SortOrder.Ascending, true, SortOrder.Descending);
        Assert.IsFalse(opd.IsValid);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert — X strictly increasing, Y strictly decreasing.
        Assert.IsTrue(opd[1].X > opd[0].X);
        Assert.IsTrue(opd[1].Y < opd[0].Y);
        Assert.IsTrue(opd[2].X > opd[1].X);
        Assert.IsTrue(opd[2].Y < opd[1].Y);
    }

    /// <summary>Verifies the descending-X / ascending-Y branch (exceedance-probability form).</summary>
    [TestMethod]
    public void Test_ForceMonotonic_DescendingAscending_RepairsCrossing()
    {
        // Arrange — X descending with a crossing at index 1 on both axes.
        var opd = new OrderedPairedData(
            new[] { 0.5, 0.6, 0.1 }, new[] { 0.1, 0.05, 0.3 },
            true, SortOrder.Descending, true, SortOrder.Ascending);
        Assert.IsFalse(opd.IsValid);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert
        Assert.IsTrue(opd[1].X < opd[0].X);
        Assert.IsTrue(opd[1].Y > opd[0].Y);
        Assert.IsTrue(opd[2].X < opd[1].X);
        Assert.IsTrue(opd[2].Y > opd[1].Y);
    }

    /// <summary>Verifies the descending-X / descending-Y branch clamps both axes downward.</summary>
    [TestMethod]
    public void Test_ForceMonotonic_DescendingDescending_RepairsCrossing()
    {
        // Arrange
        var opd = new OrderedPairedData(
            new[] { 0.5, 0.6, 0.1 }, new[] { 0.4, 0.45, 0.2 },
            true, SortOrder.Descending, true, SortOrder.Descending);
        Assert.IsFalse(opd.IsValid);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert
        Assert.IsTrue(opd[1].X < opd[0].X);
        Assert.IsTrue(opd[1].Y < opd[0].Y);
        Assert.IsTrue(opd[2].X < opd[1].X);
        Assert.IsTrue(opd[2].Y < opd[1].Y);
    }

    /// <summary>
    /// Pins the v1.1 improvement: exact ties repair to a strictly monotonic, valid curve (the
    /// v1.0 one-epsilon nudge was discarded by the tolerance-equal ordinate setter).
    /// </summary>
    [TestMethod]
    public void Test_ForceMonotonic_ExactTie_Repairs()
    {
        // Arrange
        var opd = new OrderedPairedData(
            new[] { 0.1, 0.1, 0.3 }, new[] { 0.2, 0.2, 0.4 },
            true, SortOrder.Ascending, true, SortOrder.Ascending);
        Assert.IsFalse(opd.IsValid);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert — strictly increasing and valid after repair.
        Assert.IsTrue(opd[1].X > opd[0].X);
        Assert.IsTrue(opd[1].Y > opd[0].Y);
        Assert.IsTrue(opd[2].X > opd[1].X);
        Assert.IsTrue(opd.IsValid);
    }

    /// <summary>
    /// Pins the v1.1 improvement at real hazard scales: the v1.0 absolute-epsilon nudge rounds
    /// away at magnitudes ≥ 1 (10000 − 1.11e−16 == 10000), leaving the violation in place; the
    /// scale-aware nudge repairs it.
    /// </summary>
    [TestMethod]
    public void Test_ForceMonotonic_LargeMagnitude_Repairs()
    {
        // Arrange — flow-scale hazards with a tie and a crossing.
        var opd = new OrderedPairedData(
            new[] { 10000d, 10000d, 9000d, 9500d }, new[] { 1000d, 1000d, 3000d, 2500d },
            true, SortOrder.Descending, true, SortOrder.Ascending);
        Assert.IsFalse(opd.IsValid);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert — strictly monotonic at every step, at full magnitude.
        for (int i = 1; i < opd.Count; i++)
        {
            Assert.IsTrue(opd[i].X < opd[i - 1].X, $"X not strictly descending at {i}.");
            Assert.IsTrue(opd[i].Y > opd[i - 1].Y, $"Y not strictly ascending at {i}.");
        }
        Assert.IsTrue(opd.IsValid);
    }

    /// <summary>Verifies an already-monotonic curve is left untouched.</summary>
    [TestMethod]
    public void Test_ForceMonotonic_ValidCurve_Unchanged()
    {
        // Arrange
        var opd = new OrderedPairedData(
            new[] { 0.1, 0.2, 0.3 }, new[] { 0.1, 0.2, 0.3 },
            true, SortOrder.Ascending, true, SortOrder.Ascending);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert — exact original ordinates.
        Assert.AreEqual(0.1, opd[0].X, 0d);
        Assert.AreEqual(0.2, opd[1].X, 0d);
        Assert.AreEqual(0.3, opd[2].X, 0d);
        Assert.AreEqual(0.2, opd[1].Y, 0d);
    }

    /// <summary>Verifies curves without a declared Y order are left untouched (v1.0 behavior).</summary>
    [TestMethod]
    public void Test_ForceMonotonic_NoDeclaredYOrder_Unchanged()
    {
        // Arrange — transform-style table: Y unordered.
        var opd = new OrderedPairedData(
            new[] { 0.1, 0.1 }, new[] { 0.5, 0.2 },
            true, SortOrder.Ascending, false, SortOrder.None);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert
        Assert.AreEqual(0.1, opd[1].X, 0d);
        Assert.AreEqual(0.2, opd[1].Y, 0d);
    }
}
