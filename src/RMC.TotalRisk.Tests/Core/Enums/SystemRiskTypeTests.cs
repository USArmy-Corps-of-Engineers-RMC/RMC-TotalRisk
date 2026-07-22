using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="SystemRiskType"/> — the system risk aggregation method. Type and
/// member names are preserved verbatim from v1.0 and pinned here because the enum serializes as a
/// string on the analysis options and participates in the options canonical hash.
/// </summary>
[TestClass]
public class SystemRiskTypeTests
{
    /// <summary>Pins the members, their order, and the v1.0 additive default ordinal.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "AdditiveRiskMethod", "JointRiskMethod" },
            Enum.GetNames<SystemRiskType>());

        // Additive is the zero value, matching the v1.0 default system risk method.
        Assert.AreEqual(SystemRiskType.AdditiveRiskMethod, default(SystemRiskType));
        Assert.AreEqual(1, (int)SystemRiskType.JointRiskMethod);
    }
}
