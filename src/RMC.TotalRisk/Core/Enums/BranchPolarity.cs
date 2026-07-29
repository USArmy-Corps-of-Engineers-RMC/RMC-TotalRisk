namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// Identifies which branch of a response chance node a failure-mode stage follows: the Fail
    /// branch (the fragility probability) or the Non-Fail branch (its complement).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The cascading end-state design (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §7.9).
    /// The numeric values are explicit
    /// because they double as the response element's output-port indices: port 0 is the Fail
    /// branch every v1.0-era connection already targets, port 1 is the Non-Fail branch the cascade
    /// design adds. A stage's contribution to its failure mode's system response probability is
    /// <c>p(h)</c> under <see cref="Fail"/> and <c>1 − p(h)</c> under <see cref="NonFail"/>, where
    /// <c>p</c> is the stage response's fragility at the stage-transformed hazard.
    /// </para>
    /// <para>
    /// This enum is serialized (the <c>BranchPolarity</c> attribute on <c>ResponseStage</c>) and
    /// therefore canonical-hash content — member names and values are append-only contract, unlike
    /// the runtime-only discriminator enums.
    /// </para>
    /// </remarks>
    public enum BranchPolarity
    {
        /// <summary>
        /// The Fail branch — output port 0 (the v1.0-implied default); the stage contributes the
        /// fragility probability <c>p(h)</c> to the polarity product.
        /// </summary>
        Fail = 0,

        /// <summary>
        /// The Non-Fail branch — output port 1; the stage contributes the complement
        /// <c>1 − p(h)</c> to the polarity product.
        /// </summary>
        NonFail = 1,
    }
}
