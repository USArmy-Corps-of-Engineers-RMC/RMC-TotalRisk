using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.Controllers;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Services;

namespace RMC.TotalRisk.Api.Tests.Controllers;

/// <summary>
/// In-process tests for the metadata controller.
/// </summary>
[TestClass]
public class MetadataControllerTests
{
    /// <summary>The metadata endpoint returns the discovery document.</summary>
    [TestMethod]
    public async Task Test_Get_ReturnsMetadata()
    {
        // Arrange
        var controller = new MetadataController(NullLogger<MetadataController>.Instance,
            new MetadataService(Options.Create(new ApiOptions())));

        // Act
        var actionResult = await controller.Get();
        var objectResult = (ObjectResult)actionResult.Result!;
        var body = (MetadataResponse)objectResult.Value!;

        // Assert
        Assert.AreEqual(200, objectResult.StatusCode);
        Assert.IsTrue(body.Success);
        Assert.IsTrue(body.Enums.ContainsKey("failureModeMethod"));
        Assert.IsTrue(body.Conventions.Count > 0);
    }
}
