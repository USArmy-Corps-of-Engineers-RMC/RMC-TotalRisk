using System;

namespace RMC.TotalRisk.RiskFunctions.Responses.Trees
{
    /// <summary>Describes one stable modeled end-state branch of a branching response.</summary>
    public sealed class ResponseBranchDescriptor
    {
        /// <summary>Initializes a response branch descriptor.</summary>
        /// <param name="id">The persistent branch identity.</param>
        /// <param name="name">The display name.</param>
        /// <param name="isFailure">Whether the branch contributes to aggregate failure.</param>
        /// <param name="outputPort">The graph output port reserved for the branch.</param>
        /// <exception cref="ArgumentException">Thrown when the id is empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the output port is negative.</exception>
        public ResponseBranchDescriptor(Guid id, string name, bool isFailure, int outputPort)
        {
            if (id == Guid.Empty) throw new ArgumentException("A response branch requires a non-empty id.", nameof(id));
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (outputPort < 0) throw new ArgumentOutOfRangeException(nameof(outputPort), "An output port cannot be negative.");
            Id = id;
            Name = name;
            IsFailure = isFailure;
            OutputPort = outputPort;
        }

        /// <summary>The persistent branch identity.</summary>
        public Guid Id { get; }

        /// <summary>The display name.</summary>
        public string Name { get; }

        /// <summary>Whether the branch contributes to aggregate failure.</summary>
        public bool IsFailure { get; }

        /// <summary>The graph output port reserved for the branch.</summary>
        public int OutputPort { get; }
    }
}
