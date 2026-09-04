using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Tests.Support;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.Tests.DTOs;

/// <summary>
/// Wire round-trip tests for the request/response DTO graph.
/// </summary>
[TestClass]
public class DtoSerializationTests
{
    /// <summary>The full example request survives a wire round trip.</summary>
    [TestMethod]
    public void Test_ExampleRequest_RoundTrips()
    {
        // Arrange
        var request = TestRequests.DamScreening();

        // Act
        var roundTripped = TestJson.Roundtrip(request)!;

        // Assert
        Assert.AreEqual(request.Name, roundTripped.Name);
        Assert.AreEqual(1, roundTripped.Components.Count);
        Assert.AreEqual(3, roundTripped.Components[0].FailureModes.Count);
        Assert.AreEqual(2, roundTripped.Components[0].FailureModes[0].Consequences[0].Branches!.Count);
        Assert.AreEqual(request.Components[0].Hazard.ExceedanceProbabilities[3],
            roundTripped.Components[0].Hazard.ExceedanceProbabilities[3]);
        Assert.AreEqual(0.58, roundTripped.Components[0].NonFailConsequences[0].Branches![0].Weight);
    }

    /// <summary>Enums ride the wire as camelCase strings.</summary>
    [TestMethod]
    public void Test_Enums_SerializeCamelCase()
    {
        // Arrange
        var component = new ComponentDto
        {
            Name = "Dam",
            FailureModeMethod = FailureModeMethod.MutuallyExclusive,
            JointConsequences = JointConsequenceType.Maximum,
        };

        // Act
        string json = TestJson.Serialize(component);

        // Assert
        StringAssert.Contains(json, "\"mutuallyExclusive\"");
        StringAssert.Contains(json, "\"maximum\"");
    }

    /// <summary>Unknown enum strings fail deserialization loudly instead of defaulting.</summary>
    [TestMethod]
    public void Test_UnknownEnumString_Throws()
    {
        // Arrange
        string json = "{\"name\":\"Dam\",\"failureModeMethod\":\"bogusMethod\"}";

        // Act & Assert
        Assert.ThrowsException<System.Text.Json.JsonException>(
            () => TestJson.Deserialize<ComponentDto>(json));
    }

    /// <summary>The validation-issue DTO carries its severity as a camelCase string.</summary>
    [TestMethod]
    public void Test_ValidationIssue_SeverityCamelCase()
    {
        // Arrange
        var issue = ValidationIssueDto.ApiError("API_TEST", "message", "path");

        // Act
        string json = TestJson.Serialize(issue);

        // Assert
        StringAssert.Contains(json, "\"error\"");
        StringAssert.Contains(json, "\"API_TEST\"");
    }

    /// <summary>Options DTO nullable fields omit cleanly and round-trip when set.</summary>
    [TestMethod]
    public void Test_OptionsDto_RoundTrips()
    {
        // Arrange
        var options = new RiskAnalysisOptionsDto
        {
            PrngSeed = 777,
            Tolerance = 1e-6,
            RiskMeasures = new List<RiskMeasureOptions> { RiskMeasureOptions.ValueAtRisk, RiskMeasureOptions.RiskProfiles },
        };

        // Act
        var roundTripped = TestJson.Roundtrip(options)!;
        string json = TestJson.Serialize(options);

        // Assert
        Assert.AreEqual(777, roundTripped.PrngSeed);
        Assert.AreEqual(1e-6, roundTripped.Tolerance);
        CollectionAssert.AreEqual(options.RiskMeasures, roundTripped.RiskMeasures!);
        Assert.IsFalse(json.Contains("maxEvaluations"), json);
        StringAssert.Contains(json, "\"valueAtRisk\"");
    }
}
