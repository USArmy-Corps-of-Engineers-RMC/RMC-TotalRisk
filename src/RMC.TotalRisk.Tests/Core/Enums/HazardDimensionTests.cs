using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="HazardDimension"/> — the member names and explicit values double as
/// output-port indices and are serialized contract.
/// </summary>
[TestClass]
public class HazardDimensionTests
{
    /// <summary>Pins the member names, order, and the explicit port-index values.</summary>
    [TestMethod]
    public void Test_Members_PinnedToContract()
    {
        // Assert — names in declared order.
        CollectionAssert.AreEqual(
            new[] { "Primary", "Secondary" },
            Enum.GetNames<HazardDimension>());

        // Explicit values are port indices (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §6.5).
        Assert.AreEqual(0, (int)HazardDimension.Primary);
        Assert.AreEqual(1, (int)HazardDimension.Secondary);
    }
}
