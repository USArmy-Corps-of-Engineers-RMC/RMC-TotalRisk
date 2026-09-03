namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// Selects whether a tree reference represents an independently sampled occurrence or the
    /// same logical event. Fault trees unify shared references onto one Boolean variable in the
    /// exact repeated-event algebra; event trees unify them onto one sampling class so a shared
    /// limb draws once per realization and computes identically at every occurrence.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public enum TreeLinkMode
    {
        /// <summary>The referenced subtree is evaluated as a distinct occurrence.</summary>
        IndependentClone = 0,

        /// <summary>The reference denotes the same logical event, sampled once per realization.</summary>
        SharedLogicalEvent = 1,
    }
}
