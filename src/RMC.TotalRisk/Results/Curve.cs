using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Numerics;
using Numerics.Data;
using Numerics.Mathematics;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// A loss exceedance curve (LEC) and its risk measures for one risk type: the exact exceedance
    /// curve built from recorded risk points, stable central moments, the risk-profile catalog
    /// (hazard frequency, conditional consequence, the ascending cumulative profiles, and the
    /// system response probability against exceedance probability), and the risk-measure catalog.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// <b>Improved over v1.0</b> (architecture doc §7.7; <c>docs/technical-reference/loss-exceedance-curves.md</c>):
    /// the curve is built <i>exactly</i> from the sorted (mass, consequence) pairs — v1.0's 200-bin
    /// log10 histogram plotted at bin midpoints biased every ordinate and conflated output
    /// resolution with compute resolution; the output length is now purely an output-resolution
    /// knob, applied by tail-preserving thinning after the moments are computed from the exact
    /// pairs. Central moments use a two-pass weighted central accumulation — v1.0's raw power sums
    /// (<c>√(u2 − u1²)</c> and the expanded fourth-moment form) catastrophically cancel when the
    /// mean dominates the spread, the normal life-loss case. <c>ValueAtRisk</c> returns 0 (not the
    /// curve's smallest consequence) when the exceedance level exceeds the curve's total
    /// probability. The conditional value-at-risk is the exact piecewise integral of the log-log
    /// LEC quantile (Phase 6.5) — v1.0 ran adaptive quadrature with library defaults on its
    /// steepest integrand; the closed form retires that per-realization integration entirely and
    /// is more exact than the quadrature it replaces.
    /// </para>
    /// <para>
    /// <b>Mass semantics:</b> a defective curve (<see cref="IsExhaustive"/> false — Fail, Excess,
    /// NonFail) carries total probability below one; its moments include the implicit atom at zero
    /// consequence with the remaining mass, so <see cref="Mean"/> is the unconditional annualized
    /// value (v1.0 semantics, computed stably). <see cref="MassBalance"/> records the raw recorded
    /// mass before any clamp — the engine surfaces a computation warning when an exhaustive
    /// curve's balance drifts more than 1e-6 from one instead of silently clamping.
    /// </para>
    /// <para>
    /// <b>Serialization:</b> results are JSON (System.Text.Json) through the realization
    /// containers; curve data serializes as plain parallel arrays with the
    /// <see cref="OrderedPairedData"/> views rebuilt on demand and the runtime
    /// <see cref="RiskPoints"/> excluded. The LEC view preserves the v1.0 orientation
    /// (X = consequence descending, Y = exceedance probability ascending) so
    /// <c>GetXFromY</c>/<c>GetYFromX</c> interpolation is unchanged; the Y axis is declared
    /// non-strict because the exact construction legitimately produces an equal-probability anchor
    /// at zero consequence.
    /// </para>
    /// </remarks>
    public class Curve
    {
        #region Construction

        /// <summary>
        /// Initializes an empty exhaustive curve.
        /// </summary>
        public Curve()
        {
            RiskPoints = new List<RiskPoint>();
        }

        #endregion

        #region Members

        /// <summary>
        /// The tolerance on the hazard probability-mass budget assertion in
        /// <see cref="ProcessHazardProbabilities"/> — the Numerics N7 interim gate.
        /// </summary>
        private const double MassBudgetTolerance = 1e-9;

        /// <summary>
        /// The lower integration limit and probability clamp shared with the legacy engine.
        /// </summary>
        private const double ProbabilityFloor = 1e-16;

        /// <summary>
        /// Backing field for <see cref="LECConsequences"/>.
        /// </summary>
        private double[] _lecConsequences = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="LECProbabilities"/>.
        /// </summary>
        private double[] _lecProbabilities = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="HazardFrequencyHazards"/>.
        /// </summary>
        private double[] _hazardFrequencyHazards = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="HazardFrequencyProbabilities"/>.
        /// </summary>
        private double[] _hazardFrequencyProbabilities = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="HazardVsCenHazards"/>.
        /// </summary>
        private double[] _hazardVsCenHazards = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="HazardVsCenConsequences"/>.
        /// </summary>
        private double[] _hazardVsCenConsequences = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="CumulativeFailureProbabilities"/>.
        /// </summary>
        private double[] _cumulativeFailureProbabilities = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="CumulativeExpectedConsequences"/>.
        /// </summary>
        private double[] _cumulativeExpectedConsequences = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="SystemResponseExceedanceProbabilities"/>.
        /// </summary>
        private double[] _systemResponseExceedanceProbabilities = Array.Empty<double>();

        /// <summary>
        /// Backing field for <see cref="SystemResponseProbabilities"/>.
        /// </summary>
        private double[] _systemResponseProbabilities = Array.Empty<double>();

        /// <summary>
        /// The cached <see cref="LEC"/> view; null until requested, invalidated when the arrays change.
        /// </summary>
        private OrderedPairedData? _lecView;

        /// <summary>
        /// The cached <see cref="HazardFrequency"/> view; null until requested, invalidated when the arrays change.
        /// </summary>
        private OrderedPairedData? _hazardFrequencyView;

        /// <summary>
        /// The cached <see cref="HazardvsCEN"/> view; null until requested, invalidated when the arrays change.
        /// </summary>
        private OrderedPairedData? _hazardVsCenView;

        /// <summary>
        /// The cached <see cref="CumulativeFailureProbability"/> view; null until requested,
        /// invalidated when the arrays change.
        /// </summary>
        private OrderedPairedData? _cumulativeFailureView;

        /// <summary>
        /// The cached <see cref="CumulativeExpectedConsequence"/> view; null until requested,
        /// invalidated when the arrays change.
        /// </summary>
        private OrderedPairedData? _cumulativeConsequenceView;

        /// <summary>
        /// The cached <see cref="SystemResponseProfile"/> view; null until requested, invalidated
        /// when the arrays change.
        /// </summary>
        private OrderedPairedData? _systemResponseView;

        /// <summary>
        /// Whether the curve's probability budget is collectively exhaustive (Background, Total) or
        /// defective (Fail, Excess, NonFail — total probability below one).
        /// </summary>
        public bool IsExhaustive { get; set; } = true;

        /// <summary>
        /// The curve's total probability: one for an exhaustive curve, the recorded mass (clamped
        /// to one) for a defective curve. On the Fail curve this is the annualized failure
        /// probability. Set by <see cref="CreateCurve(IReadOnlyList{ValueTuple{double, double}}, int)"/>;
        /// settable for deserialization.
        /// </summary>
        public double TotalProbability { get; set; }

        /// <summary>
        /// The raw recorded probability mass Σ (hazard mass × response probability) before any
        /// clamp — the exhaustive-leak witness the engine checks instead of silently clamping.
        /// </summary>
        public double MassBalance { get; set; }

        /// <summary>
        /// The unconditional mean of the loss distribution (the expected annual consequence for
        /// this risk type). Computed exactly from the pairs; settable for deserialization.
        /// </summary>
        public double Mean { get; set; }

        /// <summary>
        /// The standard deviation of the loss distribution, computed by the stable two-pass
        /// weighted central accumulation.
        /// </summary>
        public double StandardDeviation { get; set; }

        /// <summary>
        /// The normalized skewness of the loss distribution; NaN when the distribution is
        /// degenerate (zero variance).
        /// </summary>
        public double Skewness { get; set; }

        /// <summary>
        /// The normalized kurtosis of the loss distribution (plain, not excess — the v1.0
        /// convention); NaN when the distribution is degenerate.
        /// </summary>
        public double Kurtosis { get; set; }

        /// <summary>
        /// The conditional mean given the curve's event occurs, <see cref="Mean"/> divided by
        /// <see cref="TotalProbability"/> — the η of the α-η conditional-mean-loss plot. NaN when
        /// the total probability is zero.
        /// </summary>
        [JsonIgnore]
        public double ConditionalMean => TotalProbability > 0d ? Mean / TotalProbability : double.NaN;

        /// <summary>
        /// The exceedance probability level for <see cref="ValueAtRisk"/> and
        /// <see cref="ConditionalValueAtRisk"/>. Recorded by <see cref="ComputeRiskMeasures"/>.
        /// </summary>
        public double Alpha { get; set; } = 0.01d;

        /// <summary>
        /// The consequence threshold behind <see cref="ConsequenceThresholdProbability"/>.
        /// Recorded by <see cref="ComputeRiskMeasures"/>.
        /// </summary>
        public double ConsequenceThreshold { get; set; }

        /// <summary>
        /// The hazard threshold behind <see cref="HazardThresholdProbability"/>. Recorded by
        /// <see cref="ComputeRiskMeasures"/>; NaN when no threshold applies.
        /// </summary>
        public double HazardThreshold { get; set; } = double.NaN;

        /// <summary>
        /// The probability that the consequence exceeds <see cref="ConsequenceThreshold"/> (the
        /// assurance measure); NaN until computed.
        /// </summary>
        public double ConsequenceThresholdProbability { get; set; } = double.NaN;

        /// <summary>
        /// The probability that the hazard level exceeds <see cref="HazardThreshold"/>, read from
        /// the hazard-frequency profile; NaN until computed or when no threshold applies.
        /// </summary>
        public double HazardThresholdProbability { get; set; } = double.NaN;

        /// <summary>
        /// The consequence quantile at exceedance level <see cref="Alpha"/>; zero when the level
        /// exceeds <see cref="TotalProbability"/> (the v1.0 minimum-consequence answer was wrong);
        /// NaN until computed.
        /// </summary>
        public double ValueAtRisk { get; set; } = double.NaN;

        /// <summary>
        /// The conditional value-at-risk (expected shortfall) at exceedance level
        /// <see cref="Alpha"/> — the exact piecewise integral of the log-log LEC quantile; NaN
        /// until computed.
        /// </summary>
        public double ConditionalValueAtRisk { get; set; } = double.NaN;

        /// <summary>
        /// The LEC consequence ordinates, descending — the serialized X axis of <see cref="LEC"/>.
        /// </summary>
        public double[] LECConsequences
        {
            get { return _lecConsequences; }
            set { _lecConsequences = value ?? Array.Empty<double>(); _lecView = null; }
        }

        /// <summary>
        /// The LEC exceedance probabilities, ascending and parallel to
        /// <see cref="LECConsequences"/> — the serialized Y axis of <see cref="LEC"/>.
        /// </summary>
        public double[] LECProbabilities
        {
            get { return _lecProbabilities; }
            set { _lecProbabilities = value ?? Array.Empty<double>(); _lecView = null; }
        }

        /// <summary>
        /// The hazard-frequency profile hazard levels, descending.
        /// </summary>
        public double[] HazardFrequencyHazards
        {
            get { return _hazardFrequencyHazards; }
            set { _hazardFrequencyHazards = value ?? Array.Empty<double>(); _hazardFrequencyView = null; }
        }

        /// <summary>
        /// The hazard-frequency profile exceedance probabilities, parallel to
        /// <see cref="HazardFrequencyHazards"/>.
        /// </summary>
        public double[] HazardFrequencyProbabilities
        {
            get { return _hazardFrequencyProbabilities; }
            set { _hazardFrequencyProbabilities = value ?? Array.Empty<double>(); _hazardFrequencyView = null; }
        }

        /// <summary>
        /// The hazard-versus-conditional-consequence profile hazard levels, descending.
        /// </summary>
        public double[] HazardVsCenHazards
        {
            get { return _hazardVsCenHazards; }
            set { _hazardVsCenHazards = value ?? Array.Empty<double>(); _hazardVsCenView = null; }
        }

        /// <summary>
        /// The conditional expected consequences, parallel to <see cref="HazardVsCenHazards"/>.
        /// </summary>
        public double[] HazardVsCenConsequences
        {
            get { return _hazardVsCenConsequences; }
            set { _hazardVsCenConsequences = value ?? Array.Empty<double>(); _hazardVsCenView = null; }
        }

        /// <summary>
        /// The cumulative failure probability by hazard — the ascending cumulate of the recorded
        /// probability mass, Σ<sub>h′ ≤ h</sub> w·P, stored parallel to
        /// <see cref="HazardFrequencyHazards"/> (hazard descending; empty = not computed — built
        /// on the Fail stream of the primary consequence type only, where probabilities live).
        /// </summary>
        /// <remarks>
        /// The terminal ordinate (the largest hazard) is the annualized failure probability;
        /// interior ordinates are cumulative partial sums — this curve is the distribution of
        /// the failure-causing hazard measure, NOT the failure probability at a hazard level.
        /// Never label it "Annual Probability of Failure by Hazard", "APF vs. Hazard", or
        /// "Cumulative APF": the correct display name is "Cumulative Failure Probability by
        /// Hazard", with <see cref="FractionOfFailureProbabilityByHazard"/> as the normalized
        /// companion ("the fraction of the failure probability contributed by hazards at or
        /// below h"). A curve still visibly rising at its largest hazard is a tail-truncation
        /// witness. Phase 6.6 (the risk-profile catalog).
        /// </remarks>
        public double[] CumulativeFailureProbabilities
        {
            get { return _cumulativeFailureProbabilities; }
            set { _cumulativeFailureProbabilities = value ?? Array.Empty<double>(); _cumulativeFailureView = null; }
        }

        /// <summary>
        /// The cumulative expected annual consequence by hazard — the ascending cumulate of the
        /// recorded expected consequence, Σ<sub>h′ ≤ h</sub> w·E, stored parallel to
        /// <see cref="HazardFrequencyHazards"/> (hazard descending; empty = not computed). The
        /// terminal ordinate is the stream's <see cref="Mean"/> — the profile decomposes the
        /// expected annual consequence by the hazard range that drives it. Phase 6.6.
        /// </summary>
        public double[] CumulativeExpectedConsequences
        {
            get { return _cumulativeExpectedConsequences; }
            set { _cumulativeExpectedConsequences = value ?? Array.Empty<double>(); _cumulativeConsequenceView = null; }
        }

        /// <summary>
        /// The system response probability profile's X axis: the driving hazard's annual
        /// exceedance probability per distinct evaluation, descending (empty = not computed —
        /// built on the Fail stream of the primary consequence type only).
        /// </summary>
        /// <remarks>
        /// Exceedance probability is deliberately the axis (Phase 6.6, user-ratified): a
        /// hazard-axis response profile is ill-posed when failure modes respond to different
        /// transformed signals, while the exceedance scale is normalized, universal across
        /// transform choices, comparable across components, and independent of the profile-axis
        /// selection (this profile is never remapped).
        /// </remarks>
        public double[] SystemResponseExceedanceProbabilities
        {
            get { return _systemResponseExceedanceProbabilities; }
            set { _systemResponseExceedanceProbabilities = value ?? Array.Empty<double>(); _systemResponseView = null; }
        }

        /// <summary>
        /// The combined (post-combination) system response probability per distinct evaluation,
        /// parallel to <see cref="SystemResponseExceedanceProbabilities"/> — at component scope
        /// the effective failure probability after the failure-mode combination method (where
        /// mutual-exclusivity normalization, common-cause factors, or competing incidence
        /// functions bend the marginal responses); at failure-mode scope the mode's raw sampled
        /// response probability. Empty = not computed. Phase 6.6.
        /// </summary>
        public double[] SystemResponseProbabilities
        {
            get { return _systemResponseProbabilities; }
            set { _systemResponseProbabilities = value ?? Array.Empty<double>(); _systemResponseView = null; }
        }

        /// <summary>
        /// The loss exceedance curve view (X = consequence descending, Y = exceedance probability
        /// ascending, non-strict — the exact construction can anchor equal probabilities at zero
        /// consequence). Rebuilt from the serialized arrays on demand; empty arrays yield an empty
        /// view.
        /// </summary>
        [JsonIgnore]
        public OrderedPairedData LEC
        {
            get
            {
                _lecView ??= BuildView(_lecConsequences, _lecProbabilities, yStrict: false, yOrder: SortOrder.Ascending);
                return _lecView;
            }
        }

        /// <summary>
        /// The hazard-versus-exceedance-probability profile view (X descending, Y ascending).
        /// </summary>
        [JsonIgnore]
        public OrderedPairedData HazardFrequency
        {
            get
            {
                _hazardFrequencyView ??= BuildView(_hazardFrequencyHazards, _hazardFrequencyProbabilities, yStrict: false, yOrder: SortOrder.Ascending);
                return _hazardFrequencyView;
            }
        }

        /// <summary>
        /// The hazard-versus-conditional-expected-consequence profile view (X descending, Y unordered).
        /// </summary>
        [JsonIgnore]
        public OrderedPairedData HazardvsCEN
        {
            get
            {
                _hazardVsCenView ??= BuildView(_hazardVsCenHazards, _hazardVsCenConsequences, yStrict: false, yOrder: SortOrder.None);
                return _hazardVsCenView;
            }
        }

        /// <summary>
        /// The cumulative-failure-probability-by-hazard profile view (X = hazard descending,
        /// Y descending non-strict — the cumulate falls as hazard falls). See the naming rules
        /// on <see cref="CumulativeFailureProbabilities"/>.
        /// </summary>
        [JsonIgnore]
        public OrderedPairedData CumulativeFailureProbability
        {
            get
            {
                _cumulativeFailureView ??= BuildView(_hazardFrequencyHazards, _cumulativeFailureProbabilities, yStrict: false, yOrder: SortOrder.Descending);
                return _cumulativeFailureView;
            }
        }

        /// <summary>
        /// The cumulative-expected-consequence-by-hazard profile view (X = hazard descending,
        /// Y descending non-strict).
        /// </summary>
        [JsonIgnore]
        public OrderedPairedData CumulativeExpectedConsequence
        {
            get
            {
                _cumulativeConsequenceView ??= BuildView(_hazardFrequencyHazards, _cumulativeExpectedConsequences, yStrict: false, yOrder: SortOrder.Descending);
                return _cumulativeConsequenceView;
            }
        }

        /// <summary>
        /// The system-response-probability profile view (X = annual exceedance probability
        /// descending, Y unordered — combination adjustments can bend the response
        /// non-monotonically).
        /// </summary>
        [JsonIgnore]
        public OrderedPairedData SystemResponseProfile
        {
            get
            {
                _systemResponseView ??= BuildView(_systemResponseExceedanceProbabilities, _systemResponseProbabilities, yStrict: false, yOrder: SortOrder.None);
                return _systemResponseView;
            }
        }

        /// <summary>
        /// The normalized companion of <see cref="CumulativeFailureProbability"/>: the fraction
        /// of the failure probability contributed by hazards at or below each level —
        /// P(H ≤ h | Failure) — dividing by the profile's own terminal ordinate. Built on
        /// demand; empty when the raw profile is absent or its terminal is not positive. On a
        /// banded curve this is the self-normalized band, which is not the band of normalized
        /// realizations (documented semantics).
        /// </summary>
        [JsonIgnore]
        public OrderedPairedData FractionOfFailureProbabilityByHazard
        {
            get { return BuildNormalizedView(_hazardFrequencyHazards, _cumulativeFailureProbabilities); }
        }

        /// <summary>
        /// The normalized companion of <see cref="CumulativeExpectedConsequence"/>: the fraction
        /// of the expected annual consequence contributed by hazards at or below each level,
        /// dividing by the profile's own terminal ordinate. Built on demand; empty when the raw
        /// profile is absent or its terminal is not positive.
        /// </summary>
        [JsonIgnore]
        public OrderedPairedData FractionOfExpectedConsequenceByHazard
        {
            get { return BuildNormalizedView(_hazardFrequencyHazards, _cumulativeExpectedConsequences); }
        }

        /// <summary>
        /// The recorded risk evaluation points — runtime working state, never serialized; cleared
        /// by <see cref="DumpMemory"/> after post-processing.
        /// </summary>
        [JsonIgnore]
        public List<RiskPoint> RiskPoints { get; private set; }

        #endregion

        #region Recording

        /// <summary>
        /// Records a risk evaluation point with no hazard level (mass defaults to the given
        /// probability until post-processed).
        /// </summary>
        /// <param name="hazardProbability">The hazard non-exceedance probability, P[X ≤ x].</param>
        /// <param name="responseProbability">The response probability, P[F|x].</param>
        /// <param name="consequence">The consequence given the response, C(x).</param>
        public void AddRiskPoint(double hazardProbability, double responseProbability, double consequence)
        {
            var point = new RiskPoint(1)
            {
                HazardProbability = hazardProbability,
                HazardProbabilityMass = hazardProbability,
            };
            point.Add(responseProbability, consequence);
            RiskPoints.Add(point);
        }

        /// <summary>
        /// Records a risk evaluation point (mass defaults to the given probability until
        /// post-processed).
        /// </summary>
        /// <param name="hazardLevel">The hazard level where the risk was evaluated.</param>
        /// <param name="hazardProbability">The hazard non-exceedance probability, P[X ≤ x].</param>
        /// <param name="responseProbability">The response probability, P[F|x].</param>
        /// <param name="consequence">The consequence given the response, C(x).</param>
        public void AddRiskPoint(double hazardLevel, double hazardProbability, double responseProbability, double consequence)
        {
            var point = new RiskPoint(1)
            {
                HazardLevel = hazardLevel,
                HazardProbability = hazardProbability,
                HazardProbabilityMass = hazardProbability,
            };
            point.Add(responseProbability, consequence);
            RiskPoints.Add(point);
        }

        /// <summary>
        /// Records a risk evaluation point carrying parallel entry lists (one entry per exposure
        /// branch or pathway combination). The lists are adopted, not copied.
        /// </summary>
        /// <param name="hazardLevel">The hazard level where the risk was evaluated.</param>
        /// <param name="hazardProbability">The hazard non-exceedance probability, P[X ≤ x].</param>
        /// <param name="responseProbabilities">The entry response probabilities, P[F|x].</param>
        /// <param name="consequences">The entry consequences, parallel to the probabilities.</param>
        /// <param name="hazardExceedanceProbability">
        /// The driving hazard's annual exceedance probability at the evaluation — the system
        /// response profile's X coordinate (Phase 6.6). NaN (the default) skips that profile.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when either list is null.</exception>
        public void AddRiskPoint(double hazardLevel, double hazardProbability, List<double> responseProbabilities, List<double> consequences,
            double hazardExceedanceProbability = double.NaN)
        {
            if (responseProbabilities == null) throw new ArgumentNullException(nameof(responseProbabilities));
            if (consequences == null) throw new ArgumentNullException(nameof(consequences));
            RiskPoints.Add(new RiskPoint
            {
                HazardLevel = hazardLevel,
                HazardProbability = hazardProbability,
                HazardProbabilityMass = hazardProbability,
                HazardExceedanceProbability = hazardExceedanceProbability,
                ResponseProbabilities = responseProbabilities,
                Consequences = consequences,
            });
        }

        #endregion

        #region Curve Construction

        /// <summary>
        /// Post-processes the recorded hazard non-exceedance probabilities into probability
        /// masses: sorts the points by probability, merges duplicate-probability points (their
        /// entry lists concatenate — a shared stratification-bin edge can collide), applies the
        /// midpoint-trapezoid mass partition, and asserts the mass budget telescopes to one.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the post-processed mass budget differs from one by more than 1e-9 — an
        /// engine invariant violation, never a data condition. The midpoint-trapezoid partition
        /// telescopes to exactly one in exact arithmetic, so a violation means the recorded point
        /// set is corrupt (the Numerics N7 interim gate; the quadrature-weight overload retires
        /// this re-derivation).
        /// </exception>
        /// <remarks>
        /// One-dimensional path only — the VEGAS path records true quadrature weights and never
        /// re-derives mass. Ported from v1.0 with the duplicate merge and the budget assertion
        /// added; v1.0 silently produced zero or negative masses on duplicate probabilities.
        /// </remarks>
        public void ProcessHazardProbabilities()
        {
            if (RiskPoints.Count < 2) return;

            RiskPoints.Sort((x, y) => x.HazardProbability.CompareTo(y.HazardProbability));

            // Merge points sharing a probability so the trapezoid partition sees strictly
            // increasing probabilities.
            var merged = new List<RiskPoint>(RiskPoints.Count) { RiskPoints[0] };
            for (int i = 1; i < RiskPoints.Count; i++)
            {
                var point = RiskPoints[i];
                var last = merged[merged.Count - 1];
                if (point.HazardProbability == last.HazardProbability)
                {
                    last.ResponseProbabilities.AddRange(point.ResponseProbabilities);
                    last.Consequences.AddRange(point.Consequences);
                }
                else
                {
                    merged.Add(point);
                }
            }
            RiskPoints = merged;
            if (RiskPoints.Count < 2) return;

            int n = RiskPoints.Count;
            RiskPoints[0].HazardProbabilityMass = (RiskPoints[0].HazardProbability + RiskPoints[1].HazardProbability) / 2d;
            for (int i = 1; i < n - 1; i++)
            {
                RiskPoints[i].HazardProbabilityMass =
                    (RiskPoints[i].HazardProbability + RiskPoints[i + 1].HazardProbability) / 2d
                    - (RiskPoints[i].HazardProbability + RiskPoints[i - 1].HazardProbability) / 2d;
            }
            RiskPoints[n - 1].HazardProbabilityMass = 1d - (RiskPoints[n - 2].HazardProbability + RiskPoints[n - 1].HazardProbability) / 2d;

            double budget = 0d;
            for (int i = 0; i < n; i++)
            {
                budget += RiskPoints[i].HazardProbabilityMass;
            }
            if (Math.Abs(budget - 1d) > MassBudgetTolerance)
            {
                throw new InvalidOperationException(
                    $"The hazard probability-mass budget is {budget:R} instead of 1. The recorded risk-point set is corrupt.");
            }
        }

        /// <summary>
        /// Builds the exact loss exceedance curve, total probability, and central moments from the
        /// recorded risk points, then thins the stored curve to the requested output length.
        /// </summary>
        /// <param name="outputLength">
        /// The output resolution of the stored curve — purely presentational; moments and total
        /// probability come from the exact pairs before thinning.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the output length is less than two.</exception>
        public void CreateCurve(int outputLength)
        {
            if (RiskPoints.Count < 2) return;
            CreateCurve(CollectRecordedPairs(), outputLength);
        }

        /// <summary>
        /// Collects the recorded weighted (mass, consequence) pairs from the risk points — the
        /// exact empirical loss distribution the curve is built from, and the input the additive
        /// system convolution consumes per component. Only meaningful after the masses are final
        /// (post <see cref="ProcessHazardProbabilities"/> on the one-dimensional path); available
        /// until <see cref="DumpMemory"/> clears the points.
        /// </summary>
        /// <returns>The weighted pairs, one per recorded entry, in recording order.</returns>
        public List<(double Mass, double Consequence)> CollectRecordedPairs()
        {
            int entries = 0;
            for (int i = 0; i < RiskPoints.Count; i++)
            {
                entries += RiskPoints[i].ResponseProbabilities.Count;
            }

            var pairs = new List<(double Mass, double Consequence)>(entries);
            for (int i = 0; i < RiskPoints.Count; i++)
            {
                var point = RiskPoints[i];
                for (int j = 0; j < point.ResponseProbabilities.Count; j++)
                {
                    pairs.Add((point.HazardProbabilityMass * point.ResponseProbabilities[j], point.Consequences[j]));
                }
            }
            return pairs;
        }

        /// <summary>
        /// Scales every recorded risk point's probability coordinate and mass by the given factor —
        /// the joint system path's self-normalization of accumulated VEGAS weights across its
        /// recording passes (the weights sum to the domain volume only in expectation; scaling by
        /// the reciprocal of the realized sum makes the exhaustive mass budget exactly one).
        /// </summary>
        /// <param name="factor">The positive scale factor to apply to each point.</param>
        internal void ScaleRecordedMass(double factor)
        {
            for (int i = 0; i < RiskPoints.Count; i++)
            {
                RiskPoints[i].HazardProbability *= factor;
                RiskPoints[i].HazardProbabilityMass *= factor;
            }
        }

        /// <summary>
        /// Builds the exact loss exceedance curve, total probability, and central moments directly
        /// from weighted (mass, consequence) pairs — the single construction both the
        /// one-dimensional path (masses from the quadrature partition) and the system paths
        /// (masses from VEGAS weights or the convolution grid) converge on.
        /// </summary>
        /// <param name="pairs">The weighted pairs; entries with non-positive mass are unreachable and ignored.</param>
        /// <param name="outputLength">The output resolution of the stored curve (at least two).</param>
        /// <exception cref="ArgumentNullException">Thrown when the pair list is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the output length is less than two.</exception>
        /// <remarks>
        /// The algorithm: sort by consequence descending, merge equal consequences, accumulate the
        /// exact reverse-cumulative exceedance, compute the two-pass weighted central moments
        /// (including the implicit zero-consequence atom carrying any unrecorded mass, which is
        /// what makes a defective curve's moments unconditional — the v1.0 semantics, computed
        /// stably), then thin the stored ordinates by log-spaced exceedance targets that always
        /// retain the extreme-tail and terminal points.
        /// </remarks>
        public void CreateCurve(IReadOnlyList<(double Mass, double Consequence)> pairs, int outputLength)
        {
            if (pairs == null) throw new ArgumentNullException(nameof(pairs));
            if (outputLength < 2) throw new ArgumentOutOfRangeException(nameof(outputLength), "The output length must be at least two.");

            // Gather the reachable pairs, sorted by consequence descending, merging equal
            // consequences so the stored X axis is strictly descending.
            var sorted = new List<(double Mass, double Consequence)>(pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].Mass > 0d) sorted.Add(pairs[i]);
            }
            if (sorted.Count == 0) return;
            sorted.Sort((x, y) => y.Consequence.CompareTo(x.Consequence));

            var mergedMass = new List<double>(sorted.Count);
            var mergedConsequence = new List<double>(sorted.Count);
            mergedMass.Add(sorted[0].Mass);
            mergedConsequence.Add(sorted[0].Consequence);
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Consequence == mergedConsequence[mergedConsequence.Count - 1])
                {
                    mergedMass[mergedMass.Count - 1] += sorted[i].Mass;
                }
                else
                {
                    mergedMass.Add(sorted[i].Mass);
                    mergedConsequence.Add(sorted[i].Consequence);
                }
            }

            // Total probability and the mass-balance witness.
            double recordedMass = 0d;
            for (int i = 0; i < mergedMass.Count; i++)
            {
                recordedMass += mergedMass[i];
            }
            MassBalance = recordedMass;
            TotalProbability = IsExhaustive ? 1d : Math.Min(recordedMass, 1d);

            // Two-pass weighted central moments, including the implicit zero-consequence atom for
            // any unrecorded mass (weight budget one). Pass one: the exact weighted mean.
            double atom = Math.Max(0d, 1d - recordedMass);
            double mean = 0d;
            for (int i = 0; i < mergedMass.Count; i++)
            {
                mean += mergedMass[i] * mergedConsequence[i];
            }

            // Pass two: central sums about the mean — no raw power sums, no cancellation.
            double m2 = atom * mean * mean;
            double m3 = atom * -(mean * mean * mean);
            double m4 = atom * mean * mean * mean * mean;
            for (int i = 0; i < mergedMass.Count; i++)
            {
                double delta = mergedConsequence[i] - mean;
                double delta2 = delta * delta;
                m2 += mergedMass[i] * delta2;
                m3 += mergedMass[i] * delta2 * delta;
                m4 += mergedMass[i] * delta2 * delta2;
            }
            Mean = mean;
            StandardDeviation = Math.Sqrt(m2);
            Skewness = m2 > 0d ? m3 / (m2 * Math.Sqrt(m2)) : double.NaN;
            Kurtosis = m2 > 0d ? m4 / (m2 * m2) : double.NaN;

            // The exact exceedance ordinates: a zero-probability anchor just above the largest
            // consequence (the v1.0 interpolation anchor), the exact reverse-cumulative points,
            // and a zero-consequence anchor carrying the total probability when the recorded
            // consequences stay positive.
            double largest = mergedConsequence[0];
            double smallest = mergedConsequence[mergedConsequence.Count - 1];
            var consequences = new List<double>(mergedMass.Count + 2);
            var probabilities = new List<double>(mergedMass.Count + 2);
            if (largest > 0d)
            {
                consequences.Add(largest * (1d + 1e-8));
                probabilities.Add(0d);
            }
            double cumulative = 0d;
            for (int i = 0; i < mergedMass.Count; i++)
            {
                cumulative += mergedMass[i];
                consequences.Add(mergedConsequence[i]);
                probabilities.Add(cumulative);
            }
            if (smallest > 0d)
            {
                consequences.Add(0d);
                probabilities.Add(Math.Max(TotalProbability, cumulative));
            }

            ThinAndStore(consequences, probabilities, outputLength);
        }

        /// <summary>
        /// Builds the risk profiles from the recorded risk points on the recorded hazard axis
        /// (the driving hazard, or the component's selected profile axis — Q-T): the descending
        /// hazard-frequency and conditional-consequence profiles (v1.0 port), the ascending
        /// cumulative-expected-consequence profile, and — when requested — the failure-stream
        /// profiles: the cumulative failure probability by hazard and the system response
        /// probability against annual exceedance probability (Phase 6.6 catalog).
        /// </summary>
        /// <param name="includeFailureProfiles">
        /// True to also build the failure-stream profiles (<see cref="CumulativeFailureProbabilities"/>
        /// and the system response profile) — the Fail stream of the primary consequence type
        /// only, where the probability structure lives (probabilities are type-independent, so
        /// per-type copies would duplicate byte-identical data).
        /// </param>
        /// <remarks>
        /// The ascending cumulates are exact forward summations over the same sorted points —
        /// never complemented from the descending frequency profile, whose ordinates carry
        /// clamps. The system response profile is skipped when any recorded point lacks its
        /// exceedance-probability coordinate.
        /// </remarks>
        public void CreateProfiles(bool includeFailureProfiles = false)
        {
            if (RiskPoints.Count < 2) return;

            // On the one-dimensional path the points arrive probability-sorted, and hazard is
            // monotone in probability — when the hazard levels are strictly ascending the
            // descending order is an O(n) reverse (identical to the sort's result, since strict
            // order has one descending arrangement), skipping the comparator-driven sort. Any
            // tie or disorder (the VEGAS path's random levels) falls back to the sort.
            bool strictlyAscending = true;
            for (int i = 1; i < RiskPoints.Count; i++)
            {
                if (!(RiskPoints[i].HazardLevel > RiskPoints[i - 1].HazardLevel))
                {
                    strictlyAscending = false;
                    break;
                }
            }
            if (strictlyAscending)
            {
                RiskPoints.Reverse();
            }
            else
            {
                RiskPoints.Sort((x, y) => -x.HazardLevel.CompareTo(y.HazardLevel));
            }

            double sumExceedance = 0d;
            double sumExpectedConsequence = 0d;
            var hazards = new List<double>(RiskPoints.Count);
            var exceedances = new List<double>(RiskPoints.Count);
            var conditionalMeans = new List<double>(RiskPoints.Count);
            for (int i = 0; i < RiskPoints.Count; i++)
            {
                RiskPoints[i].SummaryStatistics(out double probability, out double consequence);
                sumExceedance += probability;
                sumExpectedConsequence += consequence;
                if (i == 0 || RiskPoints[i].HazardLevel != hazards[hazards.Count - 1])
                {
                    hazards.Add(RiskPoints[i].HazardLevel);
                    exceedances.Add(Math.Min(1d - ProbabilityFloor, Math.Max(ProbabilityFloor, sumExceedance)));
                    conditionalMeans.Add(sumExpectedConsequence / sumExceedance);
                }
            }

            _hazardFrequencyHazards = hazards.ToArray();
            _hazardFrequencyProbabilities = exceedances.ToArray();
            _hazardVsCenHazards = hazards.ToArray();
            _hazardVsCenConsequences = conditionalMeans.ToArray();
            _hazardFrequencyView = null;
            _hazardVsCenView = null;

            // The ascending pass (Phase 6.6): reverse-iterate the descending-sorted points,
            // accumulating the cumulative profiles by exact forward summation; an ordinate
            // completes when its distinct hazard is fully accumulated, so tie mass (VEGAS path
            // only) lands on its own ordinate. Stored descending in hazard (the container
            // convention shared with the banding kernel); the system response profile stores its
            // own descending exceedance-probability axis.
            int distinctCount = hazards.Count;
            var cumulativeProbabilities = includeFailureProfiles ? new double[distinctCount] : null;
            var cumulativeConsequences = new double[distinctCount];
            var responseExceedances = includeFailureProfiles ? new List<double>(distinctCount) : null;
            var responseProbabilities = includeFailureProfiles ? new List<double>(distinctCount) : null;
            bool exceedanceAvailable = includeFailureProfiles;
            double ascendingProbability = 0d;
            double ascendingConsequence = 0d;
            double levelProbability = 0d;
            double levelMass = 0d;
            double levelExceedance = double.NaN;
            double previousLevelMass = 0d;
            int ordinate = distinctCount - 1;
            for (int i = RiskPoints.Count - 1; i >= 0; i--)
            {
                var point = RiskPoints[i];
                point.SummaryStatistics(out double probability, out double consequence);
                ascendingProbability += probability;
                ascendingConsequence += consequence;
                levelProbability += probability;
                levelMass += point.HazardProbabilityMass;
                if (double.IsNaN(levelExceedance)) levelExceedance = point.HazardExceedanceProbability;

                if (i == 0 || RiskPoints[i - 1].HazardLevel != point.HazardLevel)
                {
                    cumulativeConsequences[ordinate] = ascendingConsequence;
                    if (includeFailureProfiles)
                    {
                        cumulativeProbabilities![ordinate] = Math.Max(ProbabilityFloor, ascendingProbability);
                        if (double.IsNaN(levelExceedance))
                        {
                            exceedanceAvailable = false;
                        }
                        else if (responseExceedances!.Count > 0 && responseExceedances[responseExceedances.Count - 1] == levelExceedance)
                        {
                            // Distinct hazards sharing an exceedance probability (a flat CDF
                            // segment) merge mass-weighted so the profile's exceedance axis
                            // stays strictly descending.
                            int last = responseProbabilities!.Count - 1;
                            double mergedMass = previousLevelMass + levelMass;
                            responseProbabilities[last] = mergedMass > 0d
                                ? (responseProbabilities[last] * previousLevelMass + levelProbability) / mergedMass
                                : 0d;
                            previousLevelMass = mergedMass;
                        }
                        else
                        {
                            responseExceedances!.Add(levelExceedance);
                            responseProbabilities!.Add(levelMass > 0d ? levelProbability / levelMass : 0d);
                            previousLevelMass = levelMass;
                        }
                    }
                    levelProbability = 0d;
                    levelMass = 0d;
                    levelExceedance = double.NaN;
                    ordinate--;
                }
            }

            _cumulativeExpectedConsequences = cumulativeConsequences;
            _cumulativeConsequenceView = null;
            if (includeFailureProfiles)
            {
                _cumulativeFailureProbabilities = cumulativeProbabilities!;
                _cumulativeFailureView = null;
                if (exceedanceAvailable)
                {
                    // The ascending-hazard completion order IS descending exceedance order (the
                    // smallest hazard carries the largest exceedance probability) — the stored
                    // convention directly.
                    _systemResponseExceedanceProbabilities = responseExceedances!.ToArray();
                    _systemResponseProbabilities = responseProbabilities!.ToArray();
                }
                else
                {
                    _systemResponseExceedanceProbabilities = Array.Empty<double>();
                    _systemResponseProbabilities = Array.Empty<double>();
                }
                _systemResponseView = null;
            }
        }

        /// <summary>
        /// Interpolates one value from a descending-X curve in log-log space with a monotone
        /// resume cursor — the percentile post-processing kernel. Replicates
        /// <c>OrderedPairedData.GetYFromX(x, Logarithmic, Logarithmic)</c> bit-for-bit: the raw
        /// end clamps, the bracketing segment whose upper index is the first ordinate at or
        /// below the query, the 1e-16-floored base-10 transforms, the identical interpolation
        /// expression, and the flat-segment (equal transformed X) rule — verified by a
        /// zero-ulp unit test. The cursor lets a caller sweeping a descending query grid walk
        /// the curve once, O(n + m), instead of a binary search per query.
        /// </summary>
        /// <param name="xValues">The curve X ordinates, strictly descending (the serialized LEC/profile orientation).</param>
        /// <param name="yValues">The curve Y ordinates, parallel to <paramref name="xValues"/>.</param>
        /// <param name="x">The query X value.</param>
        /// <param name="cursor">
        /// The resume index (the candidate upper segment index, at least one). Pass 1 for the
        /// first query and reuse the reference for subsequent non-increasing queries.
        /// </param>
        /// <returns>The interpolated Y value; NaN for an empty curve.</returns>
        public static double InterpolateLogLogDescending(double[] xValues, double[] yValues, double x, ref int cursor)
        {
            int count = xValues.Length;
            if (count == 0) return double.NaN;
            if (count == 1) return yValues[0];
            if (x >= xValues[0]) return yValues[0];
            if (x <= xValues[count - 1]) return yValues[count - 1];

            // Advance to the first index at or below the query — monotone in a descending
            // query sweep, so the cursor never rewinds.
            int upper = cursor < 1 ? 1 : cursor;
            while (x < xValues[upper])
            {
                upper++;
            }
            cursor = upper;
            int lower = upper - 1;

            double xt = Tools.Log10(x);
            double x1 = Tools.Log10(xValues[lower]);
            double x2 = Tools.Log10(xValues[upper]);
            double y1 = Tools.Log10(yValues[lower]);
            double y2 = Tools.Log10(yValues[upper]);
            double y = (x2 - x1) == 0 ? y1 : y1 + (xt - x1) / (x2 - x1) * (y2 - y1);
            return Math.Pow(10d, y);
        }

        #endregion

        #region Risk Measures

        /// <summary>
        /// Computes the risk-measure catalog from the finished curve: the assurance
        /// (consequence-threshold) probability, value-at-risk, conditional value-at-risk, and the
        /// hazard-threshold probability from the frequency profile.
        /// </summary>
        /// <param name="consequenceThreshold">The consequence threshold for the assurance measure.</param>
        /// <param name="alpha">The exceedance level for value-at-risk and conditional value-at-risk, in (0, 1).</param>
        /// <param name="hazardThreshold">The hazard threshold, or NaN when none applies.</param>
        /// <remarks>
        /// Measures are NaN when the curve is too short to interpolate. <see cref="ValueAtRisk"/>
        /// returns 0 when <paramref name="alpha"/> exceeds <see cref="TotalProbability"/> — at
        /// that exceedance level the consequence is not realized (v1.0 returned the curve's
        /// smallest consequence). A NaN <paramref name="consequenceThreshold"/> skips the
        /// assurance lookup and leaves <see cref="ConsequenceThresholdProbability"/> NaN — the
        /// Phase 6.5 secondary-consequence-type convention: the analysis threshold is declared in
        /// the primary type's units, so it cannot be evaluated on another type's axis (per-type
        /// thresholds land with the risk-measures phase). The conditional value-at-risk is the
        /// EXACT segment-by-segment integral of the log-log LEC quantile over [1e-16, α]
        /// (Phase 6.5): the quantile is piecewise <c>c·(p/p₁)^s</c> in the 1e-16-floored base-10
        /// space, so each segment integrates in closed form — replacing the per-realization
        /// adaptive Gauss–Kronrod pass (relative tolerance 1e-8, the engine's deepest recurring
        /// integration) with an O(knots) computation that is more exact than the quadrature it
        /// retires.
        /// </remarks>
        public void ComputeRiskMeasures(double consequenceThreshold, double alpha, double hazardThreshold = double.NaN)
        {
            ConsequenceThreshold = consequenceThreshold;
            Alpha = alpha;
            HazardThreshold = hazardThreshold;
            ConsequenceThresholdProbability = double.NaN;
            ValueAtRisk = double.NaN;
            ConditionalValueAtRisk = double.NaN;
            HazardThresholdProbability = double.NaN;

            if (_lecConsequences.Length > 2)
            {
                var lec = LEC;
                if (!double.IsNaN(consequenceThreshold))
                {
                    ConsequenceThresholdProbability = lec.GetYFromX(consequenceThreshold, Transform.Logarithmic, Transform.Logarithmic);
                }

                if (alpha > TotalProbability)
                {
                    // At an exceedance level above the curve's total probability the consequence
                    // is not realized at all — the value at risk is zero, not the smallest
                    // recorded consequence (the v1.0 defect).
                    ValueAtRisk = 0d;
                }
                else
                {
                    ValueAtRisk = lec.GetXFromY(alpha);
                }

                ConditionalValueAtRisk = ClosedFormConditionalValueAtRisk(alpha);
            }

            if (!double.IsNaN(hazardThreshold) && _hazardFrequencyHazards.Length > 1)
            {
                HazardThresholdProbability = HazardFrequency.GetYFromX(hazardThreshold, Transform.Logarithmic, Transform.Logarithmic);
            }
        }

        /// <summary>
        /// The conditional value-at-risk by exact piecewise integration of the log-log LEC
        /// quantile over [1e-16, α], divided by α. The quantile mirrors
        /// <c>GetXFromY(p, Logarithmic, Logarithmic)</c> piece for piece: the raw end clamps
        /// contribute constant slabs, and each interior segment is
        /// <c>c(p) = 10^(x₁ + (log₁₀ p − y₁)·s)</c> in the 1e-16-floored transforms, integrated
        /// through the numerically stable <c>c(p)·p</c> arrangement.
        /// </summary>
        /// <param name="alpha">The exceedance level, in (0, 1).</param>
        /// <returns>The conditional value-at-risk (zero for a degenerate integration domain).</returns>
        private double ClosedFormConditionalValueAtRisk(double alpha)
        {
            var consequences = _lecConsequences;
            var probabilities = _lecProbabilities;
            int count = probabilities.Length;
            double position = ProbabilityFloor;
            if (alpha <= position) return 0d;

            double integral = 0d;

            // The clamp region below the first ordinate's exceedance returns the largest
            // consequence (the interpolator's raw end clamp).
            if (probabilities[0] > position)
            {
                double to = Math.Min(alpha, probabilities[0]);
                integral += consequences[0] * (to - position);
                position = to;
            }

            // Interior segments intersected with the remaining domain; zero-width (flat
            // probability) segments carry no measure and are skipped.
            for (int i = 0; i + 1 < count && position < alpha; i++)
            {
                if (probabilities[i + 1] <= position) continue;
                double from = Math.Max(position, probabilities[i]);
                double to = Math.Min(alpha, probabilities[i + 1]);
                if (to > from)
                {
                    integral += SegmentQuantileIntegral(consequences[i], consequences[i + 1],
                        probabilities[i], probabilities[i + 1], from, to);
                    position = to;
                }
            }

            // The clamp region above the last ordinate's exceedance returns the smallest
            // consequence.
            if (position < alpha)
            {
                integral += consequences[count - 1] * (alpha - position);
            }

            return integral / alpha;
        }

        /// <summary>
        /// Integrates one log-log quantile segment over a probability sub-interval in closed
        /// form: with <c>s = (x₂ − x₁)/(y₂ − y₁)</c> in the floored base-10 transforms, the
        /// quantile is a power function whose antiderivative is <c>c(p)·p/(s + 1)</c>; the flat
        /// transformed-probability rule mirrors the interpolator's division-by-zero branch, and
        /// <c>s → −1</c> takes the logarithmic form.
        /// </summary>
        /// <param name="consequenceLow">The segment's higher-consequence ordinate (lower exceedance).</param>
        /// <param name="consequenceHigh">The segment's lower-consequence ordinate (higher exceedance).</param>
        /// <param name="probabilityLow">The segment's lower exceedance probability.</param>
        /// <param name="probabilityHigh">The segment's higher exceedance probability.</param>
        /// <param name="from">The sub-interval lower probability.</param>
        /// <param name="to">The sub-interval upper probability.</param>
        /// <returns>The exact sub-interval integral of the quantile.</returns>
        private static double SegmentQuantileIntegral(double consequenceLow, double consequenceHigh,
            double probabilityLow, double probabilityHigh, double from, double to)
        {
            double x1 = Tools.Log10(consequenceLow);
            double x2 = Tools.Log10(consequenceHigh);
            double y1 = Tools.Log10(probabilityLow);
            double y2 = Tools.Log10(probabilityHigh);
            if ((y2 - y1) == 0)
            {
                // The interpolator's flat rule: the quantile is 10^x1 across the segment.
                return Math.Pow(10d, x1) * (to - from);
            }
            double slope = (x2 - x1) / (y2 - y1);
            double quantileFrom = Math.Pow(10d, x1 + (Tools.Log10(from) - y1) * slope);
            double quantileTo = Math.Pow(10d, x1 + (Tools.Log10(to) - y1) * slope);
            if (Math.Abs(slope + 1d) < 1e-12)
            {
                // s → −1: ∫ A/p dp = A·ln(to/from), with A = c(p)·p constant on the segment.
                return quantileFrom * from * Math.Log(to / from);
            }
            return (quantileTo * to - quantileFrom * from) / (slope + 1d);
        }

        #endregion

        #region Utility

        /// <summary>
        /// Creates a deep copy of the curve's stored state. Recorded risk points are runtime
        /// working state and are not copied (v1.0 behavior).
        /// </summary>
        /// <returns>The copy.</returns>
        public Curve Clone()
        {
            return new Curve
            {
                IsExhaustive = IsExhaustive,
                TotalProbability = TotalProbability,
                MassBalance = MassBalance,
                Mean = Mean,
                StandardDeviation = StandardDeviation,
                Skewness = Skewness,
                Kurtosis = Kurtosis,
                Alpha = Alpha,
                ConsequenceThreshold = ConsequenceThreshold,
                HazardThreshold = HazardThreshold,
                ConsequenceThresholdProbability = ConsequenceThresholdProbability,
                HazardThresholdProbability = HazardThresholdProbability,
                ValueAtRisk = ValueAtRisk,
                ConditionalValueAtRisk = ConditionalValueAtRisk,
                LECConsequences = (double[])_lecConsequences.Clone(),
                LECProbabilities = (double[])_lecProbabilities.Clone(),
                HazardFrequencyHazards = (double[])_hazardFrequencyHazards.Clone(),
                HazardFrequencyProbabilities = (double[])_hazardFrequencyProbabilities.Clone(),
                HazardVsCenHazards = (double[])_hazardVsCenHazards.Clone(),
                HazardVsCenConsequences = (double[])_hazardVsCenConsequences.Clone(),
                CumulativeFailureProbabilities = (double[])_cumulativeFailureProbabilities.Clone(),
                CumulativeExpectedConsequences = (double[])_cumulativeExpectedConsequences.Clone(),
                SystemResponseExceedanceProbabilities = (double[])_systemResponseExceedanceProbabilities.Clone(),
                SystemResponseProbabilities = (double[])_systemResponseProbabilities.Clone(),
            };
        }

        /// <summary>
        /// Clears the recorded risk points — call only after the curve, profiles, and measures
        /// have been built (v1.0's post-aggregation memory dump).
        /// </summary>
        public void DumpMemory()
        {
            RiskPoints.Clear();
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Builds a self-normalized profile view: each ordinate divided by the profile's own
        /// terminal (index 0 — the largest hazard in the descending storage). Empty when the
        /// arrays are absent or the terminal is not positive.
        /// </summary>
        /// <param name="xValues">The hazard ordinates (descending).</param>
        /// <param name="yValues">The cumulative ordinates, parallel to the hazards.</param>
        /// <returns>The normalized view.</returns>
        private static OrderedPairedData BuildNormalizedView(double[] xValues, double[] yValues)
        {
            if (xValues.Length == 0 || xValues.Length != yValues.Length || !(yValues[0] > 0d))
            {
                return new OrderedPairedData(true, SortOrder.Descending, false, SortOrder.Descending);
            }
            var normalized = new double[yValues.Length];
            for (int i = 0; i < yValues.Length; i++)
            {
                normalized[i] = yValues[i] / yValues[0];
            }
            return BuildView(xValues, normalized, yStrict: false, yOrder: SortOrder.Descending);
        }

        /// <summary>
        /// Builds an <see cref="OrderedPairedData"/> view over parallel arrays; mismatched or
        /// empty arrays yield an empty view so a partially populated curve never throws on read.
        /// </summary>
        /// <param name="xValues">The X ordinates (descending).</param>
        /// <param name="yValues">The Y ordinates.</param>
        /// <param name="yStrict">Whether the Y axis is strictly ordered.</param>
        /// <param name="yOrder">The declared Y sort order.</param>
        /// <returns>The view.</returns>
        private static OrderedPairedData BuildView(double[] xValues, double[] yValues, bool yStrict, SortOrder yOrder)
        {
            if (xValues.Length == 0 || xValues.Length != yValues.Length)
            {
                return new OrderedPairedData(true, SortOrder.Descending, yStrict, yOrder);
            }
            var ordinates = new List<Ordinate>(xValues.Length);
            for (int i = 0; i < xValues.Length; i++)
            {
                ordinates.Add(new Ordinate(xValues[i], yValues[i]));
            }
            return new OrderedPairedData(ordinates, true, SortOrder.Descending, yStrict, yOrder);
        }

        /// <summary>
        /// Thins the exact ordinates to the output length with a hybrid target ladder — half the
        /// targets log-spaced in exceedance probability (dense where the extreme tail lives),
        /// half linear in consequence (bounding the interpolation gap across the flat bulk of
        /// the curve, where whole decades of consequence can share nearly one exceedance value)
        /// — always retaining the first and last ordinates, and stores the result.
        /// </summary>
        /// <param name="consequences">The exact consequence ordinates, descending.</param>
        /// <param name="probabilities">The exact exceedance ordinates, ascending.</param>
        /// <param name="outputLength">The output resolution.</param>
        private void ThinAndStore(List<double> consequences, List<double> probabilities, int outputLength)
        {
            if (consequences.Count <= outputLength)
            {
                _lecConsequences = consequences.ToArray();
                _lecProbabilities = probabilities.ToArray();
                _lecView = null;
                return;
            }

            var keep = new SortedSet<int> { 0, consequences.Count - 1 };
            int half = Math.Max(2, outputLength / 2);

            // The tail ladder: log-spaced exceedance targets from the smallest positive
            // exceedance up to the terminal exceedance.
            double smallest = double.NaN;
            for (int i = 0; i < probabilities.Count; i++)
            {
                if (probabilities[i] > 0d) { smallest = probabilities[i]; break; }
            }
            double largest = probabilities[probabilities.Count - 1];
            if (!double.IsNaN(smallest) && largest > smallest)
            {
                double logSmallest = Math.Log(smallest);
                double logRatio = Math.Log(largest / smallest);
                int cursor = 0;
                for (int j = 0; j < half; j++)
                {
                    double target = Math.Exp(logSmallest + logRatio * j / (half - 1d));
                    while (cursor < probabilities.Count - 1 && probabilities[cursor] < target)
                    {
                        cursor++;
                    }
                    keep.Add(cursor);
                }
            }

            // The bulk ladder: linear consequence targets from the largest down to the smallest
            // recorded consequence.
            double largestConsequence = consequences[0];
            double smallestConsequence = consequences[consequences.Count - 1];
            if (largestConsequence > smallestConsequence)
            {
                int cursor = 0;
                for (int j = 0; j < half; j++)
                {
                    double target = largestConsequence - (largestConsequence - smallestConsequence) * j / (half - 1d);
                    while (cursor < consequences.Count - 1 && consequences[cursor] > target)
                    {
                        cursor++;
                    }
                    keep.Add(cursor);
                }
            }

            var thinnedConsequences = new double[keep.Count];
            var thinnedProbabilities = new double[keep.Count];
            int index = 0;
            foreach (int i in keep)
            {
                thinnedConsequences[index] = consequences[i];
                thinnedProbabilities[index] = probabilities[i];
                index++;
            }
            _lecConsequences = thinnedConsequences;
            _lecProbabilities = thinnedProbabilities;
            _lecView = null;
        }

        #endregion
    }
}
