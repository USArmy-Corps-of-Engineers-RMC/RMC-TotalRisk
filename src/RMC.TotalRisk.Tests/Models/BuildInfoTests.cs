using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Models;

namespace RMC.TotalRisk.Tests.Models;

/// <summary>
/// Smoke tests proving the test-project wire-up against the model library.
/// Retired with <see cref="BuildInfo"/> once real model content lands.
/// </summary>
[TestClass]
public class BuildInfoTests
{
    /// <summary>Verifies the seeded version constant matches the v1.1.0 release line.</summary>
    [TestMethod]
    public void Test_Version_MatchesExpectedRelease()
    {
        // Assert
        Assert.AreEqual("1.1.0", BuildInfo.Version);
    }

    /// <summary>Verifies the seeded product constant matches the product name.</summary>
    [TestMethod]
    public void Test_Product_MatchesExpectedName()
    {
        // Assert
        Assert.AreEqual("RMC-TotalRisk", BuildInfo.Product);
    }
}
