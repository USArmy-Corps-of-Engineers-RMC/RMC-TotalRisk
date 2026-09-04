using RMC.TotalRisk.Api.DTOs;

namespace RMC.TotalRisk.Api.Services.Exceptions
{
    /// <summary>
    /// Thrown when a compute or validate request fails request-shape checks or model-layer
    /// validation. Maps to HTTP 400 with the structured issue list in the response body.
    /// </summary>
    public class RequestValidationException : Exception
    {
        /// <summary>
        /// Initializes the exception with the structured issue list.
        /// </summary>
        /// <param name="message">The summary message for the response's errorMessage field.</param>
        /// <param name="issues">The structured validation issues (errors and any accompanying warnings).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="issues"/> is null.</exception>
        public RequestValidationException(string message, IReadOnlyList<ValidationIssueDto> issues)
            : base(message)
        {
            Issues = issues ?? throw new ArgumentNullException(nameof(issues));
        }

        /// <summary>
        /// The structured validation issues carried to the response body.
        /// </summary>
        public IReadOnlyList<ValidationIssueDto> Issues { get; }
    }
}
