using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for the <see cref="RiskElementBase"/> shared behaviors — identity, metadata
/// coercion, change notification, and clone base-copy semantics — exercised through
/// <see cref="HazardElement"/> as the concrete vehicle. Name-authority enforcement is covered by
/// the graph container tests (the authority hook is internal wiring).
/// </summary>
[TestClass]
public class RiskElementBaseTests
{
    /// <summary>Verifies fresh elements get unique persistent identities.</summary>
    [TestMethod]
    public void Test_Id_FreshAndUnique()
    {
        // Act
        var a = new HazardElement("A");
        var b = new HazardElement("B");

        // Assert
        Assert.AreNotEqual(Guid.Empty, a.Id);
        Assert.AreNotEqual(a.Id, b.Id);
    }

    /// <summary>Verifies AssignNewId changes the identity and notifies.</summary>
    [TestMethod]
    public void Test_AssignNewId_ChangesIdAndNotifies()
    {
        // Arrange
        var element = new HazardElement("A");
        var before = element.Id;
        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        element.AssignNewId();

        // Assert
        Assert.AreNotEqual(before, element.Id);
        CollectionAssert.Contains(raised, nameof(HazardElement.Id));
    }

    /// <summary>Verifies metadata null coercion and change notification.</summary>
    [TestMethod]
    public void Test_Metadata_CoercionAndNotification()
    {
        // Arrange
        var element = new HazardElement("A");
        var raised = new List<string>();
        element.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        element.Name = "Renamed";
        element.Name = "Renamed";              // unchanged — no raise
        element.Description = "Described";
        element.Description = null!;           // coerces to empty and raises
        element.LeftPosition = 10d;
        element.TopPosition = 20d;
        element.TopPosition = 20d;             // unchanged — no raise

        // Assert
        Assert.AreEqual(string.Empty, element.Description);
        CollectionAssert.AreEqual(new[]
        {
            nameof(HazardElement.Name),
            nameof(HazardElement.Description),
            nameof(HazardElement.Description),
            nameof(HazardElement.LeftPosition),
            nameof(HazardElement.TopPosition),
        }, raised);
    }

    /// <summary>Verifies a detached element renames freely (no authority attached).</summary>
    [TestMethod]
    public void Test_DetachedRename_Unrestricted()
    {
        // Arrange
        var a = new HazardElement("Same");
        var b = new HazardElement("Same");

        // Assert — no throw while detached; the graph enforces uniqueness on attach.
        Assert.AreEqual(a.Name, b.Name);
    }

    /// <summary>Verifies the base name check is the shared validation floor.</summary>
    [TestMethod]
    public void Test_Validate_MissingName_IsError()
    {
        // Act
        var (isValid, messages) = new HazardElement().Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Exists(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("name")));
    }

    /// <summary>Verifies cloning copies base identity and metadata, sharing the Id.</summary>
    [TestMethod]
    public void Test_Clone_CopiesBase_SharesId()
    {
        // Arrange
        var element = new HazardElement("Original")
        {
            Description = "The hazard root.",
            LeftPosition = 120d,
            TopPosition = 45.5d,
        };

        // Act
        var clone = (HazardElement)element.Clone();

        // Assert — a clone is the same logical element (shared Id) with copied metadata.
        Assert.AreEqual(element.Id, clone.Id);
        Assert.AreEqual(element.Name, clone.Name);
        Assert.AreEqual(element.Description, clone.Description);
        Assert.AreEqual(element.LeftPosition, clone.LeftPosition, 0d);
        Assert.AreEqual(element.TopPosition, clone.TopPosition, 0d);
    }
}
