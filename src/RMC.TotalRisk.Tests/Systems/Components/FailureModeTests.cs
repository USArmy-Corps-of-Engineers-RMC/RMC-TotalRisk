using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;
using RMC.TotalRisk.Systems.Components;
using RMC.TotalRisk.Tests.Core;

namespace RMC.TotalRisk.Tests.Systems.Components;

/// <summary>
/// Unit tests for <see cref="FailureMode"/> — v1.0 defaults and compat views over the staged
/// shape, response chains, consequence lists, binding resolution, the validation matrix, the
/// self-contained serialization round trip, and the canonical-hash identity contract.
/// </summary>
[TestClass]
public class FailureModeTests
{
    /// <summary>Builds a labeled deterministic transform from one hazard type to another.</summary>
    private static TabularTransform Rating(string fromHazard, string fromUnit, string toHazard, string toUnit)
    {
        return new TabularTransform
        {
            Name = $"{fromHazard} to {toHazard}",
            SpecifiedHazard = fromHazard,
            HazardUnit = fromUnit,
            TransformedHazard = toHazard,
            TransformedHazardUnit = toUnit,
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(50d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a labeled tabular response on the default table.</summary>
    private static TabularResponse Response(string hazard, string unit)
    {
        return new TabularResponse { Name = "Fragility", SpecifiedHazard = hazard, HazardUnit = unit };
    }

    /// <summary>Builds a labeled tabular consequence on the default table.</summary>
    private static TabularConsequence Consequence(string hazard, string unit, string type = "Damages", string typeUnit = "$")
    {
        return new TabularConsequence
        {
            Name = $"{type} curve",
            SpecifiedHazard = hazard,
            HazardUnit = unit,
            SpecifiedConsequence = type,
            ConsequenceUnit = typeUnit,
        };
    }

    /// <summary>Builds the standard single-response chain: Flow→Stage rating, stage response, stage consequence.</summary>
    private static FailureMode ChainMode()
    {
        return new FailureMode(
            new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") },
            new List<ITransformFunction>(),
            Response("Stage", "ft"),
            Consequence("Stage", "ft"));
    }

    /// <summary>Verifies the v1.0 default construction state.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var fm = new FailureMode();

        // Assert
        Assert.AreEqual(1, fm.ResponseStages.Count);
        Assert.IsInstanceOfType(fm.ResponseFunction, typeof(NonFailResponse));
        Assert.IsTrue(fm.IsNonFailureMode);
        Assert.AreEqual(0, fm.HazardToResponse.Count);
        Assert.AreEqual(0, fm.ResponseToConsequence.Count);
        Assert.AreEqual(0, fm.ConsequenceFunctions.Count);
        Assert.IsNull(fm.ConsequenceFunction);
        Assert.AreEqual(HazardDimension.Primary, fm.HazardBinding);
        Assert.AreEqual(HazardDimension.Primary, fm.ConsequenceHazardDimension);
        Assert.IsNull(fm.ConsequenceHazardPosition);
        Assert.AreEqual(0, fm.ResolvedConsequenceHazardPosition);
        Assert.IsFalse(fm.MultipleConsequences);
        Assert.IsTrue(fm.IsDeterministic);
    }

    /// <summary>Verifies the v1.0 constructor maps onto the staged shape.</summary>
    [TestMethod]
    public void Test_V10Constructor_MapsToStagedShape()
    {
        // Arrange
        var transform = Rating("Flow", "cfs", "Stage", "ft");
        var response = Response("Stage", "ft");
        var consequence = Consequence("Stage", "ft");

        // Act
        var fm = new FailureMode(new List<ITransformFunction> { transform }, new List<ITransformFunction>(), response, consequence);

        // Assert — one stage; entries are the same instances; views reflect them.
        Assert.AreEqual(1, fm.ResponseStages.Count);
        Assert.AreSame(transform, fm.ResponseStages[0].Transforms[0]);
        Assert.AreSame(response, fm.ResponseStages[0].Response);
        Assert.AreSame(transform, fm.HazardToResponse[0]);
        Assert.AreSame(response, fm.ResponseFunction);
        Assert.AreSame(consequence, fm.ConsequenceFunction);
        Assert.AreEqual(1, fm.ConsequenceFunctions.Count);
        Assert.IsFalse(fm.IsNonFailureMode);
    }

    /// <summary>Verifies the v1.0 constructor coerces null arguments (v1.0 accepted nulls).</summary>
    [TestMethod]
    public void Test_V10Constructor_NullArguments_Coerce()
    {
        // Act
        var fm = new FailureMode(null, null, null, null);

        // Assert
        Assert.AreEqual(0, fm.HazardToResponse.Count);
        Assert.AreEqual(0, fm.ResponseToConsequence.Count);
        Assert.IsInstanceOfType(fm.ResponseFunction, typeof(NonFailResponse));
        Assert.AreEqual(0, fm.ConsequenceFunctions.Count);
        Assert.IsTrue(fm.IsNonFailureMode);
    }

    /// <summary>Verifies the generalized constructor: chains, coercions, and the null throw.</summary>
    [TestMethod]
    public void Test_GeneralizedConstructor_StagedChain()
    {
        // Arrange — a two-response chain: Flow→Stage response, then Stage→Depth response.
        var stages = new List<ResponseStage>
        {
            new ResponseStage(new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") }, Response("Stage", "ft")),
            new ResponseStage(new List<ITransformFunction> { Rating("Stage", "ft", "Depth", "ft") }, Response("Depth", "ft")),
        };

        // Act
        var fm = new FailureMode(stages, new List<ITransformFunction>(),
            new List<IConsequenceFunction> { Consequence("Depth", "ft"), Consequence("Depth", "ft", "Life Loss", "lives") });

        // Assert
        Assert.AreEqual(2, fm.ResponseStages.Count);
        Assert.AreEqual(2, fm.TotalStageTransformCount);
        Assert.AreEqual(2, fm.ConsequenceFunctions.Count);
        Assert.IsFalse(fm.IsNonFailureMode);

        // An empty stage list coerces to one default stage; a null list throws.
        Assert.AreEqual(1, new FailureMode(new List<ResponseStage>(), null, null).ResponseStages.Count);
        Assert.ThrowsException<ArgumentNullException>(() => new FailureMode((IList<ResponseStage>)null!, null, null));
    }

    /// <summary>Verifies the v1.0 views write through to stage 0 and self-heal an emptied list.</summary>
    [TestMethod]
    public void Test_CompatViews_ReflectStageZero()
    {
        // Arrange
        var fm = new FailureMode();
        var response = Response("Stage", "ft");

        // Act — view writes land on stage 0.
        fm.ResponseFunction = response;
        fm.HazardToResponse = new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") };

        // Assert
        Assert.AreSame(response, fm.ResponseStages[0].Response);
        Assert.AreEqual(1, fm.ResponseStages[0].Transforms.Count);

        // Null response assignment coerces to the sentinel (v1.0 defaulting).
        fm.ResponseFunction = null!;
        Assert.IsInstanceOfType(fm.ResponseFunction, typeof(NonFailResponse));

        // Emptying the exposed stage list is self-healed by the views.
        fm.ResponseStages.Clear();
        Assert.IsInstanceOfType(fm.ResponseFunction, typeof(NonFailResponse));
        Assert.AreEqual(1, fm.ResponseStages.Count);
    }

    /// <summary>Verifies the single-slot consequence view over the ordered list.</summary>
    [TestMethod]
    public void Test_ConsequenceFunction_View()
    {
        // Arrange
        var fm = new FailureMode();
        var econ = Consequence("Stage", "ft");
        var life = Consequence("Stage", "ft", "Life Loss", "lives");

        // Act / Assert — set on empty creates index 0.
        fm.ConsequenceFunction = econ;
        Assert.AreEqual(1, fm.ConsequenceFunctions.Count);
        Assert.AreSame(econ, fm.ConsequenceFunction);

        // A second entry keeps index 0 as the view target.
        fm.ConsequenceFunctions.Add(life);
        Assert.AreSame(econ, fm.ConsequenceFunction);

        // Replacing via the view swaps index 0 and keeps the count.
        var revised = Consequence("Stage", "ft");
        fm.ConsequenceFunction = revised;
        Assert.AreEqual(2, fm.ConsequenceFunctions.Count);
        Assert.AreSame(revised, fm.ConsequenceFunctions[0]);

        // Null removes index 0 (v1.0 single-slot semantics).
        fm.ConsequenceFunction = null;
        Assert.AreEqual(1, fm.ConsequenceFunctions.Count);
        Assert.AreSame(life, fm.ConsequenceFunction);
    }

    /// <summary>Verifies non-failure identification is by type on the sole stage.</summary>
    [TestMethod]
    public void Test_IsNonFailureMode_ByTypeTest()
    {
        // A fresh sentinel instance still identifies (no singleton in v1.1).
        var fm = new FailureMode();
        fm.ResponseFunction = new NonFailResponse();
        Assert.IsTrue(fm.IsNonFailureMode);

        // A real response is a failure mode.
        fm.ResponseFunction = Response("Stage", "ft");
        Assert.IsFalse(fm.IsNonFailureMode);

        // A multi-stage chain is never the non-failure mode.
        fm.ResponseStages.Add(new ResponseStage(new List<ITransformFunction>(), new NonFailResponse()));
        Assert.IsFalse(fm.IsNonFailureMode);
    }

    /// <summary>Verifies the binding position resolves to the last response's input by default.</summary>
    [TestMethod]
    public void Test_ResolvedConsequenceHazardPosition_Defaults()
    {
        // Arrange — two stage transforms across two stages.
        var fm = new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") }, Response("Stage", "ft")),
                new ResponseStage(new List<ITransformFunction> { Rating("Stage", "ft", "Depth", "ft") }, Response("Depth", "ft")),
            },
            null,
            new List<IConsequenceFunction> { Consequence("Depth", "ft") });

        // Assert — the default is the total stage transform count; explicit values pin.
        Assert.AreEqual(2, fm.ResolvedConsequenceHazardPosition);
        fm.ConsequenceHazardPosition = 0;
        Assert.AreEqual(0, fm.ResolvedConsequenceHazardPosition);
    }

    /// <summary>Verifies determinism aggregates over stages, trailing transforms, and consequences.</summary>
    [TestMethod]
    public void Test_IsDeterministic_AggregatesMembers()
    {
        // Deterministic chain.
        Assert.IsTrue(ChainMode().IsDeterministic);

        // An uncertain response makes the mode uncertain.
        var uncertainResponse = ChainMode();
        uncertainResponse.ResponseFunction = new ParametricResponse();
        Assert.IsFalse(uncertainResponse.IsDeterministic);

        // An uncertain consequence makes the mode uncertain.
        var uncertainConsequence = ChainMode();
        var consequence = Consequence("Stage", "ft");
        consequence.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Normal(0d, 1d)), new UncertainOrdinate(100d, new Normal(10d, 1d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal);
        uncertainConsequence.ConsequenceFunction = consequence;
        Assert.IsFalse(uncertainConsequence.IsDeterministic);
    }

    /// <summary>Verifies the invalidating-error matrix.</summary>
    [TestMethod]
    public void Test_Validate_ErrorMatrix()
    {
        // A labeled chain is valid.
        Assert.IsTrue(ChainMode().Validate().IsValid);

        // No consequence functions.
        var noConsequence = ChainMode();
        noConsequence.ConsequenceFunctions.Clear();
        Assert.IsFalse(noConsequence.Validate().IsValid);

        // Null entries: consequence, trailing transform, stage.
        var nullConsequence = ChainMode();
        nullConsequence.ConsequenceFunctions.Add(null!);
        Assert.IsFalse(nullConsequence.Validate().IsValid);

        var nullTrailing = ChainMode();
        nullTrailing.ResponseToConsequence.Add(null!);
        Assert.IsFalse(nullTrailing.Validate().IsValid);

        var nullStage = ChainMode();
        nullStage.ResponseStages.Add(null!);
        Assert.IsFalse(nullStage.Validate().IsValid);

        // Out-of-range explicit binding position.
        var badPosition = ChainMode();
        badPosition.ConsequenceHazardPosition = 5;
        Assert.IsFalse(badPosition.Validate().IsValid);
        badPosition.ConsequenceHazardPosition = -1;
        Assert.IsFalse(badPosition.Validate().IsValid);

        // The sentinel inside a multi-stage chain.
        var sentinelInChain = ChainMode();
        sentinelInChain.ResponseStages.Add(new ResponseStage(new List<ITransformFunction>(), new NonFailResponse()));
        Assert.IsFalse(sentinelInChain.Validate().IsValid);

        // Secondary dimensions require a bivariate hazard (a future capability) and error today.
        var secondaryBinding = ChainMode();
        secondaryBinding.HazardBinding = HazardDimension.Secondary;
        Assert.IsFalse(secondaryBinding.Validate().IsValid);
        var secondaryDimension = ChainMode();
        secondaryDimension.ConsequenceHazardDimension = HazardDimension.Secondary;
        Assert.IsFalse(secondaryDimension.Validate().IsValid);

        // An emptied stage list (views untouched) is reported.
        var emptied = ChainMode();
        emptied.ResponseStages.Clear();
        var (isValid, messages) = emptied.Validate();
        Assert.IsFalse(isValid);
        Assert.IsTrue(messages.Any(m => m.Contains("at least one response stage")));
    }

    /// <summary>Verifies label mismatches are advisory warnings, not errors (v1.1 divergence).</summary>
    [TestMethod]
    public void Test_Validate_LabelMismatch_IsWarning()
    {
        // Arrange — the response declares 'Depth' but the rating produces 'Stage'.
        var fm = new FailureMode(
            new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") },
            new List<ITransformFunction>(),
            Response("Depth", "ft"),
            Consequence("Stage", "ft"));

        // Act
        var (isValid, messages) = fm.Validate();

        // Assert — valid, with a hazard-type warning naming the response.
        Assert.IsTrue(isValid);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("'Depth'")));
    }

    /// <summary>Verifies continuity is binding-aware: the bound position selects the compared signal.</summary>
    [TestMethod]
    public void Test_Validate_BindingAwareContinuity()
    {
        // Arrange — two stage transforms: Flow→Stage, Stage→Depth; consequence declares 'Stage'.
        FailureMode Build() => new FailureMode(
            new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft"), Rating("Stage", "ft", "Depth", "ft") },
            new List<ITransformFunction>(),
            Response("Depth", "ft"),
            Consequence("Stage", "ft"));

        // Default binding (position 2 = 'Depth') mismatches the consequence's 'Stage'.
        var defaulted = Build();
        Assert.IsTrue(defaulted.Validate().ValidationMessages.Any(
            m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("consequence function")));

        // Binding position 1 selects the 'Stage' signal — no consequence warning.
        var bound = Build();
        bound.ConsequenceHazardPosition = 1;
        Assert.IsFalse(bound.Validate().ValidationMessages.Any(
            m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("consequence function")));
    }

    /// <summary>Verifies the XElement round-trip restores every serialized member.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange — a full-feature mode: two stages, a trailing transform, two consequences,
        // non-default binding attributes.
        var original = new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction> { Rating("Flow", "cfs", "Stage", "ft") }, Response("Stage", "ft")),
                new ResponseStage(new List<ITransformFunction> { Rating("Stage", "ft", "Depth", "ft") }, Response("Depth", "ft")),
            },
            new List<ITransformFunction> { Rating("Depth", "ft", "Damage Depth", "ft") },
            new List<IConsequenceFunction> { Consequence("Damage Depth", "ft"), Consequence("Damage Depth", "ft", "Life Loss", "lives") })
        {
            ConsequenceHazardPosition = 1,
            MultipleConsequences = true,
        };

        // Act
        var restored = new FailureMode(original.ToXElement());

        // Assert
        Assert.AreEqual(2, restored.ResponseStages.Count);
        Assert.AreEqual(1, restored.ResponseToConsequence.Count);
        Assert.AreEqual(2, restored.ConsequenceFunctions.Count);
        Assert.AreEqual(HazardDimension.Primary, restored.HazardBinding);
        Assert.AreEqual(1, restored.ConsequenceHazardPosition);
        Assert.IsTrue(restored.MultipleConsequences);
        Assert.AreEqual(original.ToXElement().ToString(), restored.ToXElement().ToString(),
            "Round-trip must reproduce the serialized form exactly.");
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash(),
            "Round-trip must preserve the canonical hash.");
    }

    /// <summary>Verifies the binding position serializes resolved, and default equals explicit.</summary>
    [TestMethod]
    public void Test_Serialization_ResolvesPositionOnWrite()
    {
        // Arrange — the default (null) position on a one-transform chain resolves to 1.
        var defaulted = ChainMode();
        var explicitEqual = ChainMode();
        explicitEqual.ConsequenceHazardPosition = 1;

        // Act / Assert — the written attribute is the resolved value.
        Assert.AreEqual("1", defaulted.ToXElement().Attribute(nameof(FailureMode.ConsequenceHazardPosition))!.Value);

        // A defaulted mode and an explicitly-equal mode hash identically.
        CollectionAssert.AreEqual(explicitEqual.CanonicalHash(), defaulted.CanonicalHash());

        // A restored mode pins the position explicitly.
        var restored = new FailureMode(defaulted.ToXElement());
        Assert.AreEqual(1, restored.ConsequenceHazardPosition);
    }

    /// <summary>Verifies unreconstructable children throw instead of silently degrading the chain.</summary>
    [TestMethod]
    public void Test_Serialization_UnknownChildren_Throw()
    {
        // Arrange
        var badTrailing = new XElement(nameof(FailureMode),
            new XElement(nameof(FailureMode.ResponseToConsequence), new XElement("BogusTransform")));
        var badConsequence = new XElement(nameof(FailureMode),
            new XElement(nameof(FailureMode.ConsequenceFunctions), new XElement("BogusConsequence")));

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => new FailureMode(badTrailing));
        Assert.ThrowsException<InvalidOperationException>(() => new FailureMode(badConsequence));
        Assert.ThrowsException<ArgumentNullException>(() => new FailureMode((XElement)null!));
    }

    /// <summary>Verifies function metadata edits and stripped attributes never move the hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_MetadataInert()
    {
        // Arrange
        var fm = ChainMode();
        byte[] baseline = fm.CanonicalHash();

        // Act — rename and relabel the owned functions.
        fm.HazardToResponse[0].Name = "Renamed rating";
        fm.ResponseFunction.Name = "Renamed response";
        fm.ResponseFunction.SpecifiedHazard = "Different label";
        fm.ConsequenceFunction!.Description = "Edited description";

        // Assert
        CollectionAssert.AreEqual(baseline, fm.CanonicalHash(),
            "Owned-function metadata edits must not change the failure-mode hash.");
        HashInvariance.AssertStrippedAttributesInert(fm.ToXElement());
    }

    /// <summary>Verifies every compute-relevant edit moves the hash.</summary>
    [TestMethod]
    public void Test_CanonicalHash_ComputeSensitive()
    {
        // Each new serialized attribute, plus structural edits.
        var fm = ChainMode();
        HashInvariance.AssertComputeSensitive(fm.CanonicalHash, () => fm.HazardBinding = HazardDimension.Secondary);
        var fm2 = ChainMode();
        HashInvariance.AssertComputeSensitive(fm2.CanonicalHash, () => fm2.ConsequenceHazardDimension = HazardDimension.Secondary);
        var fm3 = ChainMode();
        HashInvariance.AssertComputeSensitive(fm3.CanonicalHash, () => fm3.ConsequenceHazardPosition = 0);
        var fm4 = ChainMode();
        HashInvariance.AssertComputeSensitive(fm4.CanonicalHash, () => fm4.MultipleConsequences = true);
        var fm5 = ChainMode();
        HashInvariance.AssertComputeSensitive(fm5.CanonicalHash,
            () => fm5.ResponseStages.Add(new ResponseStage(new List<ITransformFunction>(), Response("Stage", "ft"))));
        var fm6 = ChainMode();
        HashInvariance.AssertComputeSensitive(fm6.CanonicalHash,
            () => fm6.ConsequenceFunctions.Add(Consequence("Stage", "ft", "Life Loss", "lives")));

        // Flipping a stage's branch polarity swaps p(h) for 1 − p(h) — a compute edit.
        var fm8 = ChainMode();
        HashInvariance.AssertComputeSensitive(fm8.CanonicalHash,
            () => fm8.ResponseStages[0].BranchPolarity = BranchPolarity.NonFail);

        // Reordering the consequence list is a compute edit (positional pairing) — provided the
        // entries differ in compute content. Two consequences differing only in their labels
        // (stripped metadata) are hash-fungible by design.
        var fm7 = ChainMode();
        var lifeLoss = Consequence("Stage", "ft", "Life Loss", "lives");
        lifeLoss.UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(100d, new Deterministic(25d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
        fm7.ConsequenceFunctions.Add(lifeLoss);
        HashInvariance.AssertComputeSensitive(fm7.CanonicalHash, () => fm7.ConsequenceFunctions.Reverse());
    }

    /// <summary>Verifies identical configurations hash identically and hashing is deterministic.</summary>
    [TestMethod]
    public void Test_CanonicalHash_Deterministic()
    {
        // Arrange
        var a = ChainMode();
        var b = ChainMode();

        // Assert
        CollectionAssert.AreEqual(a.CanonicalHash(), a.CanonicalHash());
        CollectionAssert.AreEqual(a.CanonicalHash(), b.CanonicalHash(),
            "Two identically configured failure modes must hash identically.");
    }

    /// <summary>Verifies the clone is deep: shared content, independent instances.</summary>
    [TestMethod]
    public void Test_Clone_DeepCopies()
    {
        // Arrange
        var original = ChainMode();

        // Act
        var clone = original.Clone();

        // Assert — equal content, distinct function instances.
        CollectionAssert.AreEqual(original.CanonicalHash(), clone.CanonicalHash());
        Assert.AreNotSame(original.HazardToResponse[0], clone.HazardToResponse[0]);
        Assert.AreNotSame(original.ResponseFunction, clone.ResponseFunction);

        // Mutating the clone's transform table leaves the original untouched.
        byte[] baseline = original.CanonicalHash();
        ((TabularTransform)clone.HazardToResponse[0]).UncertainOrderedPairedData = new UncertainOrderedPairedData(
            new[] { new UncertainOrdinate(0d, new Deterministic(1d)), new UncertainOrdinate(100d, new Deterministic(99d)) },
            true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic);
        CollectionAssert.AreEqual(baseline, original.CanonicalHash());
        CollectionAssert.AreNotEqual(baseline, clone.CanonicalHash());
    }

    /// <summary>Verifies property change notification on the setters and views.</summary>
    [TestMethod]
    public void Test_PropertyChanged_Raised()
    {
        // Arrange
        var fm = new FailureMode();
        var raised = new List<string>();
        fm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // Act
        fm.HazardBinding = HazardDimension.Secondary;
        fm.ConsequenceHazardDimension = HazardDimension.Secondary;
        fm.ConsequenceHazardPosition = 3;
        fm.MultipleConsequences = true;
        fm.ResponseFunction = Response("Stage", "ft");
        fm.ConsequenceFunction = Consequence("Stage", "ft");
        fm.ResponseToConsequence = new List<ITransformFunction>();
        fm.ConsequenceFunctions = new List<IConsequenceFunction>();

        // Assert
        CollectionAssert.AreEqual(new[]
        {
            nameof(FailureMode.HazardBinding),
            nameof(FailureMode.ConsequenceHazardDimension),
            nameof(FailureMode.ConsequenceHazardPosition),
            nameof(FailureMode.MultipleConsequences),
            nameof(FailureMode.ResponseFunction),
            nameof(FailureMode.ConsequenceFunction),
            nameof(FailureMode.ResponseToConsequence),
            nameof(FailureMode.ConsequenceFunctions),
        }, raised);
    }

    /// <summary>Verifies the serialized attribute set is exactly the pinned contract.</summary>
    [TestMethod]
    public void Test_Serialization_AttributeContract()
    {
        // Arrange
        var element = ChainMode().ToXElement();

        // Assert — element name, attribute names, and child order are append-only contract.
        Assert.AreEqual(nameof(FailureMode), element.Name.LocalName);
        CollectionAssert.AreEqual(new[]
        {
            nameof(FailureMode.HazardBinding),
            nameof(FailureMode.ConsequenceHazardDimension),
            nameof(FailureMode.ConsequenceHazardPosition),
            nameof(FailureMode.MultipleConsequences),
        }, element.Attributes().Select(a => a.Name.LocalName).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            nameof(FailureMode.ResponseStages),
            nameof(FailureMode.ResponseToConsequence),
            nameof(FailureMode.ConsequenceFunctions),
        }, element.Elements().Select(e => e.Name.LocalName).ToArray());
        Assert.AreEqual(bool.FalseString.ToLower(CultureInfo.InvariantCulture),
            element.Attribute(nameof(FailureMode.MultipleConsequences))!.Value);
    }

    /// <summary>Builds a labeled n-branch mixture of deterministic children with equal weights.</summary>
    private static CompositeConsequence GuardrailMixture(string name, int branches)
    {
        var mixture = new CompositeConsequence
        {
            Name = name,
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
        };
        for (int i = 0; i < branches; i++)
        {
            var child = new TabularConsequence
            {
                Name = $"{name} Branch {i}",
                SpecifiedHazard = "Stage",
                HazardUnit = "ft",
                SpecifiedConsequence = "Life Loss",
                ConsequenceUnit = "lives",
                UncertainOrderedPairedData = new UncertainOrderedPairedData(
                    new[] { new UncertainOrdinate(0d, new Deterministic(0d)), new UncertainOrdinate(30d, new Deterministic(300d)) },
                    true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
            };
            mixture.ConsequenceFunctions.Add(new WeightedConsequenceFunction(child, 1d / branches));
        }
        return mixture;
    }

    /// <summary>
    /// Verifies the mode-level branch guardrails under per-type marginal compute
    /// (the docs/requirements/MODEL_LIBRARY_ARCHITECTURE.md §6.4.1 erratum): the SUM of
    /// exposure branches across the mode's consequence positions
    /// warns above 64 and errors above 1024 — consequence types never cross, so two 33-branch
    /// mixtures are 66 branches of per-evaluation work (a warning), not the earlier product
    /// bound's 1089 (which modeled a cross-type coupling the engine never performs).
    /// </summary>
    [TestMethod]
    public void Test_Validate_ExposureBranchGuardrails()
    {
        // Arrange — a data-bearing fragility so the rest of the mode validates.
        var fragility = new TabularResponse
        {
            Name = "Fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(10d, new Deterministic(0d)), new UncertainOrdinate(20d, new Deterministic(1d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };

        // Act / Assert — a 65-branch mixture on one position: warning only.
        var warningMode = new FailureMode(null, null, fragility, GuardrailMixture("Big Mixture", 65));
        var (warnValid, warnMessages) = warningMode.Validate();
        Assert.IsTrue(warnValid, string.Join("; ", warnMessages));
        Assert.IsTrue(warnMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("exposure branches (65)")),
            string.Join("; ", warnMessages));

        // Two positions of 33 branches sum to 66 — a warning, not an error (the Σ pin).
        var sumMode = new FailureMode(null, null, fragility, GuardrailMixture("Mix One", 33));
        sumMode.ConsequenceFunctions.Add(GuardrailMixture("Mix Two", 33));
        var (sumValid, sumMessages) = sumMode.Validate();
        Assert.IsTrue(sumValid, string.Join("; ", sumMessages));
        Assert.IsTrue(sumMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal) && m.Contains("exposure branches (66)")),
            string.Join("; ", sumMessages));

        // Two positions of 513 branches sum to 1026 — crosses the error threshold.
        var errorMode = new FailureMode(null, null, fragility, GuardrailMixture("Mix Big One", 513));
        errorMode.ConsequenceFunctions.Add(GuardrailMixture("Mix Big Two", 513));
        var (errorValid, errorMessages) = errorMode.Validate();
        Assert.IsFalse(errorValid);
        Assert.IsTrue(errorMessages.Any(m => m.StartsWith("Error:", StringComparison.Ordinal) && m.Contains("1024")),
            string.Join("; ", errorMessages));
    }

    /// <summary>
    /// Verifies the mode-aware validation overload: a consequence-free mode fails
    /// risk-mode validation with the pinned message, passes reliability-mode validation, and a
    /// null consequence slot stays an error in both modes (a wiring defect, not a relaxation).
    /// </summary>
    [TestMethod]
    public void Test_Validate_ReliabilityMode_RelaxesConsequenceRequirement()
    {
        // Arrange — a valid fragility, no consequence.
        var fragility = new TabularResponse
        {
            Name = "Fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[] { new UncertainOrdinate(10d, new Deterministic(0d)), new UncertainOrdinate(20d, new Deterministic(1d)) },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
        };
        var mode = new FailureMode(null, null, fragility, null);

        // Act / Assert — risk mode rejects; the parameterless overload is the risk-mode contract.
        var (riskValid, riskMessages) = mode.Validate();
        Assert.IsFalse(riskValid);
        Assert.IsTrue(riskMessages.Any(m => m.Contains("at least one consequence function")), string.Join("; ", riskMessages));
        CollectionAssert.AreEqual(riskMessages, mode.Validate(RiskAnalysisMode.Risk).ValidationMessages);

        // Reliability mode accepts the consequence-free mode.
        var (reliabilityValid, reliabilityMessages) = mode.Validate(RiskAnalysisMode.Reliability);
        Assert.IsTrue(reliabilityValid, string.Join("; ", reliabilityMessages));

        // A null slot is a wiring defect in both modes.
        var nullSlot = new FailureMode(null, null, fragility, null);
        nullSlot.ConsequenceFunctions.Add(null!);
        Assert.IsFalse(nullSlot.Validate(RiskAnalysisMode.Reliability).IsValid);
    }

    #region Bivariate gate matrix

    /// <summary>Builds a valid labeled bivariate hazard for parenting joint-mode fixtures.</summary>
    private static BivariateHazard JointHazard()
    {
        return new BivariateHazard(
            new TabularHazard { Name = "Surge Frequency", SpecifiedHazard = "Surge", HazardUnit = "ft" },
            new TabularHazard { Name = "Pool Frequency", SpecifiedHazard = "Pool Elevation", HazardUnit = "ft" })
        {
            Name = "Joint Hazard",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
        };
    }

    /// <summary>Builds a component whose hazard is bivariate, for parenting standalone modes.</summary>
    private static SystemComponent JointParent()
    {
        return new SystemComponent(JointHazard());
    }

    /// <summary>Builds a valid labeled bivariate response on the default 2×2 grid.</summary>
    private static BivariateResponse JointResponse()
    {
        return new BivariateResponse
        {
            Name = "Joint Fragility",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
        };
    }

    /// <summary>Builds a valid labeled bivariate transform.</summary>
    private static BivariateTransform JointTransform()
    {
        return new BivariateTransform
        {
            Name = "Surge-Pool Stage",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
            TransformedHazard = "Stage",
            TransformedHazardUnit = "ft",
            X1Values = new[] { 0d, 10d, 20d },
            X2Values = new[] { 100d, 200d },
            ZValues = new[,] { { 1d, 2d }, { 3d, 5d }, { 4d, 8d } },
        };
    }

    /// <summary>Builds a valid labeled bivariate consequence surface.</summary>
    private static BivariateConsequence JointConsequence()
    {
        return new BivariateConsequence
        {
            Name = "Surge-Pool Damages",
            SpecifiedHazard = "Surge",
            HazardUnit = "ft",
            SecondarySpecifiedHazard = "Pool Elevation",
            SecondaryHazardUnit = "ft",
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
            X1Values = new[] { 0d, 10d, 20d },
            X2Values = new[] { 100d, 200d },
            ZValues = new[,] { { 1d, 2d }, { 3d, 5d }, { 4d, 8d } },
        };
    }

    /// <summary>
    /// Verifies the univariate/unparented gate rows: Secondary bindings, a secondary chain, and
    /// bivariate transform/consequence functions all error without a bivariate component hazard.
    /// </summary>
    [TestMethod]
    public void Test_Validate_UnivariateParent_BivariateShapesError()
    {
        // Secondary hazard binding.
        var secondaryBound = ChainMode();
        secondaryBound.HazardBinding = HazardDimension.Secondary;
        Assert.IsTrue(secondaryBound.Validate().ValidationMessages
            .Any(m => m.Contains("hazard binding is Secondary")));

        // Secondary consequence dimension (with a stamped in-range position, so only the
        // dimension rule fires).
        var secondaryDimension = ChainMode();
        secondaryDimension.ConsequenceHazardDimension = HazardDimension.Secondary;
        secondaryDimension.ConsequenceHazardPosition = 0;
        Assert.IsTrue(secondaryDimension.Validate().ValidationMessages
            .Any(m => m.Contains("consequence hazard dimension is Secondary")));

        // A non-empty secondary chain.
        var chained = ChainMode();
        chained.SecondaryHazardToResponse.Add(Rating("Pool Elevation", "ft", "Pool Stage", "ft"));
        Assert.IsTrue(chained.Validate().ValidationMessages
            .Any(m => m.Contains("carries a secondary-hazard chain")));

        // Bivariate functions in the mode: a stage transform, a trailing transform, and a
        // consequence each error under a univariate hazard.
        var stageTransform = ChainMode();
        stageTransform.HazardToResponse.Add(JointTransform());
        Assert.IsTrue(stageTransform.Validate().ValidationMessages
            .Any(m => m.Contains("stage transform") && m.Contains("is bivariate")));

        var trailingTransform = ChainMode();
        trailingTransform.ResponseToConsequence.Add(JointTransform());
        Assert.IsTrue(trailingTransform.Validate().ValidationMessages
            .Any(m => m.Contains("response-to-consequence transform") && m.Contains("is bivariate")));

        var consequence = ChainMode();
        consequence.ConsequenceFunctions.Add(JointConsequence());
        Assert.IsTrue(consequence.Validate().ValidationMessages
            .Any(m => m.Contains("consequence function") && m.Contains("is bivariate")));
    }

    /// <summary>
    /// Verifies the collapse-mode row: a bivariate RESPONSE under a univariate (or absent)
    /// component hazard is legal — standalone, and as a stage of a multi-stage cascade.
    /// </summary>
    [TestMethod]
    public void Test_Validate_UnivariateParent_BivariateResponseLegal()
    {
        // A standalone single-stage mode wrapping the bivariate response.
        var collapse = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            JointResponse(),
            Consequence("Surge", "ft"));
        var (collapseValid, collapseMessages) = collapse.Validate();
        Assert.IsTrue(collapseValid, string.Join("; ", collapseMessages));

        // A cascade: a univariate initiation stage followed by the collapse-mode response.
        var cascade = new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction>(), Response("Surge", "ft")),
                new ResponseStage(new List<ITransformFunction>(), JointResponse()),
            },
            new List<ITransformFunction>(),
            new List<IConsequenceFunction> { Consequence("Surge", "ft") });
        var (cascadeValid, cascadeMessages) = cascade.Validate();
        Assert.IsTrue(cascadeValid, string.Join("; ", cascadeMessages));
    }

    /// <summary>
    /// Verifies the bivariate-parent rows for univariate responses: both hazard bindings are
    /// legal, and the Secondary consequence dimension requires a stamped position inside
    /// [0, secondary-chain length].
    /// </summary>
    [TestMethod]
    public void Test_Validate_BivariateParent_BindingsAndPositions()
    {
        var parent = JointParent();

        // A Secondary-bound univariate mode is legal.
        var secondaryBound = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            Response("Pool Elevation", "ft"),
            Consequence("Pool Elevation", "ft"))
        {
            Parent = parent,
            HazardBinding = HazardDimension.Secondary,
        };
        var (boundValid, boundMessages) = secondaryBound.Validate();
        Assert.IsTrue(boundValid, string.Join("; ", boundMessages));

        // The Secondary consequence dimension: position 0 (the raw secondary signal) and the
        // chain length are in range; beyond the chain length errors; a missing position errors.
        var mode = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            Response("Surge", "ft"),
            Consequence("Pool Stage", "ft"))
        {
            Parent = parent,
            ConsequenceHazardDimension = HazardDimension.Secondary,
        };
        mode.SecondaryHazardToResponse.Add(Rating("Pool Elevation", "ft", "Pool Stage", "ft"));

        mode.ConsequenceHazardPosition = 0;
        Assert.IsTrue(mode.Validate().IsValid, string.Join("; ", mode.Validate().ValidationMessages));
        mode.ConsequenceHazardPosition = 1;
        Assert.IsTrue(mode.Validate().IsValid);

        mode.ConsequenceHazardPosition = 2;
        Assert.IsTrue(mode.Validate().ValidationMessages
            .Any(m => m.Contains("secondary-hazard chain length (1)")));

        mode.ConsequenceHazardPosition = null;
        Assert.IsTrue(mode.Validate().ValidationMessages
            .Any(m => m.Contains("requires an explicit consequence hazard position")));
    }

    /// <summary>
    /// Verifies the joint-mode response rows under a bivariate parent: single-stage only, and
    /// the surface needs at least two secondary levels for an interpolable axis.
    /// </summary>
    [TestMethod]
    public void Test_Validate_BivariateParent_JointResponseRules()
    {
        var parent = JointParent();

        // A single-stage joint mode with the default two-level surface is legal.
        var joint = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            JointResponse(),
            Consequence("Surge", "ft"))
        {
            Parent = parent,
        };
        var (jointValid, jointMessages) = joint.Validate();
        Assert.IsTrue(jointValid, string.Join("; ", jointMessages));

        // Multi-stage through the bivariate response errors.
        var multiStage = new FailureMode(
            new List<ResponseStage>
            {
                new ResponseStage(new List<ITransformFunction>(), Response("Surge", "ft")),
                new ResponseStage(new List<ITransformFunction>(), JointResponse()),
            },
            new List<ITransformFunction>(),
            new List<IConsequenceFunction> { Consequence("Surge", "ft") })
        {
            Parent = parent,
        };
        Assert.IsTrue(multiStage.Validate().ValidationMessages
            .Any(m => m.Contains("single-stage modes only")));

        // A single secondary level cannot interpolate the secondary axis.
        var singleLevel = JointResponse();
        singleLevel.SecondaryHazardLevels.RemoveAt(1);
        var degenerate = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            singleLevel,
            Consequence("Surge", "ft"))
        {
            Parent = parent,
        };
        Assert.IsTrue(degenerate.Validate().ValidationMessages
            .Any(m => m.Contains("secondary hazard level(s)") && m.Contains("at least two")));
    }

    /// <summary>
    /// Verifies the secondary-chain entry rules: entries validate like trailing transforms
    /// (null entries and their own errors), a bivariate transform inside the chain errors, and
    /// trailing transforms alongside a bivariate consequence error.
    /// </summary>
    [TestMethod]
    public void Test_Validate_SecondaryChainEntries()
    {
        var parent = JointParent();

        // A null chain entry.
        var nullEntry = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            Response("Surge", "ft"),
            Consequence("Surge", "ft"))
        {
            Parent = parent,
        };
        nullEntry.SecondaryHazardToResponse.Add(null!);
        Assert.IsTrue(nullEntry.Validate().ValidationMessages
            .Any(m => m.Contains("secondary-hazard transform at index 0 has not been defined")));

        // A bivariate transform inside the chain — the chain is univariate by construction.
        var bivariateEntry = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            Response("Surge", "ft"),
            Consequence("Surge", "ft"))
        {
            Parent = parent,
        };
        bivariateEntry.SecondaryHazardToResponse.Add(JointTransform());
        Assert.IsTrue(bivariateEntry.Validate().ValidationMessages
            .Any(m => m.Contains("secondary-hazard transform at index 0 is bivariate")));

        // An invalid chain entry surfaces its own messages.
        var invalidEntry = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            Response("Surge", "ft"),
            Consequence("Surge", "ft"))
        {
            Parent = parent,
        };
        invalidEntry.SecondaryHazardToResponse.Add(new TabularTransform());
        Assert.IsFalse(invalidEntry.Validate().IsValid);

        // Trailing transforms cannot precede a bivariate consequence.
        var trailingUnderSurface = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction> { Rating("Surge", "ft", "Stage", "ft") },
            Response("Surge", "ft"),
            null)
        {
            Parent = parent,
        };
        trailingUnderSurface.ConsequenceFunctions.Add(JointConsequence());
        Assert.IsTrue(trailingUnderSurface.Validate().ValidationMessages
            .Any(m => m.Contains("trailing transforms cannot precede it")));
    }

    /// <summary>
    /// Verifies the secondary-axis label continuity warnings: the chain walks from the bound
    /// marginal's declared pair, a Secondary-bound mode's incoming labels come from the
    /// secondary pair, and a joint response's secondary labels compare against the chain's end
    /// signal — all advisory.
    /// </summary>
    [TestMethod]
    public void Test_Validate_SecondaryAxisLabelWarnings()
    {
        var parent = JointParent();

        // The chain's first transform declares a different input than the secondary pair.
        var chainMismatch = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            Response("Surge", "ft"),
            Consequence("Surge", "ft"))
        {
            Parent = parent,
        };
        chainMismatch.SecondaryHazardToResponse.Add(Rating("Tailwater", "ft", "Pool Stage", "ft"));
        var (chainValid, chainMessages) = chainMismatch.Validate();
        Assert.IsTrue(chainValid, string.Join("; ", chainMessages));
        Assert.IsTrue(chainMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)
            && m.Contains("'Pool Elevation'") && m.Contains("secondary-hazard transform")));

        // A Secondary-bound mode's stage walks from the secondary pair: a stage response
        // declaring the primary label mismatches.
        var boundMismatch = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            Response("Surge", "ft"),
            Consequence("Pool Elevation", "ft"))
        {
            Parent = parent,
            HazardBinding = HazardDimension.Secondary,
        };
        var (boundValid, boundMessages) = boundMismatch.Validate();
        Assert.IsTrue(boundValid, string.Join("; ", boundMessages));
        Assert.IsTrue(boundMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)
            && m.Contains("'Pool Elevation'") && m.Contains("response function")));

        // A joint response whose secondary labels disagree with the (chainless) secondary pair.
        var jointMismatch = JointResponse();
        jointMismatch.SecondarySpecifiedHazard = "Tailwater";
        var jointMode = new FailureMode(
            new List<ITransformFunction>(),
            new List<ITransformFunction>(),
            jointMismatch,
            Consequence("Surge", "ft"))
        {
            Parent = parent,
        };
        var (jointValid, jointMessages) = jointMode.Validate();
        Assert.IsTrue(jointValid, string.Join("; ", jointMessages));
        Assert.IsTrue(jointMessages.Any(m => m.StartsWith("Warning:", StringComparison.Ordinal)
            && m.Contains("(secondary axis)")));
    }

    /// <summary>
    /// Verifies the secondary chain's serialization contract: ABSENT when empty — with the
    /// explicit-empty assignment byte-identical to the default (the hash-preservation pin) —
    /// present and hash-moving when populated, round-tripping faithfully, and carried by Clone.
    /// </summary>
    [TestMethod]
    public void Test_Serialization_SecondaryChain_ConditionalPresence()
    {
        // Arrange — the established univariate chain shape.
        var mode = ChainMode();
        var baselineXml = mode.ToXElement().ToString();
        byte[] baselineHash = mode.CanonicalHash();
        Assert.IsNull(mode.ToXElement().Element(nameof(FailureMode.SecondaryHazardToResponse)));

        // The explicit-empty assignment is byte-inert.
        mode.SecondaryHazardToResponse = new List<ITransformFunction>();
        Assert.AreEqual(baselineXml, mode.ToXElement().ToString());
        CollectionAssert.AreEqual(baselineHash, mode.CanonicalHash());

        // A populated chain serializes, moves the hash, and round-trips faithfully.
        mode.SecondaryHazardToResponse.Add(Rating("Pool Elevation", "ft", "Pool Stage", "ft"));
        var populatedXml = mode.ToXElement();
        Assert.IsNotNull(populatedXml.Element(nameof(FailureMode.SecondaryHazardToResponse)));
        Assert.IsFalse(mode.CanonicalHash().SequenceEqual(baselineHash),
            "The secondary chain is compute content and must move the canonical hash.");

        var restored = new FailureMode(populatedXml);
        Assert.AreEqual(1, restored.SecondaryHazardToResponse.Count);
        CollectionAssert.AreEqual(mode.CanonicalHash(), restored.CanonicalHash());
        Assert.AreEqual(populatedXml.ToString(), restored.ToXElement().ToString());

        // Clone carries the chain (the serialization round trip is the clone contract).
        var clone = mode.Clone();
        Assert.AreEqual(1, clone.SecondaryHazardToResponse.Count);
        CollectionAssert.AreEqual(mode.CanonicalHash(), clone.CanonicalHash());

        // The null-coercing setter and its change notification.
        var raised = new List<string>();
        clone.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        clone.SecondaryHazardToResponse = null!;
        Assert.AreEqual(0, clone.SecondaryHazardToResponse.Count);
        CollectionAssert.AreEqual(new[] { nameof(FailureMode.SecondaryHazardToResponse) }, raised);
    }

    /// <summary>
    /// Verifies the secondary chain participates in the determinism aggregate: an uncertain
    /// chain transform makes the mode non-deterministic.
    /// </summary>
    [TestMethod]
    public void Test_IsDeterministic_IncludesSecondaryChain()
    {
        // Arrange
        var mode = ChainMode();
        Assert.IsTrue(mode.IsDeterministic);

        // Act
        mode.SecondaryHazardToResponse.Add(new LinearTransform
        {
            Name = "Pool Rating",
            SpecifiedHazard = "Pool Elevation",
            HazardUnit = "ft",
            TransformedHazard = "Pool Stage",
            TransformedHazardUnit = "ft",
            IsUncertain = true,
        });

        // Assert
        Assert.IsFalse(mode.IsDeterministic);
    }

    #endregion
}
