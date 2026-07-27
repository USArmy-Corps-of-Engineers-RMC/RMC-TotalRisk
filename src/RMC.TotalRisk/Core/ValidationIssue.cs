using System;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Core
{
    /// <summary>A machine-readable model or analysis validation issue.</summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed record ValidationIssue
    {
        /// <summary>Initializes a structured validation issue.</summary>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="severity">The issue severity.</param>
        /// <param name="message">The caller-facing message without a severity prefix.</param>
        /// <param name="objectPath">The definition object path; empty when no narrower path is known.</param>
        /// <exception cref="ArgumentException">Thrown when the code is blank.</exception>
        public ValidationIssue(string code, DiagnosticSeverity severity, string? message, string? objectPath)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A validation issue code is required.", nameof(code));
            Code = code;
            Severity = severity;
            Message = message ?? string.Empty;
            ObjectPath = objectPath ?? string.Empty;
        }

        /// <summary>The stable machine-readable code.</summary>
        public string Code { get; }

        /// <summary>The issue severity.</summary>
        public DiagnosticSeverity Severity { get; }

        /// <summary>The caller-facing message without a severity prefix.</summary>
        public string Message { get; }

        /// <summary>The definition object path; empty when no narrower path is known.</summary>
        public string ObjectPath { get; }

        /// <summary>Converts a legacy prefixed validation message to a structured issue.</summary>
        /// <param name="message">The legacy message.</param>
        /// <param name="objectPath">The associated definition path.</param>
        /// <returns>The structured issue.</returns>
        public static ValidationIssue FromLegacyMessage(string? message, string? objectPath = null)
        {
            string text = message ?? string.Empty;
            DiagnosticSeverity severity;
            string code;
            if (text.StartsWith("Error: ", StringComparison.Ordinal))
            {
                severity = DiagnosticSeverity.Error;
                code = "TRV0001";
                text = text.Substring("Error: ".Length);
            }
            else if (text.StartsWith("Warning: ", StringComparison.Ordinal))
            {
                severity = DiagnosticSeverity.Warning;
                code = "TRV0002";
                text = text.Substring("Warning: ".Length);
            }
            else
            {
                severity = DiagnosticSeverity.Informational;
                code = "TRV0003";
            }
            return new ValidationIssue(code, severity, text, objectPath);
        }

        /// <summary>Formats the compatibility string used by the legacy validation API.</summary>
        /// <returns>The severity-prefixed message.</returns>
        public string ToLegacyMessage()
        {
            return Severity switch
            {
                DiagnosticSeverity.Error => $"Error: {Message}",
                DiagnosticSeverity.Warning => $"Warning: {Message}",
                _ => Message,
            };
        }
    }
}
