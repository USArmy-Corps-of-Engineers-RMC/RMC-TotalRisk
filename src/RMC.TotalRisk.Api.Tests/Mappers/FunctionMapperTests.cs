using Numerics.Data;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Mappers;
using RMC.TotalRisk.Api.Tests.Support;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Transforms;

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

    /// <summary>Builds a valid linear stage-to-depth transform DTO with blank input labels.</summary>
    /// <returns>The transform DTO.</returns>
    private static TransformFunctionDto LinearStageToDepth()
    {
        return new TransformFunctionDto
        {
            Type = FunctionTypeNames.LinearTransform,
            TransformedHazard = "Overtopping Depth",
            TransformedHazardUnit = "ft",
            Alpha = -1214,
            Minimum = -1e9,
            Maximum = 1e9,
        };
    }

    /// <summary>A linear transform maps deterministically with defaults and inherited input labels.</summary>
    [TestMethod]
    public void Test_Transform_Linear_Maps()
    {
        // Arrange
        var dtos = new List<TransformFunctionDto> { LinearStageToDepth() };
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(dtos, "t", Limits, ("Reservoir Stage", "ft"), "Overtopping", issues)!;

        // Assert
        Assert.AreEqual(0, issues.Count);
        var linear = (LinearTransform)chain.Single();
        Assert.AreEqual(-1214, linear.Alpha);
        Assert.AreEqual(1, linear.Beta);
        Assert.IsFalse(linear.IsUncertain);
        Assert.IsTrue(linear.IsDeterministic);
        Assert.AreEqual(-1e9, linear.Minimum);
        Assert.AreEqual(1e9, linear.Maximum);
        Assert.AreEqual("Reservoir Stage", linear.SpecifiedHazard);
        Assert.AreEqual("ft", linear.HazardUnit);
        Assert.AreEqual("Overtopping Depth", linear.TransformedHazard);
        Assert.AreEqual("Overtopping Transform 1", linear.Name);
        var (isValid, messages) = linear.Validate();
        Assert.IsTrue(isValid, string.Join("; ", messages));
    }

    /// <summary>Omitted clamp bounds are rejected — the model default range would silently clamp.</summary>
    [TestMethod]
    public void Test_Transform_Linear_MissingRange_Errors()
    {
        // Arrange
        var dto = LinearStageToDepth();
        dto.Minimum = null;
        dto.Maximum = null;
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(new List<TransformFunctionDto> { dto }, "t", Limits,
            ("Stage", "ft"), "FM", issues);

        // Assert
        Assert.IsNull(chain);
        Assert.AreEqual(2, issues.Count(i => i.Code == "API_TRANSFORM_RANGE_REQUIRED"));
        Assert.AreEqual(1, issues.Count(i => i.ObjectPath == "t[0].minimum"));
        Assert.AreEqual(1, issues.Count(i => i.ObjectPath == "t[0].maximum"));
    }

    /// <summary>An inverted clamp range is rejected with both bounds echoed.</summary>
    [TestMethod]
    public void Test_Transform_Linear_InvertedRange_Error()
    {
        // Arrange
        var dto = LinearStageToDepth();
        dto.Minimum = 1220;
        dto.Maximum = 1200;
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(new List<TransformFunctionDto> { dto }, "t", Limits,
            ("Stage", "ft"), "FM", issues);

        // Assert
        Assert.IsNull(chain);
        var issue = issues.Single(i => i.Code == "API_TRANSFORM_RANGE_INVALID");
        StringAssert.Contains(issue.Message, "1220");
        StringAssert.Contains(issue.Message, "1200");
    }

    /// <summary>A power transform maps deterministically with the weir-form defaults.</summary>
    [TestMethod]
    public void Test_Transform_Power_Maps()
    {
        // Arrange
        var dto = new TransformFunctionDto
        {
            Type = FunctionTypeNames.PowerTransform,
            TransformedHazard = "Spillway Discharge",
            TransformedHazardUnit = "cfs",
            Alpha = 150,
            Xi = 1214,
            Minimum = 1214,
            Maximum = 1230,
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(new List<TransformFunctionDto> { dto }, "t", Limits,
            ("Reservoir Stage", "ft"), "Spillway Erosion", issues)!;

        // Assert
        Assert.AreEqual(0, issues.Count);
        var power = (PowerTransform)chain.Single();
        Assert.AreEqual(150, power.Alpha);
        Assert.AreEqual(1.5, power.Beta);
        Assert.AreEqual(1214, power.Xi);
        Assert.IsFalse(power.IsInverse);
        Assert.IsFalse(power.IsUncertain);
        Assert.IsTrue(power.IsDeterministic);
        Assert.AreEqual("Reservoir Stage", power.SpecifiedHazard);
        Assert.AreEqual("Spillway Discharge", power.TransformedHazard);
        var (isValid, messages) = power.Validate();
        Assert.IsTrue(isValid, string.Join("; ", messages));
    }

    /// <summary>A tabular transform maps its table, interpolation spaces, and extrapolation policy.</summary>
    [TestMethod]
    public void Test_Transform_Tabular_Maps()
    {
        // Arrange
        var dto = new TransformFunctionDto
        {
            Name = "Rating Curve",
            TransformedHazard = "Discharge",
            TransformedHazardUnit = "cfs",
            HazardValues = new List<double> { 1200, 1210, 1220 },
            TransformedHazardValues = new List<double> { 1, 5000, 20000 },
            HazardTransform = Transform.Logarithmic,
            TransformedHazardTransform = Transform.Logarithmic,
            Extrapolation = ExtrapolationPolicy.Both,
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(new List<TransformFunctionDto> { dto }, "t", Limits,
            ("Stage", "ft"), "FM", issues)!;

        // Assert
        Assert.AreEqual(0, issues.Count);
        var tabular = (TabularTransform)chain.Single();
        Assert.AreEqual("Rating Curve", tabular.Name);
        Assert.AreEqual(3, tabular.UncertainOrderedPairedData.Count);
        Assert.IsTrue(tabular.IsDeterministic);
        Assert.AreEqual(Transform.Logarithmic, tabular.HazardTransform);
        Assert.AreEqual(Transform.Logarithmic, tabular.TransformTransform);
        Assert.AreEqual(ExtrapolationPolicy.Both, tabular.Extrapolation);
        var (isValid, messages) = tabular.Validate();
        Assert.IsTrue(isValid, string.Join("; ", messages));
    }

    /// <summary>A non-ascending tabular transform axis is rejected with the offending pair echoed.</summary>
    [TestMethod]
    public void Test_Transform_Tabular_UnorderedX_ErrorEchoesValues()
    {
        // Arrange
        var dto = new TransformFunctionDto
        {
            TransformedHazard = "Discharge",
            TransformedHazardUnit = "cfs",
            HazardValues = new List<double> { 1200, 1200, 1220 },
            TransformedHazardValues = new List<double> { 0, 5000, 20000 },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(new List<TransformFunctionDto> { dto }, "t", Limits,
            ("Stage", "ft"), "FM", issues);

        // Assert
        Assert.IsNull(chain);
        var issue = issues.Single(i => i.Code == "API_TABLE_ORDER");
        StringAssert.Contains(issue.Message, "strictly ascending");
        StringAssert.Contains(issue.ObjectPath, "hazardValues");
    }

    /// <summary>Missing output-axis labels are rejected — the next function's axis cannot be inferred.</summary>
    [TestMethod]
    public void Test_Transform_MissingOutputLabels_Errors()
    {
        // Arrange
        var dto = new TransformFunctionDto
        {
            HazardValues = new List<double> { 1200, 1220 },
            TransformedHazardValues = new List<double> { 0, 6 },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(new List<TransformFunctionDto> { dto }, "t", Limits,
            ("Stage", "ft"), "FM", issues);

        // Assert
        Assert.IsNull(chain);
        Assert.AreEqual(2, issues.Count(i => i.Code == "API_LABEL_REQUIRED"));
        Assert.AreEqual(1, issues.Count(i => i.ObjectPath == "t[0].transformedHazard"));
        Assert.AreEqual(1, issues.Count(i => i.ObjectPath == "t[0].transformedHazardUnit"));
    }

    /// <summary>An unsupported transform discriminator is rejected listing all three accepted values.</summary>
    [TestMethod]
    public void Test_Transform_UnknownType_ErrorListsAccepted()
    {
        // Arrange
        var dto = new TransformFunctionDto { Type = "bivariateTransform" };
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(new List<TransformFunctionDto> { dto }, "t", Limits,
            ("Stage", "ft"), "FM", issues);

        // Assert
        Assert.IsNull(chain);
        var issue = issues.Single(i => i.Code == "API_FUNCTION_TYPE_UNSUPPORTED");
        StringAssert.Contains(issue.Message, "tabularTransform");
        StringAssert.Contains(issue.Message, "linearTransform");
        StringAssert.Contains(issue.Message, "powerTransform");
    }

    /// <summary>Blank input labels on a later chain entry inherit the previous entry's output labels.</summary>
    [TestMethod]
    public void Test_TransformChain_LabelsThreadThroughChain()
    {
        // Arrange
        var dtos = new List<TransformFunctionDto>
        {
            LinearStageToDepth(),
            new TransformFunctionDto
            {
                TransformedHazard = "Velocity",
                TransformedHazardUnit = "ft/s",
                HazardValues = new List<double> { 0, 10 },
                TransformedHazardValues = new List<double> { 0, 30 },
            },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var chain = FunctionMapper.ToTransforms(dtos, "t", Limits, ("Reservoir Stage", "ft"), "FM", issues)!;

        // Assert
        Assert.AreEqual(0, issues.Count);
        Assert.AreEqual(2, chain.Count);
        Assert.AreEqual("Reservoir Stage", chain[0].SpecifiedHazard);
        Assert.AreEqual("Overtopping Depth", chain[1].SpecifiedHazard);
        Assert.AreEqual("ft", chain[1].HazardUnit);
        Assert.AreEqual("Velocity", chain[1].TransformedHazard);
    }
}
