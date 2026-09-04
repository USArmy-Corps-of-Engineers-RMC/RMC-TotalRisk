using System.Text.Json;
using Microsoft.Extensions.Options;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.Mcp;
using RMC.TotalRisk.Api.Services;
using RMC.TotalRisk.Api.Services.Exceptions;
using RMC.TotalRisk.Api.Tests.Support;

namespace RMC.TotalRisk.Api.Tests.Mcp;

/// <summary>
/// In-process tests for the MCP tool class (constructed directly; the JSON-RPC transport is
/// covered by the integration suite).
/// </summary>
[TestClass]
public class RiskAnalysisToolsTests
{
    /// <summary>Builds the tools over fresh services.</summary>
    /// <returns>The tools.</returns>
    private static RiskAnalysisTools CreateTools()
    {
        var options = Options.Create(new ApiOptions());
        return new RiskAnalysisTools(new RiskAnalysisComputeService(options), new MetadataService(options));
    }

    /// <summary>run_risk_analysis computes the example and returns the wire JSON.</summary>
    [TestMethod]
    public async Task Test_RunRiskAnalysis_Computes()
    {
        // Arrange
        var tools = CreateTools();

        // Act
        string json = await tools.RunRiskAnalysis(TestRequests.DamScreening());
        using var document = JsonDocument.Parse(json);

        // Assert
        Assert.IsTrue(document.RootElement.GetProperty("success").GetBoolean());
        Assert.IsTrue(document.RootElement.GetProperty("results").GetProperty("components").GetArrayLength() == 1);
        Assert.IsTrue(document.RootElement.TryGetProperty("provenance", out _));
    }

    /// <summary>validate_risk_analysis returns the structured verdict without computing.</summary>
    [TestMethod]
    public void Test_ValidateRiskAnalysis_ReportsIssues()
    {
        // Arrange
        var tools = CreateTools();
        var request = TestRequests.DamScreening();
        request.Components[0].Hazard.ExceedanceProbabilities.Reverse();

        // Act
        string json = tools.ValidateRiskAnalysis(request);
        using var document = JsonDocument.Parse(json);

        // Assert
        Assert.IsFalse(document.RootElement.GetProperty("isValid").GetBoolean());
        string issues = document.RootElement.GetProperty("validationIssues").GetRawText();
        StringAssert.Contains(issues, "API_TABLE_ORDER");
    }

    /// <summary>A validation failure propagates as a remediation-worded exception (the MCP error surface).</summary>
    [TestMethod]
    public async Task Test_RunRiskAnalysis_InvalidThrowsWithCodes()
    {
        // Arrange
        var tools = CreateTools();
        var request = TestRequests.DamScreening();
        request.Components.Clear();

        // Act
        var ex = await Assert.ThrowsExceptionAsync<RequestValidationException>(
            () => tools.RunRiskAnalysis(request));

        // Assert
        StringAssert.Contains(ex.Message, "API_COMPONENTS_REQUIRED");
    }

    /// <summary>get_metadata and get_example_request return their documents.</summary>
    [TestMethod]
    public void Test_Discovery_ReturnsDocuments()
    {
        // Arrange
        var tools = CreateTools();

        // Act
        string metadata = tools.GetMetadata();
        string example = tools.GetExampleRequest();

        // Assert
        StringAssert.Contains(metadata, "jointFailures");
        StringAssert.Contains(metadata, "conventions");
        StringAssert.Contains(example, "Example Dam Screening");
        StringAssert.Contains(example, "compositeMixture");
    }
}
