using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="ConsequenceElement"/> — the input-only terminal: the ordered
/// multi-type function list, the hazard-source binding override, serialization, and cloning.
/// </summary>
[TestClass]
public class ConsequenceElementTests
{
    /// <summary>Builds a valid labeled tabular consequence on the default table.</summary>
    private static TabularConsequence Damages(string type = "Damages", string unit = "$")
    {
        return new TabularConsequence
        {
            Name = $"{type} curve",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = type,
            ConsequenceUnit = unit,
        };
    }

    /// <summary>Builds a valid labeled bivariate stage-pool consequence surface.</summary>
    private static BivariateConsequence BivariateDamages()
    {
        return new BivariateConsequence
        {
            Name = "Stage-Pool Life Loss",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            X1Values = new[] { 0d, 10d, 20d },
            X2Values = new[] { 100d, 200d },
            ZValues = new[,] { { 1d, 2d }, { 3d, 5d }, { 4d, 8d } },
        };
    }

    /// <summary>Builds a resolver over a fixed element set.</summary>
    private static RiskElementResolver ResolverOver(params IRiskElement[] elements)
    {
        var list = new List<IRiskElement>(elements);
        return new RiskElementResolver(
            id => list.Find(e => e.Id == id),
            name => list.Find(e => string.Equals(e.Name, name, StringComparison.Ordinal)));
    }

    /// <summary>Verifies the sink-analog shape: one input, no outputs, empty function list.</summary>
    [TestMethod]
    public void Test_Defaults_SinkShape()
    {
        // Act
        var element = new ConsequenceElement("Damages");

        // Assert
        Assert.AreEqual(1, element.InputCount);
        Assert.AreEqual(0, element.OutputCount);
        Assert.AreEqual(0, element.Functions.Count);
        Assert.IsNull(element.Input);
        Assert.IsNull(element.HazardSource);
    }

