namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// Selects whether a tree reference represents an independently sampled occurrence or the
    /// same logical event. Event trees admit only <see cref="IndependentClone"/>.
    /// </summary>
    public enum TreeLinkMode
    {
        /// <summary>The referenced subtree is evaluated as a distinct occurrence.</summary>
        IndependentClone = 0,

        /// <summary>The reference denotes the same Boolean event (fault trees only).</summary>
        SharedLogicalEvent = 1,
    }
}
