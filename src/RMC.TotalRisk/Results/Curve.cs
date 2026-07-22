using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Numerics.Data;
using Numerics.Mathematics;
using Numerics.Mathematics.Integration;

namespace RMC.TotalRisk.Results
{
    /// <summary>
    /// A loss exceedance curve (LEC) and its risk measures for one risk type: the exact exceedance
    /// curve built from recorded risk points, stable central moments, the hazard-frequency and
    /// hazard-versus-conditional-consequence profiles, and the risk-measure catalog.
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
    /// probability. The conditional-value-at-risk integral runs on
    /// <see cref="AdaptiveGaussKronrod"/> with explicit tolerance and evaluation caps — v1.0 used
    /// library defaults on its steepest integrand.
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
        /// <see cref="Alpha"/>, integrated over the log-log LEC quantile by
        /// <see cref="AdaptiveGaussKronrod"/>; NaN until computed or when the integration fails.
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
        /// <exception cref="ArgumentNullException">Thrown when either list is null.</exception>
        public void AddRiskPoint(double hazardLevel, double hazardProbability, List<double> responseProbabilities, List<double> consequences)
        {
            if (responseProbabilities == null) throw new ArgumentNullException(nameof(responseProbabilities));
            if (consequences == null) throw new ArgumentNullException(nameof(consequences));
            RiskPoints.Add(new RiskPoint
            {
                HazardLevel = hazardLevel,
                HazardProbability = hazardProbability,
                HazardProbabilityMass = hazardProbability,
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
            CreateCurve(pairs, outputLength);
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
        /// Builds the hazard-frequency and hazard-versus-conditional-consequence profiles from the
        /// recorded risk points on the driving hazard axis (v1.0 port; the optional profile-axis
        /// remap is deferred with open question Q-T to the risk-diagnostics sessions).
        /// </summary>
        public void CreateProfiles()
        {
            if (RiskPoints.Count < 2) return;

            RiskPoints.Sort((x, y) => -x.HazardLevel.CompareTo(y.HazardLevel));

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
        /// smallest consequence). The conditional-value-at-risk integral runs
        /// <see cref="AdaptiveGaussKronrod"/> over [1e-16, α] of the log-log LEC quantile with
        /// explicit caps (relative tolerance 1e-8, depth 100, one million evaluations, minimum
        /// depth 2 — the engine's steepest integrand gets the same discipline as the risk
        /// integral; v1.0 used library defaults).
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
                ConsequenceThresholdProbability = lec.GetYFromX(consequenceThreshold, Transform.Logarithmic, Transform.Logarithmic);

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

                var integrator = new AdaptiveGaussKronrod(p => lec.GetXFromY(p, Transform.Logarithmic, Transform.Logarithmic),
                    ProbabilityFloor, alpha)
                {
                    ReportFailure = false,
                    RelativeTolerance = 1e-8,
                    MaxDepth = 100,
                    MaxFunctionEvaluations = 1_000_000,
                    MinDepth = 2,
                };
                integrator.Integrate();
                ConditionalValueAtRisk = integrator.Status != IntegrationStatus.Failure
                    ? integrator.Result / alpha
                    : double.NaN;
            }

            if (!double.IsNaN(hazardThreshold) && _hazardFrequencyHazards.Length > 1)
            {
                HazardThresholdProbability = HazardFrequency.GetYFromX(hazardThreshold, Transform.Logarithmic, Transform.Logarithmic);
            }
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
