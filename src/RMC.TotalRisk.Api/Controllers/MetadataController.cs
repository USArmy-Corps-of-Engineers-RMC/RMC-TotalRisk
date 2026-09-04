using Microsoft.AspNetCore.Mvc;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Services;

namespace RMC.TotalRisk.Api.Controllers
{
    /// <summary>
    /// The discovery surface: enum values, function kinds, defaults, limits, and contract
    /// conventions.
    /// </summary>
    [Route("api/metadata")]
    public class MetadataController : ApiControllerBase
    {
        /// <summary>
        /// The controller's logger.
        /// </summary>
        private readonly ILogger<MetadataController> _logger;

        /// <summary>
        /// The metadata service shared with the MCP tools.
        /// </summary>
        private readonly IMetadataService _service;

        /// <summary>
        /// Initializes the controller.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="service">The metadata service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public MetadataController(ILogger<MetadataController> logger, IMetadataService service)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Returns the contract metadata: every enum's accepted values, the function-kind
        /// discriminators, the effective defaults, the host limits, and the conventions.
        /// </summary>
        /// <returns>The metadata body.</returns>
        /// <response code="200">The metadata.</response>
        [HttpGet]
        [ProducesResponseType(typeof(MetadataResponse), StatusCodes.Status200OK)]
        public Task<ActionResult<MetadataResponse>> Get()
        {
            return ExecuteAsync(() => _service.GetMetadata(), _logger, "metadata.get");
        }
    }
}
