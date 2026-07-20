using System;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>
/// Unit tests for <see cref="CanonicalizationRules"/> — the audited, append-only strip list.
/// </summary>
[TestClass]
public class CanonicalizationRulesTests
{
    /// <summary>
    /// Pins the audited strip list. APPEND-ONLY contract: additions are expected over time, but a
    /// removal or rename here means seeds re-roll — this test failing on a removal is the guard.
    /// </summary>
    [TestMethod]
    public void Test_ModelRules_ContainsAuditedStripList()
    {
        // Arrange
        string[] audited =
        {
            "Name", "Description",
            "SpecifiedHazard", "HazardUnit", "TransformedHazard", "TransformedHazardUnit",
            "SpecifiedConsequence", "ConsequenceUnit",
            "NameOnDisk", "Guid", "LeftPosition", "TopPosition", "ChartSettings",
        };

        // Act
        var rules = CanonicalizationRules.ModelRules;

        // Assert
        Assert.AreEqual(audited.Length, rules.StrippedAttributes.Count);
        foreach (string name in audited)
        {
            Assert.IsTrue(rules.StrippedAttributes.Contains(name), $"Missing audited strip entry '{name}'.");
        }
        Assert.AreEqual(0, rules.StrippedElements.Count);
        Assert.AreEqual(0, rules.Rewriters.Count);
    }

    /// <summary>Verifies the strip-name match is ordinal (case-sensitive).</summary>
    [TestMethod]
    public void Test_ModelRules_MatchIsOrdinal()
    {
        // Assert — lower-cased variants are NOT stripped names.
        Assert.IsFalse(CanonicalizationRules.ModelRules.StrippedAttributes.Contains("name"));
        Assert.IsFalse(CanonicalizationRules.ModelRules.StrippedAttributes.Contains("NAME"));
    }

    /// <summary>Verifies constructor null-argument contracts.</summary>
    [TestMethod]
    public void Test_Constructor_NullArguments_Throw()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => new CanonicalizationRules(
            null!, Array.Empty<string>(), Array.Empty<Action<XElement>>()));
        Assert.ThrowsException<ArgumentNullException>(() => new CanonicalizationRules(
            Array.Empty<string>(), null!, Array.Empty<Action<XElement>>()));
        Assert.ThrowsException<ArgumentNullException>(() => new CanonicalizationRules(
            Array.Empty<string>(), Array.Empty<string>(), null!));
    }

    /// <summary>Verifies a custom rule set reflects its constructor inputs.</summary>
    [TestMethod]
    public void Test_Constructor_ReflectsInputs()
    {
        // Arrange
        Action<XElement> rewriter = _ => { };

        // Act
        var rules = new CanonicalizationRules(new[] { "A", "B" }, new[] { "E" }, new[] { rewriter });

        // Assert
        Assert.AreEqual(2, rules.StrippedAttributes.Count);
        Assert.IsTrue(rules.StrippedAttributes.Contains("A"));
        Assert.IsTrue(rules.StrippedElements.Contains("E"));
        Assert.AreEqual(1, rules.Rewriters.Count);
        Assert.AreSame(rewriter, rules.Rewriters[0]);
    }
}
