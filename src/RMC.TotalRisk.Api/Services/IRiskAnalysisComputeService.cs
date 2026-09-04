using RMC.TotalRisk.Api.DTOs;

namespace RMC.TotalRisk.Api.Services
{
    /// <summary>
    /// The stateless round-trip compute facade: builds a model from a request, validates it, and
    /// runs the risk engine. Shared verbatim by the REST controllers and the MCP tools.
    /// </summary>
    public interface IRiskAnalysisComputeService
    {
        /// <summary>
        /// Maps and validates a compute request without running the engine.
        /// </summary>
        /// <param name="request">The compute request.</param>
        /// <returns>The structured validation verdict (never throws for validation problems).</returns>
        ValidateRiskAnalysisResponse Validate(ComputeRiskAnalysisRequest request);

        /// <summary>
        /// Maps, validates, and computes a risk analysis, returning the full results body.
        /// </summary>
        /// <param name="request">The compute request.</param>
        /// <param name="cancellationToken">Cancels the run (the client disconnect token on REST).</param>
        /// <returns>The compute response.</returns>
        /// <exception cref="Exceptions.RequestValidationException">Thrown when the request fails request-shape checks or model validation (maps to HTTP 400).</exception>
        /// <exception cref="OperationCanceledException">Thrown when the run is cancelled (maps to HTTP 499).</exception>
        Task<ComputeRiskAnalysisResponse> ComputeAsync(ComputeRiskAnalysisRequest request, CancellationToken cancellationToken);
    }
}
