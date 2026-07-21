using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Consequences;

namespace RMC.TotalRisk.Tests.RiskFunctions.Consequences;

/// <summary>
/// Unit tests for <see cref="WeightedConsequenceFunction"/> — defaults and constructors, guarded
/// change notification, subscription swapping, and verbatim forwarding of wrapped-function edits
/// (the BestFit <c>WeightedUnivariateAnalysis</c> parity surface).
/// </summary>
[TestClass]
public class WeightedConsequenceFunctionTests
{
    /// <summary>Verifies the v1.0 defaults and the convenience constructor.</summary>
    [TestMethod]
    public void Test_Defaults_And_Ctors()
    {
        // Act
        var empty = new WeightedConsequenceFunction();
        var configured = new WeightedConsequenceFunction(new TabularConsequence { Name = "Day" }, 0.42d);

        // Assert
        Assert.AreEqual(0d, empty.Weight, 0d);
        Assert.IsNull(empty.ConsequenceFunction);
        Assert.AreEqual(0.42d, configured.Weight, 0d);
        Assert.AreEqual("Day", configured.ConsequenceFunction!.Name);
    }

    /// <summary>Verifies weight changes notify and same-value writes do not.</summary>
    [TestMethod]
    public void Test_WeightChange_Notifies()
    {
        // Arrange
        var entry = new WeightedConsequenceFunction();
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.Weight = 0.58d;
        entry.Weight = 0.58d;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(WeightedConsequenceFunction.Weight) }, raised);
    }

    /// <summary>Verifies swapping the function detaches the previous subscription.</summary>
    [TestMethod]
    public void Test_FunctionSwap_DetachesPreviousSubscription()
    {
        // Arrange
        var first = new TabularConsequence { Name = "Day" };
        var second = new TabularConsequence { Name = "Night" };
        var entry = new WeightedConsequenceFunction(first, 0.5d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act — swap, then edit both functions.
        entry.ConsequenceFunction = second;
        raised.Clear();
        first.SpecifiedConsequence = "Damages";
        second.SpecifiedConsequence = "Life Loss";

        // Assert — only the currently wrapped function notifies through the entry.
        CollectionAssert.AreEqual(new[] { nameof(TabularConsequence.SpecifiedConsequence) }, raised);
    }

    /// <summary>Verifies wrapped-function edits are forwarded verbatim by property name.</summary>
    [TestMethod]
    public void Test_ChildEdits_ForwardedVerbatim()
    {
        // Arrange
        var child = new ParametricConsequence { Name = "Life loss" };
        var entry = new WeightedConsequenceFunction(child, 1d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        child.Alpha = 12d;
        child.Threshold = 3d;

        // Assert — the child's own property names surface, so owners can filter.
        CollectionAssert.AreEqual(
            new[] { nameof(ParametricConsequence.Alpha), nameof(ParametricConsequence.Threshold) },
            raised);
    }

    /// <summary>Verifies function assignment notifies once and a same-reference write is a no-op.</summary>
    [TestMethod]
    public void Test_FunctionSwap_Notifies_AndSameReferenceNoOp()
    {
        // Arrange
        var child = new TabularConsequence { Name = "Day" };
        var entry = new WeightedConsequenceFunction();
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.ConsequenceFunction = child;
        entry.ConsequenceFunction = child;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(WeightedConsequenceFunction.ConsequenceFunction) }, raised);
    }
}
