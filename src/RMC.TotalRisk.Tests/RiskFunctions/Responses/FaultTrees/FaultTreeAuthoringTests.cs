using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.FaultTrees;

/// <summary>Tests transactional fault-tree fragment, transfer, delete, and pruning operations.</summary>
[TestClass]
public class FaultTreeAuthoringTests
{
    /// <summary>Builds the shared authoring fixture: Top ← And(A, B) with a shared transfer to And.</summary>
    /// <param name="and">The interior gate.</param>
    /// <param name="transferId">The internal shared transfer id.</param>
    /// <returns>The tree.</returns>
    private static FaultTree BuildFixture(out FaultTreeGateNode and, out Guid transferId)
    {
        var tree = new FaultTree();
        and = new FaultTreeGateNode("Joint", FaultTreeGateType.And);
        tree.Add(tree.Root.Id, and);
        tree.Add(and.Id, new FaultTreeBasicEventNode("A", new ProbabilitySource(0.1d)));
        tree.Add(and.Id, new FaultTreeBasicEventNode("B", new ProbabilitySource(0.2d)));
        transferId = tree.LinkShared(tree.Root.Id, and.Id, "Shared joint");
        return tree;
    }

    /// <summary>Verifies copy/paste produces fresh ids and remaps fragment-internal targets.</summary>
    [TestMethod]
    public void Test_CopyPasteClone_FreshIdsAndInternalRemap()
    {
        // Arrange — copy a subtree that contains a transfer to a node inside the fragment.
        var tree = new FaultTree();
        var outer = new FaultTreeGateNode("Outer", FaultTreeGateType.Or);
        tree.Add(tree.Root.Id, outer);
        var basic = new FaultTreeBasicEventNode("A", new ProbabilitySource(0.1d));
        tree.Add(outer.Id, basic);
        tree.LinkShared(outer.Id, basic.Id, "Shared A");

        // Act
        TreeFragment fragment = tree.Copy(outer.Id);
        Guid pastedId = tree.PasteClone(tree.Root.Id, fragment);
        var pasted = (FaultTreeGateNode)tree.FindById(pastedId)!;

        // Assert — fresh ids everywhere; the pasted transfer targets the pasted basic.
        Assert.AreNotEqual(outer.Id, pastedId);
        var pastedTransfer = (FaultTreeTransferNode)pasted.Children[1];
        var pastedBasic = pasted.Children[0];
        Assert.AreNotEqual(basic.Id, pastedBasic.Id);
        Assert.AreEqual(pastedBasic.Id, pastedTransfer.Target.NodeId);
        Assert.AreEqual(7, tree.Nodes.Count);
    }

