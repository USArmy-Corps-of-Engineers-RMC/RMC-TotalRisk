namespace RMC.TotalRisk.Api.Configuration
{
    /// <summary>
    /// The API wire-contract identity.
    /// </summary>
    public static class ApiContractInfo
    {
        /// <summary>
        /// The wire-contract version of the compute request/response DTOs. Additive contract
        /// changes keep the major version; breaking changes increment it.
        /// </summary>
        public const string Version = "1.0.0";
    }
}
