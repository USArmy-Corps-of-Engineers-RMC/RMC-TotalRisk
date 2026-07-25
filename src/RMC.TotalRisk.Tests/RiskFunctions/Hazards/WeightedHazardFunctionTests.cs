using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.RiskFunctions.Hazards;

namespace RMC.TotalRisk.Tests.RiskFunctions.Hazards;

/// <summary>
/// Unit tests for <see cref="WeightedHazardFunction"/> — defaults and constructors, guarded change
/// notification, subscription swapping, and verbatim forwarding of wrapped-function edits.
/// </summary>
[TestClass]
public class WeightedHazardFunctionTests
{
    /// <summary>Verifies the v1.0 defaults and the convenience constructor.</summary>
    [TestMethod]
    public void Test_Defaults_And_Ctors()
    {
        // Act
        var empty = new WeightedHazardFunction();
        var configured = new WeightedHazardFunction(new TabularHazard { Name = "Rain" }, 0.45d);

        // Assert
        Assert.AreEqual(0d, empty.Weight, 0d);
        Assert.IsNull(empty.HazardFunction);
        Assert.AreEqual(0.45d, configured.Weight, 0d);
        Assert.AreEqual("Rain", configured.HazardFunction!.Name);
    }

    /// <summary>Verifies weight changes notify and same-value writes do not.</summary>
    [TestMethod]
    public void Test_WeightChange_Notifies()
    {
        // Arrange
        var entry = new WeightedHazardFunction();
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.Weight = 0.55d;
        entry.Weight = 0.55d;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(WeightedHazardFunction.Weight) }, raised);
    }

    /// <summary>Verifies swapping the function detaches the previous subscription.</summary>
    [TestMethod]
    public void Test_FunctionSwap_DetachesPreviousSubscription()
    {
        // Arrange
        var first = new TabularHazard { Name = "Rain" };
        var second = new TabularHazard { Name = "Snow" };
        var entry = new WeightedHazardFunction(first, 0.5d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act — swap, then edit both functions.
        entry.HazardFunction = second;
        raised.Clear();
        first.HazardTransform = Transform.Logarithmic;
        second.HazardTransform = Transform.Logarithmic;

        // Assert — only the currently wrapped function notifies through the entry.
        CollectionAssert.AreEqual(new[] { nameof(TabularHazard.HazardTransform) }, raised);
    }

    /// <summary>Verifies wrapped-function edits are forwarded verbatim by property name.</summary>
    [TestMethod]
    public void Test_ChildEdits_ForwardedVerbatim()
    {
        // Arrange
        var child = new ParametricUnivariateHazard { Name = "Flow", ParentDistribution = new Normal(100d, 20d) };
        var entry = new WeightedHazardFunction(child, 1d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        child.EffectiveRecordLength = 75;
        child.PRNGSeed = 999;

        // Assert — the child's own property names surface, so owners can filter.
        CollectionAssert.AreEqual(
            new[] { nameof(ParametricUnivariateHazard.EffectiveRecordLength), nameof(ParametricUnivariateHazard.PRNGSeed) },
            raised);
    }

    /// <summary>Verifies function assignment notifies once and a same-reference write is a no-op.</summary>
    [TestMethod]
    public void Test_FunctionSwap_Notifies_AndSameReferenceNoOp()
    {
        // Arrange
        var child = new TabularHazard { Name = "Rain" };
        var entry = new WeightedHazardFunction();
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.HazardFunction = child;
        entry.HazardFunction = child;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(WeightedHazardFunction.HazardFunction) }, raised);
    }

    /// <summary>Verifies assigning null detaches cleanly and stops forwarding.</summary>
    [TestMethod]
    public void Test_AssigningNull_DetachesCleanly()
    {
        // Arrange
        var child = new TabularHazard { Name = "Rain" };
        var entry = new WeightedHazardFunction(child, 1d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.HazardFunction = null;
        raised.Clear();
        child.HazardTransform = Transform.Logarithmic;

        // Assert
        Assert.IsNull(entry.HazardFunction);
        Assert.AreEqual(0, raised.Count);
    }
}
