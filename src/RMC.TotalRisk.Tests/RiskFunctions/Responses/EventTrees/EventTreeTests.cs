using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.EventTrees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.EventTrees;

/// <summary>Tests controlled event-tree authoring, traversal, search, and serialization.</summary>
[TestClass]
public class EventTreeTests
{
    /// <summary>Verifies add/insert preserve remainder uniqueness and presentation order.</summary>
    [TestMethod]
    public void Test_AddInsert_RemainderIsUniqueAndLast()
    {
        var tree = new EventTree();
        var remainder = new RemainderNode("Other");
        tree.Add(tree.Root.Id, remainder);
        var first = new ChanceNode("First", new ProbabilitySource(0.2d));
        tree.Add(tree.Root.Id, first);
        var inserted = new ChanceNode("Inserted", new ProbabilitySource(0.3d));
        tree.Insert(first.Id, inserted);

        CollectionAssert.AreEqual(new[] { "Inserted", "First", "Other" },
            tree.Root.Children.Select(node => node.Name).ToArray());
        Assert.ThrowsException<InvalidOperationException>(() =>
            tree.Add(tree.Root.Id, new RemainderNode("Duplicate")));
    }

    /// <summary>Verifies failed cyclic/root moves and deletes leave structure unchanged.</summary>
    [TestMethod]
    public void Test_FailedMutations_AreTransactional()
    {
        var tree = new EventTree();
        var parent = new ChanceNode("Parent", new ProbabilitySource(0.4d));
        tree.Add(tree.Root.Id, parent);
        var child = new ChanceNode("Child", new ProbabilitySource(0.5d));
        tree.Add(parent.Id, child);
        string before = tree.ToXElement().ToString();

        Assert.ThrowsException<InvalidOperationException>(() => tree.Move(parent.Id, child.Id));
        Assert.AreEqual(before, tree.ToXElement().ToString());
        Assert.ThrowsException<InvalidOperationException>(() => tree.Delete(tree.Root.Id));
        Assert.AreEqual(before, tree.ToXElement().ToString());
    }

    /// <summary>Verifies deterministic traversals, searches, ancestry, and reachability.</summary>
    [TestMethod]
    public void Test_TraversalAndSearch_ReturnExpectedTopology()
    {
        var tree = new EventTree();
        var a = new ChanceNode("A", new ProbabilitySource(0.4d));
        var b = new ChanceNode("B", new ProbabilitySource(0.6d));
        var c = new ChanceNode("c", new ProbabilitySource(0.5d));
        tree.Add(tree.Root.Id, a);
        tree.Add(tree.Root.Id, b);
        tree.Add(a.Id, c);

        CollectionAssert.AreEqual(new[] { tree.Root.Id, a.Id, c.Id, b.Id },
            tree.DepthFirstPreOrder().Select(node => node.Id).ToArray());
        CollectionAssert.AreEqual(new[] { c.Id, a.Id, b.Id, tree.Root.Id },
            tree.DepthFirstPostOrder().Select(node => node.Id).ToArray());
        CollectionAssert.AreEqual(new[] { tree.Root.Id, a.Id, b.Id, c.Id },
            tree.BreadthFirst().Select(node => node.Id).ToArray());
        Assert.AreSame(c, tree.FindByNameIgnoreCase("C").Single());
        Assert.IsTrue(tree.IsReachable(a.Id, c.Id));
        Assert.AreSame(a, tree.GetAncestors(c.Id).First());
    }

    /// <summary>Verifies explicit graph XML preserves ids and compute structure.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTripsStableIdentityAndStructure()
    {
        var tree = new EventTree();
        tree.Root.Name = "Hazard";
        var chance = new ChanceNode("Failure", new ProbabilitySource(0.25d));
        tree.Add(tree.Root.Id, chance);
        tree.Add(tree.Root.Id, new RemainderNode("No Failure"));

        var restored = new EventTree(tree.ToXElement());

        Assert.AreEqual(tree.Root.Id, restored.Root.Id);
        Assert.AreEqual(chance.Id, restored.FindByName("Failure").Single().Id);
        Assert.IsTrue(tree.StructuralEquals(restored));
        CollectionAssert.AreEqual(tree.SubtreeCanonicalHash(tree.Root.Id), restored.SubtreeCanonicalHash(restored.Root.Id));
    }
}
