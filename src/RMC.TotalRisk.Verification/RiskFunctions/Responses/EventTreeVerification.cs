using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Responses.EventTrees;

namespace RMC.TotalRisk.Verification.RiskFunctions.Responses;

/// <summary>
/// Focused Phase 10A event-tree verification against independently derived conditional path
/// products and direct uncertainty-table samples. This first family covers the implemented scalar
/// and aligned-table vertical slice; links, legacy XML, graph-expanded consequences, and routing
/// Monte Carlo remain future rows in the normative verification plan.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// No legacy <c>Test_Product</c> value is used as an oracle. Expected values below are derived
/// directly from conditional probability identities: a terminal path is the product of its
/// conditional branches, explicit sibling values above one are divided by their compensated sum,
/// and aggregate failure is the sum over explicitly classified failure terminals.
/// </para>
/// </remarks>
[TestClass]
public class EventTreeVerification
{
    /// <summary>Verifies a representative deep/wide tree against hand-derived terminal path products.</summary>
    [TestMethod]
    public void Test_DeepWideTree_EqualsAnalyticPathProducts()
    {
        var tree = new EventTree();
        var loadCase = new ChanceNode("Load case", new ProbabilitySource(0.6d)) { IsFailure = false };
        tree.Add(tree.Root.Id, loadCase);
        tree.Add(tree.Root.Id, new ChanceNode("Direct failure", new ProbabilitySource(0.1d)));
        tree.Add(tree.Root.Id, new RemainderNode("Root residual"));
        tree.Add(loadCase.Id, new ChanceNode("Mechanism A", new ProbabilitySource(0.25d)));
        tree.Add(loadCase.Id, new ChanceNode("Mechanism B", new ProbabilitySource(0.15d)));
        tree.Add(loadCase.Id, new RemainderNode("Survival"));
        var response = Response(tree);

        var sample = response.SampleBranches();
        double expectedFailure = 0.1d + (0.6d * 0.25d) + (0.6d * 0.15d);

        Assert.AreEqual(expectedFailure, response.SampleResponseFunction()[0].Y, 1e-14d);
        Assert.AreEqual(1d, sample.Probabilities.Sum(row => row[0]), 1e-14d);
        Assert.AreEqual(1d, sample.Probabilities.Sum(row => row[1]), 1e-14d);
    }

    /// <summary>Verifies proportional sibling normalization and zero residual in the over-allocated case.</summary>
    [TestMethod]
    public void Test_OverAllocatedSiblings_EqualsAnalyticNormalization()
    {
        var tree = new EventTree();
        tree.Add(tree.Root.Id, new ChanceNode("Failure", new ProbabilitySource(0.8d)));
        tree.Add(tree.Root.Id, new ChanceNode("Survival", new ProbabilitySource(0.7d)) { IsFailure = false });
        tree.Add(tree.Root.Id, new RemainderNode("Residual"));
        var response = Response(tree);

        var sample = response.SampleBranches();
        int residual = Enumerable.Range(0, sample.Branches.Count).Single(i => sample.Branches[i].Name == "Residual");

        Assert.AreEqual(0.8d / (0.8d + 0.7d), response.SampleResponseFunction()[0].Y, 1e-14d);
        Assert.AreEqual(0d, sample.Probabilities[residual][0], 1e-14d);
    }

    /// <summary>Verifies indexed uncertain-tree outputs realization-for-realization against the table source.</summary>
    [TestMethod]
    public void Test_IndexedUncertainty_EqualsDirectChildCurveSamples()
    {
        var table = new UncertainOrderedPairedData(
            new[]
            {
                new UncertainOrdinate(0d, new Uniform(0.05d, 0.25d)),
                new UncertainOrdinate(1d, new Uniform(0.4d, 0.8d)),
            }, true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Uniform);
        var tree = new EventTree();
        tree.Add(tree.Root.Id, new ChanceNode("Failure", new ProbabilitySource(table)));
        tree.Add(tree.Root.Id, new RemainderNode("No failure"));
        var response = Response(tree);
        response.SetupSampler(256, 8675309, SamplingScheme.LatinHypercube);

        for (int realization = 0; realization < 256; realization++)
        {
            double percentile = response.SampledPercentile(realization, 0);
            OrderedPairedData direct = table.CurveSample(percentile);
            OrderedPairedData actual = response.SampleResponseFunction(realization);
            Assert.AreEqual(direct[0].Y, actual[0].Y);
            Assert.AreEqual(direct[1].Y, actual[1].Y);
        }
    }

    /// <summary>Builds a labeled event-tree response over the common two-knot hazard axis.</summary>
    /// <param name="tree">The authored tree.</param>
    /// <returns>The valid response.</returns>
    private static EventTreeResponse Response(EventTree tree)
    {
        return new EventTreeResponse(new[] { 0d, 1d }, tree)
        {
            Name = "Verification event tree",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
        };
    }
}
