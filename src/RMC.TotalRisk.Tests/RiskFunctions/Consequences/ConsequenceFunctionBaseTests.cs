using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RMC.TotalRisk.RiskFunctions.Consequences;

namespace RMC.TotalRisk.Tests.RiskFunctions.Consequences;

/// <summary>Unit tests for <see cref="ConsequenceFunctionBase"/> labels and default branch contract.</summary>
/// <remarks>
/// <para><b>Authors:</b> Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil</para>
/// </remarks>
[TestClass]
public class ConsequenceFunctionBaseTests
{
    /// <summary>Verifies label notifications and the single-branch default.</summary>
    [TestMethod]
    public void Test_LabelsAndExposureBranch_Defaults()
    {
        ConsequenceFunctionBase function = new TabularConsequence();
        var raised = new List<string>();
        function.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        function.SpecifiedConsequence = "Damages";
        function.ConsequenceUnit = "$";
        var branches = function.SampleExposureBranches();

        Assert.AreEqual("Damages", function.SpecifiedConsequence);
        Assert.AreEqual("$", function.ConsequenceUnit);
        CollectionAssert.AreEqual(new[] { "SpecifiedConsequence", "ConsequenceUnit" }, raised);
        Assert.AreEqual(1, function.CountExposureBranches());
        Assert.AreEqual(1, branches.Count);
        Assert.AreEqual(1d, branches[0].Weight);
    }
}
