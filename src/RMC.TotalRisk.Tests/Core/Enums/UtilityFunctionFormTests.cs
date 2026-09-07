using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the utility-function form members: names, order, and values are serialized contract.
/// </summary>
[TestClass]
public class UtilityFunctionFormTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "ExponentialCara", "PowerCrra" },
            Enum.GetNames<UtilityFunctionForm>());
        Assert.AreEqual(0, (int)UtilityFunctionForm.ExponentialCara);
        Assert.AreEqual(1, (int)UtilityFunctionForm.PowerCrra);
    }
}
