namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The extrapolation policy applied when a tabular or nonparametric function is evaluated
    /// outside its table range.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The default, <see cref="None"/>, holds the endpoint ordinates outside the table — the
    /// historical behavior, bit-identical for every existing model. <see cref="Below"/>,
    /// <see cref="Above"/>, and <see cref="Both"/> extend the boundary segments linearly in the
    /// function's configured transform spaces (the Numerics
    /// <c>ExtrapolationSides</c> semantics). <see cref="Error"/> refuses out-of-range forward
    /// evaluation loudly — for life-safety review where a silent extrapolation far beyond the
    /// data is worse than a stopped run — while probability-axis inverse lookups retain the
    /// endpoint hold (the engine probes them to locate the table's own span).
    /// </para>
    /// <para>
    /// The policy is serialized by enum name only when non-default, so every pre-existing
    /// serialized form, canonical hash, and seed is unchanged; a non-default policy is
    /// compute-relevant hashed content and deliberately re-rolls the owning function's sampling
    /// streams. This is not a flags enum: <see cref="Both"/> is the only combination, and
    /// <see cref="Error"/> stands alone.
    /// </para>
    /// </remarks>
    public enum ExtrapolationPolicy
    {
        /// <summary>
        /// Hold the endpoint ordinates outside the table range (the historical default).
        /// </summary>
        None = 0,

        /// <summary>
        /// Extend the first boundary segment linearly, in transform space, below the table.
        /// </summary>
        Below = 1,

        /// <summary>
        /// Extend the last boundary segment linearly, in transform space, above the table.
        /// </summary>
        Above = 2,

        /// <summary>
        /// Extend both boundary segments.
        /// </summary>
        Both = 3,

        /// <summary>
        /// Refuse out-of-range forward evaluation with a loud diagnostic naming the function,
        /// axis, offending value, and table range.
        /// </summary>
        Error = 4,
    }
}
