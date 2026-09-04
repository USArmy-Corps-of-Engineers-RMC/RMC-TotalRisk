namespace RMC.TotalRisk.Api.DTOs
{
    /// <summary>
    /// The wire-contract discriminator strings carried in each function DTO's "type" field.
    /// The set is append-only: new function kinds add new discriminators without moving the
    /// existing ones.
    /// </summary>
    public static class FunctionTypeNames
    {
        /// <summary>
        /// A deterministic tabular hazard (exceedance-probability vs. hazard) function.
        /// </summary>
        public const string TabularHazard = "tabularHazard";

        /// <summary>
        /// A deterministic tabular response (hazard vs. system response probability) function.
        /// </summary>
        public const string TabularResponse = "tabularResponse";

        /// <summary>
        /// A deterministic tabular consequence (hazard vs. consequence) function.
        /// </summary>
        public const string TabularConsequence = "tabularConsequence";

        /// <summary>
        /// A composite consequence combining weighted child consequence functions as an aleatory
        /// exposure mixture (e.g., day/night exposure weights).
        /// </summary>
        public const string CompositeMixture = "compositeMixture";
    }
}
