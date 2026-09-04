using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RMC.TotalRisk.Api.Controllers;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Services.Exceptions;

namespace RMC.TotalRisk.Api.Tests.Controllers;

/// <summary>
/// Tests for the shared execute-and-map pipeline: contract stamping, the exception→status map,
/// and the non-finite audit.
/// </summary>
[TestClass]
public class ApiControllerBaseTests
{
    /// <summary>A response DTO carrying a double for the finite-audit cases.</summary>
    private sealed class ProbeResponse : ResponseBase
    {
        /// <summary>The audited value.</summary>
        public double Value { get; set; }
    }

    /// <summary>A minimal concrete controller exposing the protected pipeline.</summary>
    private sealed class ProbeController : ApiControllerBase
    {
        /// <summary>Runs an operation through the pipeline.</summary>
        /// <param name="operation">The operation.</param>
        /// <returns>The action result.</returns>
        public Task<ActionResult<ProbeResponse>> Run(Func<ProbeResponse> operation)
        {
            return ExecuteAsync(operation, NullLogger.Instance, "probe.run");
        }
    }

    /// <summary>Unwraps the pipeline's action result into (status, body).</summary>
    /// <param name="actionResult">The action result.</param>
    /// <returns>The status code and typed body.</returns>
    private static (int StatusCode, ProbeResponse Body) Unwrap(ActionResult<ProbeResponse> actionResult)
    {
        var objectResult = (ObjectResult)actionResult.Result!;
        return (objectResult.StatusCode!.Value, (ProbeResponse)objectResult.Value!);
    }

    /// <summary>A success stamps timing, timestamp, and keeps success true.</summary>
    [TestMethod]
    public async Task Test_Success_StampsContract()
    {
        // Arrange
        var controller = new ProbeController();

        // Act
        var (status, body) = Unwrap(await controller.Run(() => new ProbeResponse { Value = 1.5 }));

        // Assert
        Assert.AreEqual(200, status);
        Assert.IsTrue(body.Success);
        Assert.IsNotNull(body.ComputationTimeMs);
        Assert.IsNotNull(body.Timestamp);
    }

    /// <summary>A validation exception maps to 400 with issues and the string mirrors.</summary>
    [TestMethod]
    public async Task Test_RequestValidation_Maps400WithIssues()
    {
        // Arrange
        var controller = new ProbeController();
        var issues = new List<ValidationIssueDto>
        {
            ValidationIssueDto.ApiError("API_TEST", "bad value 42", "path.to.field"),
        };

        // Act
        var (status, body) = Unwrap(await controller.Run(
            () => throw new RequestValidationException("failed", issues)));

        // Assert
        Assert.AreEqual(400, status);
        Assert.IsFalse(body.Success);
        Assert.AreEqual("API_TEST", body.ValidationIssues![0].Code);
        CollectionAssert.Contains(body.ValidationErrors!, "bad value 42");
        Assert.IsNull(body.ValidationWarnings);
    }

    /// <summary>Cancellation maps to 499.</summary>
    [TestMethod]
    public async Task Test_Cancellation_Maps499()
    {
        // Arrange
        var controller = new ProbeController();

        // Act
        var (status, body) = Unwrap(await controller.Run(() => throw new OperationCanceledException()));

        // Assert
        Assert.AreEqual(499, status);
        Assert.IsFalse(body.Success);
    }

    /// <summary>Argument problems map to 400 and everything else to 500.</summary>
    [TestMethod]
    public async Task Test_ArgumentAndUnhandled_Map400And500()
    {
        // Arrange
        var controller = new ProbeController();

        // Act
        var (argStatus, _) = Unwrap(await controller.Run(() => throw new ArgumentException("bad arg")));
        var (errStatus, errBody) = Unwrap(await controller.Run(() => throw new InvalidOperationException("boom")));

        // Assert
        Assert.AreEqual(400, argStatus);
        Assert.AreEqual(500, errStatus);
        StringAssert.Contains(errBody.ErrorMessage!, "boom");
    }

    /// <summary>±Infinity in a response is rejected as a 500 with the finding path; NaN passes.</summary>
    [TestMethod]
    public async Task Test_FiniteAudit_InfinityRejected_NaNPasses()
    {
        // Arrange
        var controller = new ProbeController();

        // Act
        var (infStatus, infBody) = Unwrap(await controller.Run(() => new ProbeResponse { Value = double.PositiveInfinity }));
        var (nanStatus, nanBody) = Unwrap(await controller.Run(() => new ProbeResponse { Value = double.NaN }));

        // Assert
        Assert.AreEqual(500, infStatus);
        Assert.IsFalse(infBody.Success);
        Assert.IsTrue(infBody.NonFiniteFindings!.Any(f => f.Contains("Value")));
        Assert.AreEqual(200, nanStatus);
        Assert.IsTrue(nanBody.Success);
    }
}
