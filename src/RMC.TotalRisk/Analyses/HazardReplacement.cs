using System;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Analyses;

/// <summary>
/// One hazard-replacement action of a life-cycle intervention: from its year onward, every
/// component hazard element whose assigned function carries the target id evaluates the
/// replacement function instead — the seat a nonstationary per-epoch hazard fit occupies.
/// </summary>
/// <remarks>
/// <para>
///     <b>Authors:</b>
///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
/// </para>
/// <para>
/// Runtime-only input — never serialized, hashed, or applied to the authored model; the
/// life-cycle query assigns a factory clone of the replacement to throwaway component clones,
/// so the authored instance is never wired into query structures. The replacement must match
/// the replaced hazard's arity (univariate for univariate, bivariate for bivariate); the query
/// refuses a mismatch loudly.
/// </para>
/// </remarks>
public sealed class HazardReplacement
{
    /// <summary>
    /// Initializes one hazard-replacement action.
    /// </summary>
    /// <param name="targetFunctionId">The id of the hazard function being replaced.</param>
    /// <param name="replacement">The already-authored hazard function that takes its place.</param>
    /// <exception cref="ArgumentException">Thrown when the target id is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the replacement is null.</exception>
    public HazardReplacement(Guid targetFunctionId, IHazardFunction replacement)
    {
        if (targetFunctionId == Guid.Empty)
            throw new ArgumentException("The hazard replacement requires a target function id.", nameof(targetFunctionId));
        TargetFunctionId = targetFunctionId;
        Replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }

    /// <summary>
    /// The id of the hazard function being replaced — matched against the live element
    /// assignment at the intervention's year, so chained replacements target the function in
    /// service, not the original.
    /// </summary>
    public Guid TargetFunctionId { get; }

    /// <summary>
    /// The already-authored hazard function that takes the target's place. Referenced, not
    /// owned: the query clones it per epoch at application.
    /// </summary>
    public IHazardFunction Replacement { get; }
}
