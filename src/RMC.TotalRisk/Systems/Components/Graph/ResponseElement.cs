using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Responses;

namespace RMC.TotalRisk.Systems.Components.Graph
{
    /// <summary>
    /// A response element of a system component's risk graph: wraps a response (fragility)
    /// function evaluated at the incoming hazard signal. One input and one output — the hazard
    /// signal passes through to downstream elements; the fail/non-fail branch accounting is the
    /// risk engine's concern.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// v1.1 allows response elements to chain (v1.0 forbade response→response): a downstream
    /// response applies to both the fail and non-fail branches of the upstream response. A path
    /// with no response element is the component's non-failure path (at most one per component) —
    /// the <see cref="NonFailResponse"/> sentinel is therefore never wrapped in a response
    /// element (validated).
    /// </para>
    /// <para>
    /// <see cref="SecondaryInput"/> and its serialized <c>SecondarySource*</c> attributes are
    /// reserved now for bivariate response functions (Phase 11): a bivariate response consumes
    /// both outputs of a bivariate hazard. Setting it while the wrapped response is univariate is
    /// an error — the serialized shape needs no change when the capability goes live.
    /// </para>
    /// </remarks>
    public class ResponseElement : RiskElementBase
    {
        #region Construction

        /// <summary>
        /// Initializes an unnamed response element with no wrapped function.
        /// </summary>
        public ResponseElement()
        {
        }

        /// <summary>
        /// Initializes a named response element with no wrapped function.
        /// </summary>
        /// <param name="name">The element name.</param>
        public ResponseElement(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Restores a response element from its serialized form. The input connections are
        /// captured pending and resolved by the graph via
        /// <see cref="ResolveDeserializedReferences"/>.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a serialized function child cannot be reconstructed — dropping the wrapped
        /// function silently would lose model content on the next save.
        /// </exception>
        public ResponseElement(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            ReadBaseFromXElement(xElement);
            _pendingInput = ReadPendingConnection(xElement, "Source");
            _pendingSecondaryInput = ReadPendingConnection(xElement, "SecondarySource");

            var functionChild = xElement.Element(nameof(Function))?.Elements().FirstOrDefault();
            if (functionChild != null)
            {
                _function = RiskFunctionFactory.CreateResponseFunction(functionChild)
                    ?? throw new InvalidOperationException(
                        $"Unrecognized response function element '{functionChild.Name.LocalName}' in the serialized response element '{Name}'. " +
                        "The element cannot be reconstructed faithfully; the serialized form may come from a newer version.");
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Function"/>.
        /// </summary>
        private IResponseFunction? _function;

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
        private (Guid? Id, string? Name, int Port)? _pendingInput;

        /// <summary>
        /// The pending serialized secondary-input reference, resolved by the graph after
        /// construction.
        /// </summary>
        private (Guid? Id, string? Name, int Port)? _pendingSecondaryInput;

        /// <summary>
        /// The wrapped response function (owned; serialized inline). Null while unset —
        /// validation reports it.
        /// </summary>
        public IResponseFunction? Function
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
        /// The structural input: which upstream element output feeds this response. Null while
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
        /// The reserved secondary input for bivariate response functions (Phase 11). Setting it
        /// while the wrapped response is univariate is a validation error.
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

        /// <inheritdoc/>
        /// <remarks>
        /// One until bivariate response functions land (Phase 11), when this getter becomes
        /// arity-derived (2 for a bivariate wrapped function).
        /// </remarks>
        /// <inheritdoc/>
        public override RiskElementType ElementType => RiskElementType.Response;

        public override int InputCount => 1;

        /// <inheritdoc/>
        public override int OutputCount => 1;

        #endregion

        #region IRiskElement Methods

        /// <inheritdoc/>
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
        /// <remarks>
        /// Errors: missing name (base); no wrapped function; the non-failure sentinel wrapped in
        /// a response element (a non-failure path is a path with NO response element); a secondary
        /// input while the wrapped response is univariate (reserved for Phase 11); the wrapped
        /// function's own errors (aggregated with the element name as context).
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var (_, messages) = base.Validate();
            if (_function is null)
            {
                messages.Add($"Error: The response element '{Name}' has no response function assigned.");
            }
            else
            {
                if (_function is NonFailResponse)
                {
                    messages.Add($"Error: The response element '{Name}' wraps the non-failure response sentinel; a non-failure path is a path with no response element.");
                }
                AggregateWithContext(messages, _function.Validate().ValidationMessages);
            }

            if (_secondaryInput != null)
            {
                messages.Add($"Error: The response element '{Name}' has a secondary input, which is reserved for bivariate response functions (Phase 11).");
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        public override XElement ToXElement()
        {
            var element = new XElement(nameof(ResponseElement));
            AddBaseAttributesToXElement(element);
            WriteConnection(element, "Source", _input);
            WriteConnection(element, "SecondarySource", _secondaryInput);
            if (_function != null) element.Add(new XElement(nameof(Function), _function.ToXElement()));
            return element;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new ResponseElement();
            CopyBaseTo(clone);
            clone._function = _function == null ? null : RiskFunctionFactory.CreateResponseFunction(_function.ToXElement());
            return clone;
        }

        /// <inheritdoc/>
        public override void ResolveDeserializedReferences(RiskElementResolver resolver)
        {
            _input = ResolveConnection(_pendingInput, resolver, $"The response element '{Name}' input");
            _secondaryInput = ResolveConnection(_pendingSecondaryInput, resolver, $"The response element '{Name}' secondary input");
            _pendingInput = null;
            _pendingSecondaryInput = null;
        }

        /// <inheritdoc/>
        public override void ResolveClonedConnections(IRiskElement original, IReadOnlyDictionary<IRiskElement, IRiskElement> cloneMap)
        {
            if (original is ResponseElement response)
            {
                _input = RemapConnection(response._input, cloneMap);
                _secondaryInput = RemapConnection(response._secondaryInput, cloneMap);
            }
        }

        #endregion
    }
}
