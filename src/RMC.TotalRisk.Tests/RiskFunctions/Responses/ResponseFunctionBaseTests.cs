using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Responses;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses;

/// <summary>Unit tests for <see cref="ResponseFunctionBase"/> capability defaults.</summary>
/// <remarks>
/// <para><b>Authors:</b> Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil</para>
/// </remarks>
[TestClass]
public class ResponseFunctionBaseTests
{
    /// <summary>Verifies tabular responses inherit ordered-curve support by default.</summary>
    [TestMethod]
    public void Test_TabularResponse_BaseCapability()
    {
        ResponseFunctionBase function = new TabularResponse();
        Assert.AreEqual(ResponseFunctionType.Tabular, function.FunctionType);
        Assert.IsTrue(function.SupportsOrderedCurveSampling);
    }
}
