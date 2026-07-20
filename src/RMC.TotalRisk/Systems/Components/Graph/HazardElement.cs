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
    /// The root element of a system component's risk graph: wraps the hazard (frequency)
    /// function. Output-only — the Hydrologics source analog: no inputs, one output port (two
    /// when bivariate hazards land in Phase 11).
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <b>Authors:</b>
    ///     Haden Smith, USACE Risk Management Center, cole.h.smith@usace.army.mil
    /// </para>
    /// <para>
    /// Every complete path of the graph starts at the component's single hazard element. The
    /// element carries no component options — <c>FailureModeMethod</c>, dependencies, and the
    /// hazard threshold live on the owning <c>SystemComponent</c> exactly as in v1.0.
    /// </para>
    /// </remarks>
    public class HazardElement : RiskElementBase
    {
        #region Construction

        /// <summary>
        /// Initializes an unnamed hazard element with no wrapped function.
        /// </summary>
        public HazardElement()
        {
        }

        /// <summary>
        /// Initializes a named hazard element with no wrapped function.
        /// </summary>
        /// <param name="name">The element name.</param>
        public HazardElement(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Restores a hazard element from its serialized form: inline function content is
        /// reconstructed, a serialized function reference is resolved through the resolver.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement(RiskSerializationMode)"/>.</param>
        /// <param name="resolver">
        /// The function resolver, required only to read a by-reference form. An unresolvable
        /// reference is recorded and reported by <see cref="Validate"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a serialized function child cannot be reconstructed — dropping the wrapped
        /// function silently would lose model content on the next save — or when a serialized
        /// reference id is stale.
        /// </exception>
        public HazardElement(XElement xElement, IRiskFunctionResolver? resolver = null)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            ReadBaseFromXElement(xElement);

            var functionChild = xElement.Element(nameof(Function))?.Elements().FirstOrDefault();
            if (functionChild != null)
            {
                _function = ReadFunctionEntry<IHazardFunction>(
                    functionChild, resolver, RiskFunctionFactory.CreateFromXElement, "hazard function");
                SubscribeFunction(_function);
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Function"/>.
        /// </summary>
        private IHazardFunction? _function;

        /// <summary>
        /// The wrapped hazard function (referenced, not owned: a consuming layer may store one function and use it in several graphs). Null while unset — validation
        /// reports it.
        /// </summary>
        public IHazardFunction? Function
        {
            get { return _function; }
            set
            {
                if (!ReferenceEquals(_function, value))
                {
                    _function = SwapFunctionSubscription(_function, value);
                    RaisePropertyChange(nameof(Function));
                }
            }
        }

        /// <inheritdoc/>
        public override RiskElementType ElementType => RiskElementType.Hazard;

        /// <inheritdoc/>
        public override int InputCount => 0;

        /// <inheritdoc/>
        /// <remarks>
        /// One until bivariate hazard functions land (Phase 11), when this getter becomes
        /// arity-derived (2 for a bivariate wrapped function) — the only code change bivariate
        /// output ports require.
        /// </remarks>
        public override int OutputCount => 1;

        #endregion

        #region IRiskElement Methods

        /// <inheritdoc/>
        public override IEnumerable<RiskConnection> GetInputConnections()
        {
            yield break;
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
            if (function is not IHazardFunction typed)
            {
                error = FunctionMismatchMessage(function, "a hazard function");
                return false;
            }

            Function = typed;
            error = string.Empty;
            return true;
        }
        /// <inheritdoc/>
        /// <remarks>
        /// Errors: missing name (base); no wrapped function; the wrapped function's own errors
        /// (aggregated with the element name as context).
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var (_, messages) = base.Validate();
            if (_function is null)
            {
                if (!HasUnresolvedFunctionReferences)
                {
                    messages.Add($"Error: The hazard element '{Name}' has no hazard function assigned.");
                }
            }
            else
            {
                AggregateWithContext(messages, _function.Validate().ValidationMessages);
            }
            return (messages.FindIndex(m => m.StartsWith("Error:", StringComparison.Ordinal)) < 0, messages);
        }

        /// <inheritdoc/>
        public override XElement ToXElement(RiskSerializationMode mode)
        {
            var element = new XElement(nameof(HazardElement));
            AddBaseAttributesToXElement(element);
            if (_function != null) element.Add(new XElement(nameof(Function), WriteFunctionEntry(_function, mode)));
            return element;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new HazardElement();
            CopyBaseTo(clone);
            clone._function = _function == null ? null : RiskFunctionFactory.CreateHazardFunction(_function.ToXElement());
            clone.SubscribeFunction(clone._function);
            return clone;
        }

        #endregion
    }
}
