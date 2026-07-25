using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Verification.Analyses;

/// <summary>
/// Engine reproducibility verification — the v1 seed-dependency bug regression at the analysis
/// level: metadata edits (renames, descriptions, fresh ids) and serialization round trips can
/// never move Monte Carlo results, repeated runs are bit-identical (the parallel realization
/// loop uses index-owned writes and sequential reductions, so thread-schedule variation across
/// runs exercises the determinism claim), and a numeric edit moves the results.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// v1.0 handed each component a seed from a master PRNG iterated in canvas order, so dragging a
/// node on the canvas changed the results. v1.1 seeds derive from
/// (analysis seed, component canonical hash, occurrence index) with per-function ordinals — the
/// identity surface is content, never presentation. These pins compare full results JSON byte
/// streams, the strongest equality the results contract can express.
/// </para>
/// </remarks>
[TestClass]
public class EngineReproducibilityVerification
{
    /// <summary>The ensemble size for the reproducibility pins (thread scheduling varies freely at this size).</summary>
    private const int Realizations = 200;

    /// <summary>Builds the standard uncertain single-component analysis.</summary>
    private static RiskAnalysis Build()
    {
        // A deterministic tabulated stage-frequency curve (Normal(100, 20) quantiles on a dense
        // z-grid); the epistemic spread comes from the fragility and consequence ordinates.
        int knotCount = 65;
        var hazardOrdinates = new UncertainOrdinate[knotCount];
        for (int i = 0; i < knotCount; i++)
        {
            double z = -8d + i * 0.25d;
            hazardOrdinates[i] = new UncertainOrdinate(1d - Normal.StandardCDF(z), new Deterministic(100d + 20d * z));
        }
        var hazard = new TabularHazard
        {
            Name = "Stage Frequency",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            ProbabilityTransform = Transform.None,
            NoUncertaintyFunction = new UncertainOrderedPairedData(hazardOrdinates,
                true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Deterministic),
        };
        var fragility = new TabularResponse
        {
            Name = "Breach Fragility",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(100d, new Triangular(0d, 0.02d, 0.05d)),
                    new UncertainOrdinate(140d, new Triangular(0.3d, 0.5d, 0.7d)),
                    new UncertainOrdinate(180d, new Triangular(0.9d, 0.97d, 1d)),
                },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Triangular),
        };
        var consequence = new TabularConsequence
        {
            Name = "Life Loss",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = "Life Loss",
            ConsequenceUnit = "lives",
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(60d, new Normal(0d, 0d)),
                    new UncertainOrdinate(200d, new Normal(1000d, 150d)),
                },
                true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Normal),
        };

        var component = new SystemComponent { Name = "Dam" };
        component.HazardFunction = hazard;
        component.AddFailureMode(new FailureMode(null, null, fragility, consequence));
        var analysis = new RiskAnalysis(new[] { component }) { Name = "Reproducibility" };
        analysis.Options.EstimateMeanRiskOnly = false;
        analysis.Options.Realizations = Realizations;
        return analysis;
    }

    /// <summary>Runs an analysis and captures its results as JSON byte streams.</summary>
    private static (string Ensemble, string Mean) Run(RiskAnalysis analysis)
    {
        analysis.RunAsync().GetAwaiter().GetResult();
        Assert.IsTrue(analysis.IsEstimated, "The run must succeed.");
        return (analysis.RiskResults!.ToJson(), analysis.MeanRiskResults!.ToJson());
    }

    /// <summary>
    /// Same content, three runs: the full results JSON must be byte-identical each time. Each
    /// run's parallel loop partitions work differently across threads, so repeated equality is
    /// the practical any-thread-count pin (writes are index-owned; reductions sequential).
    /// </summary>
    [TestMethod]
    public void Test_Reproducibility_RepeatedRuns_BitIdentical()
    {
        // Arrange / Act
        var first = Run(Build());
        var second = Run(Build());
        var third = Run(Build());

        // Assert
        Assert.AreEqual(first.Ensemble, second.Ensemble, "Run 2 ensemble diverged.");
        Assert.AreEqual(first.Ensemble, third.Ensemble, "Run 3 ensemble diverged.");
        Assert.AreEqual(first.Mean, second.Mean, "Run 2 mean curves diverged.");
        Assert.AreEqual(first.Mean, third.Mean, "Run 3 mean curves diverged.");
    }

    /// <summary>
    /// Metadata edits — renaming everything, editing descriptions, assigning fresh ids, and a
    /// serialization round trip — must leave every computed number byte-identical: presentation
    /// is not identity. Since the Phase 6.7 Q3 labels, the ensemble JSON carries the stamped
    /// end-state <c>Name</c> and <c>PathLabel</c> display fields, which legitimately echo the
    /// CURRENT function names (labels are display metadata by design, never identity), so the
    /// comparison strips exactly those two fields and asserts every remaining byte — the whole
    /// numeric surface — is identical.
    /// </summary>
    [TestMethod]
    public void Test_Reproducibility_MetadataEdits_BitIdentical()
    {
        // Arrange — baseline.
        var baselineAnalysis = Build();
        var baseline = Run(baselineAnalysis);

        // Act — a heavily re-labeled equivalent, round-tripped through serialization.
        var edited = Build();
        edited.Name = "Completely Different Analysis Name";
        edited.Description = "Edited description";
        var component = (SystemComponent)edited.Components[0];
        component.Name = "Renamed Dam";
        var functions = new System.Collections.Generic.List<RMC.TotalRisk.Core.Interfaces.IRiskFunction>(component.GetReferencedFunctions());
        foreach (var function in functions)
        {
            function.Name = $"{function.Name} (renamed)";
            function.Description = "Edited";
            function.AssignNewId();
        }
        var roundTripped = new RiskAnalysis(
            new[] { new SystemComponent(component.ToXElement()) }, edited.ToXElement());
        roundTripped.Options.EstimateMeanRiskOnly = false;
        roundTripped.Options.Realizations = Realizations;
        var result = Run(roundTripped);

        // Assert — the numeric surfaces are byte-identical. The stamped display labels (the
        // Phase 6.7 Q3 Name/PathLabel fields) echo the renamed functions by design and are
        // stripped from both sides; every other byte of the ensemble JSON must match.
        static string StripDisplayLabels(string json) => System.Text.RegularExpressions.Regex.Replace(
            json, "\"(Name|PathLabel)\":\"[^\"]*\",?", string.Empty);
        Assert.AreEqual(StripDisplayLabels(baseline.Ensemble), StripDisplayLabels(result.Ensemble),
            "Metadata edits or the round trip moved the ensemble's numeric surface.");
        var baselineTotal = baselineAnalysis.MeanRiskResults!.Curves.Total;
        var resultTotal = roundTripped.MeanRiskResults!.Curves.Total;
        CollectionAssert.AreEqual(baselineTotal.LECConsequences, resultTotal.LECConsequences,
            "Metadata edits or the round trip moved the mean Total curve ordinates.");
        CollectionAssert.AreEqual(baselineTotal.LECProbabilities, resultTotal.LECProbabilities,
            "Metadata edits or the round trip moved the mean Total curve probabilities.");
        CollectionAssert.AreEqual(
            baselineAnalysis.UpperRiskResults!.Curves.Fail.LECProbabilities,
            roundTripped.UpperRiskResults!.Curves.Fail.LECProbabilities,
            "Metadata edits or the round trip moved the upper confidence curve.");
    }

    /// <summary>
    /// A compute-relevant numeric edit must move the results — the counter-pin proving the
    /// equality asserts are not vacuous.
    /// </summary>
    [TestMethod]
    public void Test_Reproducibility_ComputeEdit_MovesResults()
    {
        // Arrange
        var baseline = Run(Build());

        // Act — nudge one fragility ordinate.
        var edited = Build();
        var component = (SystemComponent)edited.Components[0];
        foreach (var function in component.GetReferencedFunctions())
        {
            if (function is TabularResponse fragility)
            {
                fragility.UncertainOrderedPairedData = new UncertainOrderedPairedData(
                    new[]
                    {
                        new UncertainOrdinate(100d, new Triangular(0d, 0.02d, 0.05d)),
                        new UncertainOrdinate(141d, new Triangular(0.3d, 0.5d, 0.7d)),
                        new UncertainOrdinate(180d, new Triangular(0.9d, 0.97d, 1d)),
                    },
                    true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Triangular);
            }
        }
        var result = Run(edited);

        // Assert
        Assert.AreNotEqual(baseline.Ensemble, result.Ensemble, "A compute edit must move the results.");
    }
}
