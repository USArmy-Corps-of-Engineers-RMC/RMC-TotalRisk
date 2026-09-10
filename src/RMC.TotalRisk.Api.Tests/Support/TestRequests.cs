using Numerics.Data;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Api.Services;

namespace RMC.TotalRisk.Api.Tests.Support;

/// <summary>
/// Canned compute requests for the test suites: the screening example, the expected-annual-damage
/// (background-risk) scenario with its exact closed form, and helpers to derange a request.
/// </summary>
public static class TestRequests
{
    /// <summary>
    /// The exact closed-form expected annual damage of the eight-knot damage-frequency scenario
    /// (trapezoids plus the endpoint clamp rectangles of the piecewise-linear curve).
    /// </summary>
    public const double EadClosedFormMean = 52085.41;

    /// <summary>The closed-form standard deviation of the same scenario.</summary>
    public const double EadClosedFormStandardDeviation = 112502.46;

    /// <summary>The closed-form value at risk at alpha = 0.01 (the knot damage).</summary>
    public const double EadClosedFormValueAtRisk = 395563;

    /// <summary>The closed-form conditional value at risk at alpha = 0.01.</summary>
    public const double EadClosedFormConditionalValueAtRisk = 614983.50;

    /// <summary>Creates the day/night screening example request.</summary>
    /// <returns>A fresh example request.</returns>
    public static ComputeRiskAnalysisRequest DamScreening() => ExampleRequestFactory.CreateDamScreeningExample();

    /// <summary>
    /// Creates the expected-annual-damage scenario: the eight-knot exceedance/damage curve as the
    /// component hazard with a piecewise-linear identity consequence on a response-free non-fail
    /// path (zero failure modes) — the pure background-risk mapping whose total mean equals the
    /// closed-form EAD.
    /// </summary>
    /// <returns>The EAD compute request.</returns>
    public static ComputeRiskAnalysisRequest ExpectedAnnualDamage()
    {
        return new ComputeRiskAnalysisRequest
        {
            Name = "Expected Annual Damage",
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
            Components = new List<ComponentDto>
            {
                new ComponentDto
                {
                    Name = "Damage Reach",
                    Hazard = new TabularHazardDto
                    {
                        Name = "Damage-Frequency",
                        SpecifiedHazard = "Damage",
                        HazardUnit = "$",
                        ExceedanceProbabilities = new List<double> { 0.5, 0.2, 0.1, 0.04, 0.02, 0.01, 0.005, 0.002 },
                        HazardValues = new List<double> { 212, 24545, 275766, 296022, 333920, 395563, 448005, 962545 },
                        // The closed form integrates the curve linear-in-probability; the model
                        // default (normal-Z probability interpolation) is a different curve.
                        ProbabilityTransform = Transform.None,
                    },
                    NonFailConsequences = new List<ConsequenceFunctionDto>
                    {
                        new ConsequenceFunctionDto
                        {
                            Type = FunctionTypeNames.TabularConsequence,
                            Name = "Identity Damages",
                            HazardValues = new List<double> { 212, 962545 },
                            ConsequenceValues = new List<double> { 212, 962545 },
                        },
                    },
                },
            },
            Options = new RiskAnalysisOptionsDto
            {
                Alpha = 0.01,
            },
        };
    }

    /// <summary>
    /// Creates the untransformed half of the transform-equivalence twin: one mode whose
    /// fragility is keyed directly to the stage axis.
    /// </summary>
    /// <returns>The baseline compute request.</returns>
    public static ComputeRiskAnalysisRequest TransformTwinBaseline() => TransformTwin(shifted: false);

    /// <summary>
    /// Creates the transformed half of the twin: the same scenario routed through a linear
    /// stage shift (alpha = -1210, beta = 1) with the fragility re-keyed to the shifted axis
    /// and the consequences bound back to the raw stage axis (position 0). The shift is exact
    /// in floating point (Sterbenz: 1210/2 = 605 &lt;= every evaluated stage &lt;= 2·1210), so
    /// the twin's results match the baseline bit for bit.
    /// </summary>
    /// <returns>The shifted compute request.</returns>
    public static ComputeRiskAnalysisRequest TransformTwinShifted() => TransformTwin(shifted: true);

    /// <summary>
    /// Builds the shared transform-equivalence scenario: a four-knot stage-frequency hazard
    /// over [1200, 1220] ft, one overtopping mode, and a stage-keyed tabular life-loss
    /// consequence.
    /// </summary>
    /// <param name="shifted">Whether to route the fragility through the exact stage shift.</param>
    /// <returns>The compute request.</returns>
    private static ComputeRiskAnalysisRequest TransformTwin(bool shifted)
    {
        var mode = new FailureModeDto
        {
            Name = "Overtopping",
            Response = new TabularResponseDto
            {
                HazardValues = shifted
                    ? new List<double> { 4, 6, 8, 10 }
                    : new List<double> { 1214, 1216, 1218, 1220 },
                ResponseProbabilities = new List<double> { 0, 0.01, 0.2, 0.8 },
            },
            Consequences = new List<ConsequenceFunctionDto>
            {
                new ConsequenceFunctionDto
                {
                    HazardValues = new List<double> { 1200, 1214, 1220 },
                    ConsequenceValues = new List<double> { 0, 10, 500 },
                },
            },
        };
        if (shifted)
        {
            mode.Transforms = new List<TransformFunctionDto>
            {
                new TransformFunctionDto
                {
                    Type = FunctionTypeNames.LinearTransform,
                    Name = "Exact Stage Shift",
                    TransformedHazard = "Shifted Stage",
                    TransformedHazardUnit = "ft",
                    Alpha = -1210,
                    Beta = 1,
                    Minimum = -1e9,
                    Maximum = 1e9,
                },
            };
            mode.ConsequenceHazardPosition = 0;
        }

        return new ComputeRiskAnalysisRequest
        {
            Name = "Transform Equivalence Twin",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            Components = new List<ComponentDto>
            {
                new ComponentDto
                {
                    Name = "Dam",
                    Hazard = new TabularHazardDto
                    {
                        Name = "Stage-Frequency",
                        SpecifiedHazard = "Stage",
                        HazardUnit = "ft",
                        ExceedanceProbabilities = new List<double> { 0.5, 0.1, 0.01, 0.001 },
                        HazardValues = new List<double> { 1200, 1210, 1216, 1220 },
                    },
                    FailureModes = new List<FailureModeDto> { mode },
                },
            },
            Options = new RiskAnalysisOptionsDto
            {
                PrngSeed = 12345,
            },
        };
    }
}
