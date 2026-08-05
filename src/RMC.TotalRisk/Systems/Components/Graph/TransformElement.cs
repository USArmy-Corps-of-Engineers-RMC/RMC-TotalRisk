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
    /// curve). One input and one output for a univariate transform; a bivariate transform takes
    /// two inputs (primary x, secondary y) and exposes two outputs (port 0 = z = f(x, y), the
    /// transformed primary; port 1 = the secondary value passed through unchanged).
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
    /// <para>
    /// <see cref="SecondaryInput"/> feeds a bivariate transform's secondary axis. It is a side
    /// input, never a path edge for the upstream walk: <see cref="GetInputConnections"/> yields
    /// <see cref="Input"/> first, so the primary path threads through <see cref="Input"/> and
    /// the secondary chain stays off-path. The graph validates that the secondary input resolves
    /// to the hazard's secondary output through univariate transforms only
    /// (<c>ComponentGraph.TryResolveSecondaryChain</c>).
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
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement(RiskSerializationMode)"/>.</param>
        /// <param name="resolver">
        /// The function resolver, required only to read a by-reference form. An unresolvable
        /// reference is recorded and reported by <see cref="Validate"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a serialized function child cannot be reconstructed — dropping the wrapped
        /// function silently would lose model content on the next save.
        /// </exception>
        public TransformElement(XElement xElement, IRiskFunctionResolver? resolver = null)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            ReadBaseFromXElement(xElement);
            _pendingInput = ReadPendingConnection(xElement, "Source");
            _pendingSecondaryInput = ReadPendingConnection(xElement, "SecondarySource");

            var functionChild = xElement.Element(nameof(Function))?.Elements().FirstOrDefault();
            if (functionChild != null)
            {
                // The resolver threads into the inline factory so an inline composite child can
                // resolve its own by-reference children.
                _function = ReadFunctionEntry<ITransformFunction>(
                    functionChild, resolver, c => RiskFunctionFactory.CreateFromXElement(c, resolver), "transform function");
                SubscribeFunction(_function);
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
        /// Backing field for <see cref="SecondaryInput"/>.
        /// </summary>
        private RiskConnection? _secondaryInput;

        /// <summary>
        /// The pending serialized input reference, resolved by the graph after construction.
        /// </summary>
        private PendingConnection? _pendingInput;

        /// <summary>
        /// The pending serialized secondary-input reference, resolved by the graph after
        /// construction.
        /// </summary>
        private PendingConnection? _pendingSecondaryInput;

        /// <summary>
        /// The wrapped transform function (referenced, not owned: a consuming layer may store one function and use it in several graphs). Null while unset —
        /// validation reports it.
        /// </summary>
        public ITransformFunction? Function
        {
            get { return _function; }
            set
            {
                if (!ReferenceEquals(_function, value))
                {
                    _function = SwapFunctionSubscription(_function, value);
                    RaisePropertyChange(nameof(Function));
                    RaisePropertyChange(nameof(InputCount));
                    RaisePropertyChange(nameof(OutputCount));
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

        /// <summary>
        /// The secondary input feeding a bivariate transform's secondary axis. Setting it while
        /// the wrapped transform is univariate is a validation error; a bivariate transform
        /// without it is one too. The graph validates that the connection resolves to the
        /// hazard's secondary output.
        /// </summary>
        public RiskConnection? SecondaryInput
        {
            get { return _secondaryInput; }
            set
            {
                if (!Equals(_secondaryInput, value))
                {
                    _secondaryInput = value;
                    RaisePropertyChange(nameof(SecondaryInput));
                }
            }
        }

        /// <summary>Restores both structural inputs without publishing a partially rolled-back edit.</summary>
        /// <param name="input">The checkpointed primary input.</param>
        /// <param name="secondaryInput">The checkpointed secondary input.</param>
        internal void RestoreInputConnections(RiskConnection? input, RiskConnection? secondaryInput)
        {
            _input = input;
            _secondaryInput = secondaryInput;
        }

        /// <inheritdoc/>
        public override RiskElementType ElementType => RiskElementType.Transform;

        /// <inheritdoc/>
        /// <remarks>
        /// Arity-derived: 2 when the wrapped function is bivariate (primary x plus secondary y),
        /// otherwise 1.
        /// </remarks>
        public override int InputCount => _function is IBivariateTransformFunction ? 2 : 1;

        /// <inheritdoc/>
        /// <remarks>
        /// Arity-derived: 2 when the wrapped function is bivariate — port 0 carries
        /// z = f(x, y), the transformed primary, and port 1 passes the secondary value through
        /// unchanged — otherwise 1.
        /// </remarks>
        public override int OutputCount => _function is IBivariateTransformFunction ? 2 : 1;

        #endregion

        #region IRiskElement Methods

        /// <inheritdoc/>
        /// <remarks>
        /// Yields <see cref="Input"/> first, then <see cref="SecondaryInput"/>. The order is
        /// load-bearing: the graph's upstream-path walk takes the first connection as the
        /// primary edge, which keeps secondary chains off-path.
        /// </remarks>
        public override IEnumerable<RiskConnection> GetInputConnections()
        {
            if (_input != null) yield return _input;
            if (_secondaryInput != null) yield return _secondaryInput;
        }

        /// <inheritdoc/>
        public override IEnumerable<IRiskFunction> GetFunctions()
        {
            if (_function != null) yield return _function;
        }


        /// <inheritdoc/>
        public override bool TryAssignFunction(IRiskFunction function, out string error)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (function is not ITransformFunction typed)
            {
                error = FunctionMismatchMessage(function, "a transform function");
                return false;
            }

            Function = typed;
            error = string.Empty;
            return true;
        }
        /// <inheritdoc/>
        /// <remarks>
        /// Errors: missing name (base); no wrapped function; a secondary input while the wrapped
        /// transform is univariate (the secondary axis has no meaning for it); a bivariate
        /// wrapped transform without a secondary input (the secondary axis has no source); the
        /// wrapped function's own errors (aggregated with the element name as context).
        /// Connectivity is validated by the graph, which sees the whole topology.
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
                if (_secondaryInput != null && _function is not IBivariateTransformFunction)
                {
                    messages.Add($"Error: The transform element '{Name}' has a secondary input, but its transform function is univariate.");
                }
                if (_secondaryInput == null && _function is IBivariateTransformFunction)
                {
                    messages.Add($"Error: The transform element '{Name}' wraps a bivariate transform but has no secondary input; connect the hazard's secondary output (or a secondary-chain transform) to its secondary input.");
                }
                AggregateWithContext(messages, _function.Validate().ValidationMessages);
            }
            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        public override XElement ToXElement(RiskSerializationMode mode)
        {
            var element = new XElement(nameof(TransformElement));
            AddBaseAttributesToXElement(element);
            WriteConnection(element, "Source", _input);
            WriteConnection(element, "SecondarySource", _secondaryInput);
            if (_function != null) element.Add(new XElement(nameof(Function), WriteFunctionEntry(_function, mode)));
            return element;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new TransformElement();
            CopyBaseTo(clone);
            clone._function = _function == null ? null : RiskFunctionFactory.CreateTransformFunction(_function.ToXElement());
            clone.SubscribeFunction(clone._function);
            return clone;
        }

        /// <inheritdoc/>
        public override void ResolveDeserializedReferences(RiskElementResolver resolver)
        {
            _input = ResolveConnection(_pendingInput, resolver, $"The transform element '{Name}' input");
            _secondaryInput = ResolveConnection(_pendingSecondaryInput, resolver, $"The transform element '{Name}' secondary input");
            _pendingInput = null;
            _pendingSecondaryInput = null;
        }

        /// <inheritdoc/>
        public override void ResolveClonedConnections(IRiskElement original, IReadOnlyDictionary<IRiskElement, IRiskElement> cloneMap)
        {
            if (original is TransformElement transform)
            {
                _input = RemapConnection(transform._input, cloneMap);
                _secondaryInput = RemapConnection(transform._secondaryInput, cloneMap);
            }
        }

        #endregion
    }
}
