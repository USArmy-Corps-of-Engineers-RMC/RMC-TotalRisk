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
    }
}
