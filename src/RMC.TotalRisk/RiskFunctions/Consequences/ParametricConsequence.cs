using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Numerics.Data.Statistics;
using Numerics.Distributions;
using Numerics.Functions;
using Numerics.Sampling;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Consequences
{
    /// <summary>
    /// A closed-form parametric consequence function: C(h) = clamp(α · max(h − h₀, 0)^β, 0, U),
    /// with optional log-space knowledge uncertainty on the two coefficients. Zero consequence at
    /// and below the damage-initiation threshold h₀, power-law growth above it, and saturation at
    /// the upper bound U.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// New in v1.1 (no v1.0 ancestor); the form follows architecture doc §6.4 and USACE
    /// depth-damage conventions (ER 1110-2-1156 / HEC-FDA). The power law is the best-fit
    /// parametric form for observed depth-damage data in the literature, the threshold captures
    /// the damage-initiation depth, and the cap reflects saturation at maximum damage.
    /// </para>
    /// <para>
    /// Knowledge uncertainty (when <see cref="IsUncertain"/>): each realization draws independent
    /// standard normal deviates z₁, z₂ and realizes α_i = α·exp(σ_α·z₁), β_i = β·exp(σ_β·z₂) —
    /// multiplicative log-space scatter that preserves coefficient positivity. Sampling dimension
    /// D = 2. <see cref="SampleFunction()"/> returns the <b>nominal (median) curve</b> at (α, β),
    /// not the analytic mean: with lognormal exponent scatter and no cap, the mean of
    /// (h − h₀)^{β_i} is the lognormal moment-generating function evaluated at ln(h − h₀), which
    /// diverges for h &gt; h₀ + 1 — only a finite cap keeps the mean bounded.
    /// <see cref="Validate"/> warns about that configuration.
    /// </para>
    /// <para>
    /// Serialization: σ_α and σ_β are written only while <see cref="IsUncertain"/> is true —
    /// recipe-literal conditional hashing (the <c>SystemComponent</c> correlation-matrix
    /// precedent), so toggling uncertainty off both removes the sigmas from the hash surface and
    /// discards them on the next save (the UI layer preserves in-memory values across toggles).
    /// </para>
    /// </remarks>
    public class ParametricConsequence : ConsequenceFunctionBase
    {
        #region Construction

        /// <summary>
        /// Initializes a parametric consequence function with the cluster defaults:
        /// α = 1, β = 1.5, h₀ = 0, no saturation cap, deterministic.
        /// </summary>
        public ParametricConsequence()
        {
        }

        /// <summary>
        /// Restores a parametric consequence function from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public ParametricConsequence(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            ReadIdentityAttributes(xElement);
            SpecifiedHazard = SerializationUtilities.ReadString(xElement, nameof(SpecifiedHazard));
            HazardUnit = SerializationUtilities.ReadString(xElement, nameof(HazardUnit));
            SpecifiedConsequence = SerializationUtilities.ReadString(xElement, nameof(SpecifiedConsequence));
            ConsequenceUnit = SerializationUtilities.ReadString(xElement, nameof(ConsequenceUnit));
            _alpha = SerializationUtilities.ReadDouble(xElement, nameof(Alpha), 1d);
            _beta = SerializationUtilities.ReadDouble(xElement, nameof(Beta), 1.5d);
            _threshold = SerializationUtilities.ReadDouble(xElement, nameof(Threshold), 0d);
            _upperBound = SerializationUtilities.ReadDouble(xElement, nameof(UpperBound), double.PositiveInfinity);
            _isUncertain = SerializationUtilities.ReadBoolean(xElement, nameof(IsUncertain), false);
            // Sigmas are persisted only while uncertain; reading the defaults when absent keeps
            // older or hand-trimmed forms loadable.
            _sigmaAlpha = SerializationUtilities.ReadDouble(xElement, nameof(SigmaAlpha), 0d);
            _sigmaBeta = SerializationUtilities.ReadDouble(xElement, nameof(SigmaBeta), 0d);
        }

        #endregion

        #region Members

        /// <summary>
        /// The number of internal realizations behind <see cref="ComputeUncertaintyResults"/>.
        /// </summary>
        private const int SummaryRealizations = 10_000;

        /// <summary>
        /// The fixed base seed folded with the canonical content hash to derive the
        /// <see cref="ComputeUncertaintyResults"/> sampling seed — content-based, so the summary
        /// is deterministic for identical compute content and independent of names or ids.
        /// </summary>
        private const int SummarySeedBase = 12345;

        /// <summary>
        /// Backing field for <see cref="Alpha"/>.
        /// </summary>
        private double _alpha = 1d;

        /// <summary>
        /// Backing field for <see cref="Beta"/>.
        /// </summary>
        private double _beta = 1.5d;

        /// <summary>
        /// Backing field for <see cref="Threshold"/>.
        /// </summary>
        private double _threshold = 0d;

        /// <summary>
        /// Backing field for <see cref="UpperBound"/>.
        /// </summary>
        private double _upperBound = double.PositiveInfinity;

        /// <summary>
        /// Backing field for <see cref="IsUncertain"/>.
        /// </summary>
        private bool _isUncertain = false;

        /// <summary>
        /// Backing field for <see cref="SigmaAlpha"/>.
        /// </summary>
        private double _sigmaAlpha = 0d;

        /// <summary>
        /// Backing field for <see cref="SigmaBeta"/>.
        /// </summary>
        private double _sigmaBeta = 0d;

        /// <summary>
        /// The scale coefficient α. Must be positive and finite.
        /// </summary>
        public double Alpha
        {
            get { return _alpha; }
            set
            {
                if (_alpha != value)
                {
                    _alpha = value;
                    RaisePropertyChange(nameof(Alpha));
                }
            }
        }

        /// <summary>
        /// The exponent β. Must be positive and finite — consequence grows with hazard.
        /// </summary>
        public double Beta
        {
            get { return _beta; }
            set
            {
                if (_beta != value)
                {
                    _beta = value;
                    RaisePropertyChange(nameof(Beta));
                }
            }
        }

        /// <summary>
        /// The damage-initiation threshold h₀: the hazard level at and below which the consequence
        /// is exactly zero. Any finite value (stages and elevations may be negative).
        /// </summary>
        public double Threshold
        {
            get { return _threshold; }
            set
            {
                if (_threshold != value)
                {
                    _threshold = value;
                    RaisePropertyChange(nameof(Threshold));
                }
            }
        }

        /// <summary>
        /// The saturation cap U: the maximum consequence the curve can produce. Must be positive;
        /// positive infinity means no cap.
        /// </summary>
        public double UpperBound
        {
            get { return _upperBound; }
            set
            {
                if (_upperBound != value)
                {
                    _upperBound = value;
                    RaisePropertyChange(nameof(UpperBound));
                }
            }
        }

        /// <summary>
        /// Whether the coefficients carry log-space knowledge uncertainty
        /// (<see cref="SigmaAlpha"/>, <see cref="SigmaBeta"/>).
        /// </summary>
        public bool IsUncertain
        {
            get { return _isUncertain; }
            set
            {
                if (_isUncertain != value)
                {
                    _isUncertain = value;
                    RaisePropertyChange(nameof(IsUncertain));
                }
            }
        }

        /// <summary>
        /// The log-space standard deviation of the scale coefficient α. Non-negative; used only
        /// while <see cref="IsUncertain"/> is true.
        /// </summary>
        public double SigmaAlpha
        {
            get { return _sigmaAlpha; }
            set
            {
                if (_sigmaAlpha != value)
                {
                    _sigmaAlpha = value;
                    RaisePropertyChange(nameof(SigmaAlpha));
                }
            }
        }

        /// <summary>
        /// The log-space standard deviation of the exponent β. Non-negative; used only while
        /// <see cref="IsUncertain"/> is true.
        /// </summary>
        public double SigmaBeta
        {
            get { return _sigmaBeta; }
            set
            {
                if (_sigmaBeta != value)
                {
                    _sigmaBeta = value;
                    RaisePropertyChange(nameof(SigmaBeta));
                }
            }
        }

        /// <inheritdoc/>
        public override ConsequenceFunctionType FunctionType => ConsequenceFunctionType.Parametric;

        /// <inheritdoc/>
        /// <remarks>
        /// Deterministic when not flagged uncertain, or when flagged uncertain with both sigmas
        /// zero (the sampler would realize the nominal coefficients every time).
        /// </remarks>
        public override bool IsDeterministic => !_isUncertain || (_sigmaAlpha == 0d && _sigmaBeta == 0d);

        /// <inheritdoc/>
        /// <remarks>
        /// Two independent dimensions while uncertain — one per coefficient (architecture doc
        /// §6.4) — otherwise zero.
        /// </remarks>
        public override int SamplingDimensions => _isUncertain ? 2 : 0;

        #endregion

        #region IRiskFunction Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Errors (invalidating): missing axis labels; a non-positive or non-finite α or β; a
        /// non-finite threshold; a non-positive or NaN upper bound; a negative or non-finite sigma
        /// while uncertain. Warnings (advisory): flagged uncertain with both sigmas zero (samples
        /// deterministically), and exponent uncertainty with no cap — with β_i lognormal, the mean
        /// of (h − h₀)^{β_i} diverges for h &gt; h₀ + 1, so expected consequences are unbounded
        /// unless the cap is finite.
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var messages = new List<string>();

            if (string.IsNullOrEmpty(SpecifiedHazard))
                messages.Add("Error: The parametric consequence function does not have a specified hazard type.");
            if (string.IsNullOrEmpty(HazardUnit))
                messages.Add("Error: The parametric consequence function does not have a specified hazard unit.");
            if (string.IsNullOrEmpty(SpecifiedConsequence))
                messages.Add("Error: The parametric consequence function does not have a specified consequence type.");
            if (string.IsNullOrEmpty(ConsequenceUnit))
                messages.Add("Error: The parametric consequence function does not have a specified consequence unit.");

            if (!(double.IsFinite(_alpha) && _alpha > 0d))
                messages.Add("Error: The scale parameter Alpha must be a positive, finite number.");
            if (!(double.IsFinite(_beta) && _beta > 0d))
                messages.Add("Error: The exponent parameter Beta must be a positive, finite number.");
            if (!double.IsFinite(_threshold))
                messages.Add("Error: The hazard threshold must be a finite number.");
            if (double.IsNaN(_upperBound) || _upperBound <= 0d)
                messages.Add("Error: The upper bound must be greater than zero. Use positive infinity for no saturation cap.");

            if (_isUncertain)
            {
                if (!(double.IsFinite(_sigmaAlpha) && _sigmaAlpha >= 0d))
                    messages.Add("Error: The log-space standard deviation SigmaAlpha cannot be negative and must be finite.");
                if (!(double.IsFinite(_sigmaBeta) && _sigmaBeta >= 0d))
                    messages.Add("Error: The log-space standard deviation SigmaBeta cannot be negative and must be finite.");
                if (_sigmaAlpha == 0d && _sigmaBeta == 0d)
                    messages.Add("Warning: The function is flagged uncertain but both log-space standard deviations are zero; it will sample deterministically.");
                if (_sigmaBeta > 0d && double.IsPositiveInfinity(_upperBound))
                    messages.Add("Warning: Exponent uncertainty with no upper bound produces a heavy-tailed consequence distribution whose mean is unbounded for hazards more than one unit above the threshold. Consider a finite upper bound.");
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The nominal (median) curve at (α, β) — not the analytic mean; see the class remarks for
        /// the heavy-tail rationale.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the parameters are invalid.</exception>
        public override IUnivariateFunction SampleFunction()
        {
            ThrowIfParametersUnusable();
            return new ClampedPowerFunction(_alpha, _beta, _threshold, _upperBound);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Co-monotonic: one standard normal deviate z = Φ⁻¹(percentile) drives both coefficients,
        /// α_p = α·exp(σ_α·z) and β_p = β·exp(σ_β·z) — the single-percentile analog of the tabular
        /// cluster's co-monotonic curve sampling. Percentile 0.5 reproduces the nominal curve.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the parameters are invalid.</exception>
        public override IUnivariateFunction SampleFunction(double percentile)
        {
            ThrowIfParametersUnusable();
            if (IsDeterministic) return new ClampedPowerFunction(_alpha, _beta, _threshold, _upperBound);
            double z = Normal.StandardZ(percentile);
            return RealizeFunction(z, z);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Engine path: the two coefficients draw from independent sampler dimensions
        /// (z₁ from dimension 0, z₂ from dimension 1).
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the parameters are invalid, or when the function is uncertain and
        /// <see cref="RiskFunctionBase.SetupSampler"/> has not been called.
        /// </exception>
        public override IUnivariateFunction SampleFunction(int realizationIndex)
        {
            ThrowIfParametersUnusable();
            if (IsDeterministic) return new ClampedPowerFunction(_alpha, _beta, _threshold, _upperBound);
            double z1 = Normal.StandardZ(Percentile(realizationIndex, 0));
            double z2 = Normal.StandardZ(Percentile(realizationIndex, 1));
            return RealizeFunction(z1, z2);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The damage-initiation threshold h₀ — the curve is identically zero at and below it.
        /// </remarks>
        public override double MinHazard()
        {
            return _threshold;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The saturation crossing h₀ + (U/α)^(1/β) when the parameters are usable and the cap is
        /// finite (beyond it the curve is flat at U, the same "no further information" semantics
        /// as a table's last ordinate under flat extrapolation); positive infinity otherwise —
        /// including defensively for invalid parameters, so a min/max probe never returns NaN.
        /// </remarks>
        public override double MaxHazard()
        {
            if (double.IsFinite(_alpha) && _alpha > 0d && double.IsFinite(_beta) && _beta > 0d
                && double.IsFinite(_threshold) && double.IsFinite(_upperBound) && _upperBound > 0d)
            {
                return _threshold + Math.Pow(_upperBound / _alpha, 1d / _beta);
            }
            return double.PositiveInfinity;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// Deterministic internal Monte Carlo, never touching the engine sampler state: a local
        /// median Latin hypercube matrix of <see cref="SummaryRealizations"/> draws seeded by
        /// <see cref="SeedHelpers.HashCombine(int, byte[], int)"/> over
        /// (<see cref="SummarySeedBase"/>, <see cref="RiskFunctionBase.CanonicalHash"/>, 0) —
        /// identical compute content always summarizes identically. Curves are index-aligned with
        /// <see cref="UncertaintySummaryHazards"/>: the mean curve is the per-hazard sample mean,
        /// the mode curve is the per-hazard sample median (the nominal curve when deterministic),
        /// and the bounds are the (1 ∓ w)/2 sample percentiles.
        /// </para>
        /// <para>
        /// Returns null when <see cref="Validate"/> reports errors.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the width is outside (0, 1).</exception>
        public override UncertaintyAnalysisResults? ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9)
        {
            if (confidenceIntervalWidth <= 0d || confidenceIntervalWidth >= 1d)
                throw new ArgumentOutOfRangeException(nameof(confidenceIntervalWidth), "The confidence interval width must be between 0 and 1.");
            if (!Validate().IsValid) return null;

            double[] hazards = UncertaintySummaryHazards();
            var results = new UncertaintyAnalysisResults
            {
                ModeCurve = new double[hazards.Length],
                MeanCurve = new double[hazards.Length],
                ConfidenceIntervals = new double[hazards.Length, 2],
            };

            if (IsDeterministic)
            {
                var nominal = new ClampedPowerFunction(_alpha, _beta, _threshold, _upperBound);
                for (int i = 0; i < hazards.Length; i++)
                {
                    double value = nominal.Function(hazards[i]);
                    results.ModeCurve[i] = value;
                    results.MeanCurve[i] = value;
                    results.ConfidenceIntervals[i, 0] = value;
                    results.ConfidenceIntervals[i, 1] = value;
                }
                return results;
            }

            // Realize the coefficient ensemble once (median LHS strata are strictly interior, so
            // the normal inverse CDF is always finite), then summarize hazard by hazard.
            int seed = ToPositiveSeed(SeedHelpers.HashCombine(SummarySeedBase, CanonicalHash(), 0));
            double[,] percentiles = LatinHypercube.Median(SummaryRealizations, 2, seed);
            var alphas = new double[SummaryRealizations];
            var betas = new double[SummaryRealizations];
            for (int j = 0; j < SummaryRealizations; j++)
            {
                alphas[j] = _alpha * Math.Exp(_sigmaAlpha * Normal.StandardZ(percentiles[j, 0]));
                betas[j] = _beta * Math.Exp(_sigmaBeta * Normal.StandardZ(percentiles[j, 1]));
            }

            double tail = (1d - confidenceIntervalWidth) / 2d;
            var row = new double[SummaryRealizations];
            for (int i = 0; i < hazards.Length; i++)
            {
                double offset = hazards[i] - _threshold;
                double sum = 0d;
                for (int j = 0; j < SummaryRealizations; j++)
                {
                    double value = 0d;
                    if (offset > 0d)
                    {
                        value = alphas[j] * Math.Pow(offset, betas[j]);
                        if (value >= _upperBound) value = _upperBound;
                    }
                    row[j] = value;
                    sum += value;
                }
                Array.Sort(row);
                results.MeanCurve[i] = sum / SummaryRealizations;
                results.ModeCurve[i] = Statistics.Percentile(row, 0.5d, dataIsSorted: true);
                results.ConfidenceIntervals[i, 0] = Statistics.Percentile(row, tail, dataIsSorted: true);
                results.ConfidenceIntervals[i, 1] = Statistics.Percentile(row, 1d - tail, dataIsSorted: true);
            }
            return results;
        }

        /// <summary>
        /// The hazard grid that <see cref="ComputeUncertaintyResults"/> summarizes over — callers
        /// pair the returned curves with these hazards by index.
        /// </summary>
        /// <returns>The summary hazard levels, strictly increasing.</returns>
        /// <remarks>
        /// The threshold, then 100 log-spaced offsets spanning [10⁻², 10²] above it. The power
        /// form is scale-free with its only intrinsic anchor at offset 1 (where C = α), so four
        /// decades bracketing that anchor cover the informative range; when the saturation
        /// crossing is finite the grid is capped there (the curve is flat at U beyond it), with
        /// the crossing itself as the final point.
        /// </remarks>
        public double[] UncertaintySummaryHazards()
        {
            const int OffsetCount = 100;
            double crossing = MaxHazard();
            double crossingOffset = double.IsFinite(crossing) ? crossing - _threshold : double.PositiveInfinity;

            var hazards = new List<double>(OffsetCount + 2) { _threshold };
            for (int i = 0; i < OffsetCount; i++)
            {
                double offset = Math.Pow(10d, -2d + 4d * i / (OffsetCount - 1));
                if (offset >= crossingOffset) break;
                hazards.Add(_threshold + offset);
            }
            if (double.IsFinite(crossingOffset)) hazards.Add(crossing);
            return hazards.ToArray();
        }

        #endregion

        #region Serialization

        /// <inheritdoc/>
        /// <remarks>
        /// The sigma attributes are written only while <see cref="IsUncertain"/> is true, matching
        /// the canonical-hash recipe (architecture doc §5.5.3): sigma edits on a function that is
        /// not uncertain can never move the hash, because they cannot affect results.
        /// </remarks>
        public override XElement ToXElement()
        {
            var element = new XElement(nameof(ParametricConsequence));
            WriteIdentityAttributes(element);
            element.SetAttributeValue(nameof(SpecifiedHazard), SpecifiedHazard);
            element.SetAttributeValue(nameof(HazardUnit), HazardUnit);
            element.SetAttributeValue(nameof(SpecifiedConsequence), SpecifiedConsequence);
            element.SetAttributeValue(nameof(ConsequenceUnit), ConsequenceUnit);
            element.SetAttributeValue(nameof(Alpha), SerializationUtilities.FormatDouble(_alpha));
            element.SetAttributeValue(nameof(Beta), SerializationUtilities.FormatDouble(_beta));
            element.SetAttributeValue(nameof(Threshold), SerializationUtilities.FormatDouble(_threshold));
            element.SetAttributeValue(nameof(UpperBound), SerializationUtilities.FormatDouble(_upperBound));
            element.SetAttributeValue(nameof(IsUncertain), _isUncertain);
            if (_isUncertain)
            {
                element.SetAttributeValue(nameof(SigmaAlpha), SerializationUtilities.FormatDouble(_sigmaAlpha));
                element.SetAttributeValue(nameof(SigmaBeta), SerializationUtilities.FormatDouble(_sigmaBeta));
            }
            return element;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Builds the realized curve for a pair of standard normal deviates:
        /// α_i = α·exp(σ_α·z₁), β_i = β·exp(σ_β·z₂).
        /// </summary>
        /// <param name="z1">The standard normal deviate driving the scale coefficient.</param>
        /// <param name="z2">The standard normal deviate driving the exponent.</param>
        /// <returns>The realized deterministic curve.</returns>
        private ClampedPowerFunction RealizeFunction(double z1, double z2)
        {
            return new ClampedPowerFunction(
                _alpha * Math.Exp(_sigmaAlpha * z1),
                _beta * Math.Exp(_sigmaBeta * z2),
                _threshold,
                _upperBound);
        }

        /// <summary>
        /// Throws when the parameters cannot produce a usable curve (the cluster's sample-time
        /// gate, mirroring the tabular consequence's invalid-table throw).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the parameters are invalid.</exception>
        private void ThrowIfParametersUnusable()
        {
            bool usable = double.IsFinite(_alpha) && _alpha > 0d
                && double.IsFinite(_beta) && _beta > 0d
                && double.IsFinite(_threshold)
                && !double.IsNaN(_upperBound) && _upperBound > 0d
                && (!_isUncertain || (double.IsFinite(_sigmaAlpha) && _sigmaAlpha >= 0d
                                      && double.IsFinite(_sigmaBeta) && _sigmaBeta >= 0d));
            if (!usable)
                throw new InvalidOperationException("The parametric consequence parameters are invalid. Call Validate() and correct the reported errors before sampling.");
        }

        #endregion
    }
}
