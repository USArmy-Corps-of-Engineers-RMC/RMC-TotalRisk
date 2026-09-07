using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Numerics;
using Numerics.Mathematics.Optimization;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Analyses
{
    /// <summary>
    /// One declared study constraint: a metric selector, an inequality sense, the threshold,
    /// and the trajectory scope the constraint is evaluated at.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The constraint is immutable (replace it to edit). The sense reuses the numerics
    /// constrained-optimization vocabulary and admits the two inequalities only — an equality
    /// on a continuous metric selects nothing. Element and attribute names are append-only
    /// serialized contract; the sense serializes by enum name, and the metric writes as the
    /// single child.
    /// </para>
    /// </remarks>
    public sealed class CostBenefitConstraint
    {
        #region Construction

        /// <summary>
        /// Initializes a constraint declaration.
        /// </summary>
        /// <param name="metric">The metric selector the constraint reads.</param>
        /// <param name="sense">The inequality sense (at most, or at least).</param>
        /// <param name="threshold">The threshold, in the metric's own units.</param>
        /// <param name="scope">The trajectory scope the constraint is evaluated at.</param>
        /// <exception cref="ArgumentNullException">Thrown when the metric is null.</exception>
        public CostBenefitConstraint(CostBenefitMetric metric, ConstraintType sense, double threshold,
            ConstraintScope scope = ConstraintScope.EveryEpoch)
        {
            Metric = metric ?? throw new ArgumentNullException(nameof(metric));
            Sense = sense;
            Threshold = threshold;
            Scope = scope;
        }

        /// <summary>
        /// Restores a constraint from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public CostBenefitConstraint(XElement xElement)
            : this(ReadMetric(SerializationUtilities.RequireElement(xElement, nameof(xElement))),
                SerializationUtilities.ReadEnum(xElement, nameof(Sense), ConstraintType.LesserThanOrEqualTo),
                SerializationUtilities.ReadDouble(xElement, nameof(Threshold)),
                SerializationUtilities.ReadEnum(xElement, nameof(Scope), ConstraintScope.EveryEpoch))
        {
        }

        /// <summary>
        /// Reads the metric child; a missing child reads as the mean-total-risk selector so
        /// older forms load forward.
        /// </summary>
        /// <param name="xElement">The serialized form.</param>
        /// <returns>The metric selector.</returns>
        private static CostBenefitMetric ReadMetric(XElement xElement)
        {
            XElement? child = xElement.Element(nameof(CostBenefitMetric));
            return child == null
                ? CostBenefitMetric.ForRiskMeasure(RiskMeasure.Mean, RiskType.Total)
                : new CostBenefitMetric(child);
        }

        #endregion

        #region Members

        /// <summary>
        /// The metric selector the constraint reads.
        /// </summary>
        public CostBenefitMetric Metric { get; }

        /// <summary>
        /// The inequality sense (at most, or at least).
        /// </summary>
        public ConstraintType Sense { get; }

        /// <summary>
        /// The threshold, in the metric's own units.
        /// </summary>
        public double Threshold { get; }

        /// <summary>
        /// The trajectory scope the constraint is evaluated at.
        /// </summary>
        public ConstraintScope Scope { get; }

        #endregion

        #region IModel Methods

        /// <summary>
        /// Validates the constraint: an inequality sense, a recognized scope, a finite
        /// threshold, and a valid metric.
        /// </summary>
        /// <returns>The validity flag and messages.</returns>
        public (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var messages = new List<string>();
            if (Sense != ConstraintType.LesserThanOrEqualTo && Sense != ConstraintType.GreaterThanOrEqualTo)
                messages.Add("Error: The constraint sense must be one of the two inequalities.");
            if (!Enum.IsDefined(Scope))
                messages.Add("Error: The constraint scope is not a recognized member.");
            if (!Tools.IsFinite(Threshold))
                messages.Add("Error: The constraint threshold must be finite.");
            (_, List<string> metricMessages) = Metric.Validate();
            messages.AddRange(metricMessages);
            return (messages.Count == 0, messages);
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes the constraint. Element and attribute names are append-only contract.
        /// </summary>
        /// <returns>The serialized form.</returns>
        public XElement ToXElement()
        {
            var element = new XElement(nameof(CostBenefitConstraint));
            element.SetAttributeValue(nameof(Sense), Sense.ToString());
            element.SetAttributeValue(nameof(Threshold), SerializationUtilities.FormatDouble(Threshold));
            element.SetAttributeValue(nameof(Scope), Scope.ToString());
            element.Add(Metric.ToXElement());
            return element;
        }

        #endregion
    }
}