    /// <summary>Verifies replace applies the reference policy and keeps fresh identity.</summary>
    [TestMethod]
    public void Test_ReplaceSubtree_HonorsReferencePolicy()
    {
        // Arrange
        FaultTree tree = BuildFixture(out FaultTreeGateNode and, out _);
        var replacementSource = new FaultTree();
        var house = new FaultTreeHouseEventNode("H", true);
        replacementSource.Add(replacementSource.Root.Id, house);
        TreeFragment replacement = replacementSource.Copy(house.Id);

        // Act / Assert — referenced subtree rejects by default, then cascades away its transfer.
        StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(
            () => tree.ReplaceSubtree(and.Id, replacement)).Message, "referenced by internal transfer");
        Guid replacedId = tree.ReplaceSubtree(and.Id, replacement, TreeDeletePolicy.CascadeLinks);
        Assert.IsInstanceOfType<FaultTreeHouseEventNode>(tree.FindById(replacedId));
        Assert.AreEqual(0, tree.GetInternalReferences().Count);
        Assert.AreEqual(2, tree.Nodes.Count);
    }

    /// <summary>Verifies all three delete policies for referenced subtrees.</summary>
    [TestMethod]
    public void Test_Delete_AllThreePolicies()
    {
        // Reject by default.
        FaultTree rejecting = BuildFixture(out FaultTreeGateNode rejectAnd, out _);
        StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(
            () => rejecting.Delete(rejectAnd.Id)).Message, "referenced by internal transfer");

        // Cascade removes the incoming transfer with the subtree.
        FaultTree cascading = BuildFixture(out FaultTreeGateNode cascadeAnd, out _);
        cascading.Delete(cascadeAnd.Id, TreeDeletePolicy.CascadeLinks);
        Assert.AreEqual(1, cascading.Nodes.Count);

        // Materialize preserves an equivalent explicit clone before removal.
        FaultTree materializing = BuildFixture(out FaultTreeGateNode materializeAnd, out _);
        materializing.Delete(materializeAnd.Id, TreeDeletePolicy.MaterializeLinks);
        var survivor = (FaultTreeGateNode)materializing.Root.Children.Single();
        Assert.AreEqual("Joint", survivor.Name);
        CollectionAssert.AreEqual(new[] { "A", "B" },
            survivor.Children.Select(node => node.Name).ToArray());
    }

    /// <summary>Verifies materialization replaces a transfer with a fresh-id deep clone.</summary>
    [TestMethod]
    public void Test_MaterializeLink_DeepClonesTarget()
    {
        // Arrange
        FaultTree tree = BuildFixture(out FaultTreeGateNode and, out Guid transferId);

        // Act
        Guid materializedId = tree.MaterializeLink(transferId);
        var materialized = (FaultTreeGateNode)tree.FindById(materializedId)!;

        // Assert
        Assert.AreNotEqual(and.Id, materializedId);
        CollectionAssert.AreEqual(new[] { "A", "B" },
            materialized.Children.Select(node => node.Name).ToArray());
        Assert.AreNotEqual(and.Children[0].Id, materialized.Children[0].Id);
        Assert.AreEqual(0, tree.GetInternalReferences().Count);
    }

    /// <summary>Verifies reference queries and reachability treat transfer targets as live.</summary>
    [TestMethod]
    public void Test_ReferenceQueriesAndPrune_TransferTargetsReachable()
    {
        // Arrange — an unreachable helper subtree kept alive only by a transfer.
        FaultTree tree = BuildFixture(out FaultTreeGateNode and, out Guid transferId);
        var orphan = new FaultTreeBasicEventNode("Orphan", new ProbabilitySource(0.4d));
        var holder = new FaultTreeGateNode("Holder", FaultTreeGateType.Or);
        tree.Add(tree.Root.Id, holder);
        tree.Add(holder.Id, orphan);
        tree.Delete(holder.Id, TreeDeletePolicy.CascadeLinks);

        // Act / Assert
        var transfer = (FaultTreeTransferNode)tree.FindById(transferId)!;
        CollectionAssert.AreEqual(new[] { transfer },
            tree.GetIncomingReferences(and.Id).ToArray());
        CollectionAssert.AreEqual(new[] { transfer },
            tree.GetOutgoingReferences(tree.Root.Id).ToArray());
        Assert.AreEqual(1, tree.GetInternalReferences().Count);
        Assert.AreEqual(0, tree.GetExternalReferences().Count);
        Assert.AreEqual(0, tree.GetUnreachableNodes().Count);
        Assert.AreEqual(0, tree.PruneUnreachable().Count);
    }

    /// <summary>Verifies a failed transactional mutation restores the exact prior topology.</summary>
    [TestMethod]
    public void Test_FailedMutation_RollsBackExactly()
    {
        // Arrange
        FaultTree tree = BuildFixture(out FaultTreeGateNode and, out _);
        string[] before = tree.DepthFirstPreOrder().Select(node => $"{node.Name}:{node.Id:N}").ToArray();

        // Act — moving the gate under itself must fail and change nothing.
        Assert.ThrowsException<InvalidOperationException>(() => tree.Move(and.Id, and.Id));

        // Assert
        CollectionAssert.AreEqual(before,
            tree.DepthFirstPreOrder().Select(node => $"{node.Name}:{node.Id:N}").ToArray());
    }
}
