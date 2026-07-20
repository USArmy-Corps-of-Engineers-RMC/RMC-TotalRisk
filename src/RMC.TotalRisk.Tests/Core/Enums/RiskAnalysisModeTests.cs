using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="RiskAnalysisMode"/> — reliability is a mode of the one risk analysis,
/// not a second analysis type, so the graph traversal has a single implementation.
/// </summary>
[TestClass]
public class RiskAnalysisModeTests
{
    /// <summary>Pins the members and the full-risk default ordinal.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "Risk", "Reliability" },
            Enum.GetNames<RiskAnalysisMode>());

        // Full risk is the zero value, so a default-initialized options object runs a full analysis.
        Assert.AreEqual(RiskAnalysisMode.Risk, default(RiskAnalysisMode));
        Assert.AreEqual(1, (int)RiskAnalysisMode.Reliability);
    }
}
