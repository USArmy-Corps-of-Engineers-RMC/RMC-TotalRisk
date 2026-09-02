using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="CompositeFunctionType"/> — pins the members and their order (the
/// legacy v1.0 <c>CompositeType</c> nested enum, serialized by name).
/// </summary>
[TestClass]
public class CompositeFunctionTypeTests
{
    /// <summary>Pins the declared members and order (EpistemicMixture appended last).</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "Additive", "Average", "Mixture", "EpistemicMixture" },
            Enum.GetNames<CompositeFunctionType>());
        Assert.AreEqual(0, (int)CompositeFunctionType.Additive);
        Assert.AreEqual(1, (int)CompositeFunctionType.Average);
        Assert.AreEqual(2, (int)CompositeFunctionType.Mixture);
        Assert.AreEqual(3, (int)CompositeFunctionType.EpistemicMixture);
    }
}
