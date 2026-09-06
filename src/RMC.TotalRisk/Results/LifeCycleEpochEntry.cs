using System;
using System.Collections.Generic;
using System.Linq;

namespace RMC.TotalRisk.Results;

/// <summary>
/// One scope row of a life-cycle epoch — the system or one component: the epoch's annualized
/// failure probability and its per-type expected annual consequences.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// A plain query result — never serialized. Values are the deterministic mean-only answers of
/// the epoch's configured quantification; consequence position 0 is the primary declared type.
/// </para>
/// </remarks>
public sealed class LifeCycleEpochEntry
{
    /// <summary>
    /// Initializes one scope row.
    /// </summary>
    /// <param name="name">The scope display label.</param>
    /// <param name="failureProbability">The epoch's annualized failure probability.</param>
    /// <param name="expectedConsequences">The per-type expected annual consequences (position 0 = primary).</param>
    /// <exception cref="ArgumentNullException">Thrown when the name or the consequence list is null.</exception>
    public LifeCycleEpochEntry(string name, double failureProbability,
        IReadOnlyList<double> expectedConsequences)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        FailureProbability = failureProbability;
        if (expectedConsequences == null) throw new ArgumentNullException(nameof(expectedConsequences));
        ExpectedConsequences = Array.AsReadOnly(expectedConsequences.ToArray());
    }

    /// <summary>
    /// The scope display label — "System" or the component's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The epoch's annualized failure probability at this scope.
    /// </summary>
    public double FailureProbability { get; }

    /// <summary>
    /// The per-type expected annual consequences at this scope (position 0 = the primary
    /// declared type).
    /// </summary>
    public IReadOnlyList<double> ExpectedConsequences { get; }
}
