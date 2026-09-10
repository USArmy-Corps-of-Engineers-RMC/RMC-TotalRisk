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

    /// <summary>A failure mode's transform chain and consequence position round-trip on the wire.</summary>
    [TestMethod]
    public void Test_TransformDto_RoundTrips()
    {
        // Arrange
        var mode = new FailureModeDto
        {
            Name = "Spillway Erosion",
            Transforms = new List<TransformFunctionDto>
            {
                new TransformFunctionDto
                {
                    Type = FunctionTypeNames.PowerTransform,
                    Name = "Weir Rating",
                    SpecifiedHazard = "Reservoir Stage",
                    HazardUnit = "ft",
                    TransformedHazard = "Spillway Discharge",
                    TransformedHazardUnit = "cfs",
                    Alpha = 150,
                    Beta = 1.5,
                    Xi = 1214,
                    IsInverse = false,
                    Minimum = 1214,
                    Maximum = 1230,
                },
                new TransformFunctionDto
                {
                    Type = FunctionTypeNames.TabularTransform,
                    TransformedHazard = "Velocity",
                    TransformedHazardUnit = "ft/s",
                    HazardValues = new List<double> { 0, 20000 },
                    TransformedHazardValues = new List<double> { 0, 35 },
                    HazardTransform = Numerics.Data.Transform.Logarithmic,
                    TransformedHazardTransform = Numerics.Data.Transform.Logarithmic,
                },
            },
            ConsequenceHazardPosition = 0,
            Response = new TabularResponseDto
            {
                HazardValues = new List<double> { 0, 35 },
                ResponseProbabilities = new List<double> { 0, 0.5 },
            },
            Consequences = new List<ConsequenceFunctionDto>
            {
                new ConsequenceFunctionDto
                {
                    HazardValues = new List<double> { 1200, 1230 },
                    ConsequenceValues = new List<double> { 0, 50 },
                },
            },
        };

        // Act
        var roundTripped = TestJson.Roundtrip(mode)!;
        string json = TestJson.Serialize(mode);

        // Assert
        Assert.AreEqual(2, roundTripped.Transforms!.Count);
        Assert.AreEqual(0, roundTripped.ConsequenceHazardPosition);
        Assert.AreEqual(150, roundTripped.Transforms[0].Alpha);
        Assert.AreEqual(1214, roundTripped.Transforms[0].Xi);
        Assert.IsFalse(roundTripped.Transforms[0].IsInverse!.Value);
        Assert.AreEqual(Numerics.Data.Transform.Logarithmic, roundTripped.Transforms[1].TransformedHazardTransform);
        StringAssert.Contains(json, "\"transforms\"");
        StringAssert.Contains(json, "\"consequenceHazardPosition\"");
        StringAssert.Contains(json, "\"powerTransform\"");
        StringAssert.Contains(json, "\"transformedHazardValues\"");
        StringAssert.Contains(json, "\"logarithmic\"");
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
