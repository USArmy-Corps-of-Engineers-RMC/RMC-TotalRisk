using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Pins the bivariate source-axis discriminator's members and values.</summary>
[TestClass]
public class BivariateSourceAxisTests
{
    /// <summary>Verifies the append-only member names and explicit values.</summary>
    [TestMethod]
    public void Test_Members_PinnedNamesAndValues()
    {
        // Assert
        CollectionAssert.AreEqual(new[] { "Primary", "Secondary" },
            Enum.GetNames(typeof(BivariateSourceAxis)));
        Assert.AreEqual(0, (int)BivariateSourceAxis.Primary);
        Assert.AreEqual(1, (int)BivariateSourceAxis.Secondary);
    }
}
