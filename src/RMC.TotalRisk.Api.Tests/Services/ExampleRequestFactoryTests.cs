using Microsoft.Extensions.Options;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.Services;
using RMC.TotalRisk.Api.Tests.Support;

namespace RMC.TotalRisk.Api.Tests.Services;

/// <summary>
/// Tests pinning the shipped example to the contract's promise: valid and computable as
/// returned.
/// </summary>
[TestClass]
public class ExampleRequestFactoryTests
{
    /// <summary>Every call returns a fresh, mutation-safe instance.</summary>
    [TestMethod]
    public void Test_Create_ReturnsFreshInstances()
    {
        // Act
        var first = ExampleRequestFactory.CreateDamScreeningExample();
        var second = ExampleRequestFactory.CreateDamScreeningExample();
        first.Components[0].Hazard.HazardValues[0] = -999;

        // Assert
        Assert.AreNotSame(first, second);
        Assert.AreNotEqual(first.Components[0].Hazard.HazardValues[0], second.Components[0].Hazard.HazardValues[0]);
    }

    /// <summary>The example is valid and computes end to end.</summary>
    [TestMethod]
    public async Task Test_Example_IsValidAndComputes()
    {
        // Arrange
        var service = new RiskAnalysisComputeService(Options.Create(new ApiOptions()));

        // Act
        var verdict = service.Validate(ExampleRequestFactory.CreateDamScreeningExample());
        var response = await service.ComputeAsync(ExampleRequestFactory.CreateDamScreeningExample(), CancellationToken.None);

        // Assert
        Assert.IsTrue(verdict.IsValid, TestJson.Serialize(verdict.ValidationIssues));
        Assert.IsTrue(response.Success);
        Assert.IsTrue(response.Results!.Curves.Total.Stats.Mean > 0d);
        Assert.IsTrue(response.Results.Curves.Fail.Stats.TotalProbability > 0d);
    }
}
