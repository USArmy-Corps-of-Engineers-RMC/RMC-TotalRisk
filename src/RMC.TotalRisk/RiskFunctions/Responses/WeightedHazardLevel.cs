using System;
using System.ComponentModel;
using System.Xml.Linq;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.RiskFunctions.Responses
{
    /// <summary>
    /// One secondary hazard level of a bivariate response surface and the probability weight it
    /// carries in the weighted collapse onto the primary axis.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Ported verbatim from the v1.0 <c>WeightedHazardLevel</c>: the serialized form is the exact
    /// legacy <c>&lt;WeightedHazardLevel Level Weight/&gt;</c> attribute shape, so a future
    /// project importer lifts legacy payloads unchanged. Both attributes are compute content —
    /// the level positions the surface column and the weight drives the standalone collapse — so
    /// neither is stripped from canonical hashing.
    /// </para>
    /// </remarks>
    public class WeightedHazardLevel : INotifyPropertyChanged
    {
        #region Construction

        /// <summary>
        /// Initializes a weighted hazard level at the v1.0 defaults (level 0, weight 0).
        /// </summary>
        public WeightedHazardLevel()
        {
        }

        /// <summary>
        /// Restores a weighted hazard level from its serialized form. Reads are permissive: a
        /// missing attribute keeps its default of zero, while an unparseable token becomes
        /// <see cref="double.NaN"/> so the owning response's validation rejects it rather than a
        /// fabricated value entering the surface.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public WeightedHazardLevel(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            string? levelText = xElement.Attribute(nameof(Level))?.Value;
            if (levelText != null) _level = SerializationUtilities.ParseDouble(levelText, double.NaN);
            string? weightText = xElement.Attribute(nameof(Weight))?.Value;
            if (weightText != null) _weight = SerializationUtilities.ParseDouble(weightText, double.NaN);
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Level"/>.
        /// </summary>
        private double _level;

        /// <summary>
        /// Backing field for <see cref="Weight"/>.
        /// </summary>
        private double _weight;

        /// <summary>
        /// Occurs when a property changes. Passive: headless callers never subscribe; the future
        /// UI layer data-binds.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// The secondary hazard level (e.g., a pool elevation the surface column represents).
        /// </summary>
        public double Level
        {
            get { return _level; }
            set
            {
                if (_level != value)
                {
                    _level = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
                }
            }
        }

        /// <summary>
        /// The probability weight this level carries in the weighted collapse. Weights across a
        /// response's secondary levels must each lie in [0, 1] and sum to one.
        /// </summary>
        public double Weight
        {
            get { return _weight; }
            set
            {
                if (_weight != value)
                {
                    _weight = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Weight)));
                }
            }
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes the weighted hazard level as the exact legacy attribute shape
        /// <c>&lt;WeightedHazardLevel Level Weight/&gt;</c> ("G17" invariant doubles).
        /// </summary>
        /// <returns>The serialized form.</returns>
        public XElement ToXElement()
        {
            var element = new XElement(nameof(WeightedHazardLevel));
            element.SetAttributeValue(nameof(Level), SerializationUtilities.FormatDouble(_level));
            element.SetAttributeValue(nameof(Weight), SerializationUtilities.FormatDouble(_weight));
            return element;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates an independent copy of this weighted hazard level.
        /// </summary>
        /// <returns>A new instance carrying the same level and weight.</returns>
        public WeightedHazardLevel Clone()
        {
            return new WeightedHazardLevel { Level = _level, Weight = _weight };
        }

        #endregion
    }
}
