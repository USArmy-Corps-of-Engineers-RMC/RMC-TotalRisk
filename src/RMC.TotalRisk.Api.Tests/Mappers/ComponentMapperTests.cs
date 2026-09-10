using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Mappers;
using RMC.TotalRisk.Api.Tests.Support;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Api.Tests.Mappers;

/// <summary>
/// Tests for the component DTO → SystemComponent mapping.
/// </summary>
[TestClass]
public class ComponentMapperTests
{
    /// <summary>The default host limits.</summary>
    private static readonly ApiOptions Limits = new();

    /// <summary>The declared single-type axis used by most tests.</summary>
    private static readonly IReadOnlyList<(string, string)> LifeLossAxis = new List<(string, string)> { ("Life Loss", "lives") };

    /// <summary>The screening component maps to a valid component with four projected modes.</summary>
    [TestMethod]
    public void Test_ScreeningComponent_MapsAndValidates()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0];
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues)!;

        // Assert
        Assert.IsNotNull(component);
        Assert.AreEqual(0, issues.Count(i => i.Severity == DiagnosticSeverity.Error), TestJson.Serialize(issues));
        Assert.AreEqual("Example Dam", component.Name);
        var modes = component.FailureModes;
        Assert.AreEqual(4, modes.Count);
        Assert.AreEqual(1, modes.Count(m => m.IsNonFailureMode));
        var (isValid, messages) = component.Validate();
        Assert.IsTrue(isValid, string.Join("; ", messages));
    }

    /// <summary>A component with no failure modes and no non-fail consequence is rejected.</summary>
    [TestMethod]
    public void Test_EmptyComponent_Error()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0];
        dto.FailureModes.Clear();
        dto.NonFailConsequences.Clear();
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues);

        // Assert
        Assert.IsNull(component);
        Assert.AreEqual(1, issues.Count(i => i.Code == "API_COMPONENT_EMPTY"));
    }

    /// <summary>A consequence-count mismatch against the declared axis is rejected with counts echoed.</summary>
    [TestMethod]
    public void Test_ConsequenceCountMismatch_Error()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0];
        var twoTypeAxis = new List<(string, string)> { ("Life Loss", "lives"), ("Damages", "$") };
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, twoTypeAxis, issues);

        // Assert
        Assert.IsNull(component);
        var issue = issues.First(i => i.Code == "API_CONSEQUENCE_COUNT");
        StringAssert.Contains(issue.Message, "2");
        StringAssert.Contains(issue.Message, "1");
    }

    /// <summary>The mutually-exclusive method coerces a requested dependency to independent with a warning.</summary>
    [TestMethod]
    public void Test_DependencyCoercion_Warning()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0];
        dto.FailureModeMethod = FailureModeMethod.MutuallyExclusive;
        dto.FailureModeDependency = DependencyType.PerfectlyPositive;
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues)!;

        // Assert
        Assert.IsNotNull(component);
        Assert.AreEqual(DependencyType.Independent, component.FailureModeDependency);
        var warning = issues.Single(i => i.Code == "API_DEPENDENCY_COERCED");
        Assert.AreEqual(DiagnosticSeverity.Warning, warning.Severity);
    }

    /// <summary>A correlation matrix outside the correlation-matrix mode is ignored with a warning.</summary>
    [TestMethod]
    public void Test_MatrixWithoutMatrixMode_Warning()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0];
        dto.FailureModeCorrelationMatrix = new List<List<double>>
        {
            new() { 1, 0, 0 }, new() { 0, 1, 0 }, new() { 0, 0, 1 },
        };
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues)!;

        // Assert
        Assert.IsNotNull(component);
        Assert.AreEqual(1, issues.Count(i => i.Code == "API_MATRIX_IGNORED" && i.Severity == DiagnosticSeverity.Warning));
    }

    /// <summary>A ragged matrix payload is rejected with the row shape echoed.</summary>
    [TestMethod]
    public void Test_RaggedMatrix_Error()
    {
        // Arrange
        var rows = new List<List<double>> { new() { 1, 0 }, new() { 0 } };
        var issues = new List<ValidationIssueDto>();

        // Act
        var matrix = ComponentMapper.ToMatrix(rows, "m", issues);

        // Assert
        Assert.IsNull(matrix);
        var issue = issues.Single(i => i.Code == "API_MATRIX_RAGGED");
        StringAssert.Contains(issue.Message, "row 1");
    }

    /// <summary>The failure-mode name labels the projected terminal via the primary consequence.</summary>
    [TestMethod]
    public void Test_FailureModeName_FlowsToTerminal()
    {
        // Arrange
        var dto = TestRequests.DamScreening().Components[0];
        dto.FailureModes[0].Consequences[0].Name = null;
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues)!;

        // Assert
        var mode = component.FailureModes.First(m => !m.IsNonFailureMode);
        Assert.AreEqual("Overtopping Erosion", mode.ConsequenceFunctions[0].Name);
    }

    /// <summary>
    /// Builds a minimal single-mode component whose failure mode carries one linear
    /// stage-to-depth transform, a depth-keyed response with blank labels, and a stage-keyed
    /// consequence with blank labels bound to the raw hazard.
    /// </summary>
    /// <returns>The component DTO.</returns>
    private static ComponentDto TransformedComponent()
    {
        return new ComponentDto
        {
            Name = "Dam",
            Hazard = new TabularHazardDto
            {
                SpecifiedHazard = "Stage",
                HazardUnit = "ft",
                ExceedanceProbabilities = new List<double> { 0.5, 0.01, 0.001 },
                HazardValues = new List<double> { 1200, 1214, 1220 },
            },
            FailureModes = new List<FailureModeDto>
            {
                new FailureModeDto
                {
                    Name = "Overtopping",
                    Transforms = new List<TransformFunctionDto>
                    {
                        new TransformFunctionDto
                        {
                            Type = FunctionTypeNames.LinearTransform,
                            TransformedHazard = "Overtopping Depth",
                            TransformedHazardUnit = "ft",
                            Alpha = -1214,
                            Minimum = -1e9,
                            Maximum = 1e9,
                        },
                    },
                    Response = new TabularResponseDto
                    {
                        HazardValues = new List<double> { 0, 6 },
                        ResponseProbabilities = new List<double> { 0, 0.8 },
                    },
                    ConsequenceHazardPosition = 0,
                    Consequences = new List<ConsequenceFunctionDto>
                    {
                        new ConsequenceFunctionDto
                        {
                            HazardValues = new List<double> { 1200, 1220 },
                            ConsequenceValues = new List<double> { 0, 100 },
                        },
                    },
                },
            },
        };
    }

    /// <summary>A transformed mode's blank response labels inherit the transform's output axis.</summary>
    [TestMethod]
    public void Test_ModeWithTransforms_ResponseInheritsTransformedAxis()
    {
        // Arrange
        var dto = TransformedComponent();
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues)!;

        // Assert
        Assert.IsNotNull(component);
        Assert.AreEqual(0, issues.Count, TestJson.Serialize(issues));
        var mode = component.FailureModes.Single(m => !m.IsNonFailureMode);
        Assert.AreEqual(1, mode.HazardToResponse.Count);
        Assert.AreEqual("Overtopping Depth", mode.ResponseFunction.SpecifiedHazard);
        Assert.AreEqual("ft", mode.ResponseFunction.HazardUnit);
        var (isValid, messages) = component.Validate();
        Assert.IsTrue(isValid, string.Join("; ", messages));
        Assert.IsFalse(messages.Any(m => m.Contains("does not match")), string.Join("; ", messages));
    }

    /// <summary>Position 0 binds the consequences to the raw hazard and inherits its labels.</summary>
    [TestMethod]
    public void Test_ModeWithTransforms_PositionZero_ConsequencesInheritRawHazardLabels()
    {
        // Arrange
        var dto = TransformedComponent();
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues)!;

        // Assert
        var mode = component.FailureModes.Single(m => !m.IsNonFailureMode);
        Assert.AreEqual(0, mode.ResolvedConsequenceHazardPosition);
        Assert.AreEqual("Stage", mode.ConsequenceFunctions[0].SpecifiedHazard);
        Assert.AreEqual("ft", mode.ConsequenceFunctions[0].HazardUnit);
    }

    /// <summary>
    /// An omitted position on a transformed mode defaults to the raw hazard (position 0) — the
    /// contract's documented divergence from the engine's last-response-input default.
    /// </summary>
    [TestMethod]
    public void Test_ModeWithTransforms_OmittedPosition_DefaultsToRawHazard()
    {
        // Arrange
        var dto = TransformedComponent();
        dto.FailureModes[0].ConsequenceHazardPosition = null;
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues)!;

        // Assert
        var mode = component.FailureModes.Single(m => !m.IsNonFailureMode);
        Assert.AreEqual(0, mode.ResolvedConsequenceHazardPosition);
        Assert.AreEqual("Stage", mode.ConsequenceFunctions[0].SpecifiedHazard);
    }

    /// <summary>
    /// An out-of-range position is rejected by the mapper. The gate is load-bearing: the model's
    /// AddFailureMode expansion silently drops an out-of-range binding, so without this check the
    /// request would compute on the wrong axis with no diagnostic.
    /// </summary>
    [TestMethod]
    public void Test_ConsequencePosition_OutOfRange_Error()
    {
        // Arrange
        var dto = TransformedComponent();
        dto.FailureModes[0].ConsequenceHazardPosition = 2;
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues);

        // Assert
        Assert.IsNull(component);
        var issue = issues.Single(i => i.Code == "API_CONSEQUENCE_POSITION_RANGE");
        StringAssert.Contains(issue.Message, "2");
        StringAssert.Contains(issue.Message, "1");
        StringAssert.Contains(issue.ObjectPath, "consequenceHazardPosition");
    }

    /// <summary>A nonzero position without transforms is rejected — only position 0 exists.</summary>
    [TestMethod]
    public void Test_ConsequencePosition_WithoutTransforms_NonzeroError()
    {
        // Arrange
        var dto = TransformedComponent();
        dto.FailureModes[0].Transforms = null;
        dto.FailureModes[0].Response.HazardValues = new List<double> { 1214, 1220 };
        dto.FailureModes[0].ConsequenceHazardPosition = 1;
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues);

        // Assert
        Assert.IsNull(component);
        Assert.AreEqual(1, issues.Count(i => i.Code == "API_CONSEQUENCE_POSITION_RANGE"));
    }

    /// <summary>
    /// A transform-free mode maps to a canonical hash identical to the pre-transform mapper
    /// shape (empty chain, no authored position) — existing requests keep their seeds and
    /// byte-identical results.
    /// </summary>
    [TestMethod]
    public void Test_TransformFreeMode_HashMatchesPreTransformShape()
    {
        // Arrange
        var dto = TransformedComponent();
        dto.FailureModes[0].Transforms = null;
        dto.FailureModes[0].ConsequenceHazardPosition = null;
        dto.FailureModes[0].Response.HazardValues = new List<double> { 1214, 1220 };
        var issues = new List<ValidationIssueDto>();

        // Act
        var component = ComponentMapper.ToComponent(dto, 0, Limits, LifeLossAxis, issues)!;
        var projected = component.FailureModes.Single(m => !m.IsNonFailureMode);

        // The pre-transform mapper shape: one stage with an empty chain and no authored position.
        var response = FunctionMapper.ToResponse(dto.FailureModes[0].Response, "r", Limits,
            ("Stage", "ft"), "Overtopping Response", issues)!;
        var consequence = FunctionMapper.ToConsequence(dto.FailureModes[0].Consequences[0], "c", Limits,
            ("Stage", "ft"), ("Life Loss", "lives"), "Overtopping", issues)!;
        var twin = new FailureMode(
            new List<ResponseStage> { new ResponseStage(new List<ITransformFunction>(), response) },
            null, new List<IConsequenceFunction> { consequence });

        // Assert
        Assert.AreEqual(0, issues.Count, TestJson.Serialize(issues));
        CollectionAssert.AreEqual(twin.CanonicalHash(), projected.CanonicalHash());
    }
}
