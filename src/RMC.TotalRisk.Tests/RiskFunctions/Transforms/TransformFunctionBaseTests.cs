using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Transforms;

namespace RMC.TotalRisk.Tests.RiskFunctions.Transforms;

/// <summary>Unit tests for <see cref="TransformFunctionBase"/> transformed-axis labels.</summary>
/// <remarks>
/// <para><b>Authors:</b> Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil</para>
/// </remarks>
[TestClass]
public class TransformFunctionBaseTests
{
    /// <summary>Verifies transformed labels store values and raise one notification per change.</summary>
    [TestMethod]
    public void Test_TransformedAxisLabels_Notify()
    {
        TransformFunctionBase function = new TabularTransform();
        var raised = new List<string>();
        function.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        function.TransformedHazard = "Stage";
        function.TransformedHazard = "Stage";
        function.TransformedHazardUnit = "ft";

        Assert.AreEqual("Stage", function.TransformedHazard);
        Assert.AreEqual("ft", function.TransformedHazardUnit);
        CollectionAssert.AreEqual(new[] { "TransformedHazard", "TransformedHazardUnit" }, raised);
    }
}
