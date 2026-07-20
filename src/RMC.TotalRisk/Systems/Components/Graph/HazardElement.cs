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
        /// Restores a hazard element from its serialized form.
        /// </summary>
        /// <param name="xElement">The serialized form produced by <see cref="ToXElement"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a serialized function child cannot be reconstructed — dropping the wrapped
        /// function silently would lose model content on the next save.
        /// </exception>
        public HazardElement(XElement xElement)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            ReadBaseFromXElement(xElement);

            var functionChild = xElement.Element(nameof(Function))?.Elements().FirstOrDefault();
            if (functionChild != null)
            {
                _function = RiskFunctionFactory.CreateHazardFunction(functionChild)
                    ?? throw new InvalidOperationException(
                        $"Unrecognized hazard function element '{functionChild.Name.LocalName}' in the serialized hazard element '{Name}'. " +
                        "The element cannot be reconstructed faithfully; the serialized form may come from a newer version.");
            }
        }

        #endregion

        #region Members

        /// <summary>
        /// Backing field for <see cref="Function"/>.
        /// </summary>
        private IHazardFunction? _function;

        /// <summary>
        /// The wrapped hazard function (owned; serialized inline). Null while unset — validation
        /// reports it.
        /// </summary>
        public IHazardFunction? Function
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

        /// <inheritdoc/>
        /// <inheritdoc/>
        public override RiskElementType ElementType => RiskElementType.Hazard;

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
        /// <remarks>
        /// Errors: missing name (base); no wrapped function; the wrapped function's own errors
        /// (aggregated with the element name as context).
        /// </remarks>
        public override (bool IsValid, List<string> ValidationMessages) Validate()
        {
            var (_, messages) = base.Validate();
            if (_function is null)
            {
                messages.Add($"Error: The hazard element '{Name}' has no hazard function assigned.");
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
            var element = new XElement(nameof(HazardElement));
            AddBaseAttributesToXElement(element);
            if (_function != null) element.Add(new XElement(nameof(Function), _function.ToXElement()));
            return element;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new HazardElement();
            CopyBaseTo(clone);
            clone._function = _function == null ? null : RiskFunctionFactory.CreateHazardFunction(_function.ToXElement());
            return clone;
        }

        #endregion
    }
}
