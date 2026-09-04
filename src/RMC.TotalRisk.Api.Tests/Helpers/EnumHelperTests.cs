using RMC.TotalRisk.Api.Helpers;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Api.Tests.Helpers;

/// <summary>
/// Tests for the camelCase enum helpers.
/// </summary>
[TestClass]
public class EnumHelperTests
{
    /// <summary>Member names camelCase per the wire policy.</summary>
    [TestMethod]
    public void Test_CamelCaseNames_MatchWirePolicy()
    {
        // Act
        var names = EnumHelper.CamelCaseNames<FailureModeMethod>();

        // Assert
        CollectionAssert.Contains(names, "jointFailures");
        CollectionAssert.Contains(names, "mutuallyExclusive");
    }

    /// <summary>Parsing is case-insensitive and never silently defaults on a bad value.</summary>
    [TestMethod]
    public void Test_ParseOrDefault_BadValueThrowsListingAccepted()
    {
        // Act
        var parsed = EnumHelper.ParseOrDefault("jointFailures", FailureModeMethod.MutuallyExclusive);
        var ex = Assert.ThrowsException<ArgumentException>(
            () => EnumHelper.ParseOrDefault("bogus", FailureModeMethod.JointFailures));

        // Assert
        Assert.AreEqual(FailureModeMethod.JointFailures, parsed);
        StringAssert.Contains(ex.Message, "jointFailures");
        StringAssert.Contains(ex.Message, "bogus");
    }

    /// <summary>Null and blank fall back to the default or null.</summary>
    [TestMethod]
    public void Test_NullOrBlank_FallsBack()
    {
        // Act & Assert
        Assert.AreEqual(FailureModeMethod.JointFailures,
            EnumHelper.ParseOrDefault<FailureModeMethod>(null, FailureModeMethod.JointFailures));
        Assert.IsNull(EnumHelper.ParseOrNull<FailureModeMethod>("  "));
    }
}
