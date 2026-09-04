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
}
