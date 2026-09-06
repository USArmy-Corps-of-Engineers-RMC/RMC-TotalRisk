using System;
using System.Collections.Generic;
using System.Linq;

namespace RMC.TotalRisk.Analyses;

/// <summary>
/// One scheduled intervention of a life-cycle evaluation: from its year onward the named house
/// events hold their configured states and the named hazard replacements are in service —
/// "fix failure mode A now, replace the hazard in year 20."
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// Runtime-only input — never serialized, hashed, or applied to the authored model; the
/// life-cycle query applies the cumulative schedule to throwaway component clones per epoch.
/// Interventions accumulate: a state configured at one year persists until a later
/// intervention overrides the same house event (re-exercise, including reversal, is legal
/// across years; within one intervention each house event and each replacement target appears
/// at most once). Year zero means "now" — applied before the first exposure year.
/// </para>
/// <para>
/// Entries are unconditional exercise plans. A future condition member gating exercise on the
/// state observed at the intervention's year — the decision-rule reading real-options
/// valuation needs, estimated by techniques such as least-squares Monte Carlo — is the named
/// extension seat of this type; until it exists, alternatives are compared as deterministic
/// schedules.
/// </para>
/// </remarks>
public sealed class LifeCycleIntervention
{
    /// <summary>
    /// Initializes one scheduled intervention.
    /// </summary>
    /// <param name="year">The year the intervention takes effect (0 = now), an offset from the start of the horizon.</param>
    /// <param name="houseEvents">The house-event states held from the year onward; null or empty for none.</param>
    /// <param name="hazardReplacements">The hazard replacements in service from the year onward; null or empty for none.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the year is negative.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when no action is supplied, when an action entry is null, when a house event is
    /// addressed more than once, or when a replacement target is addressed more than once.
    /// </exception>
    public LifeCycleIntervention(int year,
        IReadOnlyList<HouseEventState>? houseEvents = null,
        IReadOnlyList<HazardReplacement>? hazardReplacements = null)
    {
        if (year < 0)
            throw new ArgumentOutOfRangeException(nameof(year), "The intervention year must be non-negative.");
        Year = year;

        var houseSnapshot = houseEvents == null ? Array.Empty<HouseEventState>() : houseEvents.ToArray();
        var replacementSnapshot = hazardReplacements == null
            ? Array.Empty<HazardReplacement>()
            : hazardReplacements.ToArray();
        if (houseSnapshot.Length == 0 && replacementSnapshot.Length == 0)
            throw new ArgumentException("The intervention requires at least one action.", nameof(houseEvents));

        var seenHouse = new HashSet<(Guid FunctionId, Guid NodeId)>();
        for (int i = 0; i < houseSnapshot.Length; i++)
        {
            var state = houseSnapshot[i]
                ?? throw new ArgumentException("The house-event list contains a null entry.", nameof(houseEvents));
            if (!seenHouse.Add((state.FunctionId, state.NodeId)))
                throw new ArgumentException(
                    $"The intervention addresses house event '{state.NodeId:D}' of function '{state.FunctionId:D}' more than once.",
                    nameof(houseEvents));
        }

        var seenTargets = new HashSet<Guid>();
        for (int i = 0; i < replacementSnapshot.Length; i++)
        {
            var replacement = replacementSnapshot[i]
                ?? throw new ArgumentException("The hazard-replacement list contains a null entry.", nameof(hazardReplacements));
            if (!seenTargets.Add(replacement.TargetFunctionId))
                throw new ArgumentException(
                    $"The intervention replaces hazard function '{replacement.TargetFunctionId:D}' more than once.",
                    nameof(hazardReplacements));
        }

        HouseEvents = Array.AsReadOnly(houseSnapshot);
        HazardReplacements = Array.AsReadOnly(replacementSnapshot);
    }

    /// <summary>
    /// The year the intervention takes effect, an offset from the start of the horizon
    /// (0 = now, before the first exposure year).
    /// </summary>
    public int Year { get; }

    /// <summary>
    /// The house-event states held from <see cref="Year"/> onward; empty when the intervention
    /// carries only replacements.
    /// </summary>
    public IReadOnlyList<HouseEventState> HouseEvents { get; }

    /// <summary>
    /// The hazard replacements in service from <see cref="Year"/> onward; empty when the
    /// intervention carries only house events.
    /// </summary>
    public IReadOnlyList<HazardReplacement> HazardReplacements { get; }
}
