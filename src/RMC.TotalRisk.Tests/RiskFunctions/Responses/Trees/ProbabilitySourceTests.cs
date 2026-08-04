using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Tests.RiskFunctions.Responses.Trees;

/// <summary>Tests tree probability-source discriminators and owned values.</summary>
[TestClass]
public class ProbabilitySourceTests
{
    /// <summary>Verifies scalar, table, and response source construction.</summary>
    [TestMethod]
    public void Test_Constructors_SelectExpectedKinds()
    {
        var table = new UncertainOrderedPairedData(
            new[]
            {
                new UncertainOrdinate(0d, new Uniform(0.1d, 0.3d)),
                new UncertainOrdinate(1d, new Uniform(0.2d, 0.4d)),
            }, true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Uniform);
        var response = new TabularResponse();

        var scalarSource = new ProbabilitySource(0.25d);
        var tableSource = new ProbabilitySource(table);
        var responseSource = new ProbabilitySource(response);

        Assert.AreEqual(ProbabilitySourceKind.DeterministicScalar, scalarSource.Kind);
        Assert.AreEqual(0.25d, scalarSource.ScalarProbability);
        Assert.AreEqual(ProbabilitySourceKind.UncertainTabular, tableSource.Kind);
        Assert.AreSame(table, tableSource.Table);
        Assert.AreEqual(ProbabilitySourceKind.ResponseFunctionReference, responseSource.Kind);
        Assert.AreSame(response, responseSource.ResponseFunction);
    }

    /// <summary>
    /// Verifies the bivariate scope guard: a source referencing a bivariate response reports a
    /// validation error (evaluating it at a single tree hazard would silently collapse the
    /// secondary hazard through the stored weights).
    /// </summary>
    [TestMethod]
    public void Test_Validate_BivariateReferencedResponse_Error()
    {
        // Arrange
        var source = new ProbabilitySource(new BivariateResponse { Name = "Surface" });

        // Act
        var messages = source.Validate(new[] { 0d, 1d }, "Chance node 'Breach'", "event-tree");

        // Assert
        Assert.IsTrue(messages.Any(m =>
            m.StartsWith("Error:") && m.Contains("bivariate response function")));
    }
}
