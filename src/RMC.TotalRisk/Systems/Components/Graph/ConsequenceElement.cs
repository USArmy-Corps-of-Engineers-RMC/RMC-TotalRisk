using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Xml.Linq;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;

namespace RMC.TotalRisk.Systems.Components.Graph
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
            Functions = new ObservableCollection<IConsequenceFunction>();
        }

        /// <summary>
        /// Initializes a named consequence element with no wrapped functions.
        /// </summary>
        /// <param name="name">The element name.</param>
        public ConsequenceElement(string name)
            : this()
        {
            Name = name;
        }

        /// <summary>
        /// Restores a consequence element from its serialized form. The input and binding
        /// connections are captured pending and resolved by the graph via
        /// <see cref="ResolveDeserializedReferences"/>.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement(RiskSerializationMode)"/>.</param>
        /// <param name="resolver">
        /// The function resolver, required only to read a by-reference form. An unresolvable
        /// reference is recorded and reported by <see cref="Validate()"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a serialized function child cannot be reconstructed — dropping a wrapped
        /// function silently would lose model content on the next save — or when a serialized
        /// reference id is stale.
        /// </exception>
        public ConsequenceElement(XElement xElement, IRiskFunctionResolver? resolver = null)
            : this()
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
                    // An unresolved reference yields null; it is dropped from the ordered list and
                    // reported by Validate. Positional consequence pairing is checked there too,
                    // so a silently shortened list cannot pass validation. The resolver threads
                    // into the inline factory so an inline composite child can resolve its own
                    // by-reference children.
                    var consequence = ReadFunctionEntry<IConsequenceFunction>(
                        child, resolver, c => RiskFunctionFactory.CreateFromXElement(c, resolver), "consequence function");
                    if (consequence != null) _functions.Add(consequence);
                }
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Functions"/>. Assigned through the property by every
        /// constructor, so the collection subscription is always attached.
        /// </summary>
        private ObservableCollection<IConsequenceFunction> _functions = null!;

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
        private PendingConnection? _pendingInput;

        /// <summary>
        /// The pending serialized binding reference, resolved by the graph after construction.
        /// </summary>
        private PendingConnection? _pendingHazardSource;

        /// <summary>
        /// The ordered consequence functions (referenced, not owned — a consuming layer may store
        /// one function and use it in several graphs): index 0 is the primary type used for risk
        /// integration; all are computed and tracked. Assigning null coerces to an empty
        /// collection.
        /// </summary>
        /// <remarks>
        /// Observable, and the element tracks its membership: adding, removing, replacing, or
        /// clearing entries attaches and detaches each function's change subscription and reports
        /// the change as <c>Functions</c>. So every mutation path behaves the same, whether a
        /// consumer assigns the whole collection or edits it in place through a bound list — the
        /// pattern the sibling BestFit <c>CompositeAnalysis</c> uses for its weighted
        /// sub-analyses, and the shape WPF data-binds to directly.
        /// </remarks>
        public ObservableCollection<IConsequenceFunction> Functions
        {
            get { return _functions; }
            set
            {
                if (ReferenceEquals(_functions, value)) return;

                if (_functions != null) _functions.CollectionChanged -= FunctionsCollectionChanged;
                _functions = value ?? new ObservableCollection<IConsequenceFunction>();
                _functions.CollectionChanged += FunctionsCollectionChanged;

                ReconcileFunctionSubscriptions();
                RaisePropertyChange(nameof(Functions));
            }
        }

        /// <inheritdoc/>
        protected override string FunctionPropertyName
        {
            get { return nameof(Functions); }
        }

        /// <summary>
        /// The distinct functions this element currently holds a change subscription on — the
        /// shadow of <see cref="Functions"/> that <see cref="FunctionsCollectionChanged"/>
        /// reconciles against.
        /// </summary>
        /// <remarks>
        /// A shadow set rather than per-item bookkeeping off the event arguments, because
        /// <see cref="NotifyCollectionChangedAction.Reset"/> — which <c>Clear()</c> raises —
        /// carries no <c>OldItems</c>. Handling
        /// only <c>OldItems</c>/<c>NewItems</c> would leak a subscription on every clear, leaving a
        /// removed function still able to notify this element. Reconciling also makes duplicates
        /// safe: a function listed twice is subscribed once, so it notifies once.
        /// </remarks>
        private readonly HashSet<IConsequenceFunction> _subscribedFunctions = new HashSet<IConsequenceFunction>();

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

        /// <summary>Restores structural and binding inputs without publishing a partially rolled-back edit.</summary>
        /// <param name="input">The checkpointed structural input.</param>
        /// <param name="hazardSource">The checkpointed hazard-source binding.</param>
        internal void RestoreInputConnections(RiskConnection? input, RiskConnection? hazardSource)
        {
            _input = input;
            _hazardSource = hazardSource;
        }

        /// <inheritdoc/>
        public override RiskElementType ElementType => RiskElementType.Consequence;

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
        /// Appends rather than replaces: a consequence element carries an ordered list of
        /// consequence types (economic, life loss, ...), all of which are computed and tracked.
        /// </remarks>
        public override bool TryAssignFunction(IRiskFunction function, out string error)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (function is not IConsequenceFunction typed)
            {
                error = FunctionMismatchMessage(function, "a consequence function");
                return false;
            }

            Functions.Add(typed);
            error = string.Empty;
            return true;
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
            return Validate(RiskAnalysisMode.Risk);
        }

        /// <summary>
        /// Validates the element for the given analysis mode. Reliability mode relaxes
        /// the no-functions error only — a consequence element stays the structural path terminal
        /// but needs no functions when the analysis computes failure probability alone. Assigned
        /// functions are still validated in both modes.
        /// </summary>
        /// <param name="mode">The analysis mode the element is being validated for.</param>
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
        public (bool IsValid, List<string> ValidationMessages) Validate(RiskAnalysisMode mode)
        {
            var (_, messages) = base.Validate();

            if (_functions.Count == 0 && !HasUnresolvedFunctionReferences && mode == RiskAnalysisMode.Risk)
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
        public override XElement ToXElement(RiskSerializationMode mode)
        {
            var element = new XElement(nameof(ConsequenceElement));
            AddBaseAttributesToXElement(element);
            WriteConnection(element, "Source", _input);
            WriteConnection(element, "HazardSource", _hazardSource);

            var functions = new XElement(nameof(Functions));
            for (int i = 0; i < _functions.Count; i++)
            {
                if (_functions[i] is not null) functions.Add(WriteFunctionEntry(_functions[i], mode));
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
                if (copied != null) clone.Functions.Add(copied);
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

        #region Private Helpers

        /// <summary>
        /// Keeps the element's change subscriptions in step with the ordered list, and reports the
        /// membership change as <c>Functions</c>.
        /// </summary>
        /// <param name="sender">The ordered collection.</param>
        /// <param name="e">The membership change.</param>
        private void FunctionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ReconcileFunctionSubscriptions();
            RaisePropertyChange(nameof(Functions));
        }

        /// <summary>
        /// Subscribes to every function now in the ordered list and unsubscribes from every
        /// function that has left it, using <see cref="_subscribedFunctions"/> as the record of
        /// what is currently attached.
        /// </summary>
        private void ReconcileFunctionSubscriptions()
        {
            foreach (var stale in _subscribedFunctions.Where(f => !_functions.Contains(f)).ToList())
            {
                UnsubscribeFunction(stale);
                _subscribedFunctions.Remove(stale);
            }

            for (int i = 0; i < _functions.Count; i++)
            {
                var function = _functions[i];
                if (function != null && _subscribedFunctions.Add(function)) SubscribeFunction(function);
            }
        }

        #endregion
    }
}
