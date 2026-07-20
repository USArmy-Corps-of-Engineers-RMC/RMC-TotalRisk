namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// How a risk graph serializes the input functions its elements wrap: with their content
    /// inline, or as references to functions the consuming layer stores and owns separately.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// This is a persistence concern only. It never reaches the canonical-hash identity surface:
    /// a system component hashes its <b>projected failure modes</b>, which always serialize their
    /// functions inline, so a component's hash — and every Monte Carlo seed derived from it — is
    /// identical whichever mode its graph was persisted in.
    /// </para>
    /// <para>
    /// Both modes always write the wrapped function's id and name; they differ only in whether the
    /// function's own content travels with the graph.
    /// </para>
    /// </remarks>
    public enum RiskSerializationMode
    {
        /// <summary>
        /// Function content is written inline, so the serialized form stands alone and can be read
        /// back with no external context. The default, and the mode headless callers, verification
        /// oracles, and the REST/MCP API use.
        /// </summary>
        SelfContained,

        /// <summary>
        /// Only the wrapped functions' ids and names are written; their content lives wherever the
        /// consuming layer stores it. Reading such a form requires an
        /// <c>IRiskFunctionResolver</c> over those stored functions, which re-attaches the live
        /// instances — so an edit made to a function in one place is seen everywhere it is used,
        /// rather than being shadowed by a stale embedded copy.
        /// </summary>
        ByReference,
    }
}
