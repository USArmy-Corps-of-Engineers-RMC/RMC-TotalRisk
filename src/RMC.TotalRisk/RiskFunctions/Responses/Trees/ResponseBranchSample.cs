using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>
    /// One sampled branching response: a common ascending hazard axis and a probability curve for
    /// every modeled end state. Branch probabilities form an exhaustive partition at each hazard.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// </remarks>
    public sealed class ResponseBranchSample
    {
        /// <summary>The absolute tolerance used to validate the exhaustive branch sum.</summary>
        public const double ProbabilitySumTolerance = 1e-10d;

        /// <summary>Initializes and validates an immutable branch sample.</summary>
        /// <param name="hazards">The common strictly ascending hazard axis.</param>
        /// <param name="branches">The branch descriptors.</param>
        /// <param name="probabilities">Branch-major probability ordinates.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when dimensions, hazards, or probabilities are invalid.</exception>
        public ResponseBranchSample(IEnumerable<double> hazards, IEnumerable<ResponseBranchDescriptor> branches,
            IEnumerable<IEnumerable<double>> probabilities)
        {
            if (hazards == null) throw new ArgumentNullException(nameof(hazards));
            if (branches == null) throw new ArgumentNullException(nameof(branches));
            if (probabilities == null) throw new ArgumentNullException(nameof(probabilities));

            var hazardArray = new List<double>(hazards).ToArray();
            var branchArray = new List<ResponseBranchDescriptor>(branches).ToArray();
            var probabilityRows = new List<IReadOnlyList<double>>();
            foreach (var row in probabilities)
            {
                if (row == null) throw new ArgumentException("A branch probability row cannot be null.", nameof(probabilities));
                probabilityRows.Add(Array.AsReadOnly(new List<double>(row).ToArray()));
            }

            ValidateHazards(hazardArray);
            if (branchArray.Length == 0) throw new ArgumentException("A branch sample requires at least one branch.", nameof(branches));
            if (probabilityRows.Count != branchArray.Length)
                throw new ArgumentException("The number of probability rows must equal the number of branches.", nameof(probabilities));

            for (int branch = 0; branch < probabilityRows.Count; branch++)
            {
                if (probabilityRows[branch].Count != hazardArray.Length)
                    throw new ArgumentException("Every branch probability row must match the hazard count.", nameof(probabilities));
            }
            ValidateProbabilities(probabilityRows, hazardArray.Length);

            Hazards = Array.AsReadOnly(hazardArray);
            Branches = Array.AsReadOnly(branchArray);
            Probabilities = new ReadOnlyCollection<IReadOnlyList<double>>(probabilityRows);
        }

        /// <summary>The common strictly ascending hazard axis.</summary>
        public IReadOnlyList<double> Hazards { get; }

        /// <summary>The modeled branch descriptors in output-port order.</summary>
        public IReadOnlyList<ResponseBranchDescriptor> Branches { get; }

        /// <summary>The branch-major conditional-probability ordinates.</summary>
        public IReadOnlyList<IReadOnlyList<double>> Probabilities { get; }

        /// <summary>Validates a finite, strictly ascending hazard axis.</summary>
        /// <param name="hazards">The copied hazard axis.</param>
        /// <exception cref="ArgumentException">Thrown when the axis is empty, non-finite, or not strictly ascending.</exception>
        private static void ValidateHazards(IReadOnlyList<double> hazards)
        {
            if (hazards.Count == 0) throw new ArgumentException("A branch sample requires at least one hazard ordinate.", nameof(hazards));
            for (int i = 0; i < hazards.Count; i++)
            {
                if (!double.IsFinite(hazards[i])) throw new ArgumentException("Hazard ordinates must be finite.", nameof(hazards));
                if (i > 0 && hazards[i] <= hazards[i - 1])
                    throw new ArgumentException("Hazard ordinates must be strictly ascending.", nameof(hazards));
            }
        }

        /// <summary>Validates unit-interval ordinates and exhaustive probability sums.</summary>
        /// <param name="rows">The branch-major rows.</param>
        /// <param name="hazardCount">The number of hazard ordinates.</param>
        /// <exception cref="ArgumentException">Thrown when an ordinate or sum is invalid.</exception>
        private static void ValidateProbabilities(IReadOnlyList<IReadOnlyList<double>> rows, int hazardCount)
        {
            for (int hazard = 0; hazard < hazardCount; hazard++)
            {
                double sum = 0d;
                double compensation = 0d;
                for (int branch = 0; branch < rows.Count; branch++)
                {
                    double value = rows[branch][hazard];
                    if (!double.IsFinite(value) || value < 0d || value > 1d)
                        throw new ArgumentException("Every branch probability must be finite and in [0, 1].", nameof(rows));
                    double adjusted = value - compensation;
                    double next = sum + adjusted;
                    compensation = (next - sum) - adjusted;
                    sum = next;
                }
                if (Math.Abs(sum - 1d) > ProbabilitySumTolerance)
                    throw new ArgumentException("Branch probabilities must sum to one at every hazard ordinate.", nameof(rows));
            }
        }
    }
}
