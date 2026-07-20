using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Models.ResponseFunctions;
using RMC.TotalRisk.Models.Support;
using RMC.TotalRisk.Tests.Models.Support;

namespace RMC.TotalRisk.Tests.Models.ResponseFunctions;

/// <summary>
/// Unit tests for <see cref="NonFailResponse"/> — the inert non-failure sentinel: v1.0 member
/// behavior, instantiable (no singleton), type-test identification, and content-free hashing.
/// </summary>
[TestClass]
public class NonFailResponseTests
{
    /// <summary>Verifies the v1.0 display identity and inert flags.</summary>
    [TestMethod]
    public void Test_Defaults_MatchV10()
    {
        // Act
        var nf = new NonFailResponse();

        // Assert
        Assert.AreEqual("< Non-Fail >", nf.Name);
        Assert.IsTrue(nf.IsDeterministic);
        Assert.IsTrue(nf.IsMonotonic());
        Assert.AreEqual(0, nf.SamplingDimensions);
        Assert.IsTrue(nf.Validate().IsValid);
    }

    /// <summary>Verifies the inert member behavior: curve sampling throws, distribution sampling is null, bounds are zero.</summary>
    [TestMethod]
    public void Test_Members_InertBehavior()
    {
        // Arrange
        var nf = new NonFailResponse();

        // Act / Assert — exact v1.0 behavior.
        Assert.ThrowsException<NotImplementedException>(() => nf.SampleResponseFunction());
        Assert.ThrowsException<NotImplementedException>(() => nf.SampleResponseFunction(0.5d));
        Assert.ThrowsException<NotImplementedException>(() => nf.SampleResponseFunction(0));
        Assert.IsNull(nf.SampleFunction());
        Assert.IsNull(nf.SampleFunction(0.5d));
        Assert.IsNull(nf.SampleFunction(0));
        Assert.AreEqual(0d, nf.MinHazard(), 0d);
        Assert.AreEqual(0d, nf.MaxHazard(), 0d);
        Assert.AreEqual(0d, nf.MinProbability(), 0d);
        Assert.AreEqual(0d, nf.MaxProbability(), 0d);
        Assert.IsNull(nf.ComputeUncertaintyResults());
    }

    /// <summary>
    /// Verifies the sentinel carries no compute content: every instance hashes identically, and
    /// identification is a type test (the v1.1 replacement for the v1.0 singleton reference-equality).
    /// </summary>
    [TestMethod]
    public void Test_CanonicalHash_ContentFree()
    {
        // Arrange
        var a = new NonFailResponse();
        var b = new NonFailResponse { Name = "renamed", Description = "different" };

        // Act / Assert
        CollectionAssert.AreEqual(a.CanonicalHash(), b.CanonicalHash(), "All non-fail sentinels must hash alike.");
        HashInvariance.AssertMetadataInvariant(a);
        Assert.IsInstanceOfType<IResponseFunction>(a);
        Assert.IsTrue((IResponseFunction)a is NonFailResponse, "Type-test identification must work through the interface.");
    }

    /// <summary>Verifies the XElement round-trip.</summary>
    [TestMethod]
    public void Test_Serialization_RoundTrip()
    {
        // Arrange
        var original = new NonFailResponse();

        // Act
        var restored = new NonFailResponse(original.ToXElement());

        // Assert
        Assert.AreEqual(original.Name, restored.Name);
        CollectionAssert.AreEqual(original.CanonicalHash(), restored.CanonicalHash());
    }
}
