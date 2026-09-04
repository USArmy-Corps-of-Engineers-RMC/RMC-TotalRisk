namespace RMC.TotalRisk.Api.Configuration
{
    /// <summary>
    /// Configurable limits for the REST API host, bound from the "Api" configuration section.
    /// </summary>
    /// <remarks>
    /// The compute surface is stateless (nothing is stored between requests), so the limits guard
    /// the host itself: CPU oversubscription from concurrent engine runs and pathological request
    /// payloads that would consume unbounded memory during model construction.
    /// </remarks>
    public class ApiOptions
    {
        /// <summary>
        /// The configuration section name the options are bound from.
        /// </summary>
        public const string SectionName = "Api";

        /// <summary>
        /// The maximum number of risk analyses allowed to compute concurrently. Full-uncertainty
        /// engine runs parallelize internally, so this throttle prevents CPU oversubscription when
        /// multiple clients compute at the same time. Default = 4.
        /// </summary>
        public int MaxConcurrentRuns { get; set; } = 4;

        /// <summary>
        /// The maximum number of system components accepted in one compute request. Guards model
        /// construction against pathological payloads; the engine itself warns on joint-method
        /// systems above 20 components. Default = 64.
        /// </summary>
        public int MaxComponents { get; set; } = 64;

        /// <summary>
        /// The maximum number of ordinates accepted in any one tabular function payload. Guards
        /// model construction and result serialization against pathological table sizes.
        /// Default = 10,000.
        /// </summary>
        public int MaxOrdinatesPerTable { get; set; } = 10_000;
    }
}
