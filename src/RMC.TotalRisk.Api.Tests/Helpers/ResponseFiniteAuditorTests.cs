using RMC.TotalRisk.Api.Helpers;

namespace RMC.TotalRisk.Api.Tests.Helpers;

/// <summary>
/// Tests for the ±Infinity response auditor.
/// </summary>
[TestClass]
public class ResponseFiniteAuditorTests
{
    /// <summary>A probe DTO exposing nested doubles for the auditor to walk.</summary>
    private sealed class Probe
    {
        /// <summary>A scalar value.</summary>
        public double Value { get; set; }

        /// <summary>An array of values.</summary>
        public double[]? Values { get; set; }

        /// <summary>A nested probe.</summary>
        public Probe? Child { get; set; }
    }

    /// <summary>Finite and NaN values produce no findings.</summary>
    [TestMethod]
    public void Test_FiniteAndNaN_NoFindings()
    {
        // Arrange
        var probe = new Probe { Value = 1.5, Values = new[] { 0d, double.NaN }, Child = new Probe { Value = -2 } };

        // Act
        var findings = ResponseFiniteAuditor.Audit(probe);

        // Assert
        Assert.AreEqual(0, findings.Count);
    }

    /// <summary>Infinity is reported with its dotted/indexed path.</summary>
    [TestMethod]
    public void Test_Infinity_ReportedWithPath()
    {
        // Arrange
        var probe = new Probe { Values = new[] { 1d, double.PositiveInfinity }, Child = new Probe { Value = double.NegativeInfinity } };

        // Act
        var findings = ResponseFiniteAuditor.Audit(probe);

        // Assert
        Assert.AreEqual(2, findings.Count);
        Assert.IsTrue(findings.Any(f => f.Contains("Values[1]")), string.Join("; ", findings));
        Assert.IsTrue(findings.Any(f => f.Contains("Child.Value")), string.Join("; ", findings));
    }

    /// <summary>A null root audits clean.</summary>
    [TestMethod]
    public void Test_NullRoot_NoFindings()
    {
        // Act
        var findings = ResponseFiniteAuditor.Audit(null);

        // Assert
        Assert.AreEqual(0, findings.Count);
    }

    /// <summary>Reference cycles do not hang the walk.</summary>
    [TestMethod]
    public void Test_Cycle_Terminates()
    {
        // Arrange
        var probe = new Probe { Value = double.PositiveInfinity };
        probe.Child = probe;

        // Act
        var findings = ResponseFiniteAuditor.Audit(probe);

        // Assert
        Assert.AreEqual(1, findings.Count);
    }
}
