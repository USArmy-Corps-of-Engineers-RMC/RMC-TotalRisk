using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Numerics.Data;
using Numerics.Distributions;
using Numerics.Utilities;
using RMC.TotalRisk.Analyses;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Results;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses.FaultTrees;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Tests.Analyses;

/// <summary>
/// Tests the cost-benefit study: guards, the validation matrix, the run orchestration
/// (dedup, baseline row zero, progress, cancellation, the veto event, replace-to-edit),
/// the cost and benefit arithmetic, author inertness, and run-to-run reproducibility.
/// </summary>
[TestClass]
public class CostBenefitAnalysisTests
{
    #region Fixtures

    /// <summary>
    /// Builds the flat OR(AND(house, 0.9), 0.2) fault-tree response: failure probability 0.2
    /// with the house event false, 0.92 with it true.
    /// </summary>
    /// <param name="houseState">The authored house state.</param>
    /// <returns>The response.</returns>
    private static FaultTreeResponse FaultResponse(bool houseState)
    {
        var faultTree = new FaultTree();
        Guid gateId = faultTree.Add(faultTree.Root.Id,
            new FaultTreeGateNode("Outage impact", FaultTreeGateType.And));
        faultTree.Add(gateId, new FaultTreeHouseEventNode("Gate out of service", houseState));
        faultTree.Add(gateId, new FaultTreeBasicEventNode("Load exceedance", new ProbabilitySource(0.9d)));
        faultTree.Add(faultTree.Root.Id,
            new FaultTreeBasicEventNode("Structural failure", new ProbabilitySource(0.2d)));
        return new FaultTreeResponse(new[] { 0d, 1d }, faultTree)
        {
            Name = "Spillway fault tree",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
        };
    }

    /// <summary>Builds the deterministic stage-frequency hazard.</summary>
    /// <returns>The hazard.</returns>
    private static TabularHazard Hazard()
    {
        return new TabularHazard
        {
            Name = "Stage frequency",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            NoUncertaintyFunction = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0.999d, new Deterministic(0d)),
                    new UncertainOrdinate(0.5d, new Deterministic(0.5d)),
                    new UncertainOrdinate(0.001d, new Deterministic(1d)),
                },
                true, SortOrder.Descending, true, SortOrder.Ascending,
                UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds the deterministic stage-to-loss consequence.</summary>
    /// <param name="label">The consequence-type label.</param>
    /// <param name="unit">The consequence-type unit.</param>
    /// <returns>The consequence.</returns>
    private static TabularConsequence Consequence(string label = "Damages", string unit = "$")
    {
        return new TabularConsequence
        {
            Name = $"Failure {label}",
            SpecifiedHazard = "Stage",
            HazardUnit = "ft",
            SpecifiedConsequence = label,
            ConsequenceUnit = unit,
            UncertainOrderedPairedData = new UncertainOrderedPairedData(
                new[]
                {
                    new UncertainOrdinate(0d, new Deterministic(500d)),
                    new UncertainOrdinate(1d, new Deterministic(1000d)),
                },
                true, SortOrder.Ascending, false, SortOrder.None,
                UnivariateDistributionType.Deterministic),
        };
    }

    /// <summary>Builds a one-component system over the given house state.</summary>
    /// <param name="houseState">The authored house state.</param>
    /// <returns>The analysis.</returns>
    private static RiskAnalysis BuildSystem(bool houseState)
    {
        var component = new SystemComponent { Name = "Dam" };
        component.HazardFunction = Hazard();
        component.AddFailureMode(new FailureMode(null, null, FaultResponse(houseState), Consequence()));
        return new RiskAnalysis(new[] { component })
        {
            SpecifiedConsequence = "Damages",
            ConsequenceUnit = "$",
        };
    }

    /// <summary>Builds a two-alternative study: a degraded baseline and a costed fix.</summary>
    /// <param name="options">The study declarations; a 30-year 5% study when null.</param>
    /// <returns>The study, its baseline, and the fix alternative.</returns>
    private static (CostBenefitAnalysis Study, RiskReductionAlternative Baseline, RiskReductionAlternative Fix)
        BuildStudy(CostBenefitOptions? options = null)
    {
        var baseline = new RiskReductionAlternative("Existing condition", BuildSystem(houseState: true));
        var fix = new RiskReductionAlternative("Gate repair", BuildSystem(houseState: false),
            new CostStream(
                new[] { new CapitalCostEntry(0, 1000d), new CapitalCostEntry(10, 500d) },
                new[] { new RecurringCostSegment(0, 10d) },
                new[] { new RecurringCostSegment(10, -5d, 30) }));
        var study = new CostBenefitAnalysis(options ?? new CostBenefitOptions(30, 0.05d));
        study.Alternatives.Add(baseline);
        study.Alternatives.Add(fix);
        study.Baseline = baseline;
        return (study, baseline, fix);
    }

