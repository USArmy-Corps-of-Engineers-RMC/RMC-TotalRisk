using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Tests.Systems.Components;

/// <summary>
/// Unit tests for <see cref="ResponseStage"/> — defaults, null coercions, determinism aggregation,
/// validation aggregation, and the self-contained serialization round trip.
/// </summary>
[TestClass]
public class ResponseStageTests
{
    /// <summary>Builds a labeled deterministic flow-to-stage rating transform.</summary>
    private static TabularTransform RatingTransform()
    {
        return new TabularTransform
        {
            Name = "Rating",
            SpecifiedHazard = "Flow",
            HazardUnit = "cfs",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(50d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a labeled Normal-uncertain transform.</summary>
    private static TabularTransform UncertainTransform()
    {
        var t = RatingTransform();
        t.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Normal(10d, 2d)), new UncertainOrdinate(100d, new Normal(20d, 2d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal);
        return t;
    }

    /// <summary>Builds a labeled tabular response on the default table.</summary>
    private static TabularResponse StageResponse()
    {
        return new TabularResponse
        {
            Name = "Fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
        };
    }

    /// <summary>Verifies the default state: no transforms, the non-failure sentinel, deterministic.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var stage = new ResponseStage();

        // Assert — the polarity default is the v1.0-implied Fail branch.
        Assert.AreEqual(0, stage.Transforms.Count);
        Assert.IsInstanceOfType(stage.Response, typeof(NonFailResponse));
        Assert.IsTrue(stage.IsDeterministic);
        Assert.AreEqual(BranchPolarity.Fail, stage.BranchPolarity);
    }

    /// <summary>Verifies the polarity constructor and property change notification.</summary>
    [TestMethod]
    public void Test_BranchPolarity_ConstructorAndNotification()
    {
        // Arrange — the polarity ctor overload.
        var stage = new ResponseStage(new List<ITransformFunction>(), StageResponse(), BranchPolarity.NonFail);
        Assert.AreEqual(BranchPolarity.NonFail, stage.BranchPolarity);

        var raised = new List<string>();
        stage.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act — a real change notifies; re-assigning the same value does not.
        stage.BranchPolarity = BranchPolarity.Fail;
        stage.BranchPolarity = BranchPolarity.Fail;

        // Assert
        CollectionAssert.AreEqual(new[] { nameof(ResponseStage.BranchPolarity) }, raised);
    }

    /// <summary>
    /// Pins the serialized attribute contract (append-only; the stage XML feeds the failure-mode
    /// canonical hash): the polarity attribute is always written resolved, and the child order is
    /// Transforms then Response.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_AttributeContract()
    {
        // Act — a default stage still writes the resolved polarity (the
        // ConsequenceHazardPosition resolved-on-write precedent; a deliberate hash event).
        var xml = new ResponseStage().ToXElement();

        // Assert — attributes.
        CollectionAssert.AreEqual(
            new[] { nameof(ResponseStage.BranchPolarity) },
            xml.Attributes().Select(a => a.Name.LocalName).ToArray());
        Assert.AreEqual("Fail", xml.Attribute(nameof(ResponseStage.BranchPolarity))!.Value);

        // Children, in append-only order.
        CollectionAssert.AreEqual(
            new[] { nameof(ResponseStage.Transforms), nameof(ResponseStage.Response) },
            xml.Elements().Select(e => e.Name.LocalName).ToArray());
    }

    /// <summary>
    /// Verifies polarity round-trips, and that an earlier payload (no polarity attribute) loads
    /// forward as the v1.0-implied Fail branch.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_PolarityRoundTripAndLoadForward()
    {
        // Arrange
        var original = new ResponseStage(new List<ITransformFunction> { RatingTransform() }, StageResponse(), BranchPolarity.NonFail);

        // Act
        var restored = new ResponseStage(original.ToXElement());

        // Assert
        Assert.AreEqual(BranchPolarity.NonFail, restored.BranchPolarity);
        Assert.AreEqual(original.ToXElement().ToString(), restored.ToXElement().ToString());

        // An earlier payload without the attribute loads forward as Fail.
        var legacy = original.ToXElement();
        legacy.Attribute(nameof(ResponseStage.BranchPolarity))!.Remove();
        Assert.AreEqual(BranchPolarity.Fail, new ResponseStage(legacy).BranchPolarity);
    }

    /// <summary>Verifies the list constructor copies the caller's list.</summary>
    [TestMethod]
    public void Test_Constructor_CopiesTransformList()
    {
        // Arrange
        var transforms = new List<ITransformFunction> { RatingTransform() };

        // Act
        var stage = new ResponseStage(transforms, StageResponse());
        transforms.Add(RatingTransform());

        // Assert — the stage owns its own copy.
        Assert.AreEqual(1, stage.Transforms.Count);
        Assert.AreEqual(2, transforms.Count);
    }

    /// <summary>Verifies the list constructor rejects a null transform list.</summary>
    [TestMethod]
    public void Test_Constructor_NullTransforms_Throws()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => new ResponseStage(null!, StageResponse()));
    }

