using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.FaultTrees;

/// <summary>Pins the common-cause failure model discriminator's members and values.</summary>
[TestClass]
public class FaultTreeCcfModelTests
{
    /// <summary>Verifies the append-only member names and explicit values.</summary>
    [TestMethod]
    public void Test_Members_PinnedNamesAndValues()
    {
        // Assert
        CollectionAssert.AreEqual(new[] { "BetaFactor", "MultipleGreekLetter", "AlphaFactor" },
            Enum.GetNames(typeof(FaultTreeCcfModel)));
        Assert.AreEqual(0, (int)FaultTreeCcfModel.BetaFactor);
        Assert.AreEqual(1, (int)FaultTreeCcfModel.MultipleGreekLetter);
        Assert.AreEqual(2, (int)FaultTreeCcfModel.AlphaFactor);
    }
}
