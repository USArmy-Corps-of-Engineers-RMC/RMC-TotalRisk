using RMC.TotalRisk.Api.DTOs;

namespace RMC.TotalRisk.Api.Services
{
    /// <summary>
    /// Builds the canned example compute request: a single dam with a deterministic reservoir
    /// stage-frequency hazard, three failure modes (overtopping erosion, backward erosion
    /// piping, concentrated leak erosion), day/night exposure-weighted life-loss mixtures on
    /// every failure mode, and a day/night non-fail mixture — the screening-tool model shape.
    /// </summary>
    public static class ExampleRequestFactory
    {
        /// <summary>
        /// The day exposure weight used throughout the example (14 waking hours of 24).
        /// </summary>
        private const double DayWeight = 0.58;

        /// <summary>
        /// The night exposure weight used throughout the example.
        /// </summary>
        private const double NightWeight = 0.42;

        /// <summary>
        /// Creates the example request. Every call returns a fresh instance safe to mutate.
        /// </summary>
        /// <returns>The example compute request.</returns>
        public static ComputeRiskAnalysisRequest CreateDamScreeningExample()
        {
            return new ComputeRiskAnalysisRequest
            {
                Name = "Example Dam Screening",
                Description = "A single-dam screening model: deterministic stage-frequency hazard, three tabular failure modes, and day/night exposure-weighted life-loss mixtures.",
                SpecifiedConsequence = "Life Loss",
                ConsequenceUnit = "lives",
                Components = new List<ComponentDto>
                {
                    new ComponentDto
                    {
                        Name = "Example Dam",
                        Hazard = new TabularHazardDto
                        {
                            Name = "Reservoir Stage-Frequency",
                            SpecifiedHazard = "Reservoir Stage",
                            HazardUnit = "ft",
                            ExceedanceProbabilities = new List<double> { 0.99, 0.5, 0.1, 0.02, 0.01, 0.002, 0.001, 0.0001, 0.00001 },
                            HazardValues = new List<double> { 1200, 1204, 1208, 1212, 1214, 1216, 1217, 1219, 1220 },
                        },
                        FailureModes = new List<FailureModeDto>
                        {
                            new FailureModeDto
                            {
                                Name = "Overtopping Erosion",
                                Response = new TabularResponseDto
                                {
                                    HazardValues = new List<double> { 1214, 1216, 1218, 1220 },
                                    ResponseProbabilities = new List<double> { 0, 0.01, 0.2, 0.8 },
                                },
                                Consequences = new List<ConsequenceFunctionDto>
                                {
                                    DayNightMixture("Overtopping Life Loss",
                                        new List<double> { 1200, 1214, 1216, 1220 },
                                        day: new List<double> { 0, 2, 20, 150 },
                                        night: new List<double> { 0, 4, 40, 300 }),
                                },
                            },
                            new FailureModeDto
                            {
                                Name = "Backward Erosion Piping",
                                Response = new TabularResponseDto
                                {
                                    HazardValues = new List<double> { 1206, 1212, 1218, 1220 },
                                    ResponseProbabilities = new List<double> { 0, 0.005, 0.05, 0.15 },
                                },
                                Consequences = new List<ConsequenceFunctionDto>
                                {
                                    DayNightMixture("Backward Erosion Piping Life Loss",
                                        new List<double> { 1200, 1212, 1216, 1220 },
                                        day: new List<double> { 0, 1, 10, 80 },
                                        night: new List<double> { 0, 2, 25, 160 }),
                                },
                            },
                            new FailureModeDto
                            {
                                Name = "Concentrated Leak Erosion",
                                Response = new TabularResponseDto
                                {
                                    HazardValues = new List<double> { 1208, 1214, 1219, 1220 },
                                    ResponseProbabilities = new List<double> { 0, 0.002, 0.03, 0.1 },
                                },
                                Consequences = new List<ConsequenceFunctionDto>
                                {
                                    DayNightMixture("Concentrated Leak Erosion Life Loss",
                                        new List<double> { 1200, 1212, 1217, 1220 },
                                        day: new List<double> { 0, 1, 8, 60 },
                                        night: new List<double> { 0, 2, 18, 120 }),
                                },
                            },
                        },
                        NonFailConsequences = new List<ConsequenceFunctionDto>
                        {
                            DayNightMixture("Non-Failure Life Loss",
                                new List<double> { 1200, 1210, 1216, 1220 },
                                day: new List<double> { 0, 0, 0.5, 5 },
                                night: new List<double> { 0, 0, 1, 10 }),
                        },
                    },
                },
                Options = new RiskAnalysisOptionsDto
                {
                    PrngSeed = 12345,
                    OutputAdjustedFailureModeCurves = true,
                },
            };
        }

        /// <summary>
        /// Builds one day/night composite-mixture consequence over a shared stage axis.
        /// </summary>
        /// <param name="name">The composite's name (labels the owning results row).</param>
        /// <param name="hazardValues">The shared stage axis.</param>
        /// <param name="day">The day life-loss ordinates.</param>
        /// <param name="night">The night life-loss ordinates.</param>
        /// <returns>The composite consequence DTO.</returns>
        private static ConsequenceFunctionDto DayNightMixture(string name, List<double> hazardValues,
            List<double> day, List<double> night)
        {
            return new ConsequenceFunctionDto
            {
                Type = FunctionTypeNames.CompositeMixture,
                Name = name,
                Branches = new List<ConsequenceBranchDto>
                {
                    new ConsequenceBranchDto
                    {
                        Weight = DayWeight,
                        Function = new ConsequenceFunctionDto
                        {
                            Type = FunctionTypeNames.TabularConsequence,
                            Name = $"{name} - Day",
                            HazardValues = new List<double>(hazardValues),
                            ConsequenceValues = day,
                        },
                    },
                    new ConsequenceBranchDto
                    {
                        Weight = NightWeight,
                        Function = new ConsequenceFunctionDto
                        {
                            Type = FunctionTypeNames.TabularConsequence,
                            Name = $"{name} - Night",
                            HazardValues = new List<double>(hazardValues),
                            ConsequenceValues = night,
                        },
                    },
                },
            };
        }
    }
}
