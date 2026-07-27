using System;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Results;

namespace RMC.TotalRisk.Tests.Results;

/// <summary>
/// Unit tests for <see cref="AnalysisRunManifest"/> deterministic provenance, defensive
/// snapshots, schema validation, and legacy manifest-free payloads.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// </remarks>
[TestClass]
public class AnalysisRunManifestTests
{
    /// <summary>Creates a compact manifest fixture.</summary>
    /// <param name="schema">The schema version.</param>
    /// <returns>The manifest.</returns>
    private static AnalysisRunManifest Manifest(int schema = AnalysisRunManifest.CurrentSchemaVersion)
    {
        return new AnalysisRunManifest(schema, "1.1.0.0", "2.2.0.0", new string('A', 64),
            new string('B', 64), new[] { new string('C', 64), new string('D', 64) },
            new[] { 0, 1 }, 12345, new string('E', 64));
    }

    /// <summary>Verifies component identity arrays are immutable defensive snapshots.</summary>
    [TestMethod]
    public void Test_ComponentIdentities_AreDefensiveSnapshots()
    {
        var sourceHashes = new[] { new string('C', 64) };
        var sourceOccurrences = new[] { 0 };
        var manifest = new AnalysisRunManifest(1, "1.1.0.0", "2.2.0.0", new string('A', 64),
            new string('B', 64), sourceHashes, sourceOccurrences, 7, new string('E', 64));

        sourceHashes[0] = "changed";
        sourceOccurrences[0] = 99;
        manifest.ComponentContentHashes[0] = "also changed";
        manifest.ComponentOccurrenceIndices[0] = 88;

        Assert.AreEqual(new string('C', 64), manifest.ComponentContentHashes[0]);
        Assert.AreEqual(0, manifest.ComponentOccurrenceIndices[0]);
        Assert.IsTrue(manifest.IsCurrentSchema);
    }

    /// <summary>Verifies both persisted roots preserve an identical manifest.</summary>
    [TestMethod]
    public void Test_ResultRoots_ManifestRoundTrip()
    {
        var manifest = Manifest();
        var ensemble = new EnsembleResults { Manifest = manifest };
        var realization = new SystemRealization { Manifest = manifest };

        var restoredEnsemble = EnsembleResults.FromJson(ensemble.ToJson());
        var restoredRealization = SystemRealization.FromJson(realization.ToJson());

        Assert.IsTrue(restoredEnsemble.IsProvenanceVerified);
        Assert.IsTrue(restoredRealization.IsProvenanceVerified);
        Assert.AreEqual(restoredEnsemble.Manifest!.AnalysisContentHash,
            restoredRealization.Manifest!.AnalysisContentHash);
        CollectionAssert.AreEqual(manifest.ComponentContentHashes,
            restoredEnsemble.Manifest.ComponentContentHashes);
    }

    /// <summary>Verifies manifest-free legacy JSON is readable and explicitly unverified.</summary>
    [TestMethod]
    public void Test_LegacyManifestFreeJson_IsUnverified()
    {
        var ensemble = EnsembleResults.FromJson("{\"Realizations\":[]}");
        var realization = SystemRealization.FromJson("{}");

        Assert.IsNull(ensemble.Manifest);
        Assert.IsFalse(ensemble.IsProvenanceVerified);
        Assert.IsNull(realization.Manifest);
        Assert.IsFalse(realization.IsProvenanceVerified);
    }

    /// <summary>Verifies unsupported future schemas fail closed on both result roots.</summary>
    [TestMethod]
    public void Test_FutureSchema_IsRejected()
    {
        var future = Manifest(AnalysisRunManifest.CurrentSchemaVersion + 1);
        var ensemble = new EnsembleResults { Manifest = future };
        var realization = new SystemRealization { Manifest = future };

        Assert.ThrowsException<JsonException>(() => EnsembleResults.FromJson(ensemble.ToJson()));
        Assert.ThrowsException<JsonException>(() => SystemRealization.FromJson(realization.ToJson()));
    }
}
