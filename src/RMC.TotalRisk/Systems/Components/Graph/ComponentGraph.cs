using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Linq;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;

namespace RMC.TotalRisk.Systems.Components.Graph
{
    /// <summary>
    /// The typed, validated, acyclic graph of risk elements inside one system component: one
    /// hazard root, transform/response chains, and terminal consequence elements. Owns element
    /// membership, name uniqueness, topology derivation (fan-out, topological order, upstream
    /// paths), structural validation, self-contained serialization, and deep cloning.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Mirrors the Hydrologics <c>BasinModel</c> container (architecture doc v0.9): elements
    /// connect by object reference (consumers store their inputs; fan-out is derived), a Kahn
    /// topological sort doubles as cycle detection, deserialization is construct-then-resolve
    /// (dual Id + Name references through <see cref="RiskElementResolver"/>), and cloning
    /// re-links an isolated copy through an original→clone map. The model library has no
    /// dependency on the UI's DAG.dll — the future UI binds its <c>RiskDiagram</c> controls to
    /// this model type.
    /// </para>
    /// <para>
    /// The graph carries no name, no canonical hash, and no compute: it is the authoring/topology
    /// surface of its owning <c>SystemComponent</c>, which projects <c>FailureMode</c> chains
    /// from the topology and hashes the projected content. Element declared order (the
    /// <see cref="Elements"/> list) is semantic — it fixes the projected failure-mode order.
    /// </para>
    /// </remarks>
    public class ComponentGraph : INotifyPropertyChanged, IRiskElementNameAuthority
    {
        #region Construction

        /// <summary>
        /// Initializes an empty component graph.
        /// </summary>
        public ComponentGraph()
        {
        }

