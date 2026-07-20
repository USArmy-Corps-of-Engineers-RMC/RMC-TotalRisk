using System;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Models.Support;

namespace RMC.TotalRisk.Tests.Models.Support;

/// <summary>
/// Unit tests for <see cref="CanonicalContentHasher"/> — the injective canonical XML encoding and
/// the strip-rule pipeline behind content-based seed identity.
/// </summary>
[TestClass]
public class CanonicalContentHasherTests
{
    /// <summary>Builds a small representative serialized form with attributes, children, and metadata.</summary>
    private static XElement SampleForm()
    {
        return new XElement("TabularThing",
            new XAttribute("Name", "My Function"),
            new XAttribute("Description", "A description."),
            new XAttribute("Alpha", "1.5"),
            new XAttribute("Beta", "2.25"),
            new XElement("Ordinate", new XAttribute("X", "0.5"), new XAttribute("Y", "10")),
            new XElement("Ordinate", new XAttribute("X", "0.9"), new XAttribute("Y", "20")));
    }

    /// <summary>Verifies hashing is deterministic for an identical tree.</summary>
    [TestMethod]
    public void Test_Hash_SameContent_SameHash()
    {
        // Arrange
        var a = SampleForm();
        var b = SampleForm();

        // Act / Assert
        CollectionAssert.AreEqual(
            CanonicalContentHasher.Hash(a, CanonicalizationRules.ModelRules),
            CanonicalContentHasher.Hash(b, CanonicalizationRules.ModelRules));
    }

    /// <summary>Verifies every audited stripped attribute is inert wherever it appears.</summary>
    [TestMethod]
    public void Test_Hash_StrippedAttributes_DoNotAffectHash()
    {
        // Arrange / Act / Assert
        HashInvariance.AssertStrippedAttributesInert(SampleForm());
    }

    /// <summary>Verifies attribute declaration order is not semantic.</summary>
    [TestMethod]
    public void Test_Hash_AttributeOrder_DoesNotAffectHash()
    {
        // Arrange
        var forward = new XElement("E", new XAttribute("A", "1"), new XAttribute("B", "2"));
        var reversed = new XElement("E", new XAttribute("B", "2"), new XAttribute("A", "1"));

        // Act / Assert
        CollectionAssert.AreEqual(
            CanonicalContentHasher.Hash(forward, CanonicalizationRules.ModelRules),
            CanonicalContentHasher.Hash(reversed, CanonicalizationRules.ModelRules));
    }

    /// <summary>Verifies owned child order IS semantic content.</summary>
    [TestMethod]
    public void Test_Hash_ChildOrder_AffectsHash()
    {
        // Arrange
        var ab = new XElement("E", new XElement("C", new XAttribute("V", "1")), new XElement("C", new XAttribute("V", "2")));
        var ba = new XElement("E", new XElement("C", new XAttribute("V", "2")), new XElement("C", new XAttribute("V", "1")));

        // Act / Assert
        CollectionAssert.AreNotEqual(
            CanonicalContentHasher.Hash(ab, CanonicalizationRules.ModelRules),
            CanonicalContentHasher.Hash(ba, CanonicalizationRules.ModelRules));
    }

    /// <summary>Verifies the element name acts as the type tag: same values, different tag, different hash.</summary>
    [TestMethod]
    public void Test_Hash_ElementName_AffectsHash()
    {
        // Arrange
        var linear = new XElement("LinearTransform", new XAttribute("Alpha", "1"));
        var power = new XElement("PowerTransform", new XAttribute("Alpha", "1"));

        // Act / Assert
        CollectionAssert.AreNotEqual(
            CanonicalContentHasher.Hash(linear, CanonicalizationRules.ModelRules),
            CanonicalContentHasher.Hash(power, CanonicalizationRules.ModelRules));
    }

    /// <summary>Verifies a compute-relevant attribute edit moves the hash.</summary>
    [TestMethod]
    public void Test_Hash_AttributeValueEdit_AffectsHash()
    {
        // Arrange
        var form = SampleForm();

        // Act / Assert
        HashInvariance.AssertComputeSensitive(
            () => CanonicalContentHasher.Hash(form, CanonicalizationRules.ModelRules),
            () => form.SetAttributeValue("Alpha", "1.5000001"));
    }

