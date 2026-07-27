using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>Unit tests for <see cref="ValidationIssue"/> structured validation adapters.</summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// </remarks>
[TestClass]
public class ValidationIssueTests
{
    /// <summary>Verifies legacy errors and warnings round-trip without changing their text.</summary>
    [TestMethod]
    public void Test_LegacyMessage_RoundTrip()
    {
        var error = ValidationIssue.FromLegacyMessage("Error: Invalid graph.", "/Components[0]/Graph");
        var warning = ValidationIssue.FromLegacyMessage("Warning: Review this.");

        Assert.AreEqual("TRV0001", error.Code);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.AreEqual("/Components[0]/Graph", error.ObjectPath);
        Assert.AreEqual("Error: Invalid graph.", error.ToLegacyMessage());
        Assert.AreEqual("TRV0002", warning.Code);
        Assert.AreEqual("Warning: Review this.", warning.ToLegacyMessage());
        Assert.ThrowsException<ArgumentException>(() => new ValidationIssue("", DiagnosticSeverity.Error, "x", ""));
    }
}
