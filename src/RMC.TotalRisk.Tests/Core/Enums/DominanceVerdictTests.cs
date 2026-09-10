using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Pins the dominance-verdict members: names, order, and values become append-only serialized
/// contract when the results body is serialized.
/// </summary>
[TestClass]
public class DominanceVerdictTests
{
    /// <summary>Verifies the member names, order, and values.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[]
            {
                "None", "FirstDominatesFirstOrder", "FirstDominatesSecondOrder",
                "SecondDominatesFirstOrder", "SecondDominatesSecondOrder", "Identical",
            },
            Enum.GetNames<DominanceVerdict>());
        Assert.AreEqual(0, (int)DominanceVerdict.None);
        Assert.AreEqual(1, (int)DominanceVerdict.FirstDominatesFirstOrder);
        Assert.AreEqual(2, (int)DominanceVerdict.FirstDominatesSecondOrder);
        Assert.AreEqual(3, (int)DominanceVerdict.SecondDominatesFirstOrder);
        Assert.AreEqual(4, (int)DominanceVerdict.SecondDominatesSecondOrder);
        Assert.AreEqual(5, (int)DominanceVerdict.Identical);
    }
}
