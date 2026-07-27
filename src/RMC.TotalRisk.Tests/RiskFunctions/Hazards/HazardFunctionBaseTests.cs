using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Hazards;

namespace RMC.TotalRisk.Tests.RiskFunctions.Hazards;

/// <summary>Unit tests for <see cref="HazardFunctionBase"/> cluster polymorphism.</summary>
/// <remarks>
/// <para><b>Authors:</b> Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil</para>
/// </remarks>
[TestClass]
public class HazardFunctionBaseTests
{
    /// <summary>Verifies a tabular hazard satisfies the base discriminator and bounds contract.</summary>
    [TestMethod]
    public void Test_TabularHazard_BaseContract()
    {
        HazardFunctionBase function = new TabularHazard();
        Assert.AreEqual(HazardFunctionType.Tabular, function.FunctionType);
        Assert.IsTrue(function.MinHazard(meanOnly: true) <= function.MaxHazard(meanOnly: true));
    }
}
