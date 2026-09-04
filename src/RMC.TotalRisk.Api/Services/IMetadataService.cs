using RMC.TotalRisk.Api.DTOs;

namespace RMC.TotalRisk.Api.Services
{
    /// <summary>
    /// The discovery facade: enumerates the contract's enums, function kinds, defaults, limits,
    /// and conventions so clients (especially agents) never guess a wire value.
    /// </summary>
    public interface IMetadataService
    {
        /// <summary>
        /// Builds the metadata body.
        /// </summary>
        /// <returns>The metadata response.</returns>
        MetadataResponse GetMetadata();
    }
}
