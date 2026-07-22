using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="RiskIntegrand"/> — the adaptive-refinement objective. Member names and
/// order are pinned because the enum serializes as a string on the analysis options and
/// participates in the options canonical hash.
/// </summary>
[TestClass]
public class RiskIntegrandTests
{
    /// <summary>Pins the members, their order, and the v1.0-equivalent default ordinal.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[]
            {
                "MeanTotalRisk",
                "MeanIncrementalRisk",
                "TotalProbabilityOfFailure",
                "TailConditionalRisk",
                "ThresholdExceedanceProbability",
                "SecondMoment",
                "Balanced",
            },
            Enum.GetNames<RiskIntegrand>());

        // The zero value is the v1.0-equivalent objective, so a default-initialized options
        // object reproduces legacy point placement.
        Assert.AreEqual(RiskIntegrand.MeanTotalRisk, default(RiskIntegrand));
        Assert.AreEqual(6, (int)RiskIntegrand.Balanced);
    }
}
