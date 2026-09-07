using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the do-no-harm policy members: names, order, and values are serialized contract.
/// </summary>
[TestClass]
public class DoNoHarmPolicyTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "Enforce", "WarnOnly", "Off" },
            Enum.GetNames<DoNoHarmPolicy>());
        Assert.AreEqual(0, (int)DoNoHarmPolicy.Enforce);
        Assert.AreEqual(1, (int)DoNoHarmPolicy.WarnOnly);
        Assert.AreEqual(2, (int)DoNoHarmPolicy.Off);
    }
}
