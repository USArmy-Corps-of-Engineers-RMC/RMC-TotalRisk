using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Tests the alternative-economics row: guards and the per-slot echo.
/// </summary>
[TestClass]
public class AlternativeEconomicsTests
{
    /// <summary>Builds a row with distinct values in every slot.</summary>
    /// <param name="name">The row name.</param>
    /// <returns>The row.</returns>
    private static AlternativeEconomics Build(string name = "Gate fix")
    {
        return new AlternativeEconomics(name, "Fix the gate", isBaseline: false,
            1d, 2d, 3d, 6d, 0.5d, 7d,
            10d, 9d, 4d, 0.4d, 1.67d,
            9.5d, 8.5d, 3.5d, 0.35d, 1.58d,
            1e-3, 5e-4, 2e-3, 1e-3, 0.002d);
    }

    /// <summary>Verifies null names are refused.</summary>
    [TestMethod]
    public void Test_Ctor_NullArguments_Throw()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => new AlternativeEconomics(null!, "", false,
            0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d));
        Assert.ThrowsException<ArgumentNullException>(() => new AlternativeEconomics("Baseline", null!, true,
            0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d));
    }

    /// <summary>Verifies every slot echoes its own value.</summary>
    [TestMethod]
    public void Test_Ctor_PropertiesEchoed()
    {
        // Act
        var row = Build();

        // Assert
        Assert.AreEqual("Gate fix", row.Name);
        Assert.AreEqual("Fix the gate", row.Description);
        Assert.IsFalse(row.IsBaseline);
        Assert.AreEqual(1d, row.CapitalPresentValue);
        Assert.AreEqual(2d, row.OperationsAndMaintenancePresentValue);
        Assert.AreEqual(3d, row.OperatingChangePresentValue);
        Assert.AreEqual(6d, row.TotalCostPresentValue);
        Assert.AreEqual(0.5d, row.EquivalentAnnualCost);
        Assert.AreEqual(7d, row.CumulativeCost);
        Assert.AreEqual(10d, row.MonetizedPresentValueBenefit);
        Assert.AreEqual(9d, row.EconomicPresentValueBenefit);
        Assert.AreEqual(4d, row.NetPresentValue);
        Assert.AreEqual(0.4d, row.NetAnnualBenefit);
        Assert.AreEqual(1.67d, row.BenefitCostRatio);
        Assert.AreEqual(9.5d, row.AbsorbingMonetizedPresentValueBenefit);
        Assert.AreEqual(8.5d, row.AbsorbingEconomicPresentValueBenefit);
        Assert.AreEqual(3.5d, row.AbsorbingNetPresentValue);
        Assert.AreEqual(0.35d, row.AbsorbingNetAnnualBenefit);
        Assert.AreEqual(1.58d, row.AbsorbingBenefitCostRatio);
        Assert.AreEqual(1e-3, row.AnnualizedFailureProbability);
        Assert.AreEqual(5e-4, row.AnnualizedFailureProbabilityReduction);
        Assert.AreEqual(2e-3, row.YearZeroFailureProbability);
        Assert.AreEqual(1e-3, row.YearZeroFailureProbabilityReduction);
        Assert.AreEqual(0.002d, row.LivesSavedEquivalentAnnual);
    }
}
