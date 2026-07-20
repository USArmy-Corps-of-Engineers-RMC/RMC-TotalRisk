using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Linq;
using Numerics.Distributions;
using Numerics.Sampling;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Core
{
    /// <summary>
    /// The shared implementation base for all risk input functions: property-change notification,
    /// identity-metadata backing, the canonical-hash pipeline, and the per-function percentile
    /// sampler machinery.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Each function owns its sampler (architecture doc §5.8): <see cref="SetupSampler"/>
    /// pre-allocates an N×D percentile matrix, where D is <see cref="SamplingDimensions"/>, and
    /// per-realization sampling reads row <c>realizationIndex</c> via
    /// <see cref="Percentile(int, int)"/>. Functions with D = 0 (deterministic, or posterior-indexed
    /// parametric functions) allocate no matrix but still record <see cref="SampleSize"/> for
    /// index-bound checks.
    /// </para>
    /// <para>
    /// <see cref="INotifyPropertyChanged"/> is a passive contract: headless callers never
    /// subscribe; the future UI layer data-binds to it.
    /// </para>
    /// </remarks>
    public abstract class RiskFunctionBase : IRiskFunction
    {
        #region Members

        /// <summary>
        /// Backing field for <see cref="Name"/>.
        /// </summary>
        private string _name = string.Empty;

        /// <summary>
        /// Backing field for <see cref="Description"/>.
        /// </summary>
        private string _description = string.Empty;

        /// <summary>
        /// Backing field for <see cref="SpecifiedHazard"/>.
        /// </summary>
        private string _specifiedHazard = string.Empty;

        /// <summary>
        /// Backing field for <see cref="HazardUnit"/>.
        /// </summary>
        private string _hazardUnit = string.Empty;

        /// <summary>
        /// The pre-allocated N×D percentile matrix; null until <see cref="SetupSampler"/> runs, and
        /// null when <see cref="SamplingDimensions"/> is zero.
        /// </summary>
        protected double[,]? _percentiles;

        /// <inheritdoc/>
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    RaisePropertyChange(nameof(Name));
                }
            }
        }

        /// <inheritdoc/>
        public string Description
        {
            get { return _description; }
            set
            {
                if (_description != value)
                {
                    _description = value;
                    RaisePropertyChange(nameof(Description));
                }
            }
        }

        /// <inheritdoc/>
        public string SpecifiedHazard
        {
            get { return _specifiedHazard; }
            set
            {
                if (_specifiedHazard != value)
                {
                    _specifiedHazard = value;
                    RaisePropertyChange(nameof(SpecifiedHazard));
                }
            }
        }

        /// <inheritdoc/>
        public string HazardUnit
        {
            get { return _hazardUnit; }
            set
            {
                if (_hazardUnit != value)
                {
                    _hazardUnit = value;
                    RaisePropertyChange(nameof(HazardUnit));
                }
            }
        }

        /// <inheritdoc/>
        public abstract bool IsDeterministic { get; }

        /// <inheritdoc/>
        public abstract int SamplingDimensions { get; }

        /// <summary>
        /// The realization count recorded by the last <see cref="SetupSampler"/> call — the row
        /// count of the percentile matrix, and the index bound for D = 0 functions. Zero until the
        /// sampler has been set up.
        /// </summary>
        public int SampleSize { get; protected set; }

        /// <summary>
        /// Occurs when a property of the function changes. Passive: headless callers never
        /// subscribe; the future UI layer data-binds.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region IRiskFunction Methods

        /// <inheritdoc/>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the sample size is not positive.</exception>
        /// <exception cref="NotSupportedException">Thrown when the sampling scheme is unrecognized.</exception>
        public virtual void SetupSampler(int sampleSize, int seed, SamplingScheme scheme)
        {
            if (sampleSize <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSize), "The sample size must be positive.");

            SampleSize = sampleSize;
            int dimensions = SamplingDimensions;
            if (dimensions == 0)
            {
                _percentiles = null;
                return;
            }

            int positiveSeed = ToPositiveSeed(seed);
            _percentiles = scheme switch
            {
                SamplingScheme.LatinHypercube => LatinHypercube.Random(sampleSize, dimensions, positiveSeed),
                SamplingScheme.LatinHypercubeMedian => LatinHypercube.Median(sampleSize, dimensions, positiveSeed),
                SamplingScheme.MonteCarlo => SeedHelpers.IndependentUniform(sampleSize, dimensions, positiveSeed),
                _ => throw new NotSupportedException($"The sampling scheme '{scheme}' is not supported."),
            };
        }

        /// <inheritdoc/>
        public abstract UncertaintyAnalysisResults? ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9);

        /// <inheritdoc/>
        public abstract (bool IsValid, List<string> ValidationMessages) Validate();

        /// <inheritdoc/>
        public abstract XElement ToXElement();

        /// <inheritdoc/>
        public byte[] CanonicalHash()
        {
            return CanonicalContentHasher.Hash(ToXElement(), CanonicalizationRules.ModelRules);
        }

        #endregion

        #region Protected Helpers

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void RaisePropertyChange(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Reads the pre-allocated percentile for a realization and sampling dimension.
        /// </summary>
        /// <param name="realizationIndex">The realization row, in [0, <see cref="SampleSize"/>).</param>
        /// <param name="dimension">The sampling dimension column, in [0, <see cref="SamplingDimensions"/>).</param>
        /// <returns>The uniform (0, 1) percentile driving this function's draw.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <see cref="SetupSampler"/> has not been called (or the function has no
        /// sampling dimensions).
        /// </exception>
        protected double Percentile(int realizationIndex, int dimension)
        {
            if (_percentiles == null)
                throw new InvalidOperationException("SetupSampler() must be called before sampling by realization index.");
            return _percentiles[realizationIndex, dimension];
        }

        /// <summary>
        /// Maps any 32-bit seed onto [1, int.MaxValue] deterministically.
        /// </summary>
        /// <param name="seed">The seed, possibly zero or negative (content-derived seeds span the full int range).</param>
        /// <returns>An equivalent strictly positive seed.</returns>
        /// <remarks>
        /// <see cref="LatinHypercube"/> treats a non-positive seed as "use the wall clock", which
        /// would silently destroy reproducibility — content-derived seeds from
        /// <see cref="SeedHelpers.HashCombine(int, byte[], int)"/> must therefore be folded into the
        /// positive range before reaching any Numerics sampler.
        /// </remarks>
        private static int ToPositiveSeed(int seed)
        {
            return (int)((uint)seed % int.MaxValue) + 1;
        }

        #endregion
    }
}
