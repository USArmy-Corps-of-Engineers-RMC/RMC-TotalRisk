using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Linq;
using RMC.TotalRisk.Systems.Components.Graph;

namespace RMC.TotalRisk.Core.Interfaces
{
    /// <summary>
    /// A node of a system component's risk graph: a hazard, transform, response, or consequence
    /// element wrapping its risk function(s), with typed input connections to upstream elements.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Mirrors the Hydrologics <c>IBasinElement</c> contract (architecture doc v0.9): elements
    /// connect by object reference and form a directed acyclic graph validated by the owning
    /// <see cref="ComponentGraph"/>. Elements are the authoring/topology surface only — they are
    /// deliberately <b>not</b> <see cref="IRiskFunction"/> and expose no canonical hash: seeding
    /// identity rides the projected <c>SystemComponent</c>/<c>FailureMode</c> content, so element
    /// names, ids, and canvas positions can never perturb Monte Carlo results (the v1.0
    /// canvas-position seed bug is structurally unreachable).
    /// </para>
    /// <para>
    /// <see cref="Name"/>, <see cref="Description"/>, and the canvas position pair are
    /// identity/display metadata. <see cref="LeftPosition"/>/<see cref="TopPosition"/> reuse the
    /// v1.0 attribute names, which the canonicalization strip list already covers.
    /// </para>
    /// </remarks>
    public interface IRiskElement : INotifyPropertyChanged, ICloneable
    {
        /// <summary>
        /// The element's persistent identity, used to serialize inter-element references. Shared
        /// by clones (a clone is the same logical element); duplication flows call
        /// <see cref="AssignNewId"/>.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Assigns a fresh <see cref="Id"/> — used when an element is duplicated into the same
        /// graph rather than cloned into an isolated copy.
        /// </summary>
        void AssignNewId();

        /// <summary>
        /// The element's display name — unique within its graph (enforced by the graph's name
        /// authority). Identity metadata: serialized, used as the human-readable link fallback,
        /// never hashed.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The element's description. Identity metadata — serialized, never hashed.
        /// </summary>
        string Description { get; set; }

        /// <summary>
        /// The canvas X position for visualization layers. Passive display metadata — serialized,
        /// never hashed, never consulted by any model-library logic.
        /// </summary>
        double LeftPosition { get; set; }

        /// <summary>
        /// The canvas Y position for visualization layers. Passive display metadata — serialized,
        /// never hashed, never consulted by any model-library logic.
        /// </summary>
        double TopPosition { get; set; }

        /// <summary>
        /// The number of input slots the element exposes: 0 for hazards, 1 elsewhere (2 for a
        /// bivariate response when Phase 11 lands).
        /// </summary>
        int InputCount { get; }

        /// <summary>
        /// The number of output ports the element exposes: 0 for consequences (terminal), 1
        /// elsewhere (2 for a bivariate hazard when Phase 11 lands).
        /// </summary>
        int OutputCount { get; }

        /// <summary>
        /// Enumerates the element's populated STRUCTURAL input connections — the path edges of
        /// the graph. Empty for hazard elements and for unconnected consumers. Binding overrides
        /// (the consequence element's <c>HazardSource</c>) are deliberately excluded: a binding
        /// reads a signal from an element already on the path, it does not create a path edge.
        /// </summary>
        /// <returns>The populated structural connections.</returns>
        IEnumerable<RiskConnection> GetInputConnections();

        /// <summary>
        /// Enumerates the risk function(s) the element wraps. Empty while unset; consequence
        /// elements may yield several (one per consequence type).
        /// </summary>
        /// <returns>The wrapped functions.</returns>
        IEnumerable<IRiskFunction> GetFunctions();

        /// <summary>
        /// Validates the element and reports any issues found.
        /// </summary>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item>
        /// <description><c>IsValid</c>: <c>true</c> if the element passes all validation checks; otherwise <c>false</c>.</description>
        /// </item>
        /// <item>
        /// <description><c>ValidationMessages</c>: messages describing validation errors ("Error: …", invalidating) and warnings ("Warning: …", advisory).</description>
        /// </item>
        /// </list>
        /// </returns>
        (bool IsValid, List<string> ValidationMessages) Validate();

        /// <summary>
        /// Serializes the element to an XElement: base identity attributes, connection references
        /// (dual Id + Name + port), and the wrapped function(s) inline. This is the persistence
        /// surface only — element XML is not a canonical-hash surface.
        /// </summary>
        /// <returns>The serialized form; element name is the concrete type name.</returns>
        XElement ToXElement();
    }
}
