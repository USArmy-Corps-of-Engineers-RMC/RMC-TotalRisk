namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// How the joint system-risk method sets the VEGAS power-transform tail-focus parameter γ,
    /// which concentrates multi-dimensional samples in the upper hazard tail via
    /// <c>p' = 1 − (1 − p)^γ</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Plain VEGAS minimizes overall variance and therefore under-samples the extreme tail where
    /// rare high-consequence failures live — the documented v1.0 limitation on joint system loss
    /// exceedance curves. The power transform (reference [16] in <c>docs/references.md</c>;
    /// <c>docs/technical-reference/risk-integration.md</c>) corrects that by mapping the sampling
    /// probability toward the tail, with the Jacobian folded into the integration weight so the
    /// recorded probability mass stays unbiased.
    /// </para>
    /// <para>
    /// This is an analysis option (a hashed field on <c>RiskAnalysisOptions</c>) read only by the
    /// joint method; the additive method ignores it. γ = 1 is the identity transform and exactly
    /// reproduces v1.0 sampling behavior.
    /// </para>
    /// </remarks>
    public enum VegasTailFocusMode
    {
        /// <summary>
        /// Pin γ = 1 (identity transform) — exact v1.0-comparable sampling with no tail focus.
        /// </summary>
        None,

        /// <summary>
        /// Derive γ from the warm-up pass: the observed per-component failure probabilities set a
        /// rare-event target <c>pTarget = clamp(min P̂_f,i · α, 1e-12, 1e-2)</c>, handed to the
        /// VEGAS rare-event configuration. Deterministic and costless — the warm-up already runs.
        /// The options default.
        /// </summary>
        Automatic,

        /// <summary>
        /// Use the user-supplied tail-focus parameter γ from the analysis options directly.
        /// </summary>
        Manual,
    }
}
