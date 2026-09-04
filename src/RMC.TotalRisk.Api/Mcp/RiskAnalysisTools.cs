using System.ComponentModel;
using ModelContextProtocol.Server;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Services;

namespace RMC.TotalRisk.Api.Mcp
{
    /// <summary>
    /// The MCP tool surface over the same services the REST controllers use: run or validate a
    /// complete risk-analysis definition, and discover the contract (metadata + a filled
    /// example). Every tool call is a self-contained round trip — nothing is stored between
    /// calls.
    /// </summary>
    [McpServerToolType]
    public class RiskAnalysisTools
    {
        /// <summary>
        /// The compute service shared with the REST controllers.
        /// </summary>
        private readonly IRiskAnalysisComputeService _computeService;

        /// <summary>
        /// The metadata service shared with the REST controllers.
        /// </summary>
        private readonly IMetadataService _metadataService;

        /// <summary>
        /// Initializes the tools.
        /// </summary>
        /// <param name="computeService">The compute service.</param>
        /// <param name="metadataService">The metadata service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public RiskAnalysisTools(IRiskAnalysisComputeService computeService, IMetadataService metadataService)
        {
            _computeService = computeService ?? throw new ArgumentNullException(nameof(computeService));
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        }

        /// <summary>
        /// The run_risk_analysis tool: compute a complete risk-analysis definition and return the
        /// mean risk results.
        /// </summary>
        /// <param name="request">The complete risk-analysis definition.</param>
        /// <param name="cancellationToken">Cancellation token supplied by the MCP host.</param>
        /// <returns>The compute response as JSON.</returns>
        [McpServerTool(Name = "run_risk_analysis")]
        [Description("Compute a complete dam/levee risk analysis synchronously and return the mean risk results: loss exceedance curves, annualized failure probability, expected annual consequences, per-failure-mode UNADJUSTED marginal and combination-ADJUSTED curves, and risk contributions. The request is a self-contained JSON definition (hazard stage-frequency table, failure modes with fragility tables and day/night consequence mixtures, engine settings) — the same contract as POST /api/risk-analyses/compute. Nothing is stored between calls. Mean-only deterministic runs complete in well under a second. Call get_metadata for enums/defaults/conventions and get_example_request for a valid filled template; validate first with validate_risk_analysis when constructing a payload incrementally.")]
        public async Task<string> RunRiskAnalysis(
            [Description("The complete risk-analysis definition: { name?, specifiedConsequence, consequenceUnit, components: [ { name, hazard: { exceedanceProbabilities (descending AEP), hazardValues (ascending) }, failureModes: [ { name, response: { hazardValues, responseProbabilities }, consequences: [ tabular or compositeMixture with branches [{ weight, function }] ] } ], nonFailConsequences: [...] } ], options?: { prngSeed?, riskMeasures?, outputAdjustedFailureModeCurves?, ... } }. See get_example_request for a complete valid instance.")]
            ComputeRiskAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await _computeService.ComputeAsync(request, cancellationToken);
            return McpJson.Serialize(response);
        }

        /// <summary>
        /// The validate_risk_analysis tool: dry-run validation of a definition.
        /// </summary>
        /// <param name="request">The complete risk-analysis definition.</param>
        /// <returns>The validation verdict as JSON.</returns>
        [McpServerTool(Name = "validate_risk_analysis")]
        [Description("Validate a risk-analysis definition WITHOUT computing it. Returns { isValid, validationIssues: [{ code, severity, message, objectPath }] } — every message echoes the offending value and the objectPath points into the request, so payloads can be repaired iteratively before paying for a run. Accepts the identical request shape as run_risk_analysis.")]
        public string ValidateRiskAnalysis(
            [Description("The complete risk-analysis definition to validate — the same shape run_risk_analysis accepts.")]
            ComputeRiskAnalysisRequest request)
        {
            var response = _computeService.Validate(request);
            return McpJson.Serialize(response);
        }

        /// <summary>
        /// The get_metadata tool: the contract discovery document.
        /// </summary>
        /// <returns>The metadata as JSON.</returns>
        [McpServerTool(Name = "get_metadata")]
        [Description("Get the contract metadata: every enum's accepted camelCase values (failureModeMethod, samplingScheme, riskMeasures, transforms, ...), the function-kind discriminators, the effective defaults (including API divergences), the host limits, and the contract conventions (probability direction, NaN policy, adjusted-vs-unadjusted semantics, contribution sum identities). Call this before constructing a request from scratch.")]
        public string GetMetadata()
        {
            return McpJson.Serialize(_metadataService.GetMetadata());
        }

        /// <summary>
        /// The get_example_request tool: a filled, valid, computable request template.
        /// </summary>
        /// <returns>The example request as JSON.</returns>
        [McpServerTool(Name = "get_example_request")]
        [Description("Get a filled example request: a single dam with a deterministic reservoir stage-frequency hazard, three failure modes (overtopping erosion, backward erosion piping, concentrated leak erosion), day/night exposure-weighted life-loss mixtures on every failure mode, and a day/night non-fail mixture. The payload is valid and computable as returned — edit it rather than building from scratch.")]
        public string GetExampleRequest()
        {
            return McpJson.Serialize(ExampleRequestFactory.CreateDamScreeningExample());
        }
    }
}
