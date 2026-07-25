using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Tests.RiskFunctions.Transforms;

/// <summary>
/// Unit tests for <see cref="WeightedTransformFunction"/> — defaults and constructors, guarded
/// change notification, subscription swapping, and verbatim forwarding of wrapped-function edits.
/// </summary>
[TestClass]
public class WeightedTransformFunctionTests
{
    /// <summary>Verifies the defaults and the convenience constructor.</summary>
    [TestMethod]
    public void Test_Defaults_And_Ctors()
    {
        // Act
        var empty = new WeightedTransformFunction();
        var configured = new WeightedTransformFunction(new LinearTransform { Name = "Rating A" }, 0.4d);

        // Assert
        Assert.AreEqual(0d, empty.Weight, 0d);
        Assert.IsNull(empty.TransformFunction);
        Assert.AreEqual(0.4d, configured.Weight, 0d);
        Assert.AreEqual("Rating A", configured.TransformFunction!.Name);
    }

    /// <summary>Verifies weight changes notify and same-value writes do not.</summary>
    [TestMethod]
    public void Test_WeightChange_Notifies()
    {
        // Arrange
        var entry = new WeightedTransformFunction();
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.Weight = 0.6d;
        entry.Weight = 0.6d;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(WeightedTransformFunction.Weight) }, raised);
    }

    /// <summary>Verifies swapping the function detaches the previous subscription.</summary>
    [TestMethod]
    public void Test_FunctionSwap_DetachesPreviousSubscription()
    {
        // Arrange
        var first = new LinearTransform { Name = "Rating A" };
        var second = new LinearTransform { Name = "Rating B" };
        var entry = new WeightedTransformFunction(first, 0.5d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act — swap, then edit both functions.
        entry.TransformFunction = second;
        raised.Clear();
        first.Beta = 2d;
        second.Beta = 3d;

        // Assert — only the currently wrapped function notifies through the entry.
        CollectionAssert.AreEqual(new[] { nameof(LinearTransform.Beta) }, raised);
    }

    /// <summary>Verifies wrapped-function edits are forwarded verbatim by property name.</summary>
    [TestMethod]
    public void Test_ChildEdits_ForwardedVerbatim()
    {
        // Arrange
        var child = new PowerTransform { Name = "Rating" };
        var entry = new WeightedTransformFunction(child, 1d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        child.Alpha = 5d;
        child.Xi = 1d;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(PowerTransform.Alpha), nameof(PowerTransform.Xi) }, raised);
    }

    /// <summary>Verifies function assignment notifies once and a same-reference write is a no-op.</summary>
    [TestMethod]
    public void Test_FunctionSwap_Notifies_AndSameReferenceNoOp()
    {
        // Arrange
        var child = new LinearTransform { Name = "Rating A" };
        var entry = new WeightedTransformFunction();
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.TransformFunction = child;
        entry.TransformFunction = child;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(WeightedTransformFunction.TransformFunction) }, raised);
    }

    /// <summary>Verifies assigning null detaches cleanly and stops forwarding.</summary>
    [TestMethod]
    public void Test_AssigningNull_DetachesCleanly()
    {
        // Arrange
        var child = new LinearTransform { Name = "Rating A" };
        var entry = new WeightedTransformFunction(child, 1d);
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        // Act
        entry.TransformFunction = null;
        raised.Clear();
        child.Beta = 2d;

        // Assert
        Assert.IsNull(entry.TransformFunction);
        Assert.AreEqual(0, raised.Count);
    }
}
