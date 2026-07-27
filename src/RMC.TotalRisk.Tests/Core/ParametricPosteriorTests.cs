using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Distributions;
using Numerics.Mathematics.Optimization;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>Unit tests for <see cref="ParametricPosterior"/> posterior summary math.</summary>
/// <remarks>
/// <para><b>Authors:</b> Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil</para>
/// </remarks>
[TestClass]
public class ParametricPosteriorTests
{
    /// <summary>Verifies posterior means, mode curve, confidence slicing, and input guards.</summary>
    [TestMethod]
    public void Test_BuildAndSlice_NormalPosterior()
    {
        var parent = new Normal(10d, 2d);
        var sets = new List<ParameterSet>
        {
            new ParameterSet(new[] { 8d, 1d }, 0d),
            new ParameterSet(new[] { 10d, 1d }, 0d),
            new ParameterSet(new[] { 12d, 1d }, 0d),
        };
        double[] probabilities = { 0.5d };

        var results = ParametricPosterior.BuildResults(parent, sets, probabilities, 0.2d);
        var sliced = ParametricPosterior.SliceConfidenceIntervals(parent,
            results.ParameterSets!, probabilities, 0.8d);

        Assert.AreEqual(10d, results.ModeCurve![0], 1e-12);
        Assert.AreEqual(10d, results.MeanCurve![0], 1e-12);
        Assert.AreEqual(results.ConfidenceIntervals![0, 0], sliced[0, 0], 1e-12);
        Assert.AreEqual(results.ConfidenceIntervals[0, 1], sliced[0, 1], 1e-12);
        Assert.AreNotSame(parent, results.ParentDistribution);
        Assert.ThrowsException<ArgumentNullException>(() =>
            ParametricPosterior.BuildResults(null!, sets, probabilities, 0.2d));
        Assert.ThrowsException<ArgumentException>(() =>
            ParametricPosterior.BuildResults(parent, new List<ParameterSet>(), probabilities, 0.2d));
    }
}
