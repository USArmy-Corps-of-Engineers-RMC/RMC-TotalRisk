using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Mappers;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.Tests.Mappers;

/// <summary>
/// Tests for the options DTO → engine options mapping, including the automatic-defaults guard.
/// </summary>
[TestClass]
public class OptionsMapperTests
{
    /// <summary>A null DTO yields engine defaults with the API's adjusted-curves divergence.</summary>
    [TestMethod]
    public void Test_NullDto_DefaultsWithAdjustedOn()
    {
        // Arrange
        var issues = new List<ValidationIssueDto>();

        // Act
        var options = OptionsMapper.ToOptions(null, issues);

        // Assert
        Assert.AreEqual(0, issues.Count);
        Assert.IsTrue(options.EstimateMeanRiskOnly);
        Assert.IsTrue(options.OutputAdjustedFailureModeCurves);
        Assert.IsTrue(options.UseDefaults);
        Assert.AreEqual(12345, options.PRNGSeed);
    }

    /// <summary>
    /// Supplying any integration knob switches the automatic defaults off, so the knob survives
    /// the engine's run-start defaults re-application.
    /// </summary>
    [TestMethod]
    public void Test_IntegrationKnob_SwitchesUseDefaultsOff()
    {
        // Arrange
        var issues = new List<ValidationIssueDto>();

        // Act
        var options = OptionsMapper.ToOptions(new RiskAnalysisOptionsDto { Tolerance = 1e-6 }, issues);

        // Assert
        Assert.IsFalse(options.UseDefaults);
        Assert.AreEqual(1e-6, options.Tolerance);
    }

    /// <summary>Non-integration settings leave the automatic defaults tracking on.</summary>
    [TestMethod]
    public void Test_NonIntegrationKnobs_KeepUseDefaults()
    {
        // Arrange
        var issues = new List<ValidationIssueDto>();

        // Act
        var options = OptionsMapper.ToOptions(new RiskAnalysisOptionsDto { PrngSeed = 777, Alpha = 0.05 }, issues);

        // Assert
        Assert.IsTrue(options.UseDefaults);
        Assert.AreEqual(777, options.PRNGSeed);
        Assert.AreEqual(0.05, options.Alpha);
    }

    /// <summary>Requesting a full-uncertainty run is rejected with the structured mean-only code.</summary>
    [TestMethod]
    public void Test_FullUncertaintyRequested_MeanOnlyError()
    {
        // Arrange
        var issues = new List<ValidationIssueDto>();

        // Act
        var options = OptionsMapper.ToOptions(new RiskAnalysisOptionsDto { EstimateMeanRiskOnly = false }, issues);

        // Assert
        Assert.IsTrue(options.EstimateMeanRiskOnly);
        var issue = issues.Single(i => i.Code == "API_MEAN_ONLY_REQUIRED");
        Assert.AreEqual(DiagnosticSeverity.Error, issue.Severity);
        Assert.AreEqual("options.estimateMeanRiskOnly", issue.ObjectPath);
    }

    /// <summary>Measure flags OR together.</summary>
    [TestMethod]
    public void Test_RiskMeasures_FlagsCombine()
    {
        // Arrange
        var issues = new List<ValidationIssueDto>();
        var dto = new RiskAnalysisOptionsDto
        {
            RiskMeasures = new List<RiskMeasureOptions> { RiskMeasureOptions.ValueAtRisk, RiskMeasureOptions.RiskProfiles },
        };

        // Act
        var options = OptionsMapper.ToOptions(dto, issues);

        // Assert
        Assert.AreEqual(RiskMeasureOptions.ValueAtRisk | RiskMeasureOptions.RiskProfiles, options.RiskMeasures);
    }

    /// <summary>Explicitly disabling adjusted curves is honored.</summary>
    [TestMethod]
    public void Test_AdjustedCurves_ExplicitOff()
    {
        // Arrange
        var issues = new List<ValidationIssueDto>();

        // Act
        var options = OptionsMapper.ToOptions(new RiskAnalysisOptionsDto { OutputAdjustedFailureModeCurves = false }, issues);

        // Assert
        Assert.IsFalse(options.OutputAdjustedFailureModeCurves);
    }

    /// <summary>The effective echo re-applies to identical engine options (a replay contract).</summary>
    [TestMethod]
    public void Test_EffectiveEcho_Replays()
    {
        // Arrange
        var issues = new List<ValidationIssueDto>();
        var original = OptionsMapper.ToOptions(new RiskAnalysisOptionsDto
        {
            PrngSeed = 999,
            Tolerance = 1e-7,
            RiskMeasures = new List<RiskMeasureOptions> { RiskMeasureOptions.All },
        }, issues);

        // Act
        var echo = OptionsMapper.ToDto(original);
        var replayed = OptionsMapper.ToOptions(echo, issues);

        // Assert
        Assert.AreEqual(0, issues.Count);
        Assert.AreEqual(original.PRNGSeed, replayed.PRNGSeed);
        Assert.AreEqual(original.Tolerance, replayed.Tolerance);
        Assert.AreEqual(original.RiskMeasures, replayed.RiskMeasures);
        Assert.AreEqual(original.OutputAdjustedFailureModeCurves, replayed.OutputAdjustedFailureModeCurves);
        CollectionAssert.AreEqual(original.CanonicalHash(), replayed.CanonicalHash());
    }
}