    /// <summary>Verifies list assignment coercion, notification, and function enumeration.</summary>
    [TestMethod]
    public void Test_Functions_AssignmentAndEnumeration()
    {
        // Arrange
        var element = new ConsequenceElement("Damages");
        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        element.Functions = new ObservableCollection<IConsequenceFunction> { Damages(), Damages("Life Loss", "lives") };
        element.Functions = null!;   // coerces to empty and notifies

        // Assert — each assignment reports the membership change and the (possibly changed)
        // arity, since a bivariate entry flips InputCount.
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(ConsequenceElement.Functions), nameof(ConsequenceElement.InputCount),
                nameof(ConsequenceElement.Functions), nameof(ConsequenceElement.InputCount),
            }, raised);
        Assert.AreEqual(0, element.Functions.Count);

        // Enumeration skips null entries.
        element.Functions.Add(Damages());
        element.Functions.Add(null!);
        Assert.AreEqual(1, element.GetFunctions().Count());
    }

    /// <summary>
    /// Verifies that mutating the ordered collection in place notifies — the path a bound editor
    /// takes. Assigning the whole collection is not the only way its membership changes, and an
    /// element whose consequence list silently changed would leave dependent results stale.
    /// </summary>
    [TestMethod]
    public void Test_Functions_InPlaceMutationNotifies()
    {
        // Arrange
        var element = new ConsequenceElement("Damages");
        var first = Damages();
        var second = Damages("Life Loss", "lives");
        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        element.Functions.Add(first);          // Add
        element.Functions.Add(second);         // Add
        element.Functions[1] = Damages("Replacement", "lives");  // Replace
        element.Functions.Remove(first);       // Remove
        element.Functions.Clear();             // Reset

        // Assert — each membership change reports Functions and the (possibly changed) arity,
        // since a bivariate entry flips InputCount.
        Assert.AreEqual(10, raised.Count);
        for (int i = 0; i < raised.Count; i += 2)
        {
            Assert.AreEqual(nameof(ConsequenceElement.Functions), raised[i]);
            Assert.AreEqual(nameof(ConsequenceElement.InputCount), raised[i + 1]);
        }
    }

    /// <summary>
    /// Verifies a function added in place is subscribed, so its own edits reach the element — the
    /// property that makes a shared, separately stored function usable. Adding through the
    /// collection must behave exactly like assigning the whole collection.
    /// </summary>
    [TestMethod]
    public void Test_Functions_AddedInPlace_ForwardsItsOwnChanges()
    {
        // Arrange
        var element = new ConsequenceElement("Damages");
        var damages = Damages();
        element.Functions.Add(damages);

        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        damages.Description = "Updated in the function editor.";

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(ConsequenceElement.Functions) }, raised);
    }

    /// <summary>
    /// Verifies every removal path detaches the subscription, including <c>Clear</c> — which
    /// raises a Reset carrying no removed items, so handling only the event's OldItems would leak
    /// a subscription and let a removed function keep notifying the element.
    /// </summary>
    [TestMethod]
    public void Test_Functions_RemovalPathsDetachSubscriptions()
    {
        // Arrange
        var removed = Damages("Removed");
        var replaced = Damages("Replaced", "lives");
        var cleared = Damages("Cleared");
        var element = new ConsequenceElement("Damages");
        element.Functions.Add(removed);
        element.Functions.Add(replaced);
        element.Functions.Remove(removed);
        element.Functions[0] = cleared;
        element.Functions.Clear();

        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act — none of these functions is held by the element any more.
        removed.Description = "Removed by Remove.";
        replaced.Description = "Removed by indexer replacement.";
        cleared.Description = "Removed by Clear.";

        // Assert
        Assert.AreEqual(0, raised.Count, "A function no longer in the collection must not notify the element.");
    }

    /// <summary>
    /// Verifies re-assigning the whole collection detaches the previous entries — the same
    /// contract as in-place removal, exercised through the property setter.
    /// </summary>
    [TestMethod]
    public void Test_Functions_ReassigningCollectionDetachesPreviousEntries()
    {
        // Arrange
        var original = Damages("Original");
        var element = new ConsequenceElement("Damages");
        element.Functions.Add(original);
        element.Functions = new ObservableCollection<IConsequenceFunction> { Damages("Fresh") };

        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        original.Description = "No longer wired to the element.";

        // Assert
        Assert.AreEqual(0, raised.Count);

        // The newly assigned entry is wired.
        element.Functions[0].Description = "Still wired.";
        CollectionAssert.AreEqual(new[] { nameof(ConsequenceElement.Functions) }, raised);
    }

    /// <summary>
    /// Verifies a function listed twice is subscribed once — so it notifies once, not once per
    /// occurrence — and stays subscribed while any occurrence remains.
    /// </summary>
    [TestMethod]
    public void Test_Functions_DuplicateEntry_NotifiesOnceAndSurvivesPartialRemoval()
    {
        // Arrange
        var shared = Damages("Shared");
        var element = new ConsequenceElement("Damages");
        element.Functions.Add(shared);
        element.Functions.Add(shared);

        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        shared.Description = "Edited while listed twice.";

        // Assert
        Assert.AreEqual(1, raised.Count, "A duplicated function must notify once, not once per occurrence.");

        // Act — one occurrence removed; the remaining one keeps the subscription alive.
        raised.Clear();
        element.Functions.RemoveAt(0);
        raised.Clear();
        shared.Description = "Still listed once.";

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(ConsequenceElement.Functions) }, raised);
    }

    /// <summary>Verifies the structural enumeration excludes the binding (not a path edge).</summary>
    [TestMethod]
    public void Test_GetInputConnections_StructuralOnly()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var response = new ResponseElement("Breach");
        var element = new ConsequenceElement("Damages")
        {
            Input = new RiskConnection(response),
            HazardSource = new RiskConnection(hazard),
        };

        // Assert — only the structural input is a path edge; the binding stays a typed property.
        Assert.AreSame(response, element.GetInputConnections().Single().Source);
    }

    /// <summary>Verifies the validation matrix: empty list, null entries, duplicate-type warning.</summary>
    [TestMethod]
    public void Test_Validate_Matrix()
    {
        // Valid: named with one valid function.
        var valid = new ConsequenceElement("Damages");
        valid.Functions.Add(Damages());
        Assert.IsTrue(valid.Validate().IsValid);

        // No functions is an error.
        Assert.IsFalse(new ConsequenceElement("Damages").Validate().IsValid);

        // A null entry is an error.
        var withNull = new ConsequenceElement("Damages");
        withNull.Functions.Add(Damages());
        withNull.Functions.Add(null!);
        Assert.IsFalse(withNull.Validate().IsValid);

        // Duplicate consequence-type labels warn (types are positional).
        var duplicate = new ConsequenceElement("Damages");
        duplicate.Functions.Add(Damages());
        duplicate.Functions.Add(Damages());
        var (duplicateValid, duplicateMessages) = duplicate.Validate();
        Assert.IsTrue(duplicateValid);
        Assert.IsTrue(duplicateMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("multiple consequence functions labeled")));
    }

    /// <summary>Verifies serialization: ordered functions, both connection triples, resolution.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange — a two-type terminal bound to the raw hazard.
        var hazard = new HazardElement("Hazard");
        var response = new ResponseElement("Breach");
        var original = new ConsequenceElement("Damages")
        {
            Input = new RiskConnection(response),
            HazardSource = new RiskConnection(hazard),
        };
        original.Functions.Add(Damages());
        original.Functions.Add(Damages("Life Loss", "lives"));

        // Act
        var xml = original.ToXElement();

        // Assert — the binding serializes under its own kind names.
        Assert.AreEqual(response.Id.ToString("D"), xml.Attribute("SourceElementId")!.Value);
        Assert.AreEqual(hazard.Id.ToString("D"), xml.Attribute("HazardSourceElementId")!.Value);
        Assert.AreEqual("Hazard", xml.Attribute("HazardSourceElement")!.Value);
        Assert.AreEqual("0", xml.Attribute("HazardSourcePort")!.Value);

        // The restored element resolves both references and preserves function order.
        var restored = new ConsequenceElement(xml);
        restored.ResolveDeserializedReferences(ResolverOver(hazard, response));
        Assert.AreSame(response, restored.Input!.Source);
        Assert.AreSame(hazard, restored.HazardSource!.Source);
        Assert.AreEqual(2, restored.Functions.Count);
        Assert.AreEqual("Life Loss", restored.Functions[1].SpecifiedConsequence);
        CollectionAssert.AreEqual(original.Functions[0].CanonicalHash(), restored.Functions[0].CanonicalHash());

        // An unknown function child throws (no silent content loss).
        var badFunction = new XElement(nameof(ConsequenceElement),
            new XElement(nameof(ConsequenceElement.Functions), new XElement("Bogus")));
        Assert.ThrowsException<InvalidOperationException>(() => new ConsequenceElement(badFunction));
    }

    /// <summary>Verifies cloning: shared Id, deep function copies, both connections re-linked.</summary>
    [TestMethod]
    public void Test_Clone_And_ResolveClonedConnections()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var original = new ConsequenceElement("Damages") { HazardSource = new RiskConnection(hazard) };
        original.Functions.Add(Damages());

        // Act
        var hazardClone = (HazardElement)hazard.Clone();
        var clone = (ConsequenceElement)original.Clone();
        clone.ResolveClonedConnections(original,
            new Dictionary<IRiskElement, IRiskElement> { [hazard] = hazardClone });

        // Assert
        Assert.AreEqual(original.Id, clone.Id);
        Assert.AreEqual(1, clone.Functions.Count);
        Assert.AreNotSame(original.Functions[0], clone.Functions[0]);
        CollectionAssert.AreEqual(original.Functions[0].CanonicalHash(), clone.Functions[0].CanonicalHash());
        Assert.AreSame(hazardClone, clone.HazardSource!.Source);
        Assert.IsNull(clone.Input);
    }

    /// <summary>
    /// Verifies the mode-aware validation overload: a function-free element errors in
    /// risk mode (and through the parameterless override), passes in reliability mode, and any
    /// functions that are present are still validated in both modes.
    /// </summary>
    [TestMethod]
    public void Test_Validate_ReliabilityMode_RelaxesFunctionRequirement()
    {
        // Arrange
        var empty = new ConsequenceElement("Terminal") { Input = new RiskConnection(new HazardElement("Hazard")) };

        // Act / Assert — risk mode rejects the empty terminal; reliability accepts it.
        Assert.IsFalse(empty.Validate().IsValid);
        Assert.IsTrue(empty.Validate().ValidationMessages.Any(m => m.Contains("no consequence functions")));
        Assert.IsTrue(empty.Validate(RiskAnalysisMode.Reliability).IsValid);

        // An assigned function still validates in reliability mode (an invalid one still errors).
        var withInvalid = new ConsequenceElement("Terminal") { Input = new RiskConnection(new HazardElement("Hazard")) };
        withInvalid.Functions.Add(new TabularConsequence { Name = "Empty Table" });
        Assert.IsFalse(withInvalid.Validate(RiskAnalysisMode.Reliability).IsValid,
            "Reliability mode relaxes absence, never correctness.");
    }

    /// <summary>
    /// Verifies the bivariate arity gate: the input count flips to two when any wrapped entry is
    /// bivariate, tracking in-place membership changes.
    /// </summary>
    [TestMethod]
    public void Test_InputCount_BivariateGate()
    {
        // Arrange
        var element = new ConsequenceElement("Damages");
        element.Functions.Add(Damages());
        Assert.AreEqual(1, element.InputCount);

        // Act / Assert — adding a bivariate entry flips the arity; removing it restores.
        var bivariate = BivariateDamages();
        element.Functions.Add(bivariate);
        Assert.AreEqual(2, element.InputCount);
        element.Functions.Remove(bivariate);
        Assert.AreEqual(1, element.InputCount);
    }

    /// <summary>
    /// Verifies the element-local bivariate validation rules: mixing the two kinds errors, a
    /// secondary input needs a bivariate entry, and a bivariate entry needs the secondary input.
    /// </summary>
    [TestMethod]
    public void Test_Validate_BivariateRules()
    {
        var hazard = new HazardElement("Hazard");

        // Mixing bivariate and univariate entries on one terminal.
        var mixed = new ConsequenceElement("Damages")
        {
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        mixed.Functions.Add(Damages());
        mixed.Functions.Add(BivariateDamages());
        var (mixedValid, mixedMessages) = mixed.Validate();
        Assert.IsFalse(mixedValid);
        Assert.IsTrue(mixedMessages.Any(m => m.Contains("mixes bivariate and univariate consequence functions")));

        // A secondary input with no bivariate entry to consume it.
        var univariate = new ConsequenceElement("Damages")
        {
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        univariate.Functions.Add(Damages());
        var (univariateValid, univariateMessages) = univariate.Validate();
        Assert.IsFalse(univariateValid);
        Assert.IsTrue(univariateMessages.Any(m => m.Contains("none of its consequence functions is bivariate")));

        // A bivariate entry without the secondary input.
        var unwired = new ConsequenceElement("Damages");
        unwired.Functions.Add(BivariateDamages());
        var (unwiredValid, unwiredMessages) = unwired.Validate();
        Assert.IsFalse(unwiredValid);
        Assert.IsTrue(unwiredMessages.Any(m => m.Contains("wraps a bivariate consequence but has no secondary input")));

        // A wired all-bivariate terminal passes element-local validation.
        var wired = new ConsequenceElement("Damages")
        {
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        wired.Functions.Add(BivariateDamages());
        Assert.IsTrue(wired.Validate().IsValid);
    }

    /// <summary>
    /// Verifies the secondary connection triple serializes under its kind names, resolves
    /// pending after load, remaps through the clone map, and is absent from univariate forms.
    /// </summary>
    [TestMethod]
    public void Test_SecondaryInput_SerializationAndCloneRemap()
    {
        // Arrange
        var hazard = new HazardElement("Hazard");
        var original = new ConsequenceElement("Damages")
        {
            Input = new RiskConnection(hazard),
            SecondaryInput = new RiskConnection(hazard, 1),
        };
        original.Functions.Add(BivariateDamages());

        // Act
        var xml = original.ToXElement();

        // Assert — the secondary triple beside the primary and binding triples.
        Assert.AreEqual(hazard.Id.ToString("D"), xml.Attribute("SecondarySourceElementId")!.Value);
        Assert.AreEqual("Hazard", xml.Attribute("SecondarySourceElement")!.Value);
        Assert.AreEqual("1", xml.Attribute("SecondarySourcePort")!.Value);

        // Pending until the graph resolves; then live.
        var restored = new ConsequenceElement(xml);
        Assert.IsNull(restored.SecondaryInput);
        restored.ResolveDeserializedReferences(ResolverOver(hazard));
        Assert.AreSame(hazard, restored.SecondaryInput!.Source);
        Assert.AreEqual(1, restored.SecondaryInput.SourcePort);

        // The clone remaps the secondary connection through the original→clone map.
        var hazardClone = (HazardElement)hazard.Clone();
        var clone = (ConsequenceElement)original.Clone();
        Assert.IsNull(clone.SecondaryInput);
        clone.ResolveClonedConnections(original,
            new Dictionary<IRiskElement, IRiskElement> { [hazard] = hazardClone });
        Assert.AreSame(hazardClone, clone.SecondaryInput!.Source);
        Assert.AreEqual(1, clone.SecondaryInput.SourcePort);

        // A univariate element's form carries no secondary attributes (byte compatibility).
        var plain = new ConsequenceElement("Damages") { Input = new RiskConnection(hazard) };
        plain.Functions.Add(Damages());
        Assert.IsNull(plain.ToXElement().Attribute("SecondarySourceElementId"));
        Assert.IsNull(plain.ToXElement().Attribute("SecondarySourcePort"));
    }
}
