using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.Core;

namespace RMC.TotalRisk.Tests.Core;

/// <summary>
/// Unit tests for <see cref="ExtrapolationRangeException"/> — the diagnostic fields and the
/// composed message carrying the function, axis, value, and table range.
/// </summary>
[TestClass]
public class ExtrapolationRangeExceptionTests
{
    /// <summary>
    /// Verifies the diagnostic fields round-trip and the message names every element of the
    /// refusal: function, axis, offending value, and the table range.
    /// </summary>
    [TestMethod]
    public void Test_Diagnostic_FieldsAndMessage()
    {
        // Act
        var fault = new ExtrapolationRangeException("Rating", "Flow (cfs)", 150.5d, 0d, 100d);

        // Assert
        Assert.AreEqual("Rating", fault.FunctionName);
        Assert.AreEqual("Flow (cfs)", fault.Axis);
        Assert.AreEqual(150.5d, fault.Value, 0d);
        Assert.AreEqual(0d, fault.RangeMinimum, 0d);
        Assert.AreEqual(100d, fault.RangeMaximum, 0d);
        StringAssert.Contains(fault.Message, "'Rating'");
        StringAssert.Contains(fault.Message, "Flow (cfs)");
        StringAssert.Contains(fault.Message, "150.5");
        StringAssert.Contains(fault.Message, "[0, 100]");
        StringAssert.Contains(fault.Message, "Error");
    }
}