    /// <summary>Verifies a null response coerces to the non-failure sentinel (v1.0 defaulting).</summary>
    [TestMethod]
    public void Test_NullResponse_CoercesToNonFail()
    {
        // Arrange — via the constructor.
        var stage = new ResponseStage(new List<ITransformFunction>(), null);
        Assert.IsInstanceOfType(stage.Response, typeof(NonFailResponse));

        // Act — via the setter, after a real response was assigned.
        stage.Response = StageResponse();
        Assert.IsInstanceOfType(stage.Response, typeof(TabularResponse));
        stage.Response = null!;

        // Assert
        Assert.IsInstanceOfType(stage.Response, typeof(NonFailResponse));
    }

    /// <summary>Verifies property change notification on the response and transforms setters.</summary>
    [TestMethod]
    public void Test_PropertyChanged_RaisedOnSetters()
    {
        // Arrange
        var stage = new ResponseStage();
        var raised = new List<string>();
        stage.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        stage.Response = StageResponse();
        stage.Transforms = new List<ITransformFunction> { RatingTransform() };
        stage.Transforms = null!;

        // Assert — null transforms coerce to empty and still notify.
        CollectionAssert.AreEqual(
            new[] { nameof(ResponseStage.Response), nameof(ResponseStage.Transforms), nameof(ResponseStage.Transforms) },
            raised);
        Assert.AreEqual(0, stage.Transforms.Count);
    }

    /// <summary>Verifies determinism aggregates over the transforms and the response.</summary>
    [TestMethod]
    public void Test_IsDeterministic_AggregatesMembers()
    {
        // Deterministic transform + sentinel response.
        var deterministic = new ResponseStage(new List<ITransformFunction> { RatingTransform() }, null);
        Assert.IsTrue(deterministic.IsDeterministic);

        // An uncertain transform makes the stage uncertain.
        var uncertainTransform = new ResponseStage(new List<ITransformFunction> { UncertainTransform() }, null);
        Assert.IsFalse(uncertainTransform.IsDeterministic);

        // An uncertain response makes the stage uncertain (parametric default IsUncertain = true).
        var uncertainResponse = new ResponseStage(new List<ITransformFunction>(), new ParametricResponse());
        Assert.IsFalse(uncertainResponse.IsDeterministic);
    }

    /// <summary>Verifies a null transform entry is an invalidating error.</summary>
    [TestMethod]
    public void Test_Validate_NullTransformEntry_IsError()
    {
        // Arrange
        var stage = new ResponseStage();
        stage.Transforms.Add(null!);

        // Act
        var (isValid, messages) = stage.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("index 0")));
    }

    /// <summary>Verifies member-function validation messages are aggregated.</summary>
    [TestMethod]
    public void Test_Validate_AggregatesFunctionMessages()
    {
        // Arrange — an unlabeled default transform carries label errors.
        var stage = new ResponseStage(new List<ITransformFunction> { new TabularTransform() }, null);

        // Act
        var (isValid, messages) = stage.Validate();

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Count(m => m.StartsWith("Error:", StringComparison.Ordinal)) >= 1);

        // A fully labeled stage is valid.
        var valid = new ResponseStage(new List<ITransformFunction> { RatingTransform() }, StageResponse());
        Assert.IsTrue(valid.Validate().IsValid);
    }

    /// <summary>Verifies the XElement round-trip restores the chain bit-faithfully.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = new ResponseStage(new List<ITransformFunction> { UncertainTransform() }, StageResponse());

        // Act
        var restored = new ResponseStage(original.ToXElement());

        // Assert
        Assert.AreEqual(1, restored.Transforms.Count);
        Assert.IsInstanceOfType(restored.Transforms[0], typeof(TabularTransform));
        Assert.IsInstanceOfType(restored.Response, typeof(TabularResponse));
        Assert.AreEqual(original.ToXElement().ToString(), restored.ToXElement().ToString(),
            "Round-trip must reproduce the serialized form exactly (the stage XML feeds the failure-mode hash).");
    }

    /// <summary>Verifies unreconstructable children throw instead of silently degrading the chain.</summary>
    [TestMethod]
    public void Test_Serialization_UnknownChildren_Throw()
    {
        // Arrange — an unknown transform type.
        var badTransform = new XElement(nameof(ResponseStage),
            new XElement(nameof(ResponseStage.Transforms), new XElement("BogusTransform")));

        // An unknown response type.
        var badResponse = new XElement(nameof(ResponseStage),
            new XElement(nameof(ResponseStage.Transforms)),
            new XElement(nameof(ResponseStage.Response), new XElement("BogusResponse")));

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => new ResponseStage(badTransform));
        Assert.ThrowsException<InvalidOperationException>(() => new ResponseStage(badResponse));
        Assert.ThrowsException<ArgumentNullException>(() => new ResponseStage((XElement)null!));
    }

    /// <summary>Verifies missing children fall back to the default state.</summary>
    [TestMethod]
    public void Test_Serialization_MissingChildren_Defaults()
    {
        // Act
        var stage = new ResponseStage(new XElement(nameof(ResponseStage)));

        // Assert
        Assert.AreEqual(0, stage.Transforms.Count);
        Assert.IsInstanceOfType(stage.Response, typeof(NonFailResponse));
    }
}
