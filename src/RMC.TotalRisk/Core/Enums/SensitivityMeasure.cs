namespace RMC.TotalRisk.Core.Enums
{
    /// <summary>
    /// The association measure a sensitivity analysis reports between a sampled knowledge input
    /// and a risk output (the legacy v1.0 member names preserved).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Runtime-only — never serialized (no hash surface). <see cref="SensitivityIndex"/> is the
    /// squared Pearson correlation, the v1.0 definition: because the unified engine's inputs
    /// are the independent per-function knowledge draws (near-orthogonal under Latin hypercube
    /// stratification), r² estimates the same main-effect variance share as the regression form
    /// of the RMC-TotalRisk Technical Reference Manual, Appendix G (Eq. 249–250), and the
    /// indices sum to at most one across inputs.
    /// </para>
    /// <para>
    /// The given-data members route through the Numerics given-data global-sensitivity
    /// estimators over the same stored realizations, under the documented 20-bin convention:
    /// they see nonlinear and (for the moment-independent pair) distribution-shape effects the
    /// correlation members cannot, and they require at least as many valid realization pairs
    /// as bins — a smaller sample returns no result rather than a noise-dominated one.
    /// </para>
    /// </remarks>
    public enum SensitivityMeasure
    {
        /// <summary>
        /// Pearson's linear correlation coefficient between the input draws and the output.
        /// </summary>
        PearsonCorrelation = 0,

        /// <summary>
        /// Spearman's rank correlation — Pearson over fractional ranks; exact under any
        /// monotone re-expression of the input, preferred for curvilinear monotone relations.
        /// </summary>
        SpearmanCorrelation = 1,

        /// <summary>
        /// The sensitivity index — squared Pearson correlation, read as the input's fractional
        /// contribution to the output variance.
        /// </summary>
        SensitivityIndex = 2,

        /// <summary>
        /// The given-data first-order Sobol index: the variance of the conditional output mean
        /// across equal-frequency input bins over the output variance — the main-effect
        /// variance share without the linearity assumption of <see cref="SensitivityIndex"/>.
        /// </summary>
        FirstOrderSobol = 3,

        /// <summary>
        /// The PAWN median: the median across input bins of the Kolmogorov–Smirnov distance
        /// between the conditional and unconditional output distributions — moment-independent,
        /// sensitive to tail and shape effects a variance share cannot see.
        /// </summary>
        PawnMedian = 4,

        /// <summary>
        /// Borgonovo's delta via the double-histogram total-variation estimator over rank
        /// classes — moment-independent and exactly invariant under monotone re-expression of
        /// the input.
        /// </summary>
        BorgonovoDelta = 5,
    }
}
