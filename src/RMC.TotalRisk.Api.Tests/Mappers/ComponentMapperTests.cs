using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Mappers;
using RMC.TotalRisk.Api.Tests.Support;
using RMC.TotalRisk.Core.Enums;

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
}
