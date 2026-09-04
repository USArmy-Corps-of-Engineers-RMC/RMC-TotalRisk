using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Mappers;
using RMC.TotalRisk.Api.Tests.Support;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Consequences;

namespace RMC.TotalRisk.Api.Tests.Mappers;

/// <summary>
/// Tests for the function DTO → model mapping and its structured request-shape checks.
/// </summary>
[TestClass]
public class FunctionMapperTests
{
    /// <summary>The default host limits.</summary>
    private static readonly ApiOptions Limits = new();

    /// <summary>A valid stage-frequency hazard maps deterministically with AEP descending.</summary>
    [TestMethod]
    public void Test_Hazard_ValidTable_Maps()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0].Hazard;
        var issues = new List<ValidationIssueDto>();

        // Act
        var hazard = FunctionMapper.ToHazard(dto, "components[0].hazard", Limits, issues)!;

        // Assert
        Assert.AreEqual(0, issues.Count);
        Assert.IsTrue(hazard.IsDeterministic);
        Assert.AreEqual(FunctionUncertainty.None, hazard.UncertaintyValue);
        Assert.AreEqual("Reservoir Stage", hazard.SpecifiedHazard);
        var (isValid, _) = hazard.Validate();
        Assert.IsTrue(isValid);
    }

    /// <summary>An ascending AEP array is rejected with the offending values echoed.</summary>
    [TestMethod]
    public void Test_Hazard_AscendingProbabilities_ErrorEchoesValues()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0].Hazard;
        dto.ExceedanceProbabilities.Reverse();
        var issues = new List<ValidationIssueDto>();

        // Act
        var hazard = FunctionMapper.ToHazard(dto, "components[0].hazard", Limits, issues);

        // Assert
        Assert.IsNull(hazard);
        var issue = issues.Single(i => i.Code == "API_TABLE_ORDER");
        StringAssert.Contains(issue.Message, "strictly descending");
        StringAssert.Contains(issue.Message, "exceedanceProbabilities[0]");
        StringAssert.Contains(issue.ObjectPath, "exceedanceProbabilities");
    }

    /// <summary>Misaligned array lengths are rejected with both lengths echoed.</summary>
    [TestMethod]
    public void Test_Hazard_LengthMismatch_ErrorEchoesLengths()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0].Hazard;
        dto.HazardValues.RemoveAt(0);
        var issues = new List<ValidationIssueDto>();

        // Act
        var hazard = FunctionMapper.ToHazard(dto, "h", Limits, issues);

        // Assert
        Assert.IsNull(hazard);
        var issue = issues.Single(i => i.Code == "API_TABLE_LENGTH_MISMATCH");
        StringAssert.Contains(issue.Message, "9");
        StringAssert.Contains(issue.Message, "8");
    }

    /// <summary>A probability outside [0, 1] is rejected with the value echoed.</summary>
    [TestMethod]
    public void Test_Response_ProbabilityOutOfRange_Error()
    {
        // Arrange
        var dto = new TabularResponseDto
        {
            HazardValues = new List<double> { 0, 1 },
            ResponseProbabilities = new List<double> { 0, 1.2 },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var response = FunctionMapper.ToResponse(dto, "r", Limits, ("Stage", "ft"), "FM Response", issues);

        // Assert
        Assert.IsNull(response);
        var issue = issues.Single(i => i.Code == "API_TABLE_RANGE");
        StringAssert.Contains(issue.Message, "responseProbabilities[1]");
    }

    /// <summary>A fragility plateauing at 0 and 1 maps legally (non-strict Y).</summary>
    [TestMethod]
    public void Test_Response_Plateaus_Map()
    {
        // Arrange
        var dto = new TabularResponseDto
        {
            HazardValues = new List<double> { 0, 1, 2, 3 },
            ResponseProbabilities = new List<double> { 0, 0, 1, 1 },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var response = FunctionMapper.ToResponse(dto, "r", Limits, ("Stage", "ft"), "FM Response", issues)!;

        // Assert
        Assert.AreEqual(0, issues.Count);
        Assert.IsTrue(response.IsDeterministic);
        Assert.AreEqual("Stage", response.SpecifiedHazard);
        Assert.AreEqual("ft", response.HazardUnit);
    }

    /// <summary>Blank labels inherit the hazard pair and the declared consequence pair.</summary>
    [TestMethod]
    public void Test_Consequence_BlankLabels_Inherit()
    {
        // Arrange
        var dto = new ConsequenceFunctionDto
        {
            HazardValues = new List<double> { 0, 10 },
            ConsequenceValues = new List<double> { 0, 5 },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var consequence = (RMC.TotalRisk.RiskFunctions.Consequences.TabularConsequence)FunctionMapper.ToConsequence(
            dto, "c", Limits, ("Stage", "ft"), ("Life Loss", "lives"), "Overtopping", issues)!;

        // Assert
        Assert.AreEqual(0, issues.Count);
        Assert.AreEqual("Stage", consequence.SpecifiedHazard);
        Assert.AreEqual("ft", consequence.HazardUnit);
        Assert.AreEqual("Life Loss", consequence.SpecifiedConsequence);
        Assert.AreEqual("lives", consequence.ConsequenceUnit);
        Assert.AreEqual("Overtopping", consequence.Name);
    }

    /// <summary>A day/night mixture maps to a Mixture-mode composite with labeled children.</summary>
    [TestMethod]
    public void Test_CompositeMixture_Maps()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0].FailureModes[0].Consequences[0];
        var issues = new List<ValidationIssueDto>();

        // Act
        var composite = (CompositeConsequence)FunctionMapper.ToConsequence(
            dto, "c", Limits, ("Reservoir Stage", "ft"), ("Life Loss", "lives"), "Overtopping Erosion", issues)!;

        // Assert
        Assert.AreEqual(0, issues.Count);
        Assert.AreEqual(CompositeFunctionType.Mixture, composite.CompositeFunctionType);
        Assert.AreEqual(2, composite.ConsequenceFunctions.Count);
        Assert.AreEqual(0.58, composite.ConsequenceFunctions[0].Weight);
        Assert.AreEqual("Life Loss", composite.SpecifiedConsequence);
        var (isValid, _) = composite.Validate();
        Assert.IsTrue(isValid);
    }

    /// <summary>Weights not summing to one are rejected with the sum echoed.</summary>
    [TestMethod]
    public void Test_CompositeMixture_WeightsSum_Error()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0].FailureModes[0].Consequences[0];
        dto.Branches![0].Weight = 0.5;
        dto.Branches[1].Weight = 0.4;
        var issues = new List<ValidationIssueDto>();

        // Act
        var composite = FunctionMapper.ToConsequence(dto, "c", Limits, ("Stage", "ft"), ("Life Loss", "lives"), "FM", issues);

        // Assert
        Assert.IsNull(composite);
        var issue = issues.Single(i => i.Code == "API_MIXTURE_WEIGHTS_SUM");
        StringAssert.Contains(issue.ObjectPath, "branches");
    }

    /// <summary>An unsupported discriminator is rejected listing the accepted values.</summary>
    [TestMethod]
    public void Test_UnsupportedType_ErrorListsAccepted()
    {
        // Arrange
        var dto = new ConsequenceFunctionDto { Type = "parametricConsequence" };
        var issues = new List<ValidationIssueDto>();

        // Act
        var consequence = FunctionMapper.ToConsequence(dto, "c", Limits, ("Stage", "ft"), ("Life Loss", "lives"), "FM", issues);

        // Assert
        Assert.IsNull(consequence);
        var issue = issues.Single(i => i.Code == "API_FUNCTION_TYPE_UNSUPPORTED");
        StringAssert.Contains(issue.Message, "tabularConsequence");
        StringAssert.Contains(issue.Message, "compositeMixture");
    }

    /// <summary>A table above the host ordinate cap is rejected.</summary>
    [TestMethod]
    public void Test_TableAboveHostCap_Error()
    {
        // Arrange
        var limits = new ApiOptions { MaxOrdinatesPerTable = 4 };
        var dto = new TabularResponseDto
        {
            HazardValues = new List<double> { 0, 1, 2, 3, 4 },
            ResponseProbabilities = new List<double> { 0, 0.1, 0.2, 0.3, 0.4 },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var response = FunctionMapper.ToResponse(dto, "r", limits, ("Stage", "ft"), "FM", issues);

        // Assert
        Assert.IsNull(response);
        Assert.AreEqual(1, issues.Count(i => i.Code == "API_TABLE_TOO_LONG"));
    }

    /// <summary>A non-finite ordinate is rejected with its index echoed.</summary>
    [TestMethod]
    public void Test_NonFiniteOrdinate_Error()
    {
        // Arrange
        var dto = new ConsequenceFunctionDto
        {
            HazardValues = new List<double> { 0, 10 },
            ConsequenceValues = new List<double> { 0, double.PositiveInfinity },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var consequence = FunctionMapper.ToConsequence(dto, "c", Limits, ("Stage", "ft"), ("Life Loss", "lives"), "FM", issues);

        // Assert
        Assert.IsNull(consequence);
        var issue = issues.Single(i => i.Code == "API_TABLE_NONFINITE");
        StringAssert.Contains(issue.Message, "consequenceValues[1]");
    }
}
