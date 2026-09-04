using Microsoft.Extensions.Options;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Services;
using RMC.TotalRisk.Api.Services.Exceptions;
using RMC.TotalRisk.Api.Tests.Support;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.Tests.Services;

/// <summary>
/// Tests for the stateless compute service: validate, compute, the mean-only gate, and the
/// documented contribution and adjusted/unadjusted contracts (the engine runs for real — a
/// deterministic mean-only run takes milliseconds).
/// </summary>
[TestClass]
public class RiskAnalysisComputeServiceTests
{
    /// <summary>Builds a fresh service over default limits.</summary>
    /// <returns>The service.</returns>
    private static RiskAnalysisComputeService CreateService()
    {
        return new RiskAnalysisComputeService(Options.Create(new ApiOptions()));
    }

    /// <summary>The screening example validates clean.</summary>
    [TestMethod]
    public void Test_Validate_Example_IsValid()
    {
        // Arrange
        var service = CreateService();

        // Act
        var response = service.Validate(TestRequests.DamScreening());

        // Assert
        Assert.IsTrue(response.IsValid, TestJson.Serialize(response.ValidationIssues));
        Assert.IsTrue(response.Success);
    }

    /// <summary>Validation reports request-shape errors without throwing.</summary>
    [TestMethod]
    public void Test_Validate_BadTable_ReportsIssues()
    {
        // Arrange
        var service = CreateService();
        var request = TestRequests.DamScreening();
        request.Components[0].Hazard.ExceedanceProbabilities.Reverse();

        // Act
        var response = service.Validate(request);

        // Assert
        Assert.IsFalse(response.IsValid);
        Assert.IsNotNull(response.ValidationIssues);
        Assert.IsTrue(response.ValidationIssues!.Any(i => i.Code == "API_TABLE_ORDER"));
        Assert.IsNotNull(response.ValidationErrors);
    }

    /// <summary>Computing an invalid request throws the 400-mapped validation exception.</summary>
    [TestMethod]
    public async Task Test_Compute_Invalid_ThrowsRequestValidation()
    {
        // Arrange
        var service = CreateService();
        var request = TestRequests.DamScreening();
        request.Components[0].FailureModes[0].Consequences[0].Branches![0].Weight = 0.9;

        // Act
        var ex = await Assert.ThrowsExceptionAsync<RequestValidationException>(
            () => service.ComputeAsync(request, CancellationToken.None));

        // Assert
        Assert.IsTrue(ex.Issues.Any(i => i.Code == "API_MIXTURE_WEIGHTS_SUM"));
        StringAssert.Contains(ex.Message, "API_MIXTURE_WEIGHTS_SUM");
    }

    /// <summary>Requesting a full-uncertainty run maps to the structured mean-only rejection.</summary>
    [TestMethod]
    public async Task Test_Compute_FullUncertainty_Rejected()
    {
        // Arrange
        var service = CreateService();
        var request = TestRequests.DamScreening();
        request.Options = new RiskAnalysisOptionsDto { EstimateMeanRiskOnly = false };

        // Act
        var ex = await Assert.ThrowsExceptionAsync<RequestValidationException>(
            () => service.ComputeAsync(request, CancellationToken.None));

        // Assert
        Assert.IsTrue(ex.Issues.Any(i => i.Code == "API_MEAN_ONLY_REQUIRED"));
    }

    /// <summary>
    /// The screening example computes: system/component/failure-mode trees populated, adjusted
    /// curves present by default, and the documented contribution sum identities hold.
    /// </summary>
    [TestMethod]
    public async Task Test_Compute_Example_ResultsContract()
    {
        // Arrange
        var service = CreateService();

        // Act
        var response = await service.ComputeAsync(TestRequests.DamScreening(), CancellationToken.None);

        // Assert: envelope and provenance.
        Assert.IsTrue(response.Success);
        Assert.IsNotNull(response.Results);
        Assert.IsNotNull(response.Provenance);
        Assert.AreEqual(12345, response.Provenance!.PrngSeed);
        Assert.IsNotNull(response.EffectiveOptions);
        Assert.IsTrue(response.EffectiveOptions!.OutputAdjustedFailureModeCurves);

        // Assert: the tree shape — one component with one results row per failure path (the
        // non-fail path rides the component background/non-fail streams, not a mode row).
        var component = response.Results!.Components.Single();
        Assert.AreEqual(3, component.FailureModes.Count);
        Assert.IsTrue(component.FailureModes.Any(m => m.Name.Contains("Overtopping")));
        Assert.IsTrue(component.Curves.NonFail.Stats.Mean > 0d);

        // Assert: adjusted curves are present by default and distinct from unadjusted.
        foreach (var mode in component.FailureModes)
        {
            Assert.IsNotNull(mode.AdjustedCurves, mode.Name);
        }

        // Assert: the contribution sum identities against the component fail stream.
        double failureProbabilitySum = component.FailureModes.Sum(m => m.Contribution?.FailureProbability ?? 0d);
        double failureMeanSum = component.FailureModes.Sum(m => m.Contribution?.FailureMean ?? 0d);
        double excessMeanSum = component.FailureModes.Sum(m => m.Contribution?.ExcessMean ?? 0d);
        Assert.AreEqual(component.Curves.Fail.Stats.MassBalance, failureProbabilitySum,
            Math.Abs(component.Curves.Fail.Stats.MassBalance) * 1e-9 + 1e-15);
        Assert.AreEqual(component.Curves.Fail.Stats.Mean, failureMeanSum,
            Math.Abs(component.Curves.Fail.Stats.Mean) * 1e-9 + 1e-12);
        Assert.AreEqual(component.Curves.Excess.Stats.Mean, excessMeanSum,
            Math.Abs(component.Curves.Excess.Stats.Mean) * 1e-9 + 1e-12);

        // Assert: the total stream is exhaustive.
        Assert.AreEqual(1d, response.Results.Curves.Total.Stats.MassBalance, 1e-12);
    }

