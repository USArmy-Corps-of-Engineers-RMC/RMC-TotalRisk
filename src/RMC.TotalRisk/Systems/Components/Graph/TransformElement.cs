using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;

namespace RMC.TotalRisk.Systems.Components.Graph
{
    /// <summary>
    /// A transform element of a system component's risk graph: wraps a transform function that
    /// converts the incoming hazard signal to another hazard type (e.g., a flow-to-stage rating
    /// curve). One input, one output.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Transforms advance the hazard signal: each transform element on a path adds a new hazard
    /// type that downstream elements (and the consequence binding) can consume. Transforms may
    /// chain and may fan out to multiple downstream consumers.
    /// </para>
    /// </remarks>
    public class TransformElement : RiskElementBase
    {
        #region Construction

        /// <summary>
        /// Initializes an unnamed transform element with no wrapped function.
        /// </summary>
        public TransformElement()
        {
        }

        /// <summary>
        /// Initializes a named transform element with no wrapped function.
        /// </summary>
        /// <param name="name">The element name.</param>
        public TransformElement(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Restores a transform element from its serialized form. The input connection is
        /// captured pending and resolved by the graph via
        /// <see cref="ResolveDeserializedReferences"/>.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a serialized function child cannot be reconstructed — dropping the wrapped
        /// function silently would lose model content on the next save.
        /// </exception>
        public TransformElement(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            ReadBaseFromXElement(xElement);
            _pendingInput = ReadPendingConnection(xElement, "Source");

            var functionChild = xElement.Element(nameof(Function))?.Elements().FirstOrDefault();
            if (functionChild != null)
            {
                _function = RiskFunctionFactory.CreateTransformFunction(functionChild)
                    ?? throw new InvalidOperationException(
                        $"Unrecognized transform function element '{functionChild.Name.LocalName}' in the serialized transform element '{Name}'. " +
                        "The element cannot be reconstructed faithfully; the serialized form may come from a newer version.");
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Function"/>.
        /// </summary>
        private ITransformFunction? _function;

        /// <summary>
        /// Backing field for <see cref="Input"/>.
        /// </summary>
        private RiskConnection? _input;

        /// <summary>
        /// The pending serialized input reference, resolved by the graph after construction.
        /// </summary>
        private (Guid? Id, string? Name, int Port)? _pendingInput;

        /// <summary>
        /// The wrapped transform function (owned; serialized inline). Null while unset —
        /// validation reports it.
        /// </summary>
        public ITransformFunction? Function
        {
            get { return _function; }
            set
            {
                if (!ReferenceEquals(_function, value))
                {
                    _function = value;
                    RaisePropertyChange(nameof(Function));
                }
            }
        }

        /// <summary>
        /// The structural input: which upstream element output feeds this transform. Null while
        /// unconnected — graph validation reports unreachable elements.
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

        /// <inheritdoc/>
        public override RiskElementType ElementType => RiskElementType.Transform;

        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override int OutputCount => 1;

        #endregion

        #region IRiskElement Methods

        /// <inheritdoc/>
        public override IEnumerable<RiskConnection> GetInputConnections()
        {
            if (_input != null) yield return _input;
        }

        /// <inheritdoc/>
        public override IEnumerable<IRiskFunction> GetFunctions()
        {
            if (_function != null) yield return _function;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Errors: missing name (base); no wrapped function; the wrapped function's own errors
        /// (aggregated with the element name as context). Connectivity is validated by the graph,
        /// which sees the whole topology.
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var (_, messages) = base.Validate();
            if (_function is null)
            {
                messages.Add($"Error: The transform element '{Name}' has no transform function assigned.");
            }
            else
            {
                AggregateWithContext(messages, _function.Validate().ValidationMessages);
            }
            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        public override XElement ToXElement()
        {
            var element = new XElement(nameof(TransformElement));
            AddBaseAttributesToXElement(element);
            WriteConnection(element, "Source", _input);
            if (_function != null) element.Add(new XElement(nameof(Function), _function.ToXElement()));
            return element;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new TransformElement();
            CopyBaseTo(clone);
            clone._function = _function == null ? null : RiskFunctionFactory.CreateTransformFunction(_function.ToXElement());
            return clone;
        }

        /// <inheritdoc/>
        public override void ResolveDeserializedReferences(RiskElementResolver resolver)
        {
            _input = ResolveConnection(_pendingInput, resolver, $"The transform element '{Name}' input");
            _pendingInput = null;
        }

        /// <inheritdoc/>
        public override void ResolveClonedConnections(IRiskElement original, IReadOnlyDictionary<IRiskElement, IRiskElement> cloneMap)
        {
            if (original is TransformElement transform)
            {
                _input = RemapConnection(transform._input, cloneMap);
            }
        }

        #endregion
    }
}
