namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// Selects how a controlled tree deletion handles links that target the removed subtree.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public enum TreeDeletePolicy
    {
        /// <summary>Reject the deletion when any link targets the removed subtree.</summary>
        RejectIfReferenced = 0,

        /// <summary>Delete links that would otherwise become dangling.</summary>
        CascadeLinks = 1,

        /// <summary>Replace affected links with independent materialized copies before deletion.</summary>
        MaterializeLinks = 2,
    }
}