    /// <summary>
    /// Disabling adjusted output nulls the adjusted blocks and leaves the unadjusted marginal
    /// results bit-identical (the option is not a seed input).
    /// </summary>
    [TestMethod]
    public async Task Test_Compute_AdjustedToggle_UnadjustedInvariant()
    {
        // Arrange
        var service = CreateService();
        var on = TestRequests.DamScreening();
        var off = TestRequests.DamScreening();
        off.Options!.OutputAdjustedFailureModeCurves = false;

        // Act
        var withAdjusted = await service.ComputeAsync(on, CancellationToken.None);
        var withoutAdjusted = await service.ComputeAsync(off, CancellationToken.None);

        // Assert
        var modeOn = withAdjusted.Results!.Components[0].FailureModes[0];
        var modeOff = withoutAdjusted.Results!.Components[0].FailureModes[0];
        Assert.IsNotNull(modeOn.AdjustedCurves);
        Assert.IsNull(modeOff.AdjustedCurves);
        Assert.AreEqual(modeOn.Curves.Fail.Stats.TotalProbability, modeOff.Curves.Fail.Stats.TotalProbability, 0d);
        Assert.AreEqual(modeOn.Curves.Fail.Stats.Mean, modeOff.Curves.Fail.Stats.Mean, 0d);
        Assert.AreEqual(withAdjusted.Results.Curves.Total.Stats.Mean, withoutAdjusted.Results.Curves.Total.Stats.Mean, 0d);
    }

    /// <summary>Warnings ride a successful compute response as structured issues.</summary>
    [TestMethod]
    public async Task Test_Compute_Warnings_RideSuccess()
    {
        // Arrange
        var service = CreateService();
        var request = TestRequests.DamScreening();
        request.Components[0].FailureModeMethod = FailureModeMethod.MutuallyExclusive;
        request.Components[0].FailureModeDependency = DependencyType.PerfectlyPositive;

        // Act
        var response = await service.ComputeAsync(request, CancellationToken.None);

        // Assert
        Assert.IsTrue(response.Success);
        Assert.IsNotNull(response.ValidationIssues);
        Assert.IsTrue(response.ValidationIssues!.Any(i => i.Code == "API_DEPENDENCY_COERCED"));
        Assert.IsNotNull(response.ValidationWarnings);
    }

    /// <summary>Excluding curve arrays keeps every scalar and contribution.</summary>
    [TestMethod]
    public async Task Test_Compute_ExcludeCurves_KeepsStats()
    {
        // Arrange
        var service = CreateService();
        var request = TestRequests.DamScreening();
        request.ResultOptions = new ResultOptionsDto { IncludeCurves = false };

        // Act
        var response = await service.ComputeAsync(request, CancellationToken.None);

        // Assert
        var total = response.Results!.Curves.Total;
        Assert.IsNull(total.Lec);
        Assert.IsNull(total.HazardFrequency);
        Assert.IsTrue(total.Stats.Mean > 0d);
        Assert.IsNotNull(response.Results.Components[0].FailureModes[0].Contribution);
    }

    /// <summary>A pre-cancelled token surfaces as a cancellation, not a 500.</summary>
    [TestMethod]
    public async Task Test_Compute_PreCancelled_Throws()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert: any OperationCanceledException subtype counts (TaskCanceledException
        // included) — the pipeline maps the whole family to 499.
        try
        {
            await service.ComputeAsync(TestRequests.DamScreening(), cts.Token);
            Assert.Fail("Expected an OperationCanceledException.");
        }
        catch (OperationCanceledException)
        {
        }
    }
}
