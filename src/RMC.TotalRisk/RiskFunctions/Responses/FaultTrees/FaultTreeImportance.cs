using System;
using System.Collections.Generic;
using System.Linq;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.RiskFunctions.Responses.FaultTrees
{
    /// <summary>
    /// The exact fault-tree importance measures — Birnbaum, criticality, Fussell-Vesely, risk
    /// achievement worth, and risk reduction worth — computed by exact algebra on the frozen
    /// decision diagram: two conditional evaluations per unified basic-event variable, with the
    /// variable forced certain (q = 1) and impossible (q = 0), on top of one baseline
    /// evaluation.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// The measures are exact and deterministic — no simulation and no seed. They stand beside
    /// the Monte Carlo node-importance sweep (<see cref="TreeNodeImportance"/>), which serves
    /// both tree kinds and measures knowledge-uncertainty influence; these measures are the
    /// standard probabilistic-risk-assessment structural set, defined for coherent fault trees
    /// only — a non-coherent tree (an Xor gate) is refused loudly, exactly like the minimal
    /// cut-set surface. A variable that shares logic through transfers is one unified variable
    /// with one entry; a variable reduced out of the frozen diagram reports a Birnbaum of
    /// exactly zero (its two conditional evaluations coincide). Cost is two allocation-free
    /// linear diagram passes per variable, cheap even at the largest committed diagrams.
    /// </para>
    /// </remarks>
    public static class FaultTreeImportance
    {
        /// <summary>
        /// Computes the exact importance measures for every unified basic-event variable at one
        /// authored hazard level.
        /// </summary>
        /// <param name="response">The valid, coherent fault-tree response.</param>
        /// <param name="options">The analysis options (hazard level; optional percentile).</param>
        /// <returns>The exact measures, one entry per unified variable in plan ordinal order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the hazard level is not an authored level.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the response is invalid, has no compiled decision diagram, or is
        /// non-coherent (an Xor gate is present — the exact measures are undefined).
        /// </exception>
        public static FaultTreeImportanceResult Compute(FaultTreeResponse response, FaultTreeImportanceOptions options)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (options == null) throw new ArgumentNullException(nameof(options));
            var validation = response.Validate();
            if (!validation.IsValid)
            {
                string errors = string.Join(" ", validation.ValidationMessages.Where(message =>
                    message.StartsWith("Error:", StringComparison.Ordinal)));
                throw new InvalidOperationException(
                    $"The fault-tree response '{response.Name}' is invalid. Call Validate() and correct the reported errors. {errors}");
            }
            bool authoredLevel = false;
            for (int i = 0; i < response.HazardLevels.Count; i++)
            {
                if (response.HazardLevels[i] == options.HazardLevel) { authoredLevel = true; break; }
            }
            if (!authoredLevel)
            {
                throw new ArgumentException(
                    $"The hazard level {options.HazardLevel} is not an authored level of fault-tree response '{response.Name}'.",
                    nameof(options));
            }

            var plan = response.GetOccurrencePlan();
            if (!plan.IsCoherent)
            {
                throw new InvalidOperationException(
                    "Exact importance measures are not defined for a non-coherent fault tree (an Xor gate is present); " +
                    "use the Monte Carlo node-importance sweep instead.");
            }
            var frozen = plan.FrozenBdd
                ?? throw new InvalidOperationException(
                    "The fault-tree response has no compiled decision diagram. Call Validate() and correct the reported errors.");

            // The baseline: every source at the options percentile (−1 = the mean).
            int count = plan.Variables.Count;
            var percentiles = new double[count];
            var probabilities = new double[count];
            var scratch = new double[frozen.NodeCount];
            Array.Fill(percentiles, options.Percentile);
            double top = response.EvaluateImportanceSample(plan, options.HazardLevel, percentiles, probabilities, scratch);

            // Two exact conditional evaluations per variable on a caller-owned copy of the
            // probability vector; the frozen diagram is immutable and shared safely.
            var entries = new List<FaultTreeImportanceEntry>(count);
            for (int j = 0; j < count; j++)
            {
                double q = probabilities[j];
                probabilities[j] = 1d;
                double atOne = ClampRoundoff(frozen.Evaluate(probabilities, scratch));
                probabilities[j] = 0d;
                double atZero = ClampRoundoff(frozen.Evaluate(probabilities, scratch));
                probabilities[j] = q;

                double birnbaum = atOne - atZero;
                double criticality = top > 0d ? birnbaum * q / top : double.NaN;
                double fussellVesely = top > 0d ? 1d - atZero / top : double.NaN;
                double achievement = top > 0d ? atOne / top : double.NaN;
                double reduction = top > 0d
                    ? (atZero > 0d ? top / atZero : double.PositiveInfinity)
                    : double.NaN;

                var variable = plan.Variables[j];
                entries.Add(new FaultTreeImportanceEntry(variable.SourceNode.Id, variable.SourceNode.Name,
                    variable.FirstOccurrence!.CanonicalPath, q, birnbaum, criticality, fussellVesely,
                    achievement, reduction));
            }
            return new FaultTreeImportanceResult(options.HazardLevel, options.Percentile, top, entries.AsReadOnly());
        }

        /// <summary>
        /// Clamps floating-point roundoff immediately outside the unit interval (the top-event
        /// clamp convention shared with the sampling surface).
        /// </summary>
        /// <param name="value">The computed conditional top-event probability.</param>
        /// <returns>The unit-interval value.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the value is outside the tolerance band.</exception>
        private static double ClampRoundoff(double value)
        {
            if (value < 0d && value >= -ResponseBranchSample.ProbabilitySumTolerance) return 0d;
            if (value > 1d && value <= 1d + ResponseBranchSample.ProbabilitySumTolerance) return 1d;
            if (value < 0d || value > 1d)
                throw new InvalidOperationException("A conditional fault-tree top-event probability fell outside [0, 1].");
            return value;
        }
    }
}
