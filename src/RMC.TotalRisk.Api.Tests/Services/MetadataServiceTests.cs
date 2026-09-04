using Microsoft.Extensions.Options;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.Services;

namespace RMC.TotalRisk.Api.Tests.Services;

/// <summary>
/// Tests for the discovery metadata.
/// </summary>
[TestClass]
public class MetadataServiceTests
{
    /// <summary>The metadata enumerates the contract's enums, kinds, defaults, and limits.</summary>
    [TestMethod]
    public void Test_GetMetadata_Complete()
    {
        // Arrange
        var service = new MetadataService(Options.Create(new ApiOptions { MaxConcurrentRuns = 7 }));

        // Act
        var metadata = service.GetMetadata();

        // Assert
        CollectionAssert.Contains(metadata.Enums["failureModeMethod"], "jointFailures");
        CollectionAssert.Contains(metadata.Enums["transform"], "normalZ");
        CollectionAssert.Contains(metadata.Enums["riskMeasureOptions"], "all");
        CollectionAssert.Contains(metadata.FunctionTypes["consequence"], "compositeMixture");
        Assert.AreEqual(true, metadata.Defaults["outputAdjustedFailureModeCurves"]);
        Assert.AreEqual(12345, metadata.Defaults["prngSeed"]);
        Assert.AreEqual(7, metadata.Limits.MaxConcurrentRuns);
        Assert.IsTrue(metadata.Conventions.Any(c => c.Contains("EXCEEDANCE", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(metadata.Conventions.Any(c => c.Contains("UNADJUSTED", StringComparison.OrdinalIgnoreCase)));
    }
}
