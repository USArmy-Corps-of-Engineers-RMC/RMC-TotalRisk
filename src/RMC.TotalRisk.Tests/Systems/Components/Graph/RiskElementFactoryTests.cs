using System;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="RiskElementFactory"/> — reconstruction of every concrete element
/// type and the unknown-name skip policy.
/// </summary>
[TestClass]
public class RiskElementFactoryTests
{
    /// <summary>Verifies every concrete element type reconstructs with its identity.</summary>
    [TestMethod]
    public void Test_CreateFromXElement_AllElementTypes()
    {
        // Arrange
        var elements = new IRiskElement[]
        {
            new HazardElement("Hazard"),
            new TransformElement("Rating"),
            new ResponseElement("Breach"),
            new ConsequenceElement("Damages"),
        };

        foreach (var original in elements)
        {
            // Act
            var restored = RiskElementFactory.CreateFromXElement(original.ToXElement());

            // Assert
            Assert.IsNotNull(restored, $"{original.GetType().Name} did not reconstruct.");
            Assert.AreEqual(original.GetType(), restored.GetType());
            Assert.AreEqual(original.Id, restored.Id);
            Assert.AreEqual(original.Name, restored.Name);
        }
    }

    /// <summary>Verifies unknown element names return null (graph-level skip policy).</summary>
    [TestMethod]
    public void Test_CreateFromXElement_UnknownName_ReturnsNull()
    {
        // Act / Assert
        Assert.IsNull(RiskElementFactory.CreateFromXElement(new XElement("BogusElement")));
    }

    /// <summary>Verifies null elements throw.</summary>
    [TestMethod]
    public void Test_CreateFromXElement_Null_Throws()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => RiskElementFactory.CreateFromXElement(null!));
    }
}
