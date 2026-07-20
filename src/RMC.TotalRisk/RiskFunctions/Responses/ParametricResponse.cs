using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Numerics;
using Numerics.Data;
using Numerics.Distributions;
using Numerics.Mathematics.Optimization;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.RiskFunctions.Responses
{
    /// <summary>
    /// A parametric response (fragility) function: a fitted parent distribution (natural-log
    /// normal by default) whose CDF is the failure probability, with knowledge uncertainty as a
    /// posterior parameter-set ensemble — built by the parametric bootstrap
    /// (<see cref="Estimate()"/>), or imported directly (<see cref="Estimate(IList{ParameterSet})"/>)
    /// from an external fit.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported from v1.0 <c>ParametricResponse</c> with the domain surface preserved: the explicit
    /// <see cref="Estimate()"/> lifecycle, NON-exceedance probability ordinates (no inversion —
    /// the deliberate difference from the parametric hazard), mean sampling from the posterior
    /// mean curve without reversal, curve-form sampling unsupported
    /// (<see cref="SampleResponseFunction()"/> throws, as in v1.0), and
    /// <see cref="IsMonotonic"/> always true. Sampling dimension D = 0 — the realization index
    /// looks up the pre-computed posterior directly.
    /// </para>
    /// <para>
    /// Improvements over v1.0 mirror the parametric hazard: the posterior-injection overload is
    /// new; the full-posterior bounds scan is race-free; percentile lookup clamps to the last
    /// posterior index; sampling before estimation throws; and a failed bootstrap propagates an
    /// exception.
    /// </para>
    /// </remarks>
    public class ParametricResponse : ResponseFunctionBase
    {
        #region Construction

        /// <summary>
        /// Initializes a parametric response with the v1.0 defaults: natural-log normal parent,
        /// uncertain, effective record length 100, confidence interval width 0.9, 10,000
        /// realizations, PRNG seed 67891, method-of-moments estimation, and the standard 23
        /// non-exceedance probability ordinates.
        /// </summary>
        public ParametricResponse()
        {
            _probabilityOrdinates = new ObservableCollection<double>(DefaultProbabilityOrdinates());
            WireOrdinates();
        }

        /// <summary>
        /// Restores a parametric response from its serialized form, including its estimated
        /// posterior when present.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public ParametricResponse(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            Name = SerializationUtilities.ReadString(xElement, nameof(Name));
            Description = SerializationUtilities.ReadString(xElement, nameof(Description));
            SpecifiedHazard = SerializationUtilities.ReadString(xElement, nameof(SpecifiedHazard));
            HazardUnit = SerializationUtilities.ReadString(xElement, nameof(HazardUnit));
            _isUncertain = SerializationUtilities.ReadBoolean(xElement, nameof(IsUncertain), true);
            _effectiveRecordLength = SerializationUtilities.ReadInt32(xElement, nameof(EffectiveRecordLength), 100);
            _confidenceIntervalWidth = SerializationUtilities.ReadDouble(xElement, nameof(ConfidenceIntervalWidth), 0.9d);
            _realizations = SerializationUtilities.ReadInt32(xElement, nameof(Realizations), 10000);
            _prngSeed = SerializationUtilities.ReadInt32(xElement, nameof(PRNGSeed), 67891);
            _estimationMethod = SerializationUtilities.ReadEnum(xElement, nameof(EstimationMethod), ParameterEstimationMethod.MethodOfMoments);
            _posteriorImported = SerializationUtilities.ReadBoolean(xElement, nameof(PosteriorImported));

            string ordinatesText = SerializationUtilities.ReadString(xElement, nameof(ProbabilityOrdinates));
            _probabilityOrdinates = new ObservableCollection<double>(
                ordinatesText.Length == 0
                    ? DefaultProbabilityOrdinates()
                    : ordinatesText.Split('|').Select(t => SerializationUtilities.ParseDouble(t)));
            WireOrdinates();

            var distributionElement = xElement.Element(ParentDistributionElementName)?.Elements().FirstOrDefault();
            if (distributionElement != null)
            {
                var parsed = UnivariateDistributionFactory.CreateDistribution(distributionElement);
                if (parsed is not null) _parentDistribution = parsed;
            }

            string resultsText = xElement.Element(ResultsElementName)?.Value ?? string.Empty;
            if (resultsText.Length > 0)
            {
                Results = UncertaintyAnalysisResults.FromByteArray(Tools.Decompress(Convert.FromBase64String(resultsText))!);
                if (Results?.ParentDistribution is not null)
                {
                    _parentDistribution = Results.ParentDistribution.Clone();
                    _isEstimated = true;
                }
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// The wrapper element name for the serialized parent distribution.
        /// </summary>
        private const string ParentDistributionElementName = "ParentDistribution";

        /// <summary>
        /// The element name for the serialized posterior results (compressed JSON, base64).
        /// </summary>
        private const string ResultsElementName = "Results";

        /// <summary>
        /// Backing field for <see cref="ParentDistribution"/> — natural-log normal (v1.0 default).
        /// </summary>
        private UnivariateDistributionBase _parentDistribution = new LnNormal();

        /// <summary>
        /// Backing field for <see cref="IsUncertain"/>.
        /// </summary>
        private bool _isUncertain = true;

        /// <summary>
        /// Backing field for <see cref="EffectiveRecordLength"/>.
        /// </summary>
        private int _effectiveRecordLength = 100;

        /// <summary>
        /// Backing field for <see cref="ConfidenceIntervalWidth"/>.
        /// </summary>
        private double _confidenceIntervalWidth = 0.9d;

        /// <summary>
        /// Backing field for <see cref="Realizations"/>.
        /// </summary>
        private int _realizations = 10000;

        /// <summary>
        /// Backing field for <see cref="PRNGSeed"/> — 67891 (v1.0 response default; the hazard
        /// default is 12345 so paired hazard/response bootstraps stay independent).
        /// </summary>
        private int _prngSeed = 67891;

        /// <summary>
        /// Backing field for <see cref="EstimationMethod"/>.
        /// </summary>
        private ParameterEstimationMethod _estimationMethod = ParameterEstimationMethod.MethodOfMoments;

        /// <summary>
        /// Backing field for <see cref="ProbabilityOrdinates"/>.
        /// </summary>
        private ObservableCollection<double> _probabilityOrdinates;

        /// <summary>
        /// Backing field for <see cref="IsEstimated"/>.
        /// </summary>
        private bool _isEstimated;

        /// <summary>
        /// Backing field for <see cref="PosteriorImported"/>.
        /// </summary>
        private bool _posteriorImported;

        /// <summary>
        /// The bounds cache (full posterior when uncertain — the v1.0 response has no mean-only
        /// bounds variant).
        /// </summary>
        private double[]? _minmax;

        /// <summary>
        /// The fitted parent distribution whose CDF is the failure probability. Setting a new
        /// distribution clears the estimate.
        /// </summary>
        public UnivariateDistributionBase ParentDistribution
        {
            get { return _parentDistribution; }
            set
            {
                if (!ReferenceEquals(_parentDistribution, value) && value is not null)
                {
                    _parentDistribution = value;
                    InvalidateEstimate();
                    RaisePropertyChange(nameof(ParentDistribution));
                }
            }
        }

        /// <summary>
        /// Determines whether the function carries knowledge uncertainty (posterior sampling).
        /// </summary>
        public bool IsUncertain
        {
            get { return _isUncertain; }
            set
            {
                if (_isUncertain != value)
                {
                    _isUncertain = value;
                    InvalidateEstimate();
                    RaisePropertyChange(nameof(IsUncertain));
                }
            }
        }

        /// <summary>
        /// The effective record length (equivalent observations) driving bootstrap sampling
        /// variability. Valid range [10, 10000] (v1.0).
        /// </summary>
        public int EffectiveRecordLength
        {
            get { return _effectiveRecordLength; }
            set
            {
                if (_effectiveRecordLength != value)
                {
                    _effectiveRecordLength = value;
                    InvalidateEstimate();
                    RaisePropertyChange(nameof(EffectiveRecordLength));
                }
            }
        }

        /// <summary>
        /// The two-sided confidence-interval width the bootstrap summarizes at. Valid range (0, 1)
        /// exclusive (v1.0); default 0.9.
        /// </summary>
        public double ConfidenceIntervalWidth
        {
            get { return _confidenceIntervalWidth; }
            set
            {
                if (_confidenceIntervalWidth != value)
                {
                    _confidenceIntervalWidth = value;
                    InvalidateEstimate();
                    RaisePropertyChange(nameof(ConfidenceIntervalWidth));
                }
            }
        }

        /// <summary>
        /// The number of bootstrap replications (the posterior size). Valid range [100, 100000]
        /// for the bootstrap path (v1.0); a warning below 1,000. Posterior injection aligns this
        /// to the imported ensemble size.
        /// </summary>
        public int Realizations
        {
            get { return _realizations; }
            set
            {
                if (_realizations != value)
                {
                    _realizations = value;
                    InvalidateEstimate();
                    RaisePropertyChange(nameof(Realizations));
                }
            }
        }

        /// <summary>
        /// The bootstrap PRNG seed. Must be positive (v1.0); default 67891.
        /// </summary>
        public int PRNGSeed
        {
            get { return _prngSeed; }
            set
            {
                if (_prngSeed != value)
                {
                    _prngSeed = value;
                    InvalidateEstimate();
                    RaisePropertyChange(nameof(PRNGSeed));
                }
            }
        }

        /// <summary>
        /// The parameter-estimation method the bootstrap refits with. Method of moments by
        /// default; product moments are rejected for Weibull, and linear moments for Logistic,
        /// Weibull, Triangular, and PERT (v1.0 matrix — deliberately different from the hazard's).
        /// </summary>
        public ParameterEstimationMethod EstimationMethod
        {
            get { return _estimationMethod; }
            set
            {
                if (_estimationMethod != value)
                {
                    _estimationMethod = value;
                    InvalidateEstimate();
                    RaisePropertyChange(nameof(EstimationMethod));
                }
            }
        }

        /// <summary>
        /// The NON-exceedance probability ordinates the estimate is summarized at, in strictly
        /// ascending order within [0, 1] (v1.0 default: 23 ordinates from 0.001 to 0.999 — no
        /// exceedance inversion, the deliberate difference from the parametric hazard). Editing
        /// the collection clears the estimate.
        /// </summary>
        public ObservableCollection<double> ProbabilityOrdinates => _probabilityOrdinates;

        /// <summary>
        /// Determines whether the posterior has been estimated (bootstrap) or imported.
        /// </summary>
        public bool IsEstimated
        {
            get { return _isEstimated; }
            private set
            {
                if (_isEstimated != value)
                {
                    _isEstimated = value;
                    RaisePropertyChange(nameof(IsEstimated));
                }
            }
        }

        /// <summary>
        /// Determines whether the current posterior was imported via
        /// <see cref="Estimate(IList{ParameterSet})"/> rather than bootstrapped.
        /// </summary>
        public bool PosteriorImported => _posteriorImported;

        /// <summary>
        /// The estimated uncertainty results, index-aligned with <see cref="ProbabilityOrdinates"/>.
        /// </summary>
        public UncertaintyAnalysisResults? Results { get; private set; }

        /// <inheritdoc/>
        public override ResponseFunctionType FunctionType => ResponseFunctionType.Parametric;

        /// <inheritdoc/>
        public override bool IsDeterministic => !IsUncertain;

        /// <inheritdoc/>
        /// <remarks>D = 0: the realization index looks up the pre-computed posterior directly.</remarks>
        public override int SamplingDimensions => 0;

        #endregion

        #region Estimation

        /// <summary>
        /// Runs the parametric bootstrap and summarizes the posterior at
        /// <see cref="ProbabilityOrdinates"/> (v1.0 lifecycle — the engine never calls this
        /// implicitly).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the configuration is invalid, or when the bootstrap fails (v1.0 swallowed
        /// bootstrap failures silently; v1.1 propagates them).
        /// </exception>
        public void Estimate()
        {
            var (isValid, messages) = ValidateConfiguration(requireEstimated: false);
            if (!isValid)
                throw new InvalidOperationException("The parametric response configuration is invalid:\n" + string.Join("\n", messages));

            IsEstimated = false;
            _posteriorImported = false;
            ClearResults();

            double[] probs = ProbabilityOrdinates.ToArray();
            var results = new UncertaintyAnalysisResults
            {
                ParentDistribution = ParentDistribution,
                ModeCurve = new double[probs.Length],
            };
            for (int i = 0; i < probs.Length; i++)
                results.ModeCurve[i] = ParentDistribution.InverseCDF(probs[i]);
            Results = results;

            if (IsUncertain)
            {
                try
                {
                    var bootstrap = new BootstrapAnalysis(ParentDistribution, EstimationMethod, EffectiveRecordLength, Realizations, PRNGSeed);
                    Results = bootstrap.Estimate(probs, 1d - ConfidenceIntervalWidth);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "The parametric bootstrap failed. Try adjusting the input parameters or changing the parameter estimation method.", ex);
                }
            }

            IsEstimated = true;
        }

        /// <summary>
        /// Imports an externally fitted fragility posterior instead of bootstrapping (see the
        /// parametric hazard's overload — same contract; the importer layer passes Numerics
        /// artifacts, never an RMC-BestFit reference).
        /// </summary>
        /// <param name="parameterSets">The posterior parameter sets for <see cref="ParentDistribution"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the posterior is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the posterior is empty or malformed.</exception>
        public void Estimate(IList<ParameterSet> parameterSets)
        {
            IsEstimated = false;
            ClearResults();

            Results = ParametricPosterior.BuildResults(ParentDistribution, parameterSets,
                ProbabilityOrdinates.ToArray(), 1d - ConfidenceIntervalWidth);

            _realizations = parameterSets.Count;
            RaisePropertyChange(nameof(Realizations));
            _isUncertain = true;
            RaisePropertyChange(nameof(IsUncertain));
            _posteriorImported = true;
            RaisePropertyChange(nameof(PosteriorImported));
            IsEstimated = true;
        }

        #endregion

        #region IRiskFunction Methods

        /// <inheritdoc/>
        /// <remarks>
        /// The v1.0 matrix, mirroring the parametric hazard with the response-specific
        /// estimation-method rejections; see the class remarks.
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            return ValidateConfiguration(requireEstimated: true);
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">Always — parametric responses do not emit curve samples (v1.0 behavior).</exception>
        public override OrderedPairedData SampleResponseFunction()
        {
            throw new NotImplementedException("Parametric response functions do not emit ordered-pair curve samples.");
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">Always — v1.0 behavior.</exception>
        public override OrderedPairedData SampleResponseFunction(double percentile)
        {
            throw new NotImplementedException("Parametric response functions do not emit ordered-pair curve samples.");
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">Always — v1.0 behavior.</exception>
        public override OrderedPairedData SampleResponseFunction(int realizationIndex)
        {
            throw new NotImplementedException("Parametric response functions do not emit ordered-pair curve samples.");
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Uncertain: the posterior mean curve as an empirical distribution (no reversal —
        /// non-exceedance ordinates; Normal-Z probability transform, v1.0). Deterministic: a clone
        /// of the parent distribution.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the function has not been estimated.</exception>
        public override IUnivariateDistribution SampleFunction()
        {
            ThrowIfNotEstimated();
            if (IsUncertain)
            {
                double[] xValues = Results!.MeanCurve!.ToArray();
                double[] pValues = ProbabilityOrdinates.ToArray();
                return new EmpiricalDistribution(xValues, pValues) { ProbabilityTransform = Transform.NormalZ };
            }
            return ParentDistribution.Clone();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Posterior lookup at ⌊percentile × Realizations⌋, clamped to the last posterior draw
        /// (v1.1 fix — v1.0 indexed out of range at percentile 1.0).
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the function has not been estimated.</exception>
        public override IUnivariateDistribution SampleFunction(double percentile)
        {
            ThrowIfNotEstimated();
            if (!IsUncertain)
                return ParentDistribution.Clone();

            int index = Math.Min((int)Math.Floor(percentile * Realizations), Realizations - 1);
            if (index < 0) index = 0;
            var distribution = ParentDistribution.Clone();
            if (Results?.ParameterSets is not null && Results.ParameterSets[index].Values is not null)
            {
                distribution.SetParameters(Results.ParameterSets[index].Values);
            }
            return distribution;
        }

        /// <inheritdoc/>
        /// <remarks>Direct posterior lookup — the v1.0 index-based semantics.</remarks>
        /// <exception cref="InvalidOperationException">Thrown when the function has not been estimated.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the index is outside [0, Realizations) (v1.0 returned null).
        /// </exception>
        public override IUnivariateDistribution SampleFunction(int realizationIndex)
        {
            ThrowIfNotEstimated();
            if (realizationIndex < 0 || realizationIndex > Realizations - 1)
                throw new ArgumentOutOfRangeException(nameof(realizationIndex), "The realization index must be within the posterior ensemble.");
            if (!IsUncertain)
                return ParentDistribution.Clone();

            var distribution = ParentDistribution.Clone();
            if (Results?.ParameterSets is not null && Results.ParameterSets[realizationIndex].Values is not null)
            {
                distribution.SetParameters(Results.ParameterSets[realizationIndex].Values);
            }
            return distribution;
        }

        /// <inheritdoc/>
        /// <remarks>Always true — the parametric CDF is monotonic by construction (v1.0).</remarks>
        public override bool IsMonotonic()
        {
            return true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The quantile at the smallest probability ordinate — across the full posterior when
        /// uncertain (race-free parallel scan, v1.1 fix), else from the parent distribution.
        /// Non-exceedance probabilities are used directly (no inversion — v1.0).
        /// </remarks>
        public override double MinHazard()
        {
            return ComputeMinMax()[0];
        }

        /// <inheritdoc/>
        /// <remarks>See <see cref="MinHazard"/>.</remarks>
        public override double MaxHazard()
        {
            return ComputeMinMax()[1];
        }

        /// <inheritdoc/>
        /// <remarks>The parent CDF evaluated at <see cref="MinHazard"/> (v1.0).</remarks>
        public override double MinProbability()
        {
            return ParentDistribution.CDF(MinHazard());
        }

        /// <inheritdoc/>
        /// <remarks>The parent CDF evaluated at <see cref="MaxHazard"/> (v1.0).</remarks>
        public override double MaxProbability()
        {
            return ParentDistribution.CDF(MaxHazard());
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Surfaces the stored posterior summary; a different width re-slices the confidence
        /// intervals exactly from the stored parameter sets. Curves are index-aligned with
        /// <see cref="ProbabilityOrdinates"/>. Returns null when not estimated.
        /// </remarks>
        public override UncertaintyAnalysisResults? ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9)
        {
            if (confidenceIntervalWidth <= 0d || confidenceIntervalWidth >= 1d)
                throw new ArgumentOutOfRangeException(nameof(confidenceIntervalWidth), "The confidence interval width must be between 0 and 1.");
            if (!IsEstimated || Results is null)
                return null;
            if (!IsUncertain || Results.ParameterSets is null || confidenceIntervalWidth == ConfidenceIntervalWidth)
                return Results;

            return new UncertaintyAnalysisResults
            {
                ParentDistribution = Results.ParentDistribution,
                ParameterSets = Results.ParameterSets,
                ModeCurve = Results.ModeCurve,
                MeanCurve = Results.MeanCurve,
                ConfidenceIntervals = ParametricPosterior.SliceConfidenceIntervals(
                    ParentDistribution, Results.ParameterSets, ProbabilityOrdinates.ToArray(), confidenceIntervalWidth),
            };
        }

        #endregion

        #region Serialization

        /// <inheritdoc/>
        public override XElement ToXElement()
        {
            var element = new XElement(nameof(ParametricResponse));
            element.SetAttributeValue(nameof(Name), Name);
            element.SetAttributeValue(nameof(Description), Description);
            element.SetAttributeValue(nameof(SpecifiedHazard), SpecifiedHazard);
            element.SetAttributeValue(nameof(HazardUnit), HazardUnit);
            element.SetAttributeValue(nameof(IsUncertain), IsUncertain.ToString());
            element.SetAttributeValue(nameof(EffectiveRecordLength), EffectiveRecordLength.ToString(CultureInfo.InvariantCulture));
            element.SetAttributeValue(nameof(ConfidenceIntervalWidth), SerializationUtilities.FormatDouble(ConfidenceIntervalWidth));
            element.SetAttributeValue(nameof(Realizations), Realizations.ToString(CultureInfo.InvariantCulture));
            element.SetAttributeValue(nameof(PRNGSeed), PRNGSeed.ToString(CultureInfo.InvariantCulture));
            element.SetAttributeValue(nameof(EstimationMethod), EstimationMethod.ToString());
            element.SetAttributeValue(nameof(PosteriorImported), PosteriorImported.ToString());
            element.SetAttributeValue(nameof(ProbabilityOrdinates),
                string.Join("|", ProbabilityOrdinates.Select(SerializationUtilities.FormatDouble)));
            element.Add(new XElement(ParentDistributionElementName, ParentDistribution.ToXElement()));
            if (IsEstimated && Results is not null)
            {
                element.Add(new XElement(ResultsElementName,
                    Convert.ToBase64String(Tools.Compress(UncertaintyAnalysisResults.ToByteArray(Results))!)));
            }
            return element;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// The v1.0 default non-exceedance probability ordinates (23 values, 0.001 through 0.999).
        /// </summary>
        /// <returns>The default ordinates.</returns>
        private static double[] DefaultProbabilityOrdinates()
        {
            return new[]
            {
                0.001d, 0.002d, 0.005d, 0.008d, 0.01d, 0.02d, 0.05d, 0.08d, 0.1d, 0.2d, 0.3d, 0.5d,
                0.7d, 0.8d, 0.9d, 0.92d, 0.95d, 0.98d, 0.99d, 0.992d, 0.995d, 0.998d, 0.999d,
            };
        }

        /// <summary>
        /// Subscribes estimate invalidation to ordinate-collection edits (v1.0 behavior).
        /// </summary>
        private void WireOrdinates()
        {
            _probabilityOrdinates.CollectionChanged += (_, _) =>
            {
                InvalidateEstimate();
                RaisePropertyChange(nameof(ProbabilityOrdinates));
            };
        }

        /// <summary>
        /// Clears the estimate and bounds cache after any compute-relevant edit.
        /// </summary>
        private void InvalidateEstimate()
        {
            IsEstimated = false;
            _posteriorImported = false;
            ClearResults();
        }

        /// <summary>
        /// Clears the stored results and bounds cache.
        /// </summary>
        private void ClearResults()
        {
            Results = null;
            _minmax = null;
        }

        /// <summary>
        /// Throws when sampling is attempted before estimation (v1.1 upgrade of the v1.0 silent
        /// null return).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the function has not been estimated.</exception>
        private void ThrowIfNotEstimated()
        {
            if (!IsEstimated)
                throw new InvalidOperationException("The parametric response has not been estimated. Call Estimate() before sampling.");
        }

        /// <summary>
        /// Computes (and caches) the hazard bounds across the probability-ordinate span — the full
        /// posterior when uncertain (the v1.0 response has no mean-only variant), race-free (v1.1
        /// fix of the v1.0 unsynchronized parallel scan).
        /// </summary>
        /// <returns>The cached [min, max] pair.</returns>
        private double[] ComputeMinMax()
        {
            if (_minmax is not null)
                return _minmax;

            double minP = ProbabilityOrdinates.Min();
            double maxP = ProbabilityOrdinates.Max();
            var minmax = new double[2];

            if (IsUncertain && Results?.ParameterSets is not null)
            {
                double globalMin = double.MaxValue;
                double globalMax = double.MinValue;
                object gate = new object();
                Parallel.For(0, Realizations,
                    () => (min: double.MaxValue, max: double.MinValue),
                    (idx, _, local) =>
                    {
                        if (Results.ParameterSets[idx].Values is null) return local;
                        var dist = ParentDistribution.Clone();
                        dist.SetParameters(Results.ParameterSets[idx].Values);
                        double minX = dist.InverseCDF(minP);
                        double maxX = dist.InverseCDF(maxP);
                        return (Math.Min(local.min, minX), Math.Max(local.max, maxX));
                    },
                    local =>
                    {
                        lock (gate)
                        {
                            if (local.min < globalMin) globalMin = local.min;
                            if (local.max > globalMax) globalMax = local.max;
                        }
                    });
                minmax[0] = globalMin;
                minmax[1] = globalMax;
            }
            else
            {
                minmax[0] = ParentDistribution.InverseCDF(minP);
                minmax[1] = ParentDistribution.InverseCDF(maxP);
            }

            _minmax = minmax;
            return minmax;
        }

        /// <summary>
        /// Runs the v1.0 validation matrix, optionally requiring an existing estimate.
        /// </summary>
        /// <param name="requireEstimated">Whether a missing estimate is an error (true for <see cref="Validate"/>).</param>
        /// <returns>The validation outcome.</returns>
        private (bool IsValid, List<string> ValidationMessages) ValidateConfiguration(bool requireEstimated)
        {
            var messages = new List<string>();

            if (string.IsNullOrEmpty(SpecifiedHazard))
                messages.Add("Error: The parametric response function does not have a specified hazard type.");
            if (string.IsNullOrEmpty(HazardUnit))
                messages.Add("Error: The parametric response function does not have a specified hazard unit.");
            if (!ParentDistribution.ParametersValid)
                messages.Add("Error: The parent distribution parameters are invalid.");
            if (EffectiveRecordLength < 10 || EffectiveRecordLength > 10000)
                messages.Add("Error: The effective record length must be between 10 and 10,000.");
            if (ConfidenceIntervalWidth <= 0d || ConfidenceIntervalWidth >= 1d)
                messages.Add("Error: The confidence interval width must be between 0 and 1.");
            if (PRNGSeed <= 0)
                messages.Add("Error: The PRNG seed must be greater than 0.");
            if (!PosteriorImported && (Realizations < 100 || Realizations > 100000))
                messages.Add("Error: The number of realizations must be between 100 and 100,000.");
            else if (Realizations < 1000)
                messages.Add("Warning: The number of realizations is less than 1,000. The accuracy of the confidence intervals and mean curve will be diminished.");

            if (ProbabilityOrdinates.Count == 0)
                messages.Add("Error: There must be at least one probability ordinate.");
            for (int i = 0; i < ProbabilityOrdinates.Count; i++)
            {
                if (ProbabilityOrdinates[i] < 0d || ProbabilityOrdinates[i] > 1d)
                {
                    messages.Add("Error: All probability values must be between 0 and 1. Please resolve all of the errors in the probability ordinate table.");
                    break;
                }
                if (i > 0 && ProbabilityOrdinates[i] <= ProbabilityOrdinates[i - 1])
                {
                    messages.Add("Error: The probability values must be in ascending order. Please resolve all of the errors in the probability ordinate table.");
                    break;
                }
            }

            if (IsUncertain && EstimationMethod == ParameterEstimationMethod.MethodOfMoments
                && ParentDistribution.Type == UnivariateDistributionType.Weibull)
            {
                messages.Add("Error: The selected distribution cannot be estimated with product moments.");
            }
            else if (IsUncertain && EstimationMethod == ParameterEstimationMethod.MethodOfLinearMoments
                && (ParentDistribution.Type == UnivariateDistributionType.Logistic
                    || ParentDistribution.Type == UnivariateDistributionType.Weibull
                    || ParentDistribution.Type == UnivariateDistributionType.Triangular
                    || ParentDistribution.Type == UnivariateDistributionType.Pert))
            {
                messages.Add("Error: The selected distribution cannot be estimated with linear moments.");
            }

            if (requireEstimated && !IsEstimated)
                messages.Add("Error: The parametric response function has not been estimated.");

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        #endregion
    }
}
