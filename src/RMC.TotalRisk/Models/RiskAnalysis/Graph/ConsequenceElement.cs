using System;
using System.Collections.Generic;
using System.Xml.Linq;
using RMC.TotalRisk.Models.ConsequenceFunctions;
using RMC.TotalRisk.Models.Support;

namespace RMC.TotalRisk.Models.RiskAnalysis.Graph
{
    /// <summary>
    /// A terminal element of a system component's risk graph: wraps the ordered consequence
    /// functions evaluated at the path's bound hazard signal. Input-only — the Hydrologics sink
    /// analog; every complete path ends at a consequence element, and each terminal defines one
    /// failure mode (or the non-failure mode when its path has no response element).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// <see cref="Functions"/> is ordered: index 0 is the primary consequence type used for risk
    /// integration (e.g., economic damages); all entries are computed and tracked (e.g., life
    /// loss alongside). Pairing across fail and non-fail paths for excess-risk subtraction is
    /// positional, never by label.
    /// </para>
    /// <para>
    /// <see cref="HazardSource"/> is the structural binding override: which upstream hazard
    /// output feeds the consequences' input axis. Null means the immediate upstream signal at the
    /// last response's input (exact v1.0 behavior). Because the binding is an element reference —
    /// never a hazard-type label — renaming labels can never rewire compute; the projection
    /// converts the reference to a chain-position index for hashing and the engine.
    /// </para>
    /// </remarks>
    public class ConsequenceElement : RiskElementBase
    {
        #region Construction

        /// <summary>
        /// Initializes an unnamed consequence element with no wrapped functions.
        /// </summary>
        public ConsequenceElement()
        {
        }

