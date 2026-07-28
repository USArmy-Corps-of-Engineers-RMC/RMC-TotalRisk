using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Responses.EventTrees;

namespace RMC.TotalRisk.Verification.RiskFunctions.Responses;

/// <summary>
/// Focused Phase 10A event-tree verification against independently derived conditional path
/// products and direct uncertainty-table samples. This partial family covers scalar and aligned
/// tables, internal/external independent-clone links, two-mode round trips, and link-occurrence
/// reproducibility. Legacy XML, graph-expanded consequences, routing Monte Carlo, LHS variance,
/// and performance remain future rows in the normative verification plan.
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
    /// <summary>
    /// Verifies two external independent links against an explicitly cloned analytic model,
    /// including the established over-allocation normalization rule.
    /// </summary>
    [TestMethod]
    public void Test_LinkedSubtrees_EqualExplicitCloneAndSerializationModes()
    {
        var targetTree = new EventTree();
        var targetSequence = new ChanceNode("Reusable sequence", new ProbabilitySource(0.6d)) { IsFailure = false };
        targetTree.Add(targetTree.Root.Id, targetSequence);
        targetTree.Add(targetTree.Root.Id, new RemainderNode("Target root survival"));
        targetTree.Add(targetSequence.Id, new ChanceNode("Failure", new ProbabilitySource(0.25d)));
        targetTree.Add(targetSequence.Id, new RemainderNode("Sequence survival"));
        var target = Response(targetTree);
        target.Name = "Stored linked tree";

        var linkedTree = new EventTree();
        linkedTree.LinkIndependent(linkedTree.Root.Id, target, targetSequence.Id, "Occurrence A");
        linkedTree.LinkIndependent(linkedTree.Root.Id, target, targetSequence.Id, "Occurrence B");
        linkedTree.Add(linkedTree.Root.Id, new RemainderNode("Owner survival"));
        var linked = Response(linkedTree);

        var explicitTree = new EventTree();
        for (int occurrence = 0; occurrence < 2; occurrence++)
        {
            var sequence = new ChanceNode($"Explicit {occurrence}", new ProbabilitySource(0.6d)) { IsFailure = false };
            explicitTree.Add(explicitTree.Root.Id, sequence);
            explicitTree.Add(sequence.Id, new ChanceNode($"Failure {occurrence}", new ProbabilitySource(0.25d)));
            explicitTree.Add(sequence.Id, new RemainderNode($"Survival {occurrence}"));
        }
        explicitTree.Add(explicitTree.Root.Id, new RemainderNode("Owner survival"));
        var explicitClone = Response(explicitTree);
        IRiskFunctionResolver resolver = new RiskFunctionResolver(
            id => id == target.Id ? target : null,
            name => name == target.Name ? target : null);
        var selfContained = new EventTreeResponse(
            linked.ToXElement(RiskSerializationMode.SelfContained));
        var byReference = new EventTreeResponse(
            linked.ToXElement(RiskSerializationMode.ByReference), resolver);

        double expected = (0.6d / 1.2d * 0.25d) + (0.6d / 1.2d * 0.25d);
        Assert.AreEqual(expected, linked.SampleResponseFunction()[0].Y, 1e-14d);
        Assert.AreEqual(explicitClone.SampleResponseFunction()[0].Y,
            linked.SampleResponseFunction()[0].Y, 1e-14d);
        Assert.AreEqual(linked.SampleResponseFunction()[0].Y,
            selfContained.SampleResponseFunction()[0].Y);
        Assert.AreEqual(linked.SampleResponseFunction()[0].Y,
            byReference.SampleResponseFunction()[0].Y);
        CollectionAssert.AreEqual(linked.CanonicalHash(), selfContained.CanonicalHash());
        CollectionAssert.AreEqual(linked.CanonicalHash(), byReference.CanonicalHash());
    }

    /// <summary>
    /// Verifies 256 linked LHS realizations reproduce bit-for-bit across self-contained,
    /// by-reference, metadata, and sibling-order representations while the two independent
    /// occurrences demonstrably consume distinct streams.
    /// </summary>
    [TestMethod]
    public void Test_LinkedUncertainty_ReproducesAcrossRoundTripsAndOrder()
    {
        var table = new UncertainOrderedPairedData(
            new[]
            {
                new UncertainOrdinate(0d, new Uniform(0.1d, 0.3d)),
                new UncertainOrdinate(1d, new Uniform(0.2d, 0.4d)),
            }, true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Uniform);
        var targetTree = new EventTree();
        var targetNode = new ChanceNode("Target", new ProbabilitySource(table));
        targetTree.Add(targetTree.Root.Id, targetNode);
        var target = Response(targetTree);
        target.Name = "Uncertain target";

        var ownerTree = new EventTree();
        ownerTree.LinkIndependent(ownerTree.Root.Id, target, targetNode.Id, "Occurrence A");
        ownerTree.LinkIndependent(ownerTree.Root.Id, target, targetNode.Id, "Occurrence B");
        ownerTree.Add(ownerTree.Root.Id, new RemainderNode("No failure"));
        var original = Response(ownerTree);
        IRiskFunctionResolver resolver = new RiskFunctionResolver(
            id => id == target.Id ? target : null,
            name => name == target.Name ? target : null);
        var selfContained = new EventTreeResponse(
            original.ToXElement(RiskSerializationMode.SelfContained));
        var byReference = new EventTreeResponse(
            original.ToXElement(RiskSerializationMode.ByReference), resolver);
        EventTreeLinkNode[] reorderedLinks = byReference.EventTree.Nodes.OfType<EventTreeLinkNode>().ToArray();
        byReference.EventTree.Move(reorderedLinks[1].Id, byReference.EventTree.Root.Id, reorderedLinks[0].Id);
        byReference.Name = "Renamed owner";
        target.Name = "Renamed target";
        targetNode.Name = "Renamed node";

        original.SetupSampler(256, 97531, SamplingScheme.LatinHypercube);
        selfContained.SetupSampler(256, 97531, SamplingScheme.LatinHypercube);
        byReference.SetupSampler(256, 97531, SamplingScheme.LatinHypercube);

        CollectionAssert.AreEqual(original.CanonicalHash(), selfContained.CanonicalHash());
        CollectionAssert.AreEqual(original.CanonicalHash(), byReference.CanonicalHash());
        bool distinctStreams = false;
        for (int realization = 0; realization < 256; realization++)
        {
            OrderedPairedData expected = original.SampleResponseFunction(realization);
            Assert.AreEqual(expected[0].Y, selfContained.SampleResponseFunction(realization)[0].Y);
            Assert.AreEqual(expected[1].Y, selfContained.SampleResponseFunction(realization)[1].Y);
            Assert.AreEqual(expected[0].Y, byReference.SampleResponseFunction(realization)[0].Y);
            Assert.AreEqual(expected[1].Y, byReference.SampleResponseFunction(realization)[1].Y);

            var branches = original.SampleBranches(realization);
            int first = Enumerable.Range(0, branches.Branches.Count)
                .Single(i => branches.Branches[i].Name == "Occurrence A");
            int second = Enumerable.Range(0, branches.Branches.Count)
                .Single(i => branches.Branches[i].Name == "Occurrence B");
            distinctStreams |= branches.Probabilities[first][0] != branches.Probabilities[second][0];
        }
        Assert.IsTrue(distinctStreams);
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
