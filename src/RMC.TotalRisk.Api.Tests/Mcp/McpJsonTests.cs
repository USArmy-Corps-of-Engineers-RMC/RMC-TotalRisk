using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Mcp;
using RMC.TotalRisk.Api.Tests.Support;

namespace RMC.TotalRisk.Api.Tests.Mcp;

/// <summary>
/// Tests pinning the MCP serializer to the REST wire contract.
/// </summary>
[TestClass]
public class McpJsonTests
{
    /// <summary>The MCP options serialize identically to the test mirror of the REST options.</summary>
    [TestMethod]
    public void Test_Serialize_MatchesRestWireContract()
    {
        // Arrange
        var request = TestRequests.DamScreening();

        // Act
        string mcp = McpJson.Serialize(request);
        string rest = TestJson.Serialize(request);

        // Assert
        Assert.AreEqual(rest, mcp);
        StringAssert.Contains(mcp, "\"compositeMixture\"");
        StringAssert.Contains(mcp, "\"exceedanceProbabilities\"");
    }

    /// <summary>NaN serializes as the named JSON literal, never as a crash.</summary>
    [TestMethod]
    public void Test_Serialize_NaN_UsesNamedLiteral()
    {
        // Arrange
        var stats = new CurveStatsDto { Mean = double.NaN };

        // Act
        string json = McpJson.Serialize(stats);

        // Assert
        StringAssert.Contains(json, "\"NaN\"");
    }

    /// <summary>Null-valued optional fields are omitted from the wire.</summary>
    [TestMethod]
    public void Test_Serialize_NullsOmitted()
    {
        // Arrange
        var response = new ValidateRiskAnalysisResponse { IsValid = true };

        // Act
        string json = McpJson.Serialize(response);

        // Assert
        Assert.IsFalse(json.Contains("errorMessage"), json);
        Assert.IsFalse(json.Contains("validationIssues"), json);
    }
}
