using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Tests.Systems.Components.Graph;

/// <summary>
/// Unit tests for <see cref="RiskElementResolver"/> — the Id-authoritative (loud) and
/// name-fallback (lenient) reference resolution policy.
/// </summary>
[TestClass]
public class RiskElementResolverTests
{
    /// <summary>Builds a resolver over a fixed element set.</summary>
    private static RiskElementResolver ResolverOver(params IRiskElement[] elements)
    {
        var byId = new Dictionary<Guid, IRiskElement>();
        var byName = new Dictionary<string, IRiskElement>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            byId[element.Id] = element;
            byName[element.Name] = element;
        }
        return new RiskElementResolver(
            id => byId.TryGetValue(id, out var e) ? e : null,
            name => byName.TryGetValue(name, out var e) ? e : null);
    }

    /// <summary>Verifies a serialized Id resolves authoritatively.</summary>
    [TestMethod]
    public void Test_Resolve_ById()
    {
        // Arrange
        var target = new HazardElement("Hazard");
        var resolver = ResolverOver(target);

        // Act / Assert — the Id wins even when the name would miss.
        Assert.AreSame(target, resolver.Resolve(target.Id, "Stale Name", "The link"));
    }

    /// <summary>Verifies a stale Id throws loudly (inconsistent serialized form).</summary>
    [TestMethod]
    public void Test_Resolve_StaleId_Throws()
    {
        // Arrange
        var resolver = ResolverOver(new HazardElement("Hazard"));

        // Act / Assert
        Assert.ThrowsException<InvalidOperationException>(() => resolver.Resolve(Guid.NewGuid(), null, "The link"));
    }

    /// <summary>Verifies the lenient name fallback: hit resolves, miss returns null.</summary>
    [TestMethod]
    public void Test_Resolve_NameFallback_Lenient()
    {
        // Arrange
        var target = new HazardElement("Hazard");
        var resolver = ResolverOver(target);

        // Act / Assert
        Assert.AreSame(target, resolver.Resolve(null, "Hazard", "The link"));
        Assert.IsNull(resolver.Resolve(null, "Absent", "The link"));
        Assert.IsNull(resolver.Resolve(null, null, "The link"));
        Assert.IsNull(resolver.Resolve(Guid.Empty, string.Empty, "The link"));
    }

    /// <summary>Verifies construction guards and Id parsing.</summary>
    [TestMethod]
    public void Test_Construction_And_ParsePendingId()
    {
        // Construction guards.
        Assert.ThrowsException<ArgumentNullException>(() => new RiskElementResolver(null!, _ => null));
        Assert.ThrowsException<ArgumentNullException>(() => new RiskElementResolver(_ => null, null!));

        // Pending-Id parsing.
        var id = Guid.NewGuid();
        Assert.AreEqual(id, RiskElementResolver.ParsePendingId(id.ToString("D")));
        Assert.IsNull(RiskElementResolver.ParsePendingId("not-a-guid"));
        Assert.IsNull(RiskElementResolver.ParsePendingId(null));
    }
}
