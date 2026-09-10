using Microsoft.Extensions.Options;
using Numerics.Data;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Helpers;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.Services
{
    /// <summary>
    /// Builds the discovery metadata from the contract's enum types, the engine defaults, and
    /// the host limits.
    /// </summary>
    public class MetadataService : IMetadataService
    {
        /// <summary>
        /// The configured host limits.
        /// </summary>
        private readonly ApiOptions _limits;

        /// <summary>
        /// Initializes the service.
        /// </summary>
        /// <param name="options">The host limits.</param>
        /// <exception cref="ArgumentNullException">Thrown when the options are null.</exception>
        public MetadataService(IOptions<ApiOptions> options)
        {
            _limits = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public MetadataResponse GetMetadata()
        {
            return new MetadataResponse
            {
                Enums = new Dictionary<string, List<string>>
                {
                    ["samplingScheme"] = EnumHelper.CamelCaseNames<SamplingScheme>(),
                    ["riskAnalysisMode"] = EnumHelper.CamelCaseNames<RiskAnalysisMode>(),
                    ["riskIntegrand"] = EnumHelper.CamelCaseNames<RiskIntegrand>(),
                    ["systemRiskMethod"] = EnumHelper.CamelCaseNames<SystemRiskType>(),
                    ["jointConsequenceType"] = EnumHelper.CamelCaseNames<JointConsequenceType>(),
                    ["dependencyType"] = EnumHelper.CamelCaseNames<DependencyType>(),
                    ["failureModeMethod"] = EnumHelper.CamelCaseNames<FailureModeMethod>(),
                    ["riskMeasureOptions"] = EnumHelper.CamelCaseNames<RiskMeasureOptions>(),
                    ["vegasTailFocusMode"] = EnumHelper.CamelCaseNames<VegasTailFocusMode>(),
                    ["transform"] = EnumHelper.CamelCaseNames<Transform>(),
                    ["extrapolationPolicy"] = EnumHelper.CamelCaseNames<ExtrapolationPolicy>(),
                    ["diagnosticSeverity"] = EnumHelper.CamelCaseNames<DiagnosticSeverity>(),
                },
                FunctionTypes = new Dictionary<string, List<string>>
                {
                    ["hazard"] = new List<string> { FunctionTypeNames.TabularHazard },
                    ["response"] = new List<string> { FunctionTypeNames.TabularResponse },
                    ["consequence"] = new List<string> { FunctionTypeNames.TabularConsequence, FunctionTypeNames.CompositeMixture },
                    ["transform"] = new List<string> { FunctionTypeNames.TabularTransform, FunctionTypeNames.LinearTransform, FunctionTypeNames.PowerTransform },
                },
                Defaults = new Dictionary<string, object?>
                {
                    ["estimateMeanRiskOnly"] = true,
                    ["realizations"] = 1000,
                    ["confidenceIntervalWidth"] = 0.9,
                    ["prngSeed"] = 12345,
                    ["lecOutputLength"] = 200,
                    ["samplingScheme"] = EnumHelper.ToCamelCase(nameof(SamplingScheme.LatinHypercube)),
                    ["mode"] = EnumHelper.ToCamelCase(nameof(RiskAnalysisMode.Risk)),
                    ["riskIntegrand"] = EnumHelper.ToCamelCase(nameof(RiskIntegrand.MeanTotalRisk)),
                    ["systemRiskMethod"] = EnumHelper.ToCamelCase(nameof(SystemRiskType.AdditiveRiskMethod)),
                    ["systemJointConsequences"] = EnumHelper.ToCamelCase(nameof(JointConsequenceType.Additive)),
                    ["componentHazardDependency"] = EnumHelper.ToCamelCase(nameof(DependencyType.Independent)),
                    ["consequenceThreshold"] = 0.0,
                    ["alpha"] = 0.01,
                    ["riskMeasures"] = new List<string> { EnumHelper.ToCamelCase(nameof(RiskMeasureOptions.All)) },
                    ["outputAdjustedFailureModeCurves"] = true,
                    ["failureModeMethod"] = EnumHelper.ToCamelCase(nameof(FailureModeMethod.JointFailures)),
                    ["failureModeDependency"] = EnumHelper.ToCamelCase(nameof(DependencyType.Independent)),
                    ["jointConsequences"] = EnumHelper.ToCamelCase(nameof(JointConsequenceType.Maximum)),
                    ["hazardThreshold"] = 0.0,
                    ["hazardTransform"] = EnumHelper.ToCamelCase(nameof(Transform.None)),
                    ["hazardProbabilityTransform"] = EnumHelper.ToCamelCase(nameof(Transform.NormalZ)),
                    ["extrapolation"] = EnumHelper.ToCamelCase(nameof(ExtrapolationPolicy.None)),
                    ["consequenceHazardPosition"] = 0,
                },
                Limits = new MetadataLimitsDto
                {
                    MaxConcurrentRuns = _limits.MaxConcurrentRuns,
                    MaxComponents = _limits.MaxComponents,
                    MaxOrdinatesPerTable = _limits.MaxOrdinatesPerTable,
                },
                Conventions = new List<string>
                {
                    "Hazard tables pair ANNUAL EXCEEDANCE probabilities (strictly descending, the frequent event first) with strictly ascending hazard levels; nothing is silently reordered.",
                    "This contract version computes mean risk only: estimateMeanRiskOnly must be true or omitted. Full-uncertainty ensembles arrive in a later contract increment.",
                    "Results are deterministic: the same request content and prngSeed produce bit-identical results at any thread count (seeds derive from canonical content hashes, so renaming functions or components never changes numbers).",
                    "A null scalar measure means 'not computed' (a disabled riskMeasures flag or an undefined value); NaN appears on the wire only as the JSON literal \"NaN\" and clients must enable named floating-point literals to parse it.",
                    "Each failure mode's curves are its raw UNADJUSTED marginal risk (what an investment decision compares across modes); adjustedCurves carry the mode's share after the component's combination method resolves the modes, so adjusted values sum to the component total. The API computes both by default.",
                    "Contribution identities: across a component's failure modes, the contribution failureProbability values sum to the component fail stream's massBalance, failureMean values sum to the component fail mean, and excessMean values sum to the component excess mean.",
                    "Contributions are always computed, independent of the riskMeasures flags.",
                    "Composite-mixture consequence weights are aleatory exposure probabilities: the engine enumerates the weighted branches (mixture), which preserves consequence variance that an average would destroy.",
                    "Under the jointFailures method, exposure branches multiply across failure paths: a component whose paths carry two-branch day/night mixtures warns above 64 branch combinations and errors above 1,024 (roughly six or ten two-branch paths).",
                    "A failure mode's transforms convert the hazard signal in order ahead of the response (e.g., stage to overtopping depth, stage to spillway discharge): the response is keyed to the LAST transform's output axis, and consequenceHazardPosition selects the axis the consequences read (0 = the raw component hazard — the omitted default; k = the signal after the k-th transform). Transform FUNCTIONS are distinct from the hazardTransform/probabilityTransform INTERPOLATION-space fields on tabular functions.",
                    "linearTransform and powerTransform evaluate inside [minimum, maximum] and clamp outside; both bounds are required and must cover the hazard table's full range (the model's own default range is only [0, 100]). Transform output labels (transformedHazard, transformedHazardUnit) are required — they define the axis the next function reads.",
                    "Recommended agent workflow: get_metadata, then get_example_request, then validate_risk_analysis until isValid, then run_risk_analysis. Mean-only runs on deterministic inputs complete in well under a second.",
                },
            };
        }
    }
}
