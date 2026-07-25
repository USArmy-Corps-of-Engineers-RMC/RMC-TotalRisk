using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.RiskFunctions.Responses;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses;

/// <summary>
/// Unit tests for <see cref="WeightedResponseFunction"/> — defaults and constructors, guarded change
/// notification, subscription swapping, and verbatim forwarding of wrapped-function edits.
/// </summary>
[TestClass]
public class WeightedResponseFunctionTests
{
    /// <summary>Verifies the v1.0 defaults and the convenience constructor.</summary>
    [TestMethod]
    public void Test_Defaults_And_Ctors()
    {
        // Act
        var empty = new WeightedResponseFunction();
        var configured = new WeightedResponseFunction(new TabularResponse { Name = "Overtopping" }, 0.45d);

        // Assert
        Assert.AreEqual(0d, empty.Weight, 0d);
        Assert.IsNull(empty.ResponseFunction);
        Assert.AreEqual(0.45d, configured.Weight, 0d);
        Assert.AreEqual("Overtopping", configured.ResponseFunction!.Name);
    }

    /// <summary>Verifies weight changes notify and same-value writes do not.</summary>
    [TestMethod]
    public void Test_WeightChange_Notifies()
    {
        // Arrange
        var entry = new WeightedResponseFunction();
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.Weight = 0.55d;
        entry.Weight = 0.55d;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(WeightedResponseFunction.Weight) }, raised);
    }

    /// <summary>Verifies swapping the function detaches the previous subscription.</summary>
    [TestMethod]
    public void Test_FunctionSwap_DetachesPreviousSubscription()
    {
        // Arrange
        var first = new TabularResponse { Name = "Overtopping" };
        var second = new TabularResponse { Name = "Piping" };
        var entry = new WeightedResponseFunction(first, 0.5d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act — swap, then edit both functions.
        entry.ResponseFunction = second;
        raised.Clear();
        first.HazardTransform = Transform.Logarithmic;
        second.HazardTransform = Transform.Logarithmic;

        // Assert — only the currently wrapped function notifies through the entry.
        CollectionAssert.AreEqual(new[] { nameof(TabularResponse.HazardTransform) }, raised);
    }

    /// <summary>Verifies wrapped-function edits are forwarded verbatim by property name.</summary>
    [TestMethod]
    public void Test_ChildEdits_ForwardedVerbatim()
    {
        // Arrange
        var child = new ParametricResponse { Name = "Fragility", ParentDistribution = new Normal(140d, 30d) };
        var entry = new WeightedResponseFunction(child, 1d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        child.EffectiveRecordLength = 75;
        child.PRNGSeed = 999;

        // Assert
        CollectionAssert.AreEqual(
            new[] { nameof(ParametricResponse.EffectiveRecordLength), nameof(ParametricResponse.PRNGSeed) },
            raised);
    }

    /// <summary>Verifies function assignment notifies once and a same-reference write is a no-op.</summary>
    [TestMethod]
    public void Test_FunctionSwap_Notifies_AndSameReferenceNoOp()
    {
        // Arrange
        var child = new TabularResponse { Name = "Overtopping" };
        var entry = new WeightedResponseFunction();
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.ResponseFunction = child;
        entry.ResponseFunction = child;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(WeightedResponseFunction.ResponseFunction) }, raised);
    }

    /// <summary>Verifies assigning null detaches cleanly and stops forwarding.</summary>
    [TestMethod]
    public void Test_AssigningNull_DetachesCleanly()
    {
        // Arrange
        var child = new TabularResponse { Name = "Overtopping" };
        var entry = new WeightedResponseFunction(child, 1d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.ResponseFunction = null;
        raised.Clear();
        child.HazardTransform = Transform.Logarithmic;

        // Assert
        Assert.IsNull(entry.ResponseFunction);
        Assert.AreEqual(0, raised.Count);
    }
}
