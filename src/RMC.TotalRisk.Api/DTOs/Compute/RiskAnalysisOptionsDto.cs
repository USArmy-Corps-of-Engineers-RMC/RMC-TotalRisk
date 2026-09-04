using System.Text.Json.Serialization;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// The risk-analysis engine settings. Every field is optional — null keeps the engine
    /// default — with two API-level behaviors documented on the fields themselves:
    /// <see cref="EstimateMeanRiskOnly"/> must be true (or omitted) in this contract version, and
    /// <see cref="OutputAdjustedFailureModeCurves"/> defaults to TRUE on the API (the engine
    /// default is false) so both adjusted and unadjusted failure-mode results come back without
    /// extra configuration.
    /// </summary>
    public class RiskAnalysisOptionsDto
    {
        /// <summary>
        /// Whether the run estimates mean risk only (a single pass on the expected input
        /// functions). Defaults to true. This contract version accepts only true — a false value
        /// is rejected with the structured code API_MEAN_ONLY_REQUIRED; full-uncertainty compute
        /// arrives in a later contract increment.
        /// </summary>
        [JsonPropertyName("estimateMeanRiskOnly")]
        public bool? EstimateMeanRiskOnly { get; set; }

        /// <summary>
        /// The number of knowledge-uncertainty realizations in a full run, in [100, 10000].
        /// Unused on a mean-only run.
        /// </summary>
        [JsonPropertyName("realizations")]
        public int? Realizations { get; set; }

        /// <summary>
        /// The width of the reported confidence interval, in (0, 1).
        /// </summary>
        [JsonPropertyName("confidenceIntervalWidth")]
        public double? ConfidenceIntervalWidth { get; set; }

        /// <summary>
        /// The positive pseudo-random seed folded with each component's content hash to derive
        /// sampling seeds. Default 12345. Same inputs + same seed = bit-identical results.
        /// </summary>
        [JsonPropertyName("prngSeed")]
        public int? PrngSeed { get; set; }

        /// <summary>
        /// The output resolution of stored loss exceedance curves, in [50, 1000]. Default 200.
        /// An output knob only — statistics are computed exactly before thinning.
        /// </summary>
        [JsonPropertyName("lecOutputLength")]
        public int? LecOutputLength { get; set; }

        /// <summary>
        /// The knowledge-uncertainty sampling scheme ("monteCarlo", "latinHypercube",
        /// "latinHypercubeMedian", "scrambledSobol"). Null keeps the default (latinHypercube).
        /// </summary>
        [JsonPropertyName("samplingScheme")]
        public SamplingScheme? SamplingScheme { get; set; }

        /// <summary>
        /// What the analysis computes ("risk" or "reliability"). Null keeps the default (risk).
        /// </summary>
        [JsonPropertyName("mode")]
        public RiskAnalysisMode? Mode { get; set; }

        /// <summary>
        /// The adaptive-refinement objective steering where the risk integrator concentrates its
        /// evaluations. All curves and measures are produced regardless. Null keeps the default
        /// (meanTotalRisk).
        /// </summary>
        [JsonPropertyName("riskIntegrand")]
        public RiskIntegrand? RiskIntegrand { get; set; }

        /// <summary>
        /// The system risk aggregation method ("additiveRiskMethod" for strictly independent
        /// components, "jointRiskMethod" for dependent ones). Null keeps the default (additive).
        /// </summary>
        [JsonPropertyName("systemRiskMethod")]
        public SystemRiskType? SystemRiskMethod { get; set; }

        /// <summary>
        /// How consequences combine when multiple components fail jointly ("additive", "average",
        /// "maximum", "minimum"). Null keeps the default (additive).
        /// </summary>
        [JsonPropertyName("jointConsequences")]
        public JointConsequenceType? JointConsequences { get; set; }

        /// <summary>
        /// The statistical dependence between component hazards (joint method only). Null keeps
        /// the default (independent).
        /// </summary>
        [JsonPropertyName("componentHazardDependency")]
        public DependencyType? ComponentHazardDependency { get; set; }

        /// <summary>
        /// The cross-component hazard correlation matrix (one row per component, positive
        /// definite). Required only under the "correlationMatrix" component hazard dependency.
        /// </summary>
        [JsonPropertyName("hazardCorrelationMatrix")]
        public List<List<double>>? HazardCorrelationMatrix { get; set; }

        /// <summary>
        /// The consequence threshold behind the assurance measure P(C &gt; threshold), in the
        /// primary consequence type's units. Null keeps the default (0).
        /// </summary>
        [JsonPropertyName("consequenceThreshold")]
        public double? ConsequenceThreshold { get; set; }

        /// <summary>
        /// The exceedance level for value-at-risk and conditional value-at-risk. Null keeps the
        /// default (0.01).
        /// </summary>
        [JsonPropertyName("alpha")]
        public double? Alpha { get; set; }

        /// <summary>
        /// Integration knob: the adaptive integrator's evaluation cap, in [10000, 1000000].
        /// Supplying any integration knob switches the engine off its automatic
        /// component-count-scaled defaults for ALL of them.
        /// </summary>
        [JsonPropertyName("maxEvaluations")]
        public int? MaxEvaluations { get; set; }

        /// <summary>
        /// Integration knob: the adaptive integrator's recursion depth cap, in [10, 500].
        /// </summary>
        [JsonPropertyName("maxDepth")]
        public int? MaxDepth { get; set; }

        /// <summary>
        /// Integration knob: the adaptive integrator's relative tolerance, in [1e-15, 0.01].
        /// The mean pass (and every mean-only run) uses this tolerance.
        /// </summary>
        [JsonPropertyName("tolerance")]
        public double? Tolerance { get; set; }

        /// <summary>
        /// Integration knob: the per-realization relative tolerance inside full-uncertainty
        /// ensembles, in [1e-15, 0.01]. Unused on a mean-only run.
        /// </summary>
        [JsonPropertyName("ensembleTolerance")]
        public double? EnsembleTolerance { get; set; }

        /// <summary>
        /// Integration knob: the minimum subdivision depth inside full-uncertainty ensemble
        /// realizations, in [0, 10]. Unused on a mean-only run.
        /// </summary>
        [JsonPropertyName("ensembleMinDepth")]
        public int? EnsembleMinDepth { get; set; }

        /// <summary>
        /// Integration knob: the VEGAS warm-up evaluations per cycle (joint method), in
        /// [100, 50000].
        /// </summary>
        [JsonPropertyName("warmupEvaluations")]
        public int? WarmupEvaluations { get; set; }

        /// <summary>
        /// Integration knob: the VEGAS warm-up cycles (joint method), in [1, 100].
        /// </summary>
        [JsonPropertyName("warmupCycles")]
        public int? WarmupCycles { get; set; }

        /// <summary>
        /// Integration knob: the VEGAS recording-pass evaluations (joint method), in
        /// [1000, 100000].
        /// </summary>
        [JsonPropertyName("finalEvaluations")]
        public int? FinalEvaluations { get; set; }

        /// <summary>
        /// How the joint method sets the VEGAS power-transform tail focus ("none", "automatic",
        /// "manual"). Null keeps the default (automatic).
        /// </summary>
        [JsonPropertyName("vegasTailFocusMode")]
        public VegasTailFocusMode? VegasTailFocusMode { get; set; }

        /// <summary>
        /// The manual VEGAS tail-focus parameter γ, in [1, 20]; 1 is the identity transform.
        /// </summary>
        [JsonPropertyName("vegasTailFocusParameter")]
        public double? VegasTailFocusParameter { get; set; }

        /// <summary>
        /// The output resolution of the additive method's FFT system convolution, in
        /// [4096, 1048576].
        /// </summary>
        [JsonPropertyName("systemConvolutionPoints")]
        public int? SystemConvolutionPoints { get; set; }

        /// <summary>
        /// The cap on exclusive component-failure combinations enumerated per joint-system
        /// evaluation. Reaching it truncates and reports a computation warning.
        /// </summary>
        [JsonPropertyName("maxSystemCombinations")]
        public int? MaxSystemCombinations { get; set; }

        /// <summary>
        /// The cap on exclusive failure pathways enumerated per component per evaluation under
        /// the joint failure-mode method.
        /// </summary>
        [JsonPropertyName("maxPathwayCombinations")]
        public int? MaxPathwayCombinations { get; set; }

        /// <summary>
        /// Which optional risk measures to compute, as flag names ("higherMoments",
        /// "valueAtRisk", "thresholdProbabilities", "riskProfiles", "contributions", "all",
        /// "none"). Entries are OR-combined. Null keeps the default (all). The mean, standard
        /// deviation, total probability, and mass balance are always computed; a disabled
        /// measure returns null.
        /// </summary>
        [JsonPropertyName("riskMeasures")]
        public List<RiskMeasureOptions>? RiskMeasures { get; set; }

        /// <summary>
        /// Whether each failure mode also reports its combination-adjusted curves (its share
        /// after the component's combination method resolves the modes, summing to the component
        /// total) alongside its raw unadjusted marginal curves. API default: TRUE (the engine
        /// default is false) — both views come back unless explicitly disabled.
        /// </summary>
        [JsonPropertyName("outputAdjustedFailureModeCurves")]
        public bool? OutputAdjustedFailureModeCurves { get; set; }

        /// <summary>
        /// Whether the joint (VEGAS) integration drives its sampling with seeded scrambled Sobol
        /// points instead of the seeded pseudo-random generator. Null keeps the default (false).
        /// </summary>
        [JsonPropertyName("useSobolJointSampling")]
        public bool? UseSobolJointSampling { get; set; }
    }
}
