using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using RMC.TotalRisk.Models.Support;

namespace RMC.TotalRisk.Tests.Models.Support;

/// <summary>
/// Unit tests for <see cref="FunctionHelpers.ForceMonotonic(OrderedPairedData)"/> — the four
/// sort-order branches of the epsilon-nudge repair, ported exactly from v1.0.
/// </summary>
/// <remarks>
/// Reference behavior discovered during the port and pinned here: the repair writes through the
/// <see cref="OrderedPairedData"/> indexer, whose setter ignores assignments that are
/// tolerance-equal to the old ordinate (<see cref="Ordinate"/> equality treats |Δ| ≤ machine
/// epsilon as equal). A genuine curve CROSSING (violation larger than machine epsilon) is
/// therefore clamped to one epsilon past the predecessor, while an exact TIE is left in place —
/// identical to v1.0, which ran against the same Numerics semantics.
/// </remarks>
[TestClass]
public class FunctionHelpersTests
{
    /// <summary>Verifies the ascending-X / ascending-Y branch clamps a crossing to just past the predecessor.</summary>
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

    /// <summary>Verifies the ascending-X / descending-Y branch clamps X upward and Y downward.</summary>
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
    /// Pins the discovered v1.0 nuance: an exact tie nudges by exactly machine epsilon, which the
    /// tolerance-equal indexer discards — the tie survives the repair pass unchanged.
    /// </summary>
    [TestMethod]
    public void Test_ForceMonotonic_ExactTie_IsLeftInPlace()
    {
        // Arrange
        var opd = new OrderedPairedData(
            new[] { 0.1, 0.1, 0.3 }, new[] { 0.2, 0.2, 0.4 },
            true, SortOrder.Ascending, true, SortOrder.Ascending);
        Assert.IsFalse(opd.IsValid);

        // Act
        FunctionHelpers.ForceMonotonic(opd);

        // Assert — tolerance-equality in the ordinate setter swallowed the one-epsilon nudge.
        Assert.AreEqual(0.1, opd[1].X, 0d);
        Assert.AreEqual(0.2, opd[1].Y, 0d);
        Assert.IsFalse(opd.IsValid);
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
}