        /// <summary>
        /// Initializes a named consequence element with no wrapped functions.
        /// </summary>
        /// <param name="name">The element name.</param>
        public ConsequenceElement(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Restores a consequence element from its serialized form. The input and binding
        /// connections are captured pending and resolved by the graph via
        /// <see cref="ResolveDeserializedReferences"/>.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a serialized function child cannot be reconstructed — dropping a wrapped
        /// function silently would lose model content on the next save.
        /// </exception>
        public ConsequenceElement(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            ReadBaseFromXElement(xElement);
            _pendingInput = ReadPendingConnection(xElement, "Source");
            _pendingHazardSource = ReadPendingConnection(xElement, "HazardSource");

            var functionsElement = xElement.Element(nameof(Functions));
            if (functionsElement != null)
            {
                foreach (var child in functionsElement.Elements())
                {
                    var consequence = RiskFunctionFactory.CreateConsequenceFunction(child);
                    if (consequence == null)
                    {
                        throw new InvalidOperationException(
                            $"Unrecognized consequence function element '{child.Name.LocalName}' in the serialized consequence element '{Name}'. " +
                            "The element cannot be reconstructed faithfully; the serialized form may come from a newer version.");
                    }
                    _functions.Add(consequence);
                }
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Functions"/>.
        /// </summary>
        private List<IConsequenceFunction> _functions = new List<IConsequenceFunction>();

        /// <summary>
        /// Backing field for <see cref="Input"/>.
        /// </summary>
        private RiskConnection? _input;

        /// <summary>
        /// Backing field for <see cref="HazardSource"/>.
        /// </summary>
        private RiskConnection? _hazardSource;

        /// <summary>
        /// The pending serialized input reference, resolved by the graph after construction.
        /// </summary>
        private (Guid? Id, string? Name, int Port)? _pendingInput;

        /// <summary>
        /// The pending serialized binding reference, resolved by the graph after construction.
        /// </summary>
        private (Guid? Id, string? Name, int Port)? _pendingHazardSource;

        /// <summary>
        /// The ordered consequence functions (owned; serialized inline): index 0 is the primary
        /// type used for risk integration; all are computed and tracked. Assigning null coerces
        /// to an empty list; the element takes ownership of an assigned list.
        /// </summary>
        public List<IConsequenceFunction> Functions
        {
            get { return _functions; }
            set
            {
                if (!ReferenceEquals(_functions, value))
                {
                    _functions = value ?? new List<IConsequenceFunction>();
                    RaisePropertyChange(nameof(Functions));
                }
            }
        }

        /// <summary>
        /// The structural input: which upstream element output this terminal sits downstream of.
        /// Null while unconnected — graph validation reports unreachable elements.
        /// </summary>
        public RiskConnection? Input
        {
            get { return _input; }
            set
            {
                if (!Equals(_input, value))
                {
                    _input = value;
                    RaisePropertyChange(nameof(Input));
                }
            }
        }

        /// <summary>
        /// The optional binding override: which upstream hazard/transform output feeds the
        /// consequences' input axis. Null means the immediate upstream signal at the last
        /// response's input (v1.0 parity). Must reference an element on this terminal's own
        /// upstream path, at or before the last response's input (graph-validated).
        /// </summary>
        public RiskConnection? HazardSource
        {
            get { return _hazardSource; }
            set
            {
                if (!Equals(_hazardSource, value))
                {
                    _hazardSource = value;
                    RaisePropertyChange(nameof(HazardSource));
                }
            }
        }

        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override int OutputCount => 0;

        #endregion

        #region IRiskElement Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Yields the structural input only. <see cref="HazardSource"/> is a binding — it reads a
        /// signal from an element already on the path and is validated separately by the graph,
        /// never treated as a path edge.
        /// </remarks>
        public override IEnumerable<RiskConnection> GetInputConnections()
        {
            if (_input != null) yield return _input;
        }

        /// <inheritdoc/>
        public override IEnumerable<IRiskFunction> GetFunctions()
        {
            for (int i = 0; i < _functions.Count; i++)
            {
                if (_functions[i] is not null) yield return _functions[i];
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Errors: missing name (base); no consequence functions; null entries; each function's
        /// own errors (aggregated with the element name as context). Warnings: duplicate
        /// consequence-type labels within the list (types are tracked positionally; duplicate
        /// labels are advisory only).
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var (_, messages) = base.Validate();

            if (_functions.Count == 0)
            {
                messages.Add($"Error: The consequence element '{Name}' has no consequence functions.");
            }
            for (int i = 0; i < _functions.Count; i++)
            {
                if (_functions[i] is null)
                {
                    messages.Add($"Error: The consequence element '{Name}' consequence function at index {i} has not been defined.");
                    continue;
                }
                AggregateWithContext(messages, _functions[i].Validate().ValidationMessages);
            }

            var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _functions.Count; i++)
            {
                string? type = _functions[i]?.SpecifiedConsequence;
                if (string.IsNullOrEmpty(type)) continue;
                if (!seenTypes.Add(type!))
                {
                    messages.Add($"Warning: The consequence element '{Name}' has multiple consequence functions labeled '{type}'; consequence types are tracked positionally, so duplicate labels may be confusing.");
                }
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        public override XElement ToXElement()
        {
            var element = new XElement(nameof(ConsequenceElement));
            AddBaseAttributesToXElement(element);
            WriteConnection(element, "Source", _input);
            WriteConnection(element, "HazardSource", _hazardSource);

            var functions = new XElement(nameof(Functions));
            for (int i = 0; i < _functions.Count; i++)
            {
                if (_functions[i] is not null) functions.Add(_functions[i].ToXElement());
            }
            element.Add(functions);
            return element;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new ConsequenceElement();
            CopyBaseTo(clone);
            for (int i = 0; i < _functions.Count; i++)
            {
                if (_functions[i] is null) continue;
                var copied = RiskFunctionFactory.CreateConsequenceFunction(_functions[i].ToXElement());
                if (copied != null) clone._functions.Add(copied);
            }
            return clone;
        }

        /// <inheritdoc/>
        public override void ResolveDeserializedReferences(RiskElementResolver resolver)
        {
            _input = ResolveConnection(_pendingInput, resolver, $"The consequence element '{Name}' input");
            _hazardSource = ResolveConnection(_pendingHazardSource, resolver, $"The consequence element '{Name}' hazard-source binding");
            _pendingInput = null;
            _pendingHazardSource = null;
        }

        /// <inheritdoc/>
        public override void ResolveClonedConnections(IRiskElement original, IReadOnlyDictionary<IRiskElement, IRiskElement> cloneMap)
        {
            if (original is ConsequenceElement consequence)
            {
                _input = RemapConnection(consequence._input, cloneMap);
                _hazardSource = RemapConnection(consequence._hazardSource, cloneMap);
            }
        }

        #endregion
    }
}