        /// <summary>
        /// Restores a component graph from its serialized form: constructs every recognized child
        /// element (unknown element types are skipped for forward compatibility — connections
        /// referencing a skipped element surface loudly through the Id-authoritative resolver or
        /// dangling-connection validation), then resolves every pending connection reference.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement()"/>.</param>
        /// <param name="resolver">
        /// The function resolver, required only when the form was written
        /// <see cref="RiskSerializationMode.ByReference"/>; null for self-contained forms. It
        /// re-attaches the live stored functions, so edits made to them are seen through the graph.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown on duplicate element Ids or names, on a stale serialized connection Id, or when
        /// a recognized element's wrapped function cannot be reconstructed.
        /// </exception>
        public ComponentGraph(XElement xElement, IRiskFunctionResolver? resolver = null)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));

            var elementsContainer = xElement.Element(nameof(Elements));
            if (elementsContainer != null)
            {
                foreach (var child in elementsContainer.Elements())
                {
                    var element = RiskElementFactory.CreateFromXElement(child, resolver);
                    if (element == null) continue;
                    AddElement(element);
                }
            }

            var elementResolver = new RiskElementResolver(GetElementById, GetElement);
            for (int i = 0; i < _elements.Count; i++)
            {
                if (_elements[i] is RiskElementBase baseElement)
                {
                    baseElement.ResolveDeserializedReferences(elementResolver);
                }
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// The element list in declared order — the semantic order that fixes projected
        /// failure-mode order.
        /// </summary>
        private readonly List<IRiskElement> _elements = new List<IRiskElement>();

        /// <summary>
        /// Cached Kahn order; null after a failed (cyclic) sort. Rebuilt lazily after
        /// <see cref="InvalidateTopology"/>.
        /// </summary>
        private List<IRiskElement>? _sortedElements;

        /// <summary>
        /// Distinguishes "sort not yet attempted" from "sort failed (cyclic)".
        /// </summary>
        private bool _sortAttempted;

        /// <summary>
        /// Cached derived fan-out map (source → consumers in declared order). Rebuilt lazily.
        /// </summary>
        private Dictionary<IRiskElement, List<IRiskElement>>? _downstreamMap;

        /// <summary>
        /// Cached Id lookup. Rebuilt lazily; invalidated when an element re-rolls its Id.
        /// </summary>
        private Dictionary<Guid, IRiskElement>? _idMap;

        /// <summary>
        /// Gets the elements in declared order. Declared order is semantic: consequence elements
        /// project failure modes in this order.
        /// </summary>
        public IReadOnlyList<IRiskElement> Elements
        {
            get { return _elements; }
        }

        /// <summary>
        /// Gets the elements in topological (upstream → downstream) order, or null when the graph
        /// is cyclic. Computed lazily and cached until the topology changes.
        /// </summary>
        public IReadOnlyList<IRiskElement>? SortedElements
        {
            get
            {
                if (!_sortAttempted) TopologicalSort();
                return _sortedElements;
            }
        }

        /// <summary>
        /// Raised when the element membership changes. Passive contract — headless callers need
        /// not subscribe.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Membership

        /// <summary>
        /// Adds an element: enforces Id and (non-empty) name uniqueness, attaches the name
        /// authority, and invalidates the topology caches.
        /// </summary>
        /// <param name="element">The element to add.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the element (or its Id or non-empty name) is already present.</exception>
        public void AddElement(IRiskElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (_elements.Contains(element))
            {
                throw new InvalidOperationException($"The element '{element.Name}' is already in the graph.");
            }
            if (GetElementById(element.Id) != null)
            {
                throw new InvalidOperationException($"An element with Id '{element.Id:D}' already exists in the graph.");
            }
            if (!IsNameAvailable(element.Name, element))
            {
                throw new InvalidOperationException($"An element named '{element.Name}' already exists in the graph.");
            }

            _elements.Add(element);
            if (element is RiskElementBase baseElement) baseElement.NameAuthority = this;
            element.PropertyChanged += ElementPropertyChanged;
            _idMap = null;
            InvalidateTopology();
            RaisePropertyChange(nameof(Elements));
        }

        /// <summary>
        /// Removes an element: detaches the name authority and invalidates the topology caches.
        /// Connections in remaining elements that referenced it are left in place — validation
        /// reports them as dangling.
        /// </summary>
        /// <param name="element">The element to remove.</param>
        /// <returns>True when the element was present and removed.</returns>
        public bool RemoveElement(IRiskElement element)
        {
            if (element == null) return false;
            if (!_elements.Remove(element)) return false;

            if (element is RiskElementBase baseElement) baseElement.NameAuthority = null;
            element.PropertyChanged -= ElementPropertyChanged;
            _idMap = null;
            InvalidateTopology();
            RaisePropertyChange(nameof(Elements));
            return true;
        }

        /// <summary>
        /// Finds an element by name (ordinal comparison).
        /// </summary>
        /// <param name="name">The element name; may be null.</param>
        /// <returns>The element, or null when absent.</returns>
        public IRiskElement? GetElement(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < _elements.Count; i++)
            {
                if (string.Equals(_elements[i].Name, name, StringComparison.Ordinal)) return _elements[i];
            }
            return null;
        }

        /// <summary>
        /// Finds an element by its persistent Id.
        /// </summary>
        /// <param name="id">The element Id.</param>
        /// <returns>The element, or null when absent.</returns>
        public IRiskElement? GetElementById(Guid id)
        {
            if (_idMap == null)
            {
                var map = new Dictionary<Guid, IRiskElement>(_elements.Count);
                for (int i = 0; i < _elements.Count; i++)
                {
                    map[_elements[i].Id] = _elements[i];
                }
                _idMap = map;
            }
            return _idMap.TryGetValue(id, out var element) ? element : null;
        }

        /// <summary>
        /// Enumerates the elements of a concrete type, in declared order.
        /// </summary>
        /// <typeparam name="T">The element type to filter by.</typeparam>
        /// <returns>The matching elements.</returns>
        public IEnumerable<T> GetElements<T>() where T : IRiskElement
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                if (_elements[i] is T match) yield return match;
            }
        }

        #endregion

        #region Name Authority

        /// <summary>
        /// Determines whether a name is available in this graph. Empty names are always
        /// available — unnamed elements are structurally allowed and reported by validation.
        /// </summary>
        /// <param name="name">The candidate name.</param>
        /// <param name="excluding">An element to exclude from the check (the element being renamed).</param>
        /// <returns>True when no other element holds the name (ordinal comparison).</returns>
        public bool IsNameAvailable(string name, IRiskElement? excluding = null)
        {
            if (string.IsNullOrEmpty(name)) return true;
            for (int i = 0; i < _elements.Count; i++)
            {
                if (ReferenceEquals(_elements[i], excluding)) continue;
                if (string.Equals(_elements[i].Name, name, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        /// Produces a unique name from a base name: the base name itself when free, otherwise
        /// "base (2)", "base (3)", ….
        /// </summary>
        /// <param name="baseName">The desired base name.</param>
        /// <returns>A name no current element holds.</returns>
        public string GetUniqueName(string baseName)
        {
            string candidate = baseName ?? string.Empty;
            if (IsNameAvailable(candidate)) return candidate;
            for (int i = 2; ; i++)
            {
                string numbered = $"{candidate} ({i})";
                if (IsNameAvailable(numbered)) return numbered;
            }
        }

        /// <summary>
        /// Renames an element without throwing: false when the element is not in this graph or
        /// the name is taken.
        /// </summary>
        /// <param name="element">The element to rename.</param>
        /// <param name="newName">The new name.</param>
        /// <returns>True when the rename was applied.</returns>
        public bool TryRenameElement(IRiskElement element, string newName)
        {
            if (element == null || !_elements.Contains(element)) return false;
            if (!IsNameAvailable(newName ?? string.Empty, element)) return false;
            element.Name = newName ?? string.Empty;
            return true;
        }

        #endregion

        #region Topology

        /// <summary>
        /// Sorts the elements upstream → downstream with Kahn's algorithm, caching the result.
        /// Cycle detection is folded in: a cycle leaves unsortable elements behind.
        /// </summary>
        /// <returns>True when the graph is acyclic; false when a cycle exists (the sorted list is then null).</returns>
        public bool TopologicalSort()
        {
            _sortAttempted = true;
            var downstream = DownstreamMap;

            var inDegree = new Dictionary<IRiskElement, int>(_elements.Count);
            for (int i = 0; i < _elements.Count; i++)
            {
                inDegree[_elements[i]] = 0;
            }
            for (int i = 0; i < _elements.Count; i++)
            {
                foreach (var connection in _elements[i].GetInputConnections())
                {
                    if (inDegree.ContainsKey(connection.Source)) inDegree[_elements[i]]++;
                }
            }

            var queue = new Queue<IRiskElement>();
            for (int i = 0; i < _elements.Count; i++)
            {
                if (inDegree[_elements[i]] == 0) queue.Enqueue(_elements[i]);
            }

            var sorted = new List<IRiskElement>(_elements.Count);
            while (queue.Count > 0)
            {
                var element = queue.Dequeue();
                sorted.Add(element);
                if (!downstream.TryGetValue(element, out var consumers)) continue;
                for (int i = 0; i < consumers.Count; i++)
                {
                    if (--inDegree[consumers[i]] == 0) queue.Enqueue(consumers[i]);
                }
            }

            if (sorted.Count != _elements.Count)
            {
                _sortedElements = null;
                return false;
            }
            _sortedElements = sorted;
            return true;
        }

        /// <summary>
        /// Enumerates the elements consuming an element's outputs (the derived fan-out), in
        /// declared order.
        /// </summary>
        /// <param name="element">The source element.</param>
        /// <returns>The downstream consumers; empty when none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public IEnumerable<IRiskElement> GetDownstreamElements(IRiskElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            return DownstreamMap.TryGetValue(element, out var consumers)
                ? consumers
                : (IEnumerable<IRiskElement>)Array.Empty<IRiskElement>();
        }

        /// <summary>
        /// Walks an element's primary structural inputs up to the root, returning the path
        /// root-first and ending with the element itself. Cycle-guarded; the walk stops at an
        /// element with no in-graph input.
        /// </summary>
        /// <param name="element">The element to walk from.</param>
        /// <returns>The upstream path, root-first, ending with the element.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public IReadOnlyList<IRiskElement> GetUpstreamPath(IRiskElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            var path = new List<IRiskElement>();
            var visited = new HashSet<IRiskElement>();
            var current = element;
            while (current != null && visited.Add(current))
            {
                path.Add(current);
                current = PrimaryInputSource(current);
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Enumerates the hazard signals available at an element's input: every hazard and
        /// transform output strictly upstream on its path, with chain positions and advisory
        /// display labels. This is the discoverability API behind binding pickers — binding
        /// validation reuses it, so the picker and the validator can never disagree. Empty when
        /// the element's path does not reach a hazard root.
        /// </summary>
        /// <param name="element">The consuming element.</param>
        /// <returns>The available upstream hazard signals, upstream-first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        public IReadOnlyList<HazardSourceOption> GetAvailableHazardSources(IRiskElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            var path = GetUpstreamPath(element);
            if (path.Count == 0 || path[0] is not HazardElement) return Array.Empty<HazardSourceOption>();

            var options = new List<HazardSourceOption>();
            int position = 0;
            for (int i = 0; i < path.Count; i++)
            {
                if (ReferenceEquals(path[i], element)) break;

                if (path[i] is HazardElement hazardElement)
                {
                    for (int port = 0; port < hazardElement.OutputCount; port++)
                    {
                        // Port 0 carries the hazard's declared label; further ports label with
                        // the bivariate cluster (Phase 11).
                        options.Add(new HazardSourceOption(hazardElement, port, 0,
                            port == 0 ? hazardElement.Function?.SpecifiedHazard ?? string.Empty : string.Empty,
                            port == 0 ? hazardElement.Function?.HazardUnit ?? string.Empty : string.Empty));
                    }
                }
                else if (path[i] is TransformElement transformElement)
                {
                    position++;
                    options.Add(new HazardSourceOption(transformElement, 0, position,
                        transformElement.Function?.TransformedHazard ?? string.Empty,
                        transformElement.Function?.TransformedHazardUnit ?? string.Empty));
                }
                // Responses are signal-transparent: they consume the signal and emit failure
                // probability; the hazard signal passes through unchanged.
            }
            return options;
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates the graph structure and reports any issues found.
        /// </summary>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item>
        /// <description><c>IsValid</c>: <c>true</c> if the graph passes all validation checks; otherwise <c>false</c>.</description>
        /// </item>
        /// <item>
        /// <description><c>ValidationMessages</c>: messages describing validation errors ("Error: …", invalidating) and warnings ("Warning: …", advisory).</description>
        /// </item>
        /// </list>
        /// </returns>
        /// <remarks>
        /// Structural checks, in order: exactly one hazard element; duplicate name/Id backstop;
        /// per-element validation; dangling connections and port bounds (structural inputs AND
        /// hazard-source bindings); cycle detection; then, on a single-root acyclic graph:
        /// reachability from the root, leaves must be consequence elements, at least one
        /// consequence element, at most one response-free (non-failure) path, binding targets on
        /// the consumer's own path at or before the last response's input, consequence-count
        /// alignment against the non-failure path (positional excess pairing; count mismatch is
        /// an error, paired label/unit mismatch a warning), and shared function instances
        /// (warning). Hazard-type label continuity along chains is validated by the projected
        /// failure modes, which see the whole path with the component hazard's labels.
        /// </remarks>
        public (bool IsValid, List<string> ValidationMessages) Validate()
        {
            return Validate(RiskAnalysisMode.Risk);
        }

        /// <summary>
        /// Validates the graph structure for the given analysis mode. Reliability mode (Phase 4c)
        /// relaxes exactly the consequence-content requirements — consequence elements remain the
        /// structural path terminals but need no functions assigned, and the positional
        /// excess-pairing alignment is not enforced (reliability computes failure probability
        /// only, never consequences). Every structural check is identical to
        /// <see cref="Validate()"/> otherwise.
        /// </summary>
        /// <param name="mode">The analysis mode the graph is being validated for.</param>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item>
        /// <description><c>IsValid</c>: <c>true</c> if the graph passes all validation checks; otherwise <c>false</c>.</description>
        /// </item>
        /// <item>
        /// <description><c>ValidationMessages</c>: messages describing validation errors ("Error: …", invalidating) and warnings ("Warning: …", advisory).</description>
        /// </item>
        /// </list>
        /// </returns>
        public (bool IsValid, List<string> ValidationMessages) Validate(RiskAnalysisMode mode)
        {
            var messages = new List<string>();

            // Exactly one hazard root.
            var hazards = new List<HazardElement>(GetElements<HazardElement>());
            if (hazards.Count != 1)
            {
                messages.Add($"Error: The component graph must contain exactly one hazard element (found {hazards.Count}).");
            }

            ValidateUniqueness(messages);

            // Per-element validation (element messages already carry element context); the
            // consequence elements take the mode so reliability can relax their function content.
            for (int i = 0; i < _elements.Count; i++)
            {
                messages.AddRange(_elements[i] is ConsequenceElement consequenceElement
                    ? consequenceElement.Validate(mode).ValidationMessages
                    : _elements[i].Validate().ValidationMessages);
            }

            ValidateConnections(messages);

            bool acyclic = TopologicalSort();
            if (!acyclic)
            {
                messages.Add("Error: Circular reference detected. The component graph must form a directed acyclic graph (DAG).");
            }

            if (hazards.Count == 1 && acyclic)
            {
                ValidatePaths(hazards[0], messages, mode);
                ValidateSharedInstances(messages);
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes the graph: every element in declared order under <c>Elements</c>. Declared
        /// order is semantic (projected failure-mode order), so it round-trips exactly. Element
        /// XML is the persistence surface only — never a canonical-hash surface.
        /// </summary>
        /// <returns>The serialized form.</returns>
        public XElement ToXElement()
        {
            return ToXElement(RiskSerializationMode.SelfContained);
        }

        /// <summary>
        /// Serializes the graph in the given mode. Under
        /// <see cref="RiskSerializationMode.ByReference"/> the elements' wrapped functions are
        /// written as id + name references, for consumers that store those functions separately.
        /// </summary>
        /// <param name="mode">The serialization mode.</param>
        /// <returns>The serialized form.</returns>
        public XElement ToXElement(RiskSerializationMode mode)
        {
            var element = new XElement(nameof(ComponentGraph));
            var elements = new XElement(nameof(Elements));
            for (int i = 0; i < _elements.Count; i++)
            {
                elements.Add(_elements[i].ToXElement(mode));
            }
            element.Add(elements);
            return element;
        }

        /// <summary>
        /// Enumerates the distinct input functions the graph's elements wrap, in declared element
        /// order (and, within a consequence element, in ordered-consequence order).
        /// </summary>
        /// <returns>The referenced functions, each appearing once.</returns>
        /// <remarks>
        /// The dependency set a consuming layer needs in order to persist the graph
        /// <see cref="RiskSerializationMode.ByReference"/>, and to answer "what would break if this
        /// function were deleted?" before it removes one from a store.
        /// </remarks>
        public IEnumerable<IRiskFunction> GetReferencedFunctions()
        {
            var seen = new HashSet<IRiskFunction>();
            for (int i = 0; i < _elements.Count; i++)
            {
                foreach (var function in _elements[i].GetFunctions())
                {
                    if (function != null && seen.Add(function)) yield return function;
                }
            }
        }

        /// <summary>
        /// Creates a deep, isolated copy: every element cloned (shared Ids — a clone is the same
        /// logical element; wrapped functions deep-copied), then every clone's connections
        /// re-linked through the original→clone map.
        /// </summary>
        /// <returns>The cloned graph.</returns>
        public ComponentGraph Clone()
        {
            var clone = new ComponentGraph();
            var map = new Dictionary<IRiskElement, IRiskElement>(_elements.Count);
            for (int i = 0; i < _elements.Count; i++)
            {
                var clonedElement = (IRiskElement)_elements[i].Clone();
                clone.AddElement(clonedElement);
                map[_elements[i]] = clonedElement;
            }
            foreach (var pair in map)
            {
                if (pair.Value is RiskElementBase baseElement)
                {
                    baseElement.ResolveClonedConnections(pair.Key, map);
                }
            }
            return clone;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// The derived fan-out map (source → consumers in declared order), rebuilt lazily.
        /// Structural inputs only — bindings are not path edges.
        /// </summary>
        private Dictionary<IRiskElement, List<IRiskElement>> DownstreamMap
        {
            get
            {
                if (_downstreamMap == null)
                {
                    var map = new Dictionary<IRiskElement, List<IRiskElement>>(_elements.Count);
                    for (int i = 0; i < _elements.Count; i++)
                    {
                        foreach (var connection in _elements[i].GetInputConnections())
                        {
                            if (!_elements.Contains(connection.Source)) continue;
                            if (!map.TryGetValue(connection.Source, out var consumers))
                            {
                                consumers = new List<IRiskElement>();
                                map[connection.Source] = consumers;
                            }
                            consumers.Add(_elements[i]);
                        }
                    }
                    _downstreamMap = map;
                }
                return _downstreamMap;
            }
        }

        /// <summary>
        /// The element's primary structural input source, or null when unconnected or the source
        /// is not in this graph.
        /// </summary>
        /// <param name="element">The consuming element.</param>
        /// <returns>The in-graph primary source, or null.</returns>
        private IRiskElement? PrimaryInputSource(IRiskElement element)
        {
            foreach (var connection in element.GetInputConnections())
            {
                return _elements.Contains(connection.Source) ? connection.Source : null;
            }
            return null;
        }

        /// <summary>
        /// Backstop uniqueness checks: duplicate non-empty names and duplicate Ids (the add path
        /// throws structurally; this reports states reached by other means).
        /// </summary>
        /// <param name="messages">The message sink.</param>
        private void ValidateUniqueness(List<string> messages)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<Guid>();
            for (int i = 0; i < _elements.Count; i++)
            {
                string name = _elements[i].Name;
                if (!string.IsNullOrEmpty(name) && !names.Add(name))
                {
                    messages.Add($"Error: Duplicate element name '{name}' in the component graph.");
                }
                if (!ids.Add(_elements[i].Id))
                {
                    messages.Add($"Error: Duplicate element Id '{_elements[i].Id:D}' in the component graph.");
                }
            }
        }

        /// <summary>
        /// Validates every connection (structural inputs and hazard-source bindings): the target
        /// must be in this graph and the port within the target's output range.
        /// </summary>
        /// <param name="messages">The message sink.</param>
        private void ValidateConnections(List<string> messages)
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                foreach (var connection in _elements[i].GetInputConnections())
                {
                    CheckConnection(_elements[i], connection, "input", messages);
                }
                if (_elements[i] is ConsequenceElement consequence && consequence.HazardSource != null)
                {
                    CheckConnection(_elements[i], consequence.HazardSource, "hazard-source binding", messages);
                }
            }
        }

        /// <summary>
        /// Checks a single connection for membership and port bounds.
        /// </summary>
        /// <param name="owner">The consuming element.</param>
        /// <param name="connection">The connection to check.</param>
        /// <param name="kind">The connection kind for the message ("input" or "hazard-source binding").</param>
        /// <param name="messages">The message sink.</param>
        private void CheckConnection(IRiskElement owner, RiskConnection connection, string kind, List<string> messages)
        {
            if (!_elements.Contains(connection.Source))
            {
                messages.Add($"Error: The element '{owner.Name}' {kind} references '{connection.Source.Name}', which is not in the graph.");
            }
            else if (connection.SourcePort >= connection.Source.OutputCount)
            {
                messages.Add($"Error: The element '{owner.Name}' {kind} references output port {connection.SourcePort} of '{connection.Source.Name}', which exposes {connection.Source.OutputCount} output(s).");
            }
        }

        /// <summary>
        /// Path-level checks on a single-root acyclic graph: reachability, consequence leaves,
        /// the non-failure-path limit, binding placement, and consequence-count alignment.
        /// </summary>
        /// <param name="root">The single hazard element.</param>
        /// <param name="messages">The message sink.</param>
        /// <param name="mode">The analysis mode (reliability skips the consequence alignment).</param>
        private void ValidatePaths(HazardElement root, List<string> messages, RiskAnalysisMode mode)
        {
            // Reachability: breadth-first over the derived fan-out from the root.
            var reachable = new HashSet<IRiskElement> { root };
            var frontier = new Queue<IRiskElement>();
            frontier.Enqueue(root);
            while (frontier.Count > 0)
            {
                foreach (var consumer in GetDownstreamElements(frontier.Dequeue()))
                {
                    if (reachable.Add(consumer)) frontier.Enqueue(consumer);
                }
            }
            for (int i = 0; i < _elements.Count; i++)
            {
                if (!reachable.Contains(_elements[i]))
                {
                    messages.Add($"Error: The element '{_elements[i].Name}' is not connected to the hazard element.");
                }
            }

            // Leaves must be consequence elements.
            for (int i = 0; i < _elements.Count; i++)
            {
                bool isLeaf = !DownstreamMap.TryGetValue(_elements[i], out var consumers) || consumers.Count == 0;
                if (isLeaf && reachable.Contains(_elements[i]) && _elements[i] is not ConsequenceElement)
                {
                    messages.Add($"Error: A path ends at '{_elements[i].Name}' without a consequence element.");
                }
            }

            // At least one complete path; at most one response-free (non-failure) path.
            var terminals = new List<ConsequenceElement>(GetElements<ConsequenceElement>());
            if (terminals.Count == 0)
            {
                messages.Add("Error: The component graph must contain at least one consequence element.");
            }

            int responseFreePaths = 0;
            ConsequenceElement? nonFailTerminal = null;
            var responseOrdinals = new Dictionary<ResponseElement, int>();
            var signatures = new List<(ConsequenceElement Terminal, List<(int Node, int Port)> Signature)>();
            for (int i = 0; i < terminals.Count; i++)
            {
                var path = GetUpstreamPath(terminals[i]);
                if (path.Count == 0 || path[0] is not HazardElement) continue;

                int transformCount = 0;
                int lastResponseInput = -1;
                bool hasResponse = false;
                var signature = new List<(int Node, int Port)>();
                for (int p = 0; p < path.Count; p++)
                {
                    if (path[p] is TransformElement) transformCount++;
                    else if (path[p] is ResponseElement response)
                    {
                        hasResponse = true;
                        lastResponseInput = transformCount;
                        if (!responseOrdinals.TryGetValue(response, out int ordinal))
                        {
                            ordinal = responseOrdinals.Count;
                            responseOrdinals.Add(response, ordinal);
                        }
                        signature.Add((ordinal, p + 1 < path.Count ? ConnectionPort(path[p + 1], response) : 0));
                    }
                }
                if (!hasResponse)
                {
                    responseFreePaths++;
                    nonFailTerminal ??= terminals[i];
                }
                else
                {
                    signatures.Add((terminals[i], signature));
                }

                ValidateBinding(terminals[i], path, hasResponse, lastResponseInput, messages);
            }
            if (responseFreePaths > 1)
            {
                messages.Add($"Error: At most one non-failure path (a path with no response element) is allowed per component; found {responseFreePaths}.");
            }

            ValidateBranchClaims(signatures, messages);

            // Positional excess pairing has no meaning in reliability mode — no consequence is
            // ever computed, so mismatched counts cannot corrupt anything.
            if (nonFailTerminal != null && mode == RiskAnalysisMode.Risk)
            {
                ValidateConsequenceAlignment(terminals, nonFailTerminal, messages);
            }
        }

        /// <summary>
        /// Polarity-aware branch-claim checks (arch doc §7.9, advisory per the Phase 6.7 Q2
        /// ruling): within a cascade-active component — any multi-response path or any Non-Fail
        /// port usage — terminals with identical leaf signatures double-count their branch under
        /// the failure-mode combination, and a terminal whose signature is a strict prefix of
        /// another's claims a branch that also continues, overlapping the deeper end states. Both
        /// stay legal (they combine as separate events, today's fan-out semantics) and warn. A
        /// response element whose Fail port has no downstream consumer routes its failure-branch
        /// mass to the background remainder — warned, because it is almost always a modeling
        /// surprise (the unwired Non-Fail port is the v1.0 default and stays silent). Components
        /// with no cascade machinery in use — every pre-6.7 shape — produce no messages here.
        /// </summary>
        /// <param name="signatures">Each failure terminal's leaf signature: the ordered (response ordinal, exit port) pairs along its path.</param>
        /// <param name="messages">The message sink.</param>
        private void ValidateBranchClaims(List<(ConsequenceElement Terminal, List<(int Node, int Port)> Signature)> signatures, List<string> messages)
        {
            bool cascadeActive = false;
            for (int i = 0; i < signatures.Count && !cascadeActive; i++)
            {
                if (signatures[i].Signature.Count > 1) cascadeActive = true;
                for (int k = 0; k < signatures[i].Signature.Count && !cascadeActive; k++)
                {
                    if (signatures[i].Signature[k].Port != 0) cascadeActive = true;
                }
            }
            if (!cascadeActive) return;

            for (int i = 0; i < signatures.Count; i++)
            {
                for (int j = i + 1; j < signatures.Count; j++)
                {
                    var a = signatures[i].Signature;
                    var b = signatures[j].Signature;
                    int shared = Math.Min(a.Count, b.Count);
                    bool prefixEqual = true;
                    for (int k = 0; k < shared; k++)
                    {
                        if (a[k].Node != b[k].Node || a[k].Port != b[k].Port)
                        {
                            prefixEqual = false;
                            break;
                        }
                    }
                    if (!prefixEqual) continue;

                    if (a.Count == b.Count)
                    {
                        messages.Add($"Warning: The consequence elements '{signatures[i].Terminal.Name}' and '{signatures[j].Terminal.Name}' occupy the same response branch (identical port path); their end states double-count that branch under the failure-mode combination — attach multiple consequence types to one terminal, or wire distinct ports.");
                    }
                    else
                    {
                        var (shorter, longer) = a.Count < b.Count ? (signatures[i].Terminal, signatures[j].Terminal) : (signatures[j].Terminal, signatures[i].Terminal);
                        messages.Add($"Warning: The consequence element '{shorter.Name}' terminates a response branch that also continues toward '{longer.Name}'; the terminal's end state overlaps the continuation's states — wire the terminal to the complementary port to make the states exclusive.");
                    }
                }
            }

            foreach (var element in GetElements<ResponseElement>())
            {
                bool failPortWired = false;
                foreach (var consumer in GetDownstreamElements(element))
                {
                    if (ConnectionPort(consumer, element) == 0)
                    {
                        failPortWired = true;
                        break;
                    }
                }
                if (!failPortWired)
                {
                    messages.Add($"Warning: The Fail port of response element '{element.Name}' has no downstream connection; its failure-branch mass flows to the component's background path.");
                }
            }
        }

        /// <summary>
        /// Reads the output port a consumer's structural connection takes from a source element:
        /// the first input connection referencing the source (the same first-connection rule the
        /// upstream path walk uses), defaulting to port 0 when none is found.
        /// </summary>
        /// <param name="consumer">The downstream element.</param>
        /// <param name="source">The upstream element whose exit port is wanted.</param>
        /// <returns>The connection's source port, or 0.</returns>
        private static int ConnectionPort(IRiskElement consumer, IRiskElement source)
        {
            foreach (var connection in consumer.GetInputConnections())
            {
                if (ReferenceEquals(connection.Source, source)) return connection.SourcePort;
            }
            return 0;
        }

        /// <summary>
        /// Validates a terminal's hazard-source binding: the target must be a hazard or transform
        /// element strictly upstream on the terminal's own path, at or before the last response's
        /// input position (trailing transforms after the last response always apply).
        /// </summary>
        /// <param name="terminal">The consequence element.</param>
        /// <param name="path">The terminal's root-first upstream path.</param>
        /// <param name="hasResponse">Whether the path contains a response element.</param>
        /// <param name="lastResponseInput">The chain position of the last response's input.</param>
        /// <param name="messages">The message sink.</param>
        private static void ValidateBinding(ConsequenceElement terminal, IReadOnlyList<IRiskElement> path,
            bool hasResponse, int lastResponseInput, List<string> messages)
        {
            if (terminal.HazardSource == null) return;

            var target = terminal.HazardSource.Source;
            int bindingPosition = -1;
            int cursor = 0;
            for (int p = 0; p < path.Count && !ReferenceEquals(path[p], terminal); p++)
            {
                if (path[p] is HazardElement && ReferenceEquals(path[p], target))
                {
                    bindingPosition = 0;
                    break;
                }
                if (path[p] is TransformElement)
                {
                    cursor++;
                    if (ReferenceEquals(path[p], target))
                    {
                        bindingPosition = cursor;
                        break;
                    }
                }
            }

            if (bindingPosition < 0)
            {
                messages.Add($"Error: The consequence element '{terminal.Name}' hazard-source binding must reference a hazard or transform element on its own upstream path ('{target.Name}' is not).");
            }
            else if (hasResponse && bindingPosition > lastResponseInput)
            {
                messages.Add($"Error: The consequence element '{terminal.Name}' hazard-source binding position ({bindingPosition}) must be at or before the last response's input position ({lastResponseInput}).");
            }
        }

        /// <summary>
        /// Validates consequence-count alignment against the non-failure path: excess-risk
        /// pairing is positional, so every failure path must carry the same number of consequence
        /// functions (error), and paired positions should agree on type and unit labels
        /// (warning).
        /// </summary>
        /// <param name="terminals">Every consequence element, in declared order.</param>
        /// <param name="nonFailTerminal">The non-failure path's terminal.</param>
        /// <param name="messages">The message sink.</param>
        private static void ValidateConsequenceAlignment(List<ConsequenceElement> terminals,
            ConsequenceElement nonFailTerminal, List<string> messages)
        {
            int expected = nonFailTerminal.Functions.Count;
            for (int i = 0; i < terminals.Count; i++)
            {
                if (ReferenceEquals(terminals[i], nonFailTerminal)) continue;

                if (terminals[i].Functions.Count != expected)
                {
                    messages.Add($"Error: The consequence element '{terminals[i].Name}' has {terminals[i].Functions.Count} consequence function(s) but the non-failure path has {expected}; excess-risk pairing is positional and requires matching counts.");
                    continue;
                }
                for (int k = 0; k < expected; k++)
                {
                    var fail = terminals[i].Functions[k];
                    var nonFail = nonFailTerminal.Functions[k];
                    if (fail is null || nonFail is null) continue;

                    if (!string.IsNullOrEmpty(fail.SpecifiedConsequence) && !string.IsNullOrEmpty(nonFail.SpecifiedConsequence) &&
                        !string.Equals(fail.SpecifiedConsequence, nonFail.SpecifiedConsequence, StringComparison.OrdinalIgnoreCase))
                    {
                        messages.Add($"Warning: The consequence element '{terminals[i].Name}' position {k} is labeled '{fail.SpecifiedConsequence}' but pairs with '{nonFail.SpecifiedConsequence}' on the non-failure path; excess pairing is positional.");
                    }
                    if (!string.IsNullOrEmpty(fail.ConsequenceUnit) && !string.IsNullOrEmpty(nonFail.ConsequenceUnit) &&
                        !string.Equals(fail.ConsequenceUnit, nonFail.ConsequenceUnit, StringComparison.OrdinalIgnoreCase))
                    {
                        messages.Add($"Warning: The consequence element '{terminals[i].Name}' position {k} unit '{fail.ConsequenceUnit}' differs from '{nonFail.ConsequenceUnit}' on the non-failure path.");
                    }
                }
            }
        }

        /// <summary>
        /// Warns when the same function instance is referenced by more than one element:
        /// compute-benign under content seeding, but the shared instance becomes independent
        /// copies on reload, which surprises editors.
        /// </summary>
        /// <param name="messages">The message sink.</param>
        private void ValidateSharedInstances(List<string> messages)
        {
            var seen = new Dictionary<IRiskFunction, string>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < _elements.Count; i++)
            {
                foreach (var function in _elements[i].GetFunctions())
                {
                    if (seen.TryGetValue(function, out string? firstOwner))
                    {
                        messages.Add($"Warning: The elements '{firstOwner}' and '{_elements[i].Name}' reference the same function instance ('{function.Name}'); the shared instance becomes independent copies on reload.");
                    }
                    else
                    {
                        seen[function] = _elements[i].Name;
                    }
                }
            }
        }

        /// <summary>
        /// Invalidates the derived topology caches (sort order and fan-out map).
        /// </summary>
        private void InvalidateTopology()
        {
            _sortedElements = null;
            _sortAttempted = false;
            _downstreamMap = null;
        }

        /// <summary>
        /// Reacts to element property changes: connection rewires invalidate the topology caches;
        /// Id re-rolls invalidate the Id lookup.
        /// </summary>
        /// <param name="sender">The element that changed.</param>
        /// <param name="e">The change description.</param>
        private void ElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(TransformElement.Input):
                case nameof(ResponseElement.SecondaryInput):
                    InvalidateTopology();
                    break;
                case nameof(IRiskElement.Id):
                    _idMap = null;
                    break;
            }

            // Forward every element change, including the wrapped functions' own edits that
            // elements re-raise. A consuming layer subscribes here (through the owning component)
            // to know that a result is stale, and it cannot do that if the graph absorbs the
            // signal after using it to invalidate its caches.
            RaisePropertyChange(nameof(Elements));
        }

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        private void RaisePropertyChange(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
