using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Tests.Systems.Components;

/// <summary>
/// Unit tests for <see cref="EndStateGroupLayout"/> — leaf-signature grouping, the
/// duplicate/prefix standalone ejection, final-polarity classification, flipped-final-sibling
/// pairing, and the trivial-layout detection every pre-cascade model relies on
/// (docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §7.9).
/// </summary>
[TestClass]
public class EndStateGroupLayoutTests
{
    /// <summary>Builds an end-state mode with the given stamped ordinals and stage polarities.</summary>
    private static FailureMode Mode(int[]? ordinals, params BranchPolarity[] polarities)
    {
        var stages = new List<ResponseStage>();
        for (int s = 0; s < polarities.Length; s++)
        {
            stages.Add(new ResponseStage(new List<ITransformFunction>(), new TabularResponse(), polarities[s]));
        }
        return new FailureMode(stages, null, new List<IConsequenceFunction> { new TabularConsequence() })
        {
            ProjectedResponseOrdinals = ordinals,
        };
    }

    /// <summary>Builds the background non-failure mode.</summary>
    private static FailureMode Background()
    {
        return new FailureMode(null, null, null, new TabularConsequence());
    }

    /// <summary>
    /// Verifies chain-authored modes (no ordinals) and legacy same-branch fan-out (identical
    /// signatures) both produce the trivial layout — every state a standalone Fail-final unit,
    /// so the engine takes the pre-cascade arithmetic.
    /// </summary>
    [TestMethod]
    public void Test_Build_TrivialLayouts()
    {
        // Chain-authored modes: no ordinals, never shared.
        var chain = EndStateGroupLayout.Build(new[] { Mode(null, BranchPolarity.Fail), Mode(null, BranchPolarity.Fail), Background() });
        Assert.IsTrue(chain.IsTrivial);
        Assert.AreEqual(2, chain.StateCount);
        Assert.AreEqual(2, chain.CombinationUnitCount);
        CollectionAssert.AreEqual(new[] { -1, -1 }, chain.PairingPartnerState);

        // Legacy fan-out: identical signatures eject to standalone units (deliberately
        // preserving the legacy fan-out semantics) — exactly today's flat combination.
        var fanOut = EndStateGroupLayout.Build(new[]
        {
            Mode(new[] { 0 }, BranchPolarity.Fail),
            Mode(new[] { 0 }, BranchPolarity.Fail),
            Background(),
        });
        Assert.IsTrue(fanOut.IsTrivial);
        Assert.AreEqual(2, fanOut.CombinationUnitCount);
        Assert.AreEqual(0, fanOut.ClaimedStateCount);
    }

    /// <summary>
    /// Verifies the partial-damage cascade: the Fail-final terminal forms the exclusive unit,
    /// the Non-Fail-final sibling is a claimed state attached to that unit, and the pairing
    /// resolves to the flipped-final sibling.
    /// </summary>
    [TestMethod]
    public void Test_Build_PartialDamageCascade()
    {
        // Arrange — full breach [(0,F),(1,F)], partial damage [(0,F),(1,NF)].
        var layout = EndStateGroupLayout.Build(new[]
        {
            Mode(new[] { 0, 1 }, BranchPolarity.Fail, BranchPolarity.Fail),
            Mode(new[] { 0, 1 }, BranchPolarity.Fail, BranchPolarity.NonFail),
            Background(),
        });

        // Assert
        Assert.IsFalse(layout.IsTrivial);
        Assert.AreEqual(1, layout.CombinationUnitCount);
        CollectionAssert.AreEqual(new[] { 0 }, layout.CombinationUnitStates[0]);
        CollectionAssert.AreEqual(new[] { true, false }, layout.IsFailureState);
        Assert.AreEqual(1, layout.ClaimedStateCount);
        Assert.AreEqual(1, layout.ClaimingCascadeCount);
        Assert.AreEqual(0, layout.ClaimedStateUnit[1], "The claimed state's conditional divisor uses its cascade's unit.");
        Assert.AreEqual(1, layout.PairingPartnerState[0], "The full-breach excess pairs against the partial sibling.");
        Assert.AreEqual(-1, layout.PairingPartnerState[1]);
        Assert.IsFalse(layout.HasNonFailBranchFailureState);
    }

    /// <summary>
    /// Verifies the else-chain: two Fail-final terminals diverging at the shared first response
    /// form one exclusive two-state unit, and the Non-Fail mid-branch flags the competing gate.
    /// </summary>
    [TestMethod]
    public void Test_Build_ElseChainExclusivePair()
    {
        // Arrange — direct failure [(0,F)] and the else path [(0,NF),(1,F)].
        var layout = EndStateGroupLayout.Build(new[]
        {
            Mode(new[] { 0 }, BranchPolarity.Fail),
            Mode(new[] { 0, 1 }, BranchPolarity.NonFail, BranchPolarity.Fail),
        });

        // Assert — one unit, both members exclusive (they diverge at ordinal 0 by polarity).
        Assert.IsFalse(layout.IsTrivial);
        Assert.AreEqual(1, layout.CombinationUnitCount);
        CollectionAssert.AreEqual(new[] { 0, 1 }, layout.CombinationUnitStates[0]);
        Assert.IsTrue(layout.HasNonFailBranchFailureState, "The else path rides a Non-Fail branch (the competing gate).");
        Assert.AreEqual(0, layout.ClaimedStateCount);
    }

    /// <summary>
    /// Verifies the prefix ejection: a terminal claiming a branch a continuation also claims
    /// leaves the partition (standalone unit — flat combination), while the deeper leaf stays.
    /// </summary>
    [TestMethod]
    public void Test_Build_PrefixNested_EjectsShorter()
    {
        // Arrange — [(0,F)] terminates the branch [(0,F),(1,F)] continues through.
        var layout = EndStateGroupLayout.Build(new[]
        {
            Mode(new[] { 0 }, BranchPolarity.Fail),
            Mode(new[] { 0, 1 }, BranchPolarity.Fail, BranchPolarity.Fail),
        });

        // Assert — two standalone units; the flat combination applies, so the layout is
        // trivial and the engine arithmetic is the pre-cascade path.
        Assert.AreEqual(2, layout.CombinationUnitCount);
        Assert.IsTrue(layout.IsTrivial);
    }

    /// <summary>
    /// Verifies claiming-cascade counting across separate cascades (the §7.9.5 single-group
    /// validation input).
    /// </summary>
    [TestMethod]
    public void Test_Build_TwoClaimingCascades_Counted()
    {
        // Arrange — two independent responses, each with a Fail terminal and a Non-Fail claim.
        var layout = EndStateGroupLayout.Build(new[]
        {
            Mode(new[] { 0 }, BranchPolarity.Fail),
            Mode(new[] { 0 }, BranchPolarity.NonFail),
            Mode(new[] { 1 }, BranchPolarity.Fail),
            Mode(new[] { 1 }, BranchPolarity.NonFail),
        });

        // Assert
        Assert.AreEqual(2, layout.CombinationUnitCount);
        Assert.AreEqual(2, layout.ClaimedStateCount);
        Assert.AreEqual(2, layout.ClaimingCascadeCount);
        Assert.AreEqual(1, layout.PairingPartnerState[0]);
        Assert.AreEqual(3, layout.PairingPartnerState[2]);
    }
}
