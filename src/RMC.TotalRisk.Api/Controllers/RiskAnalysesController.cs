using Microsoft.AspNetCore.Mvc;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Services;

namespace RMC.TotalRisk.Api.Controllers
{
    /// <summary>
    /// The stateless round-trip compute surface: validate a risk-analysis definition, compute
    /// it, or fetch a filled example request.
    /// </summary>
    [Route("api/risk-analyses")]
    public class RiskAnalysesController : ApiControllerBase
    {
        /// <summary>
        /// The controller's logger.
        /// </summary>
        private readonly ILogger<RiskAnalysesController> _logger;

        /// <summary>
        /// The compute service shared with the MCP tools.
        /// </summary>
        private readonly IRiskAnalysisComputeService _service;

        /// <summary>
        /// Initializes the controller.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="service">The compute service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public RiskAnalysesController(ILogger<RiskAnalysesController> logger, IRiskAnalysisComputeService service)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Computes a risk analysis synchronously: the complete definition in, the mean risk
        /// results out. Nothing is stored — the call is a self-contained round trip.
        /// </summary>
        /// <param name="request">The complete risk-analysis definition and settings.</param>
        /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
        /// <returns>The compute results body.</returns>
        /// <response code="200">The analysis computed successfully.</response>
        /// <response code="400">The request failed validation; see validationIssues.</response>
        /// <response code="500">The computation failed.</response>
        [HttpPost("compute")]
        [ProducesResponseType(typeof(ComputeRiskAnalysisResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ComputeRiskAnalysisResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ComputeRiskAnalysisResponse), StatusCodes.Status500InternalServerError)]
        public Task<ActionResult<ComputeRiskAnalysisResponse>> Compute(
            [FromBody] ComputeRiskAnalysisRequest request, CancellationToken cancellationToken)
        {
            return ExecuteAsync(() => _service.ComputeAsync(request, cancellationToken),
                _logger, "riskanalyses.compute");
        }

        /// <summary>
        /// Validates a risk-analysis definition without computing it: the identical request body
        /// returns the structured issue list, so clients can iterate cheaply toward a valid
        /// payload.
        /// </summary>
        /// <param name="request">The complete risk-analysis definition and settings.</param>
        /// <returns>The validation verdict (HTTP 200 whether or not the definition is valid).</returns>
        /// <response code="200">The verdict; isValid is false when errors are present.</response>
        /// <response code="500">The validation itself failed.</response>
        [HttpPost("validate")]
        [ProducesResponseType(typeof(ValidateRiskAnalysisResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidateRiskAnalysisResponse), StatusCodes.Status500InternalServerError)]
        public Task<ActionResult<ValidateRiskAnalysisResponse>> Validate([FromBody] ComputeRiskAnalysisRequest request)
        {
            return ExecuteAsync(() => _service.Validate(request), _logger, "riskanalyses.validate");
        }

        /// <summary>
        /// Returns a filled example request: a single dam with three failure modes and day/night
        /// exposure-weighted life-loss mixtures — a valid, computable payload to start from.
        /// </summary>
        /// <returns>The example compute request.</returns>
        /// <response code="200">The example request body.</response>
        [HttpGet("example")]
        [ProducesResponseType(typeof(ComputeRiskAnalysisRequest), StatusCodes.Status200OK)]
        public ActionResult<ComputeRiskAnalysisRequest> Example()
        {
            return Ok(ExampleRequestFactory.CreateDamScreeningExample());
        }
    }
}
