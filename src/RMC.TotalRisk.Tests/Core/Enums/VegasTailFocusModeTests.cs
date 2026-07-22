using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="VegasTailFocusMode"/> — the joint-method VEGAS tail-focus selector.
/// Member names and order are pinned because the enum serializes as a string on the analysis
/// options and participates in the options canonical hash.
/// </summary>
[TestClass]
public class VegasTailFocusModeTests
{
    /// <summary>
    /// Pins the members and their order. The options default is <see cref="VegasTailFocusMode.Automatic"/>
    /// (set explicitly by the options type); the enum zero value is <see cref="VegasTailFocusMode.None"/>,
    /// the v1.0-identical identity transform.
    /// </summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "None", "Automatic", "Manual" },
            Enum.GetNames<VegasTailFocusMode>());

        Assert.AreEqual(VegasTailFocusMode.None, default(VegasTailFocusMode));
        Assert.AreEqual(1, (int)VegasTailFocusMode.Automatic);
        Assert.AreEqual(2, (int)VegasTailFocusMode.Manual);
    }
}