    /// <summary>Verifies text-node content participates in the hash.</summary>
    [TestMethod]
    public void Test_Hash_TextNode_AffectsHash()
    {
        // Arrange
        var a = new XElement("E", "text-a");
        var b = new XElement("E", "text-b");

        // Act / Assert
        CollectionAssert.AreNotEqual(
            CanonicalContentHasher.Hash(a, CanonicalizationRules.ModelRules),
            CanonicalContentHasher.Hash(b, CanonicalizationRules.ModelRules));
    }

    /// <summary>Verifies nesting structure is injective: nested vs flattened children hash differently.</summary>
    [TestMethod]
    public void Test_Hash_NestingStructure_AffectsHash()
    {
        // Arrange — [B[C]] vs [B][C]: same names/values, different structure.
        var nested = new XElement("A", new XElement("B", new XElement("C")));
        var flat = new XElement("A", new XElement("B"), new XElement("C"));

        // Act / Assert
        CollectionAssert.AreNotEqual(
            CanonicalContentHasher.Hash(nested, CanonicalizationRules.ModelRules),
            CanonicalContentHasher.Hash(flat, CanonicalizationRules.ModelRules));
    }

    /// <summary>Verifies stripped ELEMENT names remove whole subtrees from the hashed content.</summary>
    [TestMethod]
    public void Test_Hash_StrippedElements_RemoveSubtree()
    {
        // Arrange — a custom rule set stripping a "ChartArea" child element.
        var rules = new CanonicalizationRules(
            strippedAttributes: Array.Empty<string>(),
            strippedElements: new[] { "ChartArea" },
            rewriters: Array.Empty<Action<XElement>>());
        var bare = new XElement("E", new XAttribute("V", "1"));
        var decorated = new XElement("E", new XAttribute("V", "1"),
            new XElement("ChartArea", new XAttribute("Color", "Blue")));

        // Act / Assert
        CollectionAssert.AreEqual(
            CanonicalContentHasher.Hash(bare, rules),
            CanonicalContentHasher.Hash(decorated, rules));
    }

    /// <summary>Verifies rewriters run against the working copy and never mutate the caller's tree.</summary>
    [TestMethod]
    public void Test_Hash_Rewriters_ApplyToWorkingCopyOnly()
    {
        // Arrange — a rewriter that renames a marker attribute into compute content.
        var rules = new CanonicalizationRules(
            strippedAttributes: Array.Empty<string>(),
            strippedElements: Array.Empty<string>(),
            rewriters: new Action<XElement>[] { root => root.SetAttributeValue("Rewritten", "yes") });
        var form = new XElement("E", new XAttribute("V", "1"));
        var untouched = new XElement("E", new XAttribute("V", "1"));

        // Act
        byte[] rewritten = CanonicalContentHasher.Hash(form, rules);
        byte[] plain = CanonicalContentHasher.Hash(untouched, CanonicalizationRules.ModelRules);

        // Assert — the rewrite changed the hashed content, and the input tree was not modified.
        CollectionAssert.AreNotEqual(plain, rewritten);
        Assert.IsNull(form.Attribute("Rewritten"));
    }

    /// <summary>Verifies null arguments throw.</summary>
    [TestMethod]
    public void Test_Hash_NullArguments_Throw()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => CanonicalContentHasher.Hash(null!, CanonicalizationRules.ModelRules));
        Assert.ThrowsException<ArgumentNullException>(() => CanonicalContentHasher.Hash(new XElement("E"), null!));
    }

    /// <summary>Verifies the hex token form: 64 lowercase hex characters for SHA-256.</summary>
    [TestMethod]
    public void Test_ToTokenHex_Formats64LowercaseHexCharacters()
    {
        // Arrange
        byte[] hash = CanonicalContentHasher.Hash(SampleForm(), CanonicalizationRules.ModelRules);

        // Act
        string token = CanonicalContentHasher.ToTokenHex(hash);

        // Assert
        Assert.AreEqual(64, token.Length);
        StringAssert.Matches(token, new System.Text.RegularExpressions.Regex("^[0-9a-f]{64}$"));
        Assert.ThrowsException<ArgumentNullException>(() => CanonicalContentHasher.ToTokenHex(null!));
    }
}
