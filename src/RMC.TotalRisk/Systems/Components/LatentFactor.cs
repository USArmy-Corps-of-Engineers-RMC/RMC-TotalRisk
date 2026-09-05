using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Linq;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.Systems.Components
{
    /// <summary>
    /// One named latent factor of a system component's failure-mode capacity dependence: a
    /// shared standard-normal driver with one loading per combination unit, inducing the
    /// between-unit correlation ρij = Σf λif·λjf across the component's declared factors.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The classic length-effect construction: many failure modes (levee segments, gates of one
    /// design) share common capacity drivers — a soil unit, a construction era, a load path — so
    /// their joint failure probability sits far above independence. Each factor carries one
    /// loading per combination unit (the state-group dimension the correlation matrix uses),
    /// positionally aligned with the matrix rows; loadings lie in [−1, 1] and each unit's
    /// squared-loading sum across factors may not exceed one — the remainder is the unit's
    /// idiosyncratic capacity variance, which keeps the induced matrix positive semi-definite by
    /// construction. The induced matrix must still pass the component's positive-definiteness
    /// gate: a unit with a squared-loading sum of exactly one has no idiosyncratic variance, and
    /// two such units on proportional loadings induce a singular matrix.
    /// </para>
    /// <para>
    /// <see cref="Name"/> and <see cref="Description"/> are display metadata — serialized, never
    /// hashed. <see cref="Loadings"/> are compute content: the factor list serializes as a
    /// conditional child of the component only under the latent-factors dependency mode, so
    /// every other component keeps a byte-identical form, canonical identity, and seed, while a
    /// configured mode hashes the loadings in declared factor order — factor order is semantic,
    /// and reordering factors is a deliberate compute edit even though the induced matrix is
    /// order-invariant.
    /// </para>
    /// </remarks>
    public sealed class LatentFactor : INotifyPropertyChanged
    {
        /// <summary>Initializes an empty latent factor.</summary>
        public LatentFactor()
        {
        }

        /// <summary>Initializes a configured latent factor.</summary>
        /// <param name="name">The factor name.</param>
        /// <param name="loadings">The per-combination-unit loadings.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public LatentFactor(string name, IReadOnlyList<double> loadings)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            if (loadings == null) throw new ArgumentNullException(nameof(loadings));
            _loadings = new double[loadings.Count];
            for (int i = 0; i < loadings.Count; i++)
            {
                _loadings[i] = loadings[i];
            }
        }

        /// <summary>Restores a factor from its serialized form. Reads are permissive; malformed content fails validation.</summary>
        /// <param name="xElement">The serialized factor.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        internal LatentFactor(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            _name = SerializationUtilities.ReadString(xElement, nameof(Name));
            _description = SerializationUtilities.ReadString(xElement, nameof(Description));
            string loadings = SerializationUtilities.ReadString(xElement, nameof(Loadings));
            if (loadings.Length == 0)
            {
                _loadings = Array.Empty<double>();
            }
            else
            {
                string[] tokens = loadings.Split('|');
                _loadings = new double[tokens.Length];
                for (int i = 0; i < tokens.Length; i++)
                {
                    _loadings[i] = SerializationUtilities.ParseDouble(tokens[i], double.NaN);
                }
            }
        }

        /// <summary>Backing field for <see cref="Name"/>.</summary>
        private string _name = string.Empty;

        /// <summary>Backing field for <see cref="Description"/>.</summary>
        private string _description = string.Empty;

        /// <summary>Backing field for <see cref="Loadings"/>.</summary>
        private double[] _loadings = Array.Empty<double>();

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>The factor display name. Metadata — serialized, never hashed.</summary>
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name == value) return;
                _name = value ?? string.Empty;
                RaisePropertyChanged(nameof(Name));
            }
        }

        /// <summary>The factor description. Metadata — serialized, never hashed.</summary>
        public string Description
        {
            get { return _description; }
            set
            {
                if (_description == value) return;
                _description = value ?? string.Empty;
                RaisePropertyChanged(nameof(Description));
            }
        }

        /// <summary>
        /// The per-combination-unit loadings, positionally aligned with the component's
        /// combination units (the correlation-matrix row order). Compute-relevant.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public IReadOnlyList<double> Loadings
        {
            get { return _loadings; }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                var loadings = new double[value.Count];
                for (int i = 0; i < value.Count; i++)
                {
                    loadings[i] = value[i];
                }
                _loadings = loadings;
                RaisePropertyChanged(nameof(Loadings));
            }
        }

        /// <summary>Serializes the factor (loadings pipe-joined, G17 invariant).</summary>
        /// <returns>The serialized factor.</returns>
        internal XElement ToXElement()
        {
            var element = new XElement(nameof(LatentFactor));
            element.SetAttributeValue(nameof(Name), _name);
            element.SetAttributeValue(nameof(Description), _description);
            var tokens = new string[_loadings.Length];
            for (int i = 0; i < _loadings.Length; i++)
            {
                tokens[i] = SerializationUtilities.FormatDouble(_loadings[i]);
            }
            element.SetAttributeValue(nameof(Loadings), string.Join("|", tokens));
            return element;
        }

        /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
        /// <param name="propertyName">The changed property.</param>
        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
