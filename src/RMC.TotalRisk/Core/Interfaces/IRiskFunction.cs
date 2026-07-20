using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Linq;
using Numerics.Distributions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// The kernel contract of the model library: a risk input function. Risk analysis integrates
    /// over hazard, transform, response, and consequence functions — every one of them implements
    /// this contract, and the four cluster interfaces (<c>IHazardFunction</c>,
    /// <c>ITransformFunction</c>, <c>IResponseFunction</c>, <c>IConsequenceFunction</c>) extend it
    /// with their cluster-specific sampling shapes.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// This interface exists because functions are consumed polymorphically by the sampler/seed
    /// orchestration: components and failure modes walk heterogeneous function chains calling
    /// <see cref="SetupSampler(int, int, SamplingScheme)"/>, <see cref="SamplingDimensions"/>, and
    /// <see cref="CanonicalHash"/> uniformly. There is deliberately no broader "model" root
    /// abstraction — the engine consumes functions by role, and no consumer of "any model" exists
    /// (architecture doc v0.8).
    /// </para>
    /// <para>
    /// <see cref="Name"/>, <see cref="Description"/>, and the axis labels are identity/display
    /// metadata: they serialize with the function and label results, but they are stripped by
    /// <see cref="CanonicalizationRules.ModelRules"/> and can never perturb Monte Carlo seeds.
    /// </para>
    /// </remarks>
    public interface IRiskFunction : INotifyPropertyChanged
    {
        /// <summary>
        /// The function's persistent identity — the rename-proof key consuming layers reference it
        /// by when a function is stored as a project element in its own right and a risk graph
        /// merely points at it rather than owning its serialized content.
        /// </summary>
        /// <remarks>
        /// Serialized, but stripped by <see cref="CanonicalizationRules.ModelRules"/> exactly as
        /// <see cref="Name"/> is: two functions with identical content and different ids hash
        /// identically, so identity can never perturb a Monte Carlo seed. A deep copy keeps the
        /// id (it is the same logical function); duplication flows call <see cref="AssignNewId"/>.
        /// </remarks>
        Guid Id { get; }

        /// <summary>
        /// Assigns a fresh <see cref="Id"/> — used when a function is duplicated into a project
        /// that already contains the original, rather than cloned as the same logical function.
        /// </summary>
        void AssignNewId();

        /// <summary>
        /// The function's display name. Identity metadata — serialized, used to label results,
        /// never hashed.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The function's description. Identity metadata — serialized, never hashed.
        /// </summary>
        string Description { get; set; }

        /// <summary>
        /// The hazard type this function's input axis represents (e.g., "Peak Flow", "Stage").
        /// Axis-label metadata — serialized, never hashed.
        /// </summary>
        string SpecifiedHazard { get; set; }

        /// <summary>
        /// The unit of the input hazard axis (e.g., "ft³/s", "ft"). Axis-label metadata —
        /// serialized, never hashed.
        /// </summary>
        string HazardUnit { get; set; }

        /// <summary>
        /// Determines whether the function carries no knowledge uncertainty — a deterministic
        /// function returns the same curve for every percentile and realization.
        /// </summary>
        bool IsDeterministic { get; }

        /// <summary>
        /// The number of independent uniform draws this function consumes per realization — the
        /// column count of the percentile matrix allocated by
        /// <see cref="SetupSampler(int, int, SamplingScheme)"/>. Zero for deterministic functions
        /// and for posterior-indexed parametric functions.
        /// </summary>
        int SamplingDimensions { get; }

        /// <summary>
        /// Pre-allocates the per-realization sampler: an N×D percentile matrix for this function,
        /// filled per the sampling scheme. Idempotent — safe to call before every analysis run.
        /// </summary>
        /// <param name="sampleSize">The number of realizations N. Must be positive.</param>
        /// <param name="seed">
        /// The seed for this function's percentile stream, derived by the caller from content-based
        /// seeding (<see cref="SeedHelpers.HashCombine(int, byte[], int)"/>).
        /// </param>
        /// <param name="scheme">The sampling scheme filling the matrix.</param>
        void SetupSampler(int sampleSize, int seed, SamplingScheme scheme);

        /// <summary>
        /// Computes the function's knowledge-uncertainty summary — mean, median/mode, and
        /// confidence-interval curves — as a Numerics <see cref="UncertaintyAnalysisResults"/>.
        /// </summary>
        /// <param name="confidenceIntervalWidth">
        /// The two-sided confidence-interval width in (0, 1); default 0.9 (bounds at the 5th and
        /// 95th percentiles).
        /// </param>
        /// <returns>
        /// The uncertainty summary, or null when the function has no meaningful uncertainty
        /// representation (the non-failure response sentinel).
        /// </returns>
        /// <remarks>
        /// Built from the SAME percentile/posterior machinery per-realization sampling uses, so
        /// function-editor plots, the risk analysis's uncertainty options, and API previews are
        /// consistent by construction. Tabular functions evaluate exact co-monotonic percentile
        /// curves (deterministic — no simulation); parametric functions surface their stored
        /// bootstrap or imported posterior.
        /// </remarks>
        UncertaintyAnalysisResults? ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9);

        /// <summary>
        /// Validates the current state of the function and reports any issues found.
        /// </summary>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item>
        /// <description><c>IsValid</c>: <c>true</c> if the function passes all validation checks; otherwise <c>false</c>.</description>
        /// </item>
        /// <item>
        /// <description><c>ValidationMessages</c>: messages describing validation errors ("Error: …", invalidating) and warnings ("Warning: …", advisory). Empty when the function is valid with no advisories.</description>
        /// </item>
        /// </list>
        /// </returns>
        (bool IsValid, List<string> ValidationMessages) Validate();

        /// <summary>
        /// Serializes the function to an XElement. This is the persistence contract AND the
        /// canonical-hash identity surface: attribute names and owned-child order are append-only.
        /// </summary>
        /// <returns>The serialized form; element name is the concrete type name.</returns>
        XElement ToXElement();

        /// <summary>
        /// Computes the function's canonical SHA-256 content hash — the stable, name-free identity
        /// that content-based Monte Carlo seeding derives from.
        /// </summary>
        /// <returns>The 32-byte SHA-256 hash of the canonicalized <see cref="ToXElement"/> form.</returns>
        byte[] CanonicalHash();
    }
}
