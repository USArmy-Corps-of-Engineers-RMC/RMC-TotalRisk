using System;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Tests.Core.Enums;

/// <summary>
/// Unit tests for the risk-function type discriminators — <see cref="HazardFunctionType"/>,
/// <see cref="TransformFunctionType"/>, <see cref="ResponseFunctionType"/>, and
/// <see cref="ConsequenceFunctionType"/>. Pins the members and asserts the two contracts that make
/// them safe: every concrete function reports its own kind, and the discriminator never reaches the
/// serialized form (so it can never move a canonical hash or a Monte Carlo seed).
/// </summary>
[TestClass]
public class FunctionTypeDiscriminatorTests
{
    /// <summary>Pins the declared members of every function-type discriminator.</summary>
    [TestMethod]
    public void Test_Members_Pinned()
    {
        // Assert
        CollectionAssert.AreEqual(
            new[] { "Tabular", "ParametricUnivariate", "Nonparametric" },
            Enum.GetNames<HazardFunctionType>());
        CollectionAssert.AreEqual(
            new[] { "Tabular", "Linear", "Power" },
            Enum.GetNames<TransformFunctionType>());
        CollectionAssert.AreEqual(
            new[] { "Tabular", "Parametric", "NonFail" },
            Enum.GetNames<ResponseFunctionType>());
        CollectionAssert.AreEqual(
            new[] { "Tabular", "Parametric", "Composite" },
            Enum.GetNames<ConsequenceFunctionType>());
    }

    /// <summary>Every concrete function reports its own kind.</summary>
    [TestMethod]
    public void Test_ConcreteFunctions_ReportTheirOwnType()
    {
        // Assert
        Assert.AreEqual(HazardFunctionType.Tabular, new TabularHazard().FunctionType);
        Assert.AreEqual(HazardFunctionType.ParametricUnivariate, new ParametricUnivariateHazard().FunctionType);
        Assert.AreEqual(HazardFunctionType.Nonparametric, new NonparametricHazard().FunctionType);
        Assert.AreEqual(TransformFunctionType.Tabular, new TabularTransform().FunctionType);
        Assert.AreEqual(TransformFunctionType.Linear, new LinearTransform().FunctionType);
        Assert.AreEqual(TransformFunctionType.Power, new PowerTransform().FunctionType);
        Assert.AreEqual(ResponseFunctionType.Tabular, new TabularResponse().FunctionType);
        Assert.AreEqual(ResponseFunctionType.Parametric, new ParametricResponse().FunctionType);
        Assert.AreEqual(ResponseFunctionType.NonFail, new NonFailResponse().FunctionType);
        Assert.AreEqual(ConsequenceFunctionType.Tabular, new TabularConsequence().FunctionType);
        Assert.AreEqual(ConsequenceFunctionType.Parametric, new ParametricConsequence().FunctionType);
        Assert.AreEqual(ConsequenceFunctionType.Composite, new CompositeConsequence().FunctionType);
    }

    /// <summary>
    /// The discriminator is a runtime concept only: it appears in no serialized attribute, so the
    /// canonical-hash identity surface is untouched by its existence.
    /// </summary>
    [TestMethod]
    public void Test_FunctionType_IsNotSerialized()
    {
        // Arrange
        XElement[] serialized =
        [
            new TabularHazard().ToXElement(),
            new ParametricUnivariateHazard().ToXElement(),
            new NonparametricHazard().ToXElement(),
            new TabularTransform().ToXElement(),
            new LinearTransform().ToXElement(),
            new PowerTransform().ToXElement(),
            new TabularResponse().ToXElement(),
            new ParametricResponse().ToXElement(),
            new NonFailResponse().ToXElement(),
            new TabularConsequence().ToXElement(),
            new ParametricConsequence().ToXElement(),
            new CompositeConsequence().ToXElement(),
        ];

        // Assert
        foreach (var element in serialized)
        {
            Assert.IsNull(
                element.Attribute("FunctionType"),
                $"{element.Name.LocalName} must not serialize its FunctionType discriminator.");
        }
    }
}
