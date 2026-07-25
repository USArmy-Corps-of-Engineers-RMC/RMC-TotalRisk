using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="CompositeCombinationType"/> — pins the members and their order. The
/// enum is serialized by name on <c>CompositeHazard</c> and <c>CompositeResponse</c> and is hashed
/// canonical content, so the member set is append-only contract.
/// </summary>
[TestClass]
public class CompositeCombinationTypeTests
{
    /// <summary>Pins the declared members and order.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "Mixture", "CompetingRisks" },
            Enum.GetNames<CompositeCombinationType>());
        Assert.AreEqual(0, (int)CompositeCombinationType.Mixture);
        Assert.AreEqual(1, (int)CompositeCombinationType.CompetingRisks);
    }

    /// <summary>
    /// The default is <see cref="CompositeCombinationType.Mixture"/> — the v1.0
    /// <c>IsMixture = true</c> default, preserved so a v1.0 project imports without a mode change.
    /// </summary>
    [TestMethod]
    public void Test_Default_IsMixture()
    {
        // Assert
        Assert.AreEqual(CompositeCombinationType.Mixture, default(CompositeCombinationType));
    }
}
