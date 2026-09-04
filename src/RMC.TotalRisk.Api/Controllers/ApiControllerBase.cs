using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Helpers;
using RMC.TotalRisk.Api.Services.Exceptions;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.Controllers
{
    /// <summary>
    /// Base controller providing the shared execute-and-map pipeline: every endpoint's work runs
    /// inside <see cref="ExecuteAsync{TResponse}(Func{Task{TResponse}}, ILogger, string, int)"/>,
    /// which stamps timing/timestamp, audits the response for non-finite values, and translates
    /// the API's typed exceptions into the documented HTTP status codes.
    /// </summary>
    /// <remarks>
    /// Status mapping: 400 validation/argument errors (with the structured issue list when
    /// available), 499 client cancellation, 500 anything else. Failures are returned as the same
    /// typed DTO as successes so clients parse one shape for both.
    /// </remarks>
    [ApiController]
    [Produces("application/json")]
    public abstract class ApiControllerBase : ControllerBase
    {
        /// <summary>
        /// Runs an asynchronous operation and maps its outcome (or exception) to the shared
        /// response contract.
        /// </summary>
        /// <typeparam name="TResponse">The response DTO type.</typeparam>
        /// <param name="operation">The operation producing the response body.</param>
        /// <param name="logger">The controller's logger.</param>
        /// <param name="operationName">Short operation name used in log messages.</param>
        /// <param name="successStatusCode">The status code for the success path (200 or 201).</param>
        /// <returns>The action result carrying the response body.</returns>
        protected async Task<ActionResult<TResponse>> ExecuteAsync<TResponse>(
            Func<Task<TResponse>> operation,
            ILogger logger,
            string operationName,
            int successStatusCode = StatusCodes.Status200OK)
            where TResponse : ResponseBase, new()
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(logger);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Success defaults to true on ResponseBase and is NOT forced here, so an endpoint
                // may report an in-body failure while still returning through this success path.
                var response = await operation();
                response.ComputationTimeMs = stopwatch.ElapsedMilliseconds;
                response.Timestamp = DateTime.UtcNow.ToString("O");

                // Final seatbelt: fail loudly on ±Infinity rather than letting the JSON serializer
                // emit a value most clients cannot parse. NaN is legitimate (a not-computed
                // measure) and passes.
                var findings = ResponseFiniteAuditor.Audit(response);
                if (findings.Count > 0)
                {
                    logger.LogError("[API] {Operation}: non-finite values detected in response at: {Paths}",
                        operationName, string.Join("; ", findings));
                    return Fail<TResponse>(StatusCodes.Status500InternalServerError,
                        "The operation produced non-finite values. See nonFiniteFindings.",
                        stopwatch, nonFiniteFindings: findings);
                }

                return StatusCode(successStatusCode, response);
            }
            catch (RequestValidationException ex)
            {
                logger.LogWarning("[API] {Operation}: validation failed: {Errors}",
                    operationName, string.Join("; ", ex.Issues.Select(i => i.Message)));
                return Fail<TResponse>(StatusCodes.Status400BadRequest, ex.Message, stopwatch,
                    validationIssues: ex.Issues.ToList());
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("[API] {Operation}: cancelled by client", operationName);
                return Fail<TResponse>(499, "Operation cancelled by client.", stopwatch);
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "[API] {Operation}: bad request", operationName);
                return Fail<TResponse>(StatusCodes.Status400BadRequest, ex.Message, stopwatch);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[API] {Operation}: unhandled error", operationName);
                return Fail<TResponse>(StatusCodes.Status500InternalServerError, $"Internal error: {ex.Message}", stopwatch);
            }
        }

        /// <summary>
        /// Runs a synchronous operation through the same pipeline as
        /// <see cref="ExecuteAsync{TResponse}(Func{Task{TResponse}}, ILogger, string, int)"/>.
        /// </summary>
        /// <typeparam name="TResponse">The response DTO type.</typeparam>
        /// <param name="operation">The operation producing the response body.</param>
        /// <param name="logger">The controller's logger.</param>
        /// <param name="operationName">Short operation name used in log messages.</param>
        /// <param name="successStatusCode">The status code for the success path (200 or 201).</param>
        /// <returns>The action result carrying the response body.</returns>
        protected Task<ActionResult<TResponse>> ExecuteAsync<TResponse>(
            Func<TResponse> operation,
            ILogger logger,
            string operationName,
            int successStatusCode = StatusCodes.Status200OK)
            where TResponse : ResponseBase, new()
        {
            ArgumentNullException.ThrowIfNull(operation);
            return ExecuteAsync(() => Task.FromResult(operation()), logger, operationName, successStatusCode);
        }

        /// <summary>
        /// Builds a failure response body with the shared contract fields populated. When a
        /// structured issue list is supplied, the legacy string mirrors (validationErrors /
        /// validationWarnings) are derived from it by severity.
        /// </summary>
        /// <typeparam name="TResponse">The response DTO type.</typeparam>
        /// <param name="statusCode">The HTTP status code to return.</param>
        /// <param name="errorMessage">The client-facing error message.</param>
        /// <param name="stopwatch">The request stopwatch, used to stamp the elapsed time.</param>
        /// <param name="validationIssues">Optional structured validation issues.</param>
        /// <param name="nonFiniteFindings">Optional non-finite audit findings.</param>
        /// <returns>The failure action result.</returns>
        private ObjectResult Fail<TResponse>(
            int statusCode,
            string errorMessage,
            Stopwatch stopwatch,
            List<ValidationIssueDto>? validationIssues = null,
            List<string>? nonFiniteFindings = null)
            where TResponse : ResponseBase, new()
        {
            List<string>? errors = null;
            List<string>? warnings = null;
            if (validationIssues != null)
            {
                errors = validationIssues.Where(i => i.Severity == DiagnosticSeverity.Error)
                    .Select(i => i.Message).ToList();
                warnings = validationIssues.Where(i => i.Severity == DiagnosticSeverity.Warning)
                    .Select(i => i.Message).ToList();
                if (errors.Count == 0) errors = null;
                if (warnings.Count == 0) warnings = null;
            }

            var response = new TResponse
            {
                Success = false,
                ErrorMessage = errorMessage,
                ValidationIssues = validationIssues,
                ValidationErrors = errors,
                ValidationWarnings = warnings,
                NonFiniteFindings = nonFiniteFindings,
                ComputationTimeMs = stopwatch.ElapsedMilliseconds,
                Timestamp = DateTime.UtcNow.ToString("O")
            };
            return StatusCode(statusCode, response);
        }
    }
}
