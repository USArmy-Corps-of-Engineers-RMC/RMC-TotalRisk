using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for <see cref="RiskElementType"/> — the graph-node role discriminator. Pins the
/// members and asserts each concrete element reports its own role without serializing it.
/// </summary>
[TestClass]
public class RiskElementTypeTests
{
    /// <summary>Pins the members in compute-chain order.</summary>
    [TestMethod]
    public void Test_Members_PinnedInComputeChainOrder()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "Hazard", "Transform", "Response", "Consequence" },
            Enum.GetNames<RiskElementType>());

        Assert.AreEqual(0, (int)RiskElementType.Hazard);
        Assert.AreEqual(1, (int)RiskElementType.Transform);
        Assert.AreEqual(2, (int)RiskElementType.Response);
        Assert.AreEqual(3, (int)RiskElementType.Consequence);
    }

    /// <summary>Every concrete element reports its own role.</summary>
    [TestMethod]
    public void Test_ConcreteElements_ReportTheirOwnType()
    {
        // Assert
        Assert.AreEqual(RiskElementType.Hazard, new HazardElement().ElementType);
        Assert.AreEqual(RiskElementType.Transform, new TransformElement().ElementType);
        Assert.AreEqual(RiskElementType.Response, new ResponseElement().ElementType);
        Assert.AreEqual(RiskElementType.Consequence, new ConsequenceElement().ElementType);
    }

    /// <summary>
    /// The role discriminator is never serialized — element XML is the persistence surface only,
    /// and its local name already carries the concrete type.
    /// </summary>
    [TestMethod]
    public void Test_ElementType_IsNotSerialized()
    {
        // Arrange
        var elements = new RiskElementBase[]
        {
            new HazardElement(),
            new TransformElement(),
            new ResponseElement(),
            new ConsequenceElement(),
        };

        // Assert
        foreach (var element in elements)
        {
            Assert.IsNull(
                element.ToXElement().Attribute("ElementType"),
                $"{element.GetType().Name} must not serialize its ElementType discriminator.");
        }
    }
}
