using System;

namespace RMC.TotalRisk.Models.RiskAnalysis.Graph
{
    /// <summary>
    /// An immutable, typed input connection: the upstream element whose output a consuming
    /// element reads, and which of that element's output ports it reads from.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Connections are stored on the consumer (fan-in is the bounded side of a risk graph: one
    /// input everywhere, two typed inputs on a future bivariate response), and fan-out is derived
    /// by the graph container. Rewiring replaces the connection object — the owning element
    /// property raises change notification. <see cref="SourcePort"/> is 0 for every univariate
    /// output; a bivariate hazard (Phase 11) additionally exposes port 1
    /// (<c>HazardDimension.Secondary</c>). Links are object references in memory and are
    /// serialized dual Id + Name by the owning element (the Hydrologics pattern).
    /// </para>
    /// </remarks>
    public sealed class RiskConnection : IEquatable<RiskConnection>
    {
        /// <summary>
        /// Initializes a connection to an upstream element output.
        /// </summary>
        /// <param name="source">The upstream element whose output is consumed.</param>
        /// <param name="sourcePort">The source output port; 0 (the primary output) unless the source is bivariate.</param>
        /// <exception cref="ArgumentNullException">Thrown when the source is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the port is negative.</exception>
        public RiskConnection(IRiskElement source, int sourcePort = 0)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (sourcePort < 0) throw new ArgumentOutOfRangeException(nameof(sourcePort), "The source port cannot be negative.");

            Source = source;
            SourcePort = sourcePort;
        }

        /// <summary>
        /// Gets the upstream element whose output is consumed.
        /// </summary>
        public IRiskElement Source { get; }

        /// <summary>
        /// Gets the source output port: 0 is the primary output; 1 is the secondary output of a
        /// bivariate hazard (Phase 11).
        /// </summary>
        public int SourcePort { get; }

        /// <summary>
        /// Determines equality: the same source element instance and the same port.
        /// </summary>
        /// <param name="other">The connection to compare against.</param>
        /// <returns>True when both connections reference the same source instance and port.</returns>
        public bool Equals(RiskConnection? other)
        {
            return other is not null && ReferenceEquals(Source, other.Source) && SourcePort == other.SourcePort;
        }

        /// <summary>
        /// Determines equality against an arbitrary object.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>True when the object is an equal <see cref="RiskConnection"/>.</returns>
        public override bool Equals(object? obj)
        {
            return Equals(obj as RiskConnection);
        }

        /// <summary>
        /// Computes the hash code from the source reference and port.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Source, SourcePort);
        }
    }
}
