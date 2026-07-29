using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>
/// Reusable assertion helpers for the canonical-hash identity contract — the landing-checklist
/// hook every cluster extends: metadata edits must never move a model object's hash, stripped
/// attributes must be inert wherever they appear in the serialized tree, and compute edits must
/// always move the hash.
/// </summary>
/// <remarks>
/// The helpers are delegate-friendly on purpose: functions register via <see cref="IRiskFunction"/>,
/// and <c>SystemComponent</c>/<c>FailureMode</c> (which share no root interface with
/// functions) register via their <c>ToXElement</c>/<c>CanonicalHash</c> members directly.
/// </remarks>
public static class HashInvariance
{
    /// <summary>
    /// Asserts that the standard identity-metadata edits (rename, re-describe, relabel axes) do
    /// not change a risk function's canonical hash.
    /// </summary>
    /// <param name="function">The function under test; its metadata properties are mutated.</param>
    public static void AssertMetadataInvariant(IRiskFunction function)
    {
        byte[] baseline = function.CanonicalHash();

        function.AssignNewId();
        CollectionAssert.AreEqual(baseline, function.CanonicalHash(),
            "Re-assigning the persistent id must not change the canonical hash — id is identity, not content.");

        function.Name = function.Name + " (renamed)";
        CollectionAssert.AreEqual(baseline, function.CanonicalHash(), "Renaming must not change the canonical hash.");

        function.Description = function.Description + " Edited description.";
        CollectionAssert.AreEqual(baseline, function.CanonicalHash(), "Editing the description must not change the canonical hash.");

        function.SpecifiedHazard = function.SpecifiedHazard + "X";
        CollectionAssert.AreEqual(baseline, function.CanonicalHash(), "Editing the hazard label must not change the canonical hash.");

        function.HazardUnit = function.HazardUnit + "X";
        CollectionAssert.AreEqual(baseline, function.CanonicalHash(), "Editing the hazard unit must not change the canonical hash.");
    }

    /// <summary>
    /// Asserts that every audited stripped attribute is inert: stamping it (with an arbitrary
    /// value) onto the serialized root and every descendant element leaves the hash unchanged.
    /// This is the guard that keeps UI-envelope metadata (canvas positions, chart settings, GUIDs)
    /// from ever perturbing Monte Carlo seeds.
    /// </summary>
    /// <param name="serializedForm">The object's <c>ToXElement()</c> output.</param>
    public static void AssertStrippedAttributesInert(XElement serializedForm)
    {
        byte[] baseline = CanonicalContentHasher.Hash(serializedForm, CanonicalizationRules.ModelRules);

        foreach (string strippedName in CanonicalizationRules.ModelRules.StrippedAttributes)
        {
            var stamped = new XElement(serializedForm);
            foreach (var node in stamped.DescendantsAndSelf().ToList())
            {
                node.SetAttributeValue(strippedName, "MUTATED-" + strippedName);
            }

            byte[] stampedHash = CanonicalContentHasher.Hash(stamped, CanonicalizationRules.ModelRules);
            CollectionAssert.AreEqual(baseline, stampedHash,
                $"Stripped attribute '{strippedName}' perturbed the canonical hash.");
        }
    }

    /// <summary>
    /// Asserts that a compute-relevant edit moves the hash: captures the hash, applies the
    /// mutation, and requires a different hash afterwards.
    /// </summary>
    /// <param name="hash">Delegate producing the object's current canonical hash.</param>
    /// <param name="computeMutation">A mutation of compute-relevant state (a numeric edit, a mode toggle).</param>
    public static void AssertComputeSensitive(Func<byte[]> hash, Action computeMutation)
    {
        byte[] before = hash();
        computeMutation();
        byte[] after = hash();
        CollectionAssert.AreNotEqual(before, after, "A compute-relevant edit must change the canonical hash.");
    }
}
