namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>Identifies the value carried by a tree probability source.</summary>
    public enum ProbabilitySourceKind
    {
        /// <summary>A fixed conditional probability.</summary>
        DeterministicScalar = 0,

        /// <summary>A co-monotonic uncertain probability table aligned to tree hazards.</summary>
        UncertainTabular = 1,

        /// <summary>A response function evaluated at the current tree hazard.</summary>
        ResponseFunctionReference = 2,
    }
}