    /// <summary>Finds one validation message by prefix.</summary>
    /// <param name="messages">The messages.</param>
    /// <param name="prefix">The required prefix.</param>
    /// <returns>True when a message starts with the prefix.</returns>
    private static bool HasMessage(IReadOnlyList<string> messages, string prefix)
    {
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    #endregion

    /// <summary>Verifies null options are refused at construction and assignment.</summary>
    [TestMethod]
    public void Test_Ctor_Guards()
    {
        // Act / Assert
        Assert.ThrowsException<ArgumentNullException>(() => new CostBenefitAnalysis(null!));
        var study = new CostBenefitAnalysis(new CostBenefitOptions(30));
        Assert.ThrowsException<ArgumentNullException>(() => study.Options = null!);
    }

    /// <summary>Verifies the structural error rules of the validation matrix.</summary>
    [TestMethod]
    public void Test_Validate_Errors()
    {
        // Arrange — an empty study has no baseline.
        var empty = new CostBenefitAnalysis(new CostBenefitOptions(30));
        (bool emptyValid, var emptyMessages) = empty.Validate();
        Assert.IsFalse(emptyValid);
        Assert.IsTrue(HasMessage(emptyMessages, "Error: The study requires a designated baseline alternative."));

        // A baseline that is not a member.
        (CostBenefitAnalysis study, _, _) = BuildStudy();
        study.Baseline = new RiskReductionAlternative("Outsider", BuildSystem(false));
        Assert.IsTrue(HasMessage(study.Validate().ValidationMessages,
            "Error: The designated baseline must be a member of the alternatives collection."));

        // Duplicate names.
        (CostBenefitAnalysis duplicates, RiskReductionAlternative anchor, _) = BuildStudy();
        duplicates.Alternatives.Add(new RiskReductionAlternative("Existing condition", BuildSystem(false)));
        Assert.IsTrue(HasMessage(duplicates.Validate().ValidationMessages,
            "Error: Two alternatives share the name 'Existing condition'."));

        // Plan and cost years against the horizon.
        (CostBenefitAnalysis horizon, RiskReductionAlternative horizonBaseline, _) = BuildStudy();
        horizon.Alternatives.Add(new RiskReductionAlternative("Late plan", horizonBaseline.System,
            plan: new LifeCyclePlan(Array.Empty<LifeCycleIntervention>(), new[] { 30 })));
        horizon.Alternatives.Add(new RiskReductionAlternative("Late capital", horizonBaseline.System,
            new CostStream(new[] { new CapitalCostEntry(30, 100d) })));
        horizon.Alternatives.Add(new RiskReductionAlternative("Long segment", horizonBaseline.System,
            new CostStream(null, new[] { new RecurringCostSegment(0, 10d, 31) })));
        var horizonMessages = horizon.Validate().ValidationMessages;
        Assert.IsTrue(HasMessage(horizonMessages, "Error: Alternative 'Late plan' declares a plan year at or beyond the horizon."));
        Assert.IsTrue(HasMessage(horizonMessages, "Error: Alternative 'Late capital' declares a cost year at or beyond the horizon."));
        Assert.IsTrue(HasMessage(horizonMessages, "Error: Alternative 'Long segment' declares a cost segment ending beyond the horizon."));

        // The consequence-type axis must align with the baseline's.
        (CostBenefitAnalysis axis, _, _) = BuildStudy();
        var widerSystem = BuildSystem(false);
        widerSystem.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Life Loss", "lives"));
        axis.Alternatives.Add(new RiskReductionAlternative("Wider axis", widerSystem));
        Assert.IsTrue(HasMessage(axis.Validate().ValidationMessages,
            "Error: Alternative 'Wider axis' declares 2 consequence types while the baseline declares 1."));

        // A conflicting non-blank label.
        (CostBenefitAnalysis labels, _, _) = BuildStudy();
        var renamedSystem = BuildSystem(false);
        renamedSystem.SpecifiedConsequence = "Losses";
        labels.Alternatives.Add(new RiskReductionAlternative("Renamed", renamedSystem));
        Assert.IsTrue(HasMessage(labels.Validate().ValidationMessages,
            "Error: Alternative 'Renamed' consequence type 0 label 'Losses' conflicts with the baseline's 'Damages'."));

        // Monetization against the axis: an undeclared position and an identity type.
        (CostBenefitAnalysis monetized, _, _) = BuildStudy(new CostBenefitOptions(30, 0.05d,
            monetization: new ConsequenceMonetization(new[]
            {
                new MonetizationFactor(3, 100d),
                new MonetizationFactor(0, 100d),
            })));
        var monetizedMessages = monetized.Validate().ValidationMessages;
        Assert.IsTrue(HasMessage(monetizedMessages,
            "Error: A monetization factor prices consequence-type position 3, which is not declared."));
        Assert.IsTrue(HasMessage(monetizedMessages,
            "Error: A monetization factor prices consequence type 0, whose unit already equals the monetary unit"));

        // A life-safety position beyond the declared axis.
        (CostBenefitAnalysis lifeSafety, _, _) = BuildStudy(new CostBenefitOptions(30, 0.05d,
            lifeSafetyConsequenceType: 2));
        Assert.IsTrue(HasMessage(lifeSafety.Validate().ValidationMessages,
            "Error: The life-safety consequence-type position 2 is not declared."));

        // An unsupported benefit stream.
        (CostBenefitAnalysis stream, _, _) = BuildStudy(new CostBenefitOptions(30, 0.05d,
            benefitRiskType: RiskType.Background));
        Assert.IsTrue(HasMessage(stream.Validate().ValidationMessages,
            "Error: The benefit stream must be Total, Excess, or Fail."));
    }

    /// <summary>Verifies the advisory rules of the validation matrix.</summary>
    [TestMethod]
    public void Test_Validate_Warnings()
    {
        // Arrange — the plain study monetizes nothing and stores no ensembles.
        (CostBenefitAnalysis study, _, _) = BuildStudy();

        // Act
        var messages = study.Validate().ValidationMessages;

        // Assert
        Assert.IsTrue(HasMessage(messages,
            "Warning: No consequence types are monetized; net present value, net annual benefit, and benefit-cost ratio will be NaN."));
        Assert.IsTrue(HasMessage(messages,
            "Warning: Alternative 'Existing condition' carries no stored full-uncertainty ensemble; the epistemic decision strategies will be skipped."));
        Assert.IsTrue(HasMessage(messages,
            "Warning: Alternative 'Gate repair' was not enumerated by the exact logic tree; the shared-state regret strategies will be skipped."));
        Assert.IsTrue(HasMessage(messages,
            "Warning: No willingness to pay is declared; the disproportionality and ALARP block is skipped."));
        Assert.IsTrue(study.Validate().IsValid, "Warnings alone must not invalidate the study.");

        // A zero-cost non-baseline alternative advises.
        (CostBenefitAnalysis costless, RiskReductionAlternative costlessBaseline, _) = BuildStudy();
        costless.Alternatives.Add(new RiskReductionAlternative("Free lunch", costlessBaseline.System));
        Assert.IsTrue(HasMessage(costless.Validate().ValidationMessages,
            "Warning: Alternative 'Free lunch' declares no costs; its benefit-cost ratio will be NaN."));

        // A study α list that omits an alternative's run level advises.
        (CostBenefitAnalysis alpha, _, _) = BuildStudy(new CostBenefitOptions(30, 0.05d,
            alphaLevels: new[] { 0.002d }));
        Assert.IsTrue(HasMessage(alpha.Validate().ValidationMessages,
            "Warning: Alternative 'Existing condition' runs exceedance level"));

        // The individual-risk proxy advisory arms once a life-safety type is declared.
        var lifeSystemBaseline = BuildSystem(true);
        lifeSystemBaseline.AdditionalConsequenceTypes.Add(new ConsequenceTypeDescriptor("Life Loss", "lives"));
        var lifeStudy = new CostBenefitAnalysis(new CostBenefitOptions(30, 0.05d, lifeSafetyConsequenceType: 1));
        var lifeBaseline = new RiskReductionAlternative("Existing condition", lifeSystemBaseline);
        lifeStudy.Alternatives.Add(lifeBaseline);
        lifeStudy.Baseline = lifeBaseline;
        Assert.IsTrue(HasMessage(lifeStudy.Validate().ValidationMessages,
            "Warning: The individual-risk seats are not declared; the survival-equivalent annualized failure probability proxy is used and echoed."));
    }

    /// <summary>
    /// Verifies the null-study run: a costless twin sharing the baseline's system and plan
    /// reuses one trajectory evaluation, publishes exactly zero reductions, and reports a NaN
    /// benefit-cost ratio at zero cost.
    /// </summary>
    [TestMethod]
    public void Test_Run_NullStudy_DedupAndZeroDeltas()
    {
        // Arrange — the twin references the same system instance with no plan and no costs;
        // the declared (empty) monetization map identity-prices the dollar type at one.
        var system = BuildSystem(houseState: true);
        var baseline = new RiskReductionAlternative("Existing condition", system);
        var twin = new RiskReductionAlternative("Do nothing", system);
        var study = new CostBenefitAnalysis(new CostBenefitOptions(30, 0.05d,
            monetization: new ConsequenceMonetization()));
        study.Alternatives.Add(baseline);
        study.Alternatives.Add(twin);
        study.Baseline = baseline;

        // Act
        study.RunAsync().GetAwaiter().GetResult();

        // Assert — one evaluation serves both rows.
        CostBenefitResults results = study.Results!;
        Assert.IsTrue(study.IsEstimated);
        Assert.IsTrue(ReferenceEquals(results.Trajectories[0], results.Trajectories[1]),
            "Alternatives sharing one (system, plan) pair must share one trajectory evaluation.");
        Assert.IsTrue(results.Alternatives[0].IsBaseline);
        Assert.AreEqual("Existing condition", results.Alternatives[0].Name);

        // Every reduction row of the twin is exactly zero.
        for (int i = 0; i < results.ConsequenceReductions.Count; i++)
        {
            ConsequenceReduction row = results.ConsequenceReductions[i];
            if (!string.Equals(row.AlternativeName, "Do nothing", StringComparison.Ordinal)) continue;
            Assert.AreEqual(0d, row.PresentValueReduction, 0d);
            Assert.AreEqual(0d, row.EquivalentAnnualReduction, 0d);
            Assert.AreEqual(0d, row.CumulativeReduction, 0d);
            Assert.AreEqual(0d, row.AbsorbingPresentValueReduction, 0d);
            Assert.AreEqual(0d, row.AbsorbingCumulativeReduction, 0d);
        }

        // The identity-monetized type ($ = $) prices the zero reduction: NPV 0 at zero cost,
        // and the benefit-cost ratio is NaN — never infinite or clamped.
        AlternativeEconomics twinRow = results.Alternatives[1];
        Assert.AreEqual(0d, twinRow.MonetizedPresentValueBenefit, 0d);
        Assert.AreEqual(0d, twinRow.NetPresentValue, 0d);
        Assert.IsTrue(double.IsNaN(twinRow.BenefitCostRatio));
        Assert.AreEqual(0d, twinRow.AnnualizedFailureProbabilityReduction, 0d);
    }

    /// <summary>
    /// Verifies the economics row arithmetic: the cost block against independent power-form
    /// discounting, the identity-monetized benefit against the published trajectories, and
    /// the net-benefit identities.
    /// </summary>
    [TestMethod]
    public void Test_Run_EconomicsRow_CostAndBenefitArithmetic()
    {
        // Arrange — the declared (empty) monetization map identity-prices the dollar type.
        (CostBenefitAnalysis study, _, _) = BuildStudy(new CostBenefitOptions(30, 0.05d,
            monetization: new ConsequenceMonetization()));

        // Act
        study.RunAsync().GetAwaiter().GetResult();

        // Assert — the fix row's cost block against an independent computation
        // (capital 1000 @ 0 + 500 @ 10; operations 10/yr over (0, 30]; operating −5/yr over
        // (10, 30]; r = 0.05). The engine evaluates in log space, so agreement is relative.
        CostBenefitResults results = study.Results!;
        AlternativeEconomics fixRow = results.Alternatives[1];
        static double Annuity(int years) => (1d - Math.Pow(1.05d, -years)) / 0.05d;
        double expectedCapital = 1000d + 500d * Math.Pow(1.05d, -10);
        double expectedOperations = 10d * Annuity(30);
        double expectedOperating = -5d * (Annuity(30) - Annuity(10));
        Assert.AreEqual(expectedCapital, fixRow.CapitalPresentValue, Math.Abs(expectedCapital) * 1e-12);
        Assert.AreEqual(expectedOperations, fixRow.OperationsAndMaintenancePresentValue,
            Math.Abs(expectedOperations) * 1e-12);
        Assert.AreEqual(expectedOperating, fixRow.OperatingChangePresentValue,
            Math.Abs(expectedOperating) * 1e-12);
        double expectedTotal = expectedCapital + expectedOperations + expectedOperating;
        Assert.AreEqual(expectedTotal, fixRow.TotalCostPresentValue, Math.Abs(expectedTotal) * 1e-12);
        Assert.AreEqual(expectedTotal / Annuity(30), fixRow.EquivalentAnnualCost,
            Math.Abs(expectedTotal / Annuity(30)) * 1e-12);
        Assert.AreEqual(1000d + 500d + 10d * 30 - 5d * 20, fixRow.CumulativeCost, 1e-12);

        // The identity-monetized benefit is the Total-stream present-value reduction read
        // straight off the published trajectories — bit-exact by construction.
        LifeCycleRiskResults baselineTrajectory = results.Trajectories[0];
        LifeCycleRiskResults fixTrajectory = results.Trajectories[1];
        double expectedBenefit = baselineTrajectory.PresentValueOfExpectedConsequences[0]
            - fixTrajectory.PresentValueOfExpectedConsequences[0];
        Assert.AreEqual(expectedBenefit, fixRow.MonetizedPresentValueBenefit, 0d);
        Assert.IsTrue(fixRow.MonetizedPresentValueBenefit > 0d, "The repair must reduce risk.");
        Assert.AreEqual(fixRow.MonetizedPresentValueBenefit - fixRow.TotalCostPresentValue,
            fixRow.NetPresentValue, 0d);
        Assert.AreEqual(fixRow.MonetizedPresentValueBenefit / fixRow.TotalCostPresentValue,
            fixRow.BenefitCostRatio, 0d);
        double expectedAbsorbingBenefit = baselineTrajectory.AbsorbingPresentValueOfExpectedConsequences[0]
            - fixTrajectory.AbsorbingPresentValueOfExpectedConsequences[0];
        Assert.AreEqual(expectedAbsorbingBenefit, fixRow.AbsorbingMonetizedPresentValueBenefit, 0d);

        // The survival-equivalent annualized failure probability against the independent
        // power form, and the signed reduction against the baseline row.
        double pT = fixTrajectory.FailureProbabilityByHorizon;
        double expectedAnnualized = 1d - Math.Pow(1d - pT, 1d / 30d);
        Assert.AreEqual(expectedAnnualized, fixRow.AnnualizedFailureProbability,
            Math.Abs(expectedAnnualized) * 1e-12);
        Assert.AreEqual(results.Alternatives[0].AnnualizedFailureProbability - fixRow.AnnualizedFailureProbability,
            fixRow.AnnualizedFailureProbabilityReduction, 0d);
        Assert.IsTrue(fixRow.AnnualizedFailureProbabilityReduction > 0d);

        // The trajectory points flatten every epoch of both alternatives.
        Assert.AreEqual(results.Trajectories[0].Epochs.Count + results.Trajectories[1].Epochs.Count,
            results.TrajectoryPoints.Count);
    }

    /// <summary>
    /// Verifies the run lifecycle: the veto event cancels before work, a pre-canceled token
    /// cancels between evaluations, and both leave the study unestimated with a completion
    /// event.
    /// </summary>
    [TestMethod]
    public void Test_Run_VetoAndCancellation()
    {
        // Arrange — a veto handler.
        (CostBenefitAnalysis study, _, _) = BuildStudy();
        AnalysisRunCompletedEventArgs? completion = null;
        study.AnalysisCompleted += (_, e) => completion = e;
        study.AnalysisStarting += (_, e) => e.Cancel = true;

        // Act / Assert — the veto cancels.
        Assert.ThrowsException<OperationCanceledException>(
            () => study.RunAsync().GetAwaiter().GetResult());
        Assert.IsNotNull(completion);
        Assert.IsTrue(completion!.Cancelled);
        Assert.IsFalse(study.IsEstimated);
        Assert.IsNull(study.Results);

        // A pre-canceled token cancels before the first evaluation.
        (CostBenefitAnalysis canceled, _, _) = BuildStudy();
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsException<OperationCanceledException>(
            () => canceled.RunAsync(null, source.Token).GetAwaiter().GetResult());
        Assert.IsFalse(canceled.IsEstimated);
    }

    /// <summary>
    /// Verifies progress reporting counts distinct evaluations and completes at one hundred
    /// percent.
    /// </summary>
    [TestMethod]
    public void Test_Run_ProgressCountsEvaluations()
    {
        // Arrange — two distinct (system, plan) pairs. The reporter posts through the
        // synchronization context captured at its construction, so the test installs an
        // inline context to observe the reports deterministically.
        (CostBenefitAnalysis study, _, _) = BuildStudy();
        SynchronizationContext? original = SynchronizationContext.Current;
        var reported = new List<double>();
        try
        {
            SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
            var reporter = new SafeProgressReporter();
            reporter.ProgressReported += (_, progress, _) => { lock (reported) reported.Add(progress); };

            // Act
            study.RunAsync(reporter).GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        // Assert
        Assert.AreEqual(2, reported.Count);
        Assert.AreEqual(50d, reported[0], 1e-12);
        Assert.AreEqual(100d, reported[1], 1e-12);
    }

    /// <summary>A synchronization context whose posts execute inline, for deterministic tests.</summary>
    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        /// <inheritdoc/>
        public override void Post(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }

    /// <summary>
    /// Verifies replace-to-edit semantics: assigning options, editing membership, or
    /// re-designating the baseline clears the published results.
    /// </summary>
    [TestMethod]
    public void Test_Run_ReplaceToEdit_ClearsPublishedState()
    {
        // Arrange
        (CostBenefitAnalysis study, RiskReductionAlternative baseline, _) = BuildStudy();
        study.RunAsync().GetAwaiter().GetResult();
        Assert.IsTrue(study.IsEstimated);

        // Act / Assert — options replacement clears.
        study.Options = new CostBenefitOptions(30, 0.05d);
        Assert.IsFalse(study.IsEstimated);
        Assert.IsNull(study.Results);

        // Membership edits clear.
        study.RunAsync().GetAwaiter().GetResult();
        study.Alternatives.Add(new RiskReductionAlternative("Another", baseline.System));
        Assert.IsFalse(study.IsEstimated);
        Assert.IsNull(study.Results);

        // Baseline re-designation clears.
        study.Alternatives.RemoveAt(study.Alternatives.Count - 1);
        study.RunAsync().GetAwaiter().GetResult();
        study.Baseline = baseline;
        Assert.IsFalse(study.IsEstimated);
        Assert.IsNull(study.Results);
    }

    /// <summary>
    /// Verifies author inertness and reproducibility: a study run leaves every referenced
    /// analysis byte-untouched, and two identical runs publish bit-identical economics.
    /// </summary>
    [TestMethod]
    public void Test_Run_AuthorInertness_And_Reproducibility()
    {
        // Arrange — monetized so the compared net present value is a finite number.
        (CostBenefitAnalysis study, RiskReductionAlternative baseline, RiskReductionAlternative fix) =
            BuildStudy(new CostBenefitOptions(30, 0.05d, monetization: new ConsequenceMonetization()));
        string baselineHash = Convert.ToHexString(baseline.System.Components[0].CanonicalHash());
        string fixHash = Convert.ToHexString(fix.System.Components[0].CanonicalHash());
        string baselineXml = baseline.System.Components[0].ToXElement().ToString();

        // Act
        study.RunAsync().GetAwaiter().GetResult();
        double firstNetPresentValue = study.Results!.Alternatives[1].NetPresentValue;
        double firstAnnualized = study.Results.Alternatives[1].AnnualizedFailureProbability;
        study.RunAsync().GetAwaiter().GetResult();

        // Assert — authors untouched, runs bit-identical.
        Assert.AreEqual(baselineHash, Convert.ToHexString(baseline.System.Components[0].CanonicalHash()));
        Assert.AreEqual(fixHash, Convert.ToHexString(fix.System.Components[0].CanonicalHash()));
        Assert.AreEqual(baselineXml, baseline.System.Components[0].ToXElement().ToString());
        Assert.IsFalse(baseline.System.IsEstimated, "The study must not estimate the referenced analyses.");
        Assert.AreEqual(firstNetPresentValue, study.Results!.Alternatives[1].NetPresentValue, 0d);
        Assert.AreEqual(firstAnnualized, study.Results.Alternatives[1].AnnualizedFailureProbability, 0d);
    }
}
