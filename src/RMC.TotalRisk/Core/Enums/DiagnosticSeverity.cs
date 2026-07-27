namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>The severity of a structured validation issue or computation diagnostic.</summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public enum DiagnosticSeverity
    {
        /// <summary>Context that does not require caller action.</summary>
        Informational = 0,

        /// <summary>A condition callers should review that does not invalidate the result.</summary>
        Warning = 1,

        /// <summary>A condition that invalidates a definition or computation.</summary>
        Error = 2,
    }
}
