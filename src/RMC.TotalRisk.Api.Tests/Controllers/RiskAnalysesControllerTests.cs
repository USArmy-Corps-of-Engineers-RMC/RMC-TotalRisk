using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.Controllers;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Services;
using RMC.TotalRisk.Api.Tests.Support;

namespace RMC.TotalRisk.Api.Tests.Controllers;

/// <summary>
/// In-process tests for the compute controller (no HTTP host).
/// </summary>
[TestClass]
public class RiskAnalysesControllerTests
{
    /// <summary>Builds the controller over a fresh service.</summary>
    /// <returns>The controller.</returns>
    private static RiskAnalysesController CreateController()
    {
        var service = new RiskAnalysisComputeService(Options.Create(new ApiOptions()));
        return new RiskAnalysesController(NullLogger<RiskAnalysesController>.Instance, service);
    }

    /// <summary>Unwraps an action result into (status, body).</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="actionResult">The action result.</param>
    /// <returns>The status code and typed body.</returns>
    private static (int StatusCode, TResponse Body) Unwrap<TResponse>(ActionResult<TResponse> actionResult)
        where TResponse : ResponseBase
    {
        var objectResult = (ObjectResult)actionResult.Result!;
        return (objectResult.StatusCode!.Value, (TResponse)objectResult.Value!);
    }

    /// <summary>Compute returns 200 with populated results for the example.</summary>
    [TestMethod]
    public async Task Test_Compute_Example_Returns200()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var (status, body) = Unwrap(await controller.Compute(TestRequests.DamScreening(), CancellationToken.None));

        // Assert
        Assert.AreEqual(200, status);
        Assert.IsTrue(body.Success);
        Assert.IsNotNull(body.Results);
        Assert.IsNotNull(body.ComputationTimeMs);
    }

    /// <summary>Compute maps a validation failure to 400 with the structured issues.</summary>
    [TestMethod]
    public async Task Test_Compute_Invalid_Returns400()
    {
        // Arrange
        var controller = CreateController();
        var request = TestRequests.DamScreening();
        request.Components[0].Hazard.HazardValues[0] = request.Components[0].Hazard.HazardValues[1];

        // Act
        var (status, body) = Unwrap(await controller.Compute(request, CancellationToken.None));

        // Assert
        Assert.AreEqual(400, status);
        Assert.IsFalse(body.Success);
        Assert.IsTrue(body.ValidationIssues!.Any(i => i.Code == "API_TABLE_ORDER"));
        Assert.IsNull(body.Results);
    }

    /// <summary>Validate returns 200 with the verdict for both valid and invalid payloads.</summary>
    [TestMethod]
    public async Task Test_Validate_Returns200Verdict()
    {
        // Arrange
        var controller = CreateController();
        var invalid = TestRequests.DamScreening();
        invalid.SpecifiedConsequence = string.Empty;

        // Act
        var (validStatus, validBody) = Unwrap(await controller.Validate(TestRequests.DamScreening()));
        var (invalidStatus, invalidBody) = Unwrap(await controller.Validate(invalid));

        // Assert
        Assert.AreEqual(200, validStatus);
        Assert.IsTrue(validBody.IsValid);
        Assert.AreEqual(200, invalidStatus);
        Assert.IsFalse(invalidBody.IsValid);
        Assert.IsTrue(invalidBody.ValidationIssues!.Any(i => i.Code == "API_LABEL_REQUIRED"));
    }

    /// <summary>The example endpoint returns the computable template.</summary>
    [TestMethod]
    public void Test_Example_ReturnsTemplate()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = controller.Example();
        var body = (ComputeRiskAnalysisRequest)((OkObjectResult)result.Result!).Value!;

        // Assert
        Assert.AreEqual("Example Dam Screening", body.Name);
        Assert.AreEqual(3, body.Components[0].FailureModes.Count);
    }
}
