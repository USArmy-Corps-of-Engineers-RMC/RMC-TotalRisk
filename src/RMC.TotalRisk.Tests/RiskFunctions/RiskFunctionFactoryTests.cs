using System;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Responses.EventTrees;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Tests.RiskFunctions;

/// <summary>
/// Unit tests for <see cref="RiskFunctionFactory"/> — reconstruction of every concrete function
/// type, the unknown-name null policy, and the typed cluster filters.
/// </summary>
[TestClass]
public class RiskFunctionFactoryTests
{
    /// <summary>Verifies every concrete type reconstructs to the same type and canonical hash.</summary>
    [TestMethod]
    public void Test_CreateFromXElement_AllConcreteTypes_RoundTripHash()
    {
        // Arrange — one default instance per concrete function type.
        var functions = new IRiskFunction[]
        {
            new TabularHazard(),
            new ParametricUnivariateHazard(),
            new NonparametricHazard(),
            new BivariateHazard(),
            new TabularTransform(),
            new LinearTransform(),
            new PowerTransform(),
            new BivariateTransform(),
            new TabularResponse(),
            new ParametricResponse(),
            new NonFailResponse(),
            new EventTreeResponse(),
            new FaultTreeResponse(),
            new TabularConsequence(),
            new ParametricConsequence(),
            new CompositeConsequence(),
            new BivariateConsequence(),
        };

        foreach (var original in functions)
        {
            // Act
            var restored = RiskFunctionFactory.CreateFromXElement(original.ToXElement());

            // Assert
            Assert.IsNotNull(restored, $"{original.GetType().Name} did not reconstruct.");
            Assert.AreEqual(original.GetType(), restored.GetType());
            CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash(),
                $"{original.GetType().Name} round-trip must preserve the canonical hash.");
        }
    }

    /// <summary>Verifies unknown element names return null (caller-selected failure policy).</summary>
    [TestMethod]
    public void Test_CreateFromXElement_UnknownName_ReturnsNull()
    {
        // Act / Assert
        Assert.IsNull(RiskFunctionFactory.CreateFromXElement(new XElement("BogusFunction")));
    }

    /// <summary>
    /// Verifies the resolver-aware overloads read self-contained forms identically with or
    /// without a resolver (leaf types ignore it; null means self-contained).
    /// </summary>
    [TestMethod]
    public void Test_CreateFromXElement_ResolverOverload_ForwardsForSelfContained()
    {
        // Arrange
        var original = new ParametricConsequence { Name = "Life loss", Alpha = 10d };

        // Act
        var withNull = RiskFunctionFactory.CreateFromXElement(original.ToXElement(), null);
        var typed = RiskFunctionFactory.CreateConsequenceFunction(original.ToXElement(), null);

        // Assert
        Assert.IsNotNull(withNull);
        Assert.IsNotNull(typed);
        CollectionAssert.AreEqual(original.CanonicalHash(), withNull.CanonicalHash());
        CollectionAssert.AreEqual(original.CanonicalHash(), typed.CanonicalHash());
    }

    /// <summary>Verifies null elements throw.</summary>
    [TestMethod]
    public void Test_CreateFromXElement_Null_Throws()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => RiskFunctionFactory.CreateFromXElement(null!));
        Assert.ThrowsException<ArgumentNullException>(() => RiskFunctionFactory.CreateHazardFunction(null!));
    }

    /// <summary>Verifies the typed wrappers admit their cluster and reject the others.</summary>
    [TestMethod]
    public void Test_TypedWrappers_FilterByCluster()
    {
        // Arrange
        var hazardXml = new TabularHazard().ToXElement();
        var transformXml = new TabularTransform().ToXElement();
        var responseXml = new TabularResponse().ToXElement();
        var nonFailXml = new NonFailResponse().ToXElement();
        var eventTreeXml = new EventTreeResponse().ToXElement();
        var consequenceXml = new TabularConsequence().ToXElement();
        var parametricConsequenceXml = new ParametricConsequence().ToXElement();

        // Assert — matches reconstruct.
        Assert.IsNotNull(RiskFunctionFactory.CreateHazardFunction(hazardXml));
        Assert.IsNotNull(RiskFunctionFactory.CreateHazardFunction(new BivariateHazard().ToXElement()));
        Assert.IsNull(RiskFunctionFactory.CreateResponseFunction(new BivariateHazard().ToXElement()));
        Assert.IsNotNull(RiskFunctionFactory.CreateTransformFunction(transformXml));
        Assert.IsNotNull(RiskFunctionFactory.CreateTransformFunction(new LinearTransform().ToXElement()));
        Assert.IsNotNull(RiskFunctionFactory.CreateTransformFunction(new PowerTransform().ToXElement()));
        Assert.IsNotNull(RiskFunctionFactory.CreateTransformFunction(new BivariateTransform().ToXElement()));
        Assert.IsNull(RiskFunctionFactory.CreateConsequenceFunction(new BivariateTransform().ToXElement()));
        Assert.IsNotNull(RiskFunctionFactory.CreateResponseFunction(responseXml));
        Assert.IsNotNull(RiskFunctionFactory.CreateResponseFunction(nonFailXml));
        Assert.IsNotNull(RiskFunctionFactory.CreateResponseFunction(eventTreeXml));
        Assert.IsNotNull(RiskFunctionFactory.CreateConsequenceFunction(consequenceXml));
        Assert.IsNotNull(RiskFunctionFactory.CreateConsequenceFunction(parametricConsequenceXml));
        Assert.IsNull(RiskFunctionFactory.CreateResponseFunction(parametricConsequenceXml));
        Assert.IsNotNull(RiskFunctionFactory.CreateConsequenceFunction(new CompositeConsequence().ToXElement()));
        Assert.IsNull(RiskFunctionFactory.CreateHazardFunction(new CompositeConsequence().ToXElement()));
        Assert.IsNotNull(RiskFunctionFactory.CreateConsequenceFunction(new BivariateConsequence().ToXElement()));
        Assert.IsNull(RiskFunctionFactory.CreateTransformFunction(new BivariateConsequence().ToXElement()));

        // Cross-cluster mismatches return null.
        Assert.IsNull(RiskFunctionFactory.CreateHazardFunction(transformXml));
        Assert.IsNull(RiskFunctionFactory.CreateTransformFunction(hazardXml));
        Assert.IsNull(RiskFunctionFactory.CreateResponseFunction(consequenceXml));
        Assert.IsNull(RiskFunctionFactory.CreateConsequenceFunction(responseXml));
    }
}
