using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>Unit tests for <see cref="TabularUncertainty"/> exact co-monotonic summaries.</summary>
/// <remarks>
/// <para><b>Authors:</b> Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil</para>
/// </remarks>
[TestClass]
public class TabularUncertaintyTests
{
    /// <summary>Verifies exact per-ordinate means, medians, bounds, and guard behavior.</summary>
    [TestMethod]
    public void Test_FromCoMonotonicTable_ExactOrdinates()
    {
        var table = new UncertainOrderedPairedData(
            new[]
            {
                new UncertainOrdinate(0d, new Deterministic(2d)),
                new UncertainOrdinate(1d, new Uniform(0d, 10d)),
            }, true, SortOrder.Ascending, false, SortOrder.None,
            UnivariateDistributionType.Uniform);

        var results = TabularUncertainty.FromCoMonotonicTable(table, 0.8d);

        Assert.IsNotNull(results);
        Assert.AreEqual(2d, results!.MeanCurve![0], 0d);
        Assert.AreEqual(5d, results.MeanCurve[1], 1e-12);
        Assert.AreEqual(5d, results.ModeCurve![1], 1e-12);
        Assert.AreEqual(1d, results.ConfidenceIntervals![1, 0], 1e-12);
        Assert.AreEqual(9d, results.ConfidenceIntervals[1, 1], 1e-12);
        Assert.IsNull(TabularUncertainty.FromCoMonotonicTable(null, 0.8d));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            TabularUncertainty.FromCoMonotonicTable(table, 1d));
    }
}
