using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="FailureModeMethod"/> — the member names and declared order are
/// serialized contract and must never change.
/// </summary>
[TestClass]
public class FailureModeMethodTests
{
    /// <summary>Pins the v1.0 member names, order, and underlying values.</summary>
    [TestMethod]
    public void Test_Members_PinnedToV10Contract()
    {
        // Assert — names in declared order.
        CollectionAssert.AreEqual(
            new[] { "JointFailures", "CompetingFailures", "CommonCauseFailures", "MutuallyExclusive" },
            Enum.GetNames<FailureModeMethod>());

        // Underlying values.
        Assert.AreEqual(0, (int)FailureModeMethod.JointFailures);
        Assert.AreEqual(1, (int)FailureModeMethod.CompetingFailures);
        Assert.AreEqual(2, (int)FailureModeMethod.CommonCauseFailures);
        Assert.AreEqual(3, (int)FailureModeMethod.MutuallyExclusive);
    }
}
