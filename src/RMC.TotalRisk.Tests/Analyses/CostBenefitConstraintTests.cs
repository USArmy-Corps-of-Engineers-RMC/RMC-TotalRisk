using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Mathematics.Optimization;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Analyses;

/// <summary>
/// Tests the constraint declaration: guards, the inequality-only sense rule, and the XML
/// round trip.
/// </summary>
[TestClass]
public class CostBenefitConstraintTests
{
    /// <summary>Verifies the construction guard and the default scope.</summary>
    [TestMethod]
    public void Test_Ctor_GuardAndDefaultScope()
    {
        // Act
        var constraint = new CostBenefitConstraint(
            CostBenefitMetric.ForRiskMeasure(RiskMeasure.Mean, RiskType.Excess, 1),
            ConstraintType.LesserThanOrEqualTo, 1e-3);

        // Assert
        Assert.AreEqual(ConstraintScope.EveryEpoch, constraint.Scope);
        Assert.AreEqual(1e-3, constraint.Threshold);
        Assert.IsTrue(constraint.Validate().IsValid);
        Assert.ThrowsException<ArgumentNullException>(() => new CostBenefitConstraint(
            null!, ConstraintType.LesserThanOrEqualTo, 1e-3));
    }

    /// <summary>Verifies the validation matrix: equality sense and non-finite threshold refuse.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Arrange
        var metric = CostBenefitMetric.ForEconomic(EconomicMetric.AnnualizedFailureProbability);

        // Act
        var equality = new CostBenefitConstraint(metric, ConstraintType.EqualTo, 1e-4);
        var nonFinite = new CostBenefitConstraint(metric, ConstraintType.LesserThanOrEqualTo, double.NaN);

        // Assert
        (bool equalityValid, var equalityMessages) = equality.Validate();
        Assert.IsFalse(equalityValid);
        StringAssert.StartsWith(equalityMessages[0], "Error: The constraint sense must be one of the two inequalities.");
        (bool finiteValid, var finiteMessages) = nonFinite.Validate();
        Assert.IsFalse(finiteValid);
        StringAssert.StartsWith(finiteMessages[0], "Error: The constraint threshold must be finite.");
    }

    /// <summary>Verifies the XML round trip preserves the sense, threshold, scope, and metric.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var constraint = new CostBenefitConstraint(
            CostBenefitMetric.ForRiskMeasure(RiskMeasure.ConditionalValueAtRisk, RiskType.Total, 0,
                MetricBasis.AnnualizedPerEpoch, alpha: 0.01d),
            ConstraintType.GreaterThanOrEqualTo, 2.5d, ConstraintScope.Horizon);

        // Act
        var restored = new CostBenefitConstraint(constraint.ToXElement());

        // Assert
        Assert.AreEqual(ConstraintType.GreaterThanOrEqualTo, restored.Sense);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(2.5d),
            BitConverter.DoubleToInt64Bits(restored.Threshold));
        Assert.AreEqual(ConstraintScope.Horizon, restored.Scope);
        Assert.AreEqual(RiskMeasure.ConditionalValueAtRisk, restored.Metric.Measure);
        Assert.AreEqual(0.01d, restored.Metric.Alpha);
        Assert.ThrowsException<ArgumentNullException>(() => new CostBenefitConstraint(null!));
    }
}
