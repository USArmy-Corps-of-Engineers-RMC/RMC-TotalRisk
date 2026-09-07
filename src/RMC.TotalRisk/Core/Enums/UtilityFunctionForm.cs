namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The utility-function family of an expected-utility ranking declaration — constant
    /// absolute risk aversion (exponential) or constant relative risk aversion (power).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Member names are append-only serialized contract: utility declarations persist the form
    /// by name, so members are never renamed or reordered.
    /// </para>
    /// </remarks>
    public enum UtilityFunctionForm
    {
        /// <summary>
        /// The exponential (constant absolute risk aversion) family.
        /// </summary>
        ExponentialCara = 0,

        /// <summary>
        /// The power (constant relative risk aversion) family.
        /// </summary>
        PowerCrra = 1,
    }
}
