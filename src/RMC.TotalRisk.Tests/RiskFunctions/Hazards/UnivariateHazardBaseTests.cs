using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Hazards;

namespace RMC.TotalRisk.Tests.RiskFunctions.Hazards;

/// <summary>Unit tests for <see cref="UnivariateHazardBase"/> interface anchoring.</summary>
/// <remarks>
/// <para><b>Authors:</b> Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil</para>
/// </remarks>
[TestClass]
public class UnivariateHazardBaseTests
{
    /// <summary>Verifies univariate hazards expose both hazard contracts through the base.</summary>
    [TestMethod]
    public void Test_TabularHazard_ImplementsUnivariateContract()
    {
        UnivariateHazardBase function = new TabularHazard();
        Assert.IsInstanceOfType<IUnivariateHazardFunction>(function);
        Assert.IsInstanceOfType<HazardFunctionBase>(function);
    }
}
