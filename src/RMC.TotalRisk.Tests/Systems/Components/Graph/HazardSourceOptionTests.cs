using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="HazardSourceOption"/> — the immutable discoverability record.
/// </summary>
[TestClass]
public class HazardSourceOptionTests
{
    /// <summary>Verifies positional capture and record value equality.</summary>
    [TestMethod]
    public void Test_Record_ValuesAndEquality()
    {
        // Arrange
        var element = new HazardElement("Hazard");

        // Act
        var option = new HazardSourceOption(element, 0, 0, "Flow", "cfs");

        // Assert
        Assert.AreSame(element, option.Element);
        Assert.AreEqual(0, option.OutputPort);
        Assert.AreEqual(0, option.ChainPosition);
        Assert.AreEqual("Flow", option.HazardLabel);
        Assert.AreEqual("cfs", option.HazardUnit);
        Assert.AreEqual(option, new HazardSourceOption(element, 0, 0, "Flow", "cfs"));
        Assert.AreNotEqual(option, new HazardSourceOption(element, 0, 1, "Stage", "ft"));
    }
}
