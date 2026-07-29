using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Responses.Trees;

namespace RMC.TotalRisk.Systems.Components.Graph
{
    /// <summary>
    /// A response element of a system component's risk graph: wraps a response (fragility)
    /// function evaluated at the incoming hazard signal. One input feeds either the established
    /// aggregate Fail/Non-Fail outputs or opt-in stable terminal outputs for a branching response.
    /// Every output carries the same pass-through hazard signal; its selected probability branch
    /// participates in downstream failure-mode projection.
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
    /// reserved now for future bivariate response functions: a bivariate response consumes
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
        public ResponseElement(XElement xElement, IRiskFunctionResolver? resolver = null)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            ReadBaseFromXElement(xElement);
            _expandBranchOutputs = SerializationUtilities.ReadBoolean(xElement, nameof(ExpandBranchOutputs));
            _pendingInput = ReadPendingConnection(xElement, "Source");
            _pendingSecondaryInput = ReadPendingConnection(xElement, "SecondarySource");

            var functionChild = xElement.Element(nameof(Function))?.Elements().FirstOrDefault();
            if (functionChild != null)
            {
                // The resolver threads into the inline factory so an inline composite child can
                // resolve its own by-reference children.
                _function = ReadFunctionEntry<IResponseFunction>(
                    functionChild, resolver, c => RiskFunctionFactory.CreateFromXElement(c, resolver), "response function");
                SubscribeFunction(_function);
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Function"/>.
        /// </summary>
        private IResponseFunction? _function;

        /// <summary>Whether this element exposes constituent branch outputs instead of aggregate outputs.</summary>
        private bool _expandBranchOutputs;

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
        /// The wrapped response function (referenced, not owned: a consuming layer may store one function and use it in several graphs). Null while unset —
        /// validation reports it.
        /// </summary>
        public IResponseFunction? Function
        {
            get { return _function; }
            set
            {
                if (!ReferenceEquals(_function, value))
                {
                    _function = SwapFunctionSubscription(_function, value);
                    RaisePropertyChange(nameof(Function));
                    RaisePropertyChange(nameof(OutputCount));
                }
            }
        }

        /// <summary>
        /// Gets or sets whether a branching response exposes its constituent end-state outputs.
        /// The default aggregate view retains ports 0 = Fail and 1 = Non-Fail. Expanded mode
        /// exposes only the stable descriptors returned by <see cref="GetAvailableBranches"/>.
        /// </summary>
        public bool ExpandBranchOutputs
        {
            get { return _expandBranchOutputs; }
            set
            {
                if (_expandBranchOutputs == value) return;
                _expandBranchOutputs = value;
                RaisePropertyChange(nameof(ExpandBranchOutputs));
                RaisePropertyChange(nameof(OutputCount));
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
        /// The reserved secondary input for future bivariate response functions. Setting it
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

        /// <summary>Restores both structural inputs without publishing a partially rolled-back edit.</summary>
        /// <param name="input">The checkpointed primary input.</param>
        /// <param name="secondaryInput">The checkpointed secondary input.</param>
        internal void RestoreInputConnections(RiskConnection? input, RiskConnection? secondaryInput)
        {
            _input = input;
            _secondaryInput = secondaryInput;
        }

        /// <inheritdoc/>
        public override RiskElementType ElementType => RiskElementType.Response;

        /// <inheritdoc/>
        /// <remarks>
        /// One until bivariate response functions are introduced, when this getter becomes
        /// arity-derived (2 for a bivariate wrapped function).
        /// </remarks>
        public override int InputCount => 1;

        /// <inheritdoc/>
        /// <remarks>
        /// In the default aggregate view, the established contract remains output port 0 =
        /// <b>Fail</b> and output port 1 = <b>Non-Fail</b>. In the opt-in expanded view, only the
        /// branching response's stable terminal descriptors are available; port 2 is the implicit
        /// unmodeled branch and authored or linked terminal ports are append-only from port 3.
        /// All outputs carry the same pass-through hazard signal and differ only in path-probability
        /// algebra.
        /// </remarks>
        public override int OutputCount
        {
            get
            {
                if (!_expandBranchOutputs) return 2;
                IReadOnlyList<ResponseBranchDescriptor> branches = GetAvailableBranches();
                return branches.Count == 0 ? 0 : branches.Max(branch => branch.OutputPort) + 1;
            }
        }

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

        /// <summary>Gets the stable branches available through the expanded output view.</summary>
        /// <returns>The current branch descriptors in output-port order, or an empty list when the wrapped response is not branching.</returns>
        public IReadOnlyList<ResponseBranchDescriptor> GetAvailableBranches()
        {
            return _function is IBranchingResponseFunction branching
                ? branching.GetBranches()
                : Array.Empty<ResponseBranchDescriptor>();
        }

        /// <summary>Creates a connection to one stable expanded branch.</summary>
        /// <param name="branchId">The selected branch id.</param>
        /// <returns>A branch-addressed risk connection.</returns>
        /// <exception cref="InvalidOperationException">Thrown when expanded outputs are disabled or the branch is unavailable.</exception>
        public RiskConnection CreateBranchConnection(Guid branchId)
        {
            return new RiskConnection(this, RequireAvailableBranch(branchId));
        }

        /// <summary>Requires one currently available expanded branch.</summary>
        /// <param name="branchId">The selected branch id.</param>
        /// <returns>The matching descriptor.</returns>
        /// <exception cref="InvalidOperationException">Thrown when expanded outputs are disabled or the branch is unavailable.</exception>
        internal ResponseBranchDescriptor RequireAvailableBranch(Guid branchId)
        {
            if (!_expandBranchOutputs)
                throw new InvalidOperationException(
                    $"The response element '{Name}' is using its aggregate Fail/Non-Fail output view; enable expanded branch outputs before connecting an end state.");
            ResponseBranchDescriptor? branch = GetAvailableBranches()
                .FirstOrDefault(item => item.Id == branchId);
            return branch ?? throw new InvalidOperationException(
                $"The response element '{Name}' does not expose branch '{branchId:D}'.");
        }

        /// <summary>Resolves persisted branch identity through id, name-only migration, or port-only migration.</summary>
        /// <param name="branchId">The primary branch id.</param>
        /// <param name="branchName">The branch-name fallback.</param>
        /// <param name="sourcePort">The persisted output port.</param>
        /// <param name="linkDescription">The consuming connection description.</param>
        /// <returns>The repaired branch-addressed connection.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the branch address is stale, missing, or ambiguous.</exception>
        internal RiskConnection ResolveBranchConnection(Guid? branchId, string? branchName,
            int sourcePort, string linkDescription)
        {
            if (!_expandBranchOutputs)
                throw new InvalidOperationException(
                    $"{linkDescription} carries an expanded branch address, but response element '{Name}' uses the aggregate output view.");

            IReadOnlyList<ResponseBranchDescriptor> branches = GetAvailableBranches();
            ResponseBranchDescriptor? branch = null;
            if (branchId.HasValue)
            {
                branch = branches.FirstOrDefault(item => item.Id == branchId.Value);
                if (branch == null)
                    throw new InvalidOperationException(
                        $"{linkDescription} references stale branch id '{branchId.Value:D}' on response element '{Name}'.");
            }
            else if (!string.IsNullOrEmpty(branchName))
            {
                ResponseBranchDescriptor[] matches = branches
                    .Where(item => string.Equals(item.Name, branchName, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(
                        $"{linkDescription} branch-name fallback '{branchName}' is {(matches.Length == 0 ? "missing" : "ambiguous")} on response element '{Name}'.");
                branch = matches[0];
            }
            else
            {
                branch = branches.FirstOrDefault(item => item.OutputPort == sourcePort);
                if (branch == null)
                    throw new InvalidOperationException(
                        $"{linkDescription} references stale output port {sourcePort} on expanded response element '{Name}'.");
            }

            return new RiskConnection(this, branch.OutputPort, branch.Id, branch.Name);
        }

        /// <summary>Validates one downstream connection against the active output view.</summary>
        /// <param name="connection">The connection whose source is this response element.</param>
        /// <param name="error">The branch-specific error text, or an empty string.</param>
        /// <returns>True when the selected output is currently available.</returns>
        internal bool TryValidateOutputConnection(RiskConnection connection, out string error)
        {
            if (!_expandBranchOutputs)
            {
                if (connection.SourceBranchId.HasValue)
                {
                    error = $"references an expanded branch of response element '{Name}', which is using its aggregate Fail/Non-Fail output view.";
                    return false;
                }
                error = string.Empty;
                return true;
            }

            if (!connection.SourceBranchId.HasValue)
            {
                error = $"references output port {connection.SourcePort} of expanded response element '{Name}' without a stable branch id.";
                return false;
            }
            ResponseBranchDescriptor? descriptor = GetAvailableBranches()
                .FirstOrDefault(branch => branch.Id == connection.SourceBranchId.Value);
            if (descriptor == null)
            {
                error = $"references stale branch id '{connection.SourceBranchId.Value:D}' on expanded response element '{Name}'.";
                return false;
            }
            if (descriptor.OutputPort != connection.SourcePort)
            {
                error = $"references stale output port {connection.SourcePort} for branch '{descriptor.Name}' on response element '{Name}'; the stable branch now uses port {descriptor.OutputPort}.";
                return false;
            }
            error = string.Empty;
            return true;
        }


        /// <inheritdoc/>
        public override bool TryAssignFunction(IRiskFunction function, out string error)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (function is not IResponseFunction typed)
            {
                error = FunctionMismatchMessage(function, "a response function");
                return false;
            }

            Function = typed;
            error = string.Empty;
            return true;
        }
        /// <inheritdoc/>
        /// <remarks>
        /// Errors: missing name (base); no wrapped function; the non-failure sentinel wrapped in
        /// a response element (a non-failure path is a path with NO response element); a secondary
        /// input while the wrapped response is univariate (reserved for future bivariate
        /// responses); the wrapped
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

            if (_expandBranchOutputs && _function is not IBranchingResponseFunction)
            {
                messages.Add($"Error: The response element '{Name}' enables expanded branch outputs, but its response function is not branching.");
            }

            if (_secondaryInput != null)
            {
                messages.Add($"Error: The response element '{Name}' has a secondary input, which is reserved for future bivariate response functions.");
            }

            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        public override XElement ToXElement(RiskSerializationMode mode)
        {
            var element = new XElement(nameof(ResponseElement));
            AddBaseAttributesToXElement(element);
            element.SetAttributeValue(nameof(ExpandBranchOutputs), _expandBranchOutputs);
            WriteConnection(element, "Source", _input);
            WriteConnection(element, "SecondarySource", _secondaryInput);
            if (_function != null) element.Add(new XElement(nameof(Function), WriteFunctionEntry(_function, mode)));
            return element;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new ResponseElement();
            CopyBaseTo(clone);
            clone._expandBranchOutputs = _expandBranchOutputs;
            clone._function = _function == null ? null : RiskFunctionFactory.CreateResponseFunction(_function.ToXElement());
            clone.SubscribeFunction(clone._function);
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
