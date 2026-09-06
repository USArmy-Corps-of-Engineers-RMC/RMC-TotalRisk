using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Tests the life-cycle trajectory root: guards, the per-type alignment rule, echoes, and the
/// defensive snapshots.
/// </summary>
[TestClass]
public class LifeCycleRiskResultsTests
{
    /// <summary>Builds a one-epoch trajectory with the given per-type list length.</summary>
    /// <param name="typeCount">The declared consequence-type count.</param>
    /// <param name="unitCount">The unit-list length (defaults to the type count).</param>
    /// <returns>The results.</returns>
    private static LifeCycleRiskResults Build(int typeCount, int? unitCount = null)
    {
        var labels = new string[typeCount];
        var units = new string[unitCount ?? typeCount];
        for (int i = 0; i < labels.Length; i++) labels[i] = $"Type {i}";
        for (int i = 0; i < units.Length; i++) units[i] = "$";
        var perType = new double[typeCount];
        var epoch = new LifeCycleEpochRisk(0, 10, 0d, 0.4d,
            new LifeCycleEpochEntry("System", 0.05d, perType),
            Array.Empty<LifeCycleEpochEntry>(), Array.Empty<string>());
        return new LifeCycleRiskResults(10, 0.035d, labels, units, new[] { epoch },
            0.4d, perType, perType, perType, perType, perType, new[] { "Year 0: fixed" });
    }

    /// <summary>Verifies null lists are refused.</summary>
    [TestMethod]
    public void Test_Ctor_NullArguments_Throw()
    {
        // Arrange
        var perType = new[] { 0d };
        var epochs = Array.Empty<LifeCycleEpochRisk>();

        // Act / Assert — one representative per list argument.
        Assert.ThrowsException<ArgumentNullException>(() => new LifeCycleRiskResults(
            10, 0d, null!, new[] { "$" }, epochs, 0d, perType, perType, perType, perType, perType,
            Array.Empty<string>()));
        Assert.ThrowsException<ArgumentNullException>(() => new LifeCycleRiskResults(
            10, 0d, new[] { "Damages" }, new[] { "$" }, null!, 0d, perType, perType, perType,
            perType, perType, Array.Empty<string>()));
        Assert.ThrowsException<ArgumentNullException>(() => new LifeCycleRiskResults(
            10, 0d, new[] { "Damages" }, new[] { "$" }, epochs, 0d, perType, perType, perType,
            perType, perType, null!));
    }

    /// <summary>Verifies label/unit and per-type list misalignments are refused.</summary>
    [TestMethod]
    public void Test_Ctor_Misalignment_Throws()
    {
        // Act / Assert — unit list shorter than the labels.
        Assert.ThrowsException<ArgumentException>(() => Build(2, unitCount: 1));

        // A per-type aggregate list of the wrong length.
        var epoch = new LifeCycleEpochRisk(0, 10, 0d, 0.4d,
            new LifeCycleEpochEntry("System", 0.05d, new[] { 0d }),
            Array.Empty<LifeCycleEpochEntry>(), Array.Empty<string>());
        Assert.ThrowsException<ArgumentException>(() => new LifeCycleRiskResults(
            10, 0d, new[] { "Damages" }, new[] { "$" }, new[] { epoch }, 0.4d,
            new[] { 0d, 0d }, new[] { 0d }, new[] { 0d }, new[] { 0d }, new[] { 0d },
            Array.Empty<string>()));
    }

    /// <summary>Verifies the properties echo the arguments.</summary>
    [TestMethod]
    public void Test_Ctor_PropertiesEchoed()
    {
        // Act
        var results = Build(2);

        // Assert
        Assert.AreEqual(10, results.PeriodYears);
        Assert.AreEqual(0.035d, results.DiscountRate);
        Assert.AreEqual(2, results.ConsequenceLabels.Count);
        Assert.AreEqual(2, results.ConsequenceUnits.Count);
        Assert.AreEqual(1, results.Epochs.Count);
        Assert.AreEqual(0.4d, results.FailureProbabilityByHorizon);
        Assert.AreEqual(2, results.CumulativeExpectedConsequences.Count);
        Assert.AreEqual(2, results.PresentValueOfExpectedConsequences.Count);
        Assert.AreEqual(2, results.EquivalentAnnualConsequences.Count);
        Assert.AreEqual(2, results.AbsorbingCumulativeExpectedConsequences.Count);
        Assert.AreEqual(2, results.AbsorbingPresentValueOfExpectedConsequences.Count);
        Assert.AreEqual(1, results.AppliedInterventions.Count);
    }

    /// <summary>Verifies the stored lists are defensive snapshots.</summary>
    [TestMethod]
    public void Test_Ctor_Snapshots_InputMutationInert()
    {
        // Arrange
        var labels = new List<string> { "Damages" };
        var units = new List<string> { "$" };
        var perType = new double[] { 0d };
        var epoch = new LifeCycleEpochRisk(0, 10, 0d, 0.4d,
            new LifeCycleEpochEntry("System", 0.05d, perType),
            Array.Empty<LifeCycleEpochEntry>(), Array.Empty<string>());
        var interventions = new List<string> { "Year 0: fixed" };
        var results = new LifeCycleRiskResults(10, 0d, labels, units, new[] { epoch },
            0.4d, perType, perType, perType, perType, perType, interventions);

        // Act
        labels.Add("Life Loss");
        interventions.Clear();

        // Assert
        Assert.AreEqual(1, results.ConsequenceLabels.Count);
        Assert.AreEqual(1, results.AppliedInterventions.Count);
    }
}
