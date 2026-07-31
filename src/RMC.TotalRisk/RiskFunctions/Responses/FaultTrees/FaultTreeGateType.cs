namespace RMC.TotalRisk.RiskFunctions.Responses.FaultTrees
{
    /// <summary>
    /// The Boolean combination applied by a fault-tree gate. Members are append-only serialized
    /// canonical-hash content.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public enum FaultTreeGateType
    {
        /// <summary>The gate output is true when every input is true.</summary>
        And = 0,

        /// <summary>The gate output is true when at least one input is true.</summary>
        Or = 1,

        /// <summary>The gate output is true when exactly one of its two inputs is true.</summary>
        Xor = 2,

        /// <summary>The gate output is true when at least <c>K</c> inputs are true.</summary>
        KOfN = 3,
    }
}
