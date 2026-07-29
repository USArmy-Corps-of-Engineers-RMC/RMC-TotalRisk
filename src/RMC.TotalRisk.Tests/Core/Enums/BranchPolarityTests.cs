using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="BranchPolarity"/> — the member names and explicit values double as
/// response-element output-port indices and are serialized canonical-hash contract
/// (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §7.9).
/// </summary>
[TestClass]
public class BranchPolarityTests
{
    /// <summary>Pins the member names, order, and the explicit port-index values.</summary>
    [TestMethod]
    public void Test_Members_PinnedToContract()
    {
        // Assert — names in declared order (serialized contract: append-only).
        CollectionAssert.AreEqual(
            new[] { "Fail", "NonFail" },
            Enum.GetNames<BranchPolarity>());

        // Explicit values are the response element's output-port indices (§7.9).
        Assert.AreEqual(0, (int)BranchPolarity.Fail);
        Assert.AreEqual(1, (int)BranchPolarity.NonFail);
    }
}
