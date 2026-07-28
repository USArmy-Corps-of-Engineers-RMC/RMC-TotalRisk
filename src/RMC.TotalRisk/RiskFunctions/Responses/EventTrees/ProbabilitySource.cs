using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Core;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions;

namespace RMC.TotalRisk.RiskFunctions.Responses.EventTrees
{
    /// <summary>
    /// The discriminated conditional-probability source of a <see cref="ChanceNode"/>: a fixed
    /// scalar, an uncertain table aligned to the owning tree hazards, or a referenced response.
    /// </summary>
    public sealed class ProbabilitySource
    {
        /// <summary>Initializes a deterministic scalar source.</summary>
        /// <param name="probability">The conditional probability.</param>
        public ProbabilitySource(double probability)
        {
            Kind = ProbabilitySourceKind.DeterministicScalar;
            ScalarProbability = probability;
        }

        /// <summary>Initializes an uncertain tabular source.</summary>
        /// <param name="table">The probability table aligned to the owning tree hazards.</param>
        /// <exception cref="ArgumentNullException">Thrown when the table is null.</exception>
        public ProbabilitySource(UncertainOrderedPairedData table)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            Kind = ProbabilitySourceKind.UncertainTabular;
        }

        /// <summary>Initializes a referenced-response source.</summary>
        /// <param name="responseFunction">The response evaluated at each tree hazard.</param>
        /// <exception cref="ArgumentNullException">Thrown when the function is null.</exception>
        public ProbabilitySource(IResponseFunction responseFunction)
        {
            ResponseFunction = responseFunction ?? throw new ArgumentNullException(nameof(responseFunction));
            Kind = ProbabilitySourceKind.ResponseFunctionReference;
        }

        /// <summary>Restores a probability source from its serialized form.</summary>
        /// <param name="xElement">The serialized probability source.</param>
        /// <param name="resolver">The optional function resolver for by-reference content.</param>
        /// <param name="ownerName">The owning event-tree response name used in diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown when the element is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the source kind or content is malformed.</exception>
        internal ProbabilitySource(XElement xElement, IRiskFunctionResolver? resolver, string ownerName)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            Kind = SerializationUtilities.ReadEnum(xElement, nameof(Kind), ProbabilitySourceKind.DeterministicScalar);
            switch (Kind)
            {
                case ProbabilitySourceKind.DeterministicScalar:
                    ScalarProbability = SerializationUtilities.ReadDouble(xElement, nameof(ScalarProbability));
                    break;
                case ProbabilitySourceKind.UncertainTabular:
                    var tableElement = xElement.Element(nameof(UncertainOrderedPairedData));
                    if (tableElement == null)
                        throw new InvalidOperationException("An uncertain event-tree probability source has no serialized table.");
                    Table = new UncertainOrderedPairedData(tableElement)
                    {
                        OrderX = SortOrder.Ascending,
                        OrderY = SortOrder.None,
                        StrictX = true,
                        StrictY = false,
                    };
                    Table.Validate();
                    break;
                case ProbabilitySourceKind.ResponseFunctionReference:
                    var child = xElement.Element("Function")?.Elements().FirstOrDefault();
                    if (child == null)
                        throw new InvalidOperationException("A response-backed event-tree probability source has no serialized function.");
                    ResponseFunction = FunctionEntry.Read<IResponseFunction>(
                        child, resolver, c => RiskFunctionFactory.CreateFromXElement(c, resolver), ownerName,
                        $"The event-tree response '{ownerName}'", "response function", _unresolvedReferences);
                    break;
                default:
                    throw new InvalidOperationException($"The event-tree probability source kind '{Kind}' is not supported.");
            }
        }

        /// <summary>The represented source kind.</summary>
        public ProbabilitySourceKind Kind { get; }

        /// <summary>The scalar probability when <see cref="Kind"/> is deterministic scalar.</summary>
        public double? ScalarProbability { get; }

        /// <summary>The aligned table when <see cref="Kind"/> is uncertain tabular.</summary>
        public UncertainOrderedPairedData? Table { get; }

        /// <summary>The referenced response when <see cref="Kind"/> is response-function reference.</summary>
        public IResponseFunction? ResponseFunction { get; }

        /// <summary>Unresolved serialized function references retained for validation.</summary>
        private readonly List<string> _unresolvedReferences = new List<string>();

        /// <summary>Whether the source carries no knowledge uncertainty.</summary>
        internal bool IsDeterministic => Kind switch
        {
            ProbabilitySourceKind.DeterministicScalar => true,
            ProbabilitySourceKind.UncertainTabular => Table!.Distribution == UnivariateDistributionType.Deterministic,
            ProbabilitySourceKind.ResponseFunctionReference => ResponseFunction?.IsDeterministic ?? false,
            _ => false,
        };

        /// <summary>The local and recursively referenced sampler dimensions.</summary>
        internal int SamplingDimensions => Kind switch
        {
            ProbabilitySourceKind.DeterministicScalar => 0,
            ProbabilitySourceKind.UncertainTabular => 1,
            ProbabilitySourceKind.ResponseFunctionReference => ResponseFunction?.SamplingDimensions ?? 0,
            _ => 0,
        };

        /// <summary>Validates the source against the owning tree's hazard axis.</summary>
        /// <param name="hazards">The owning tree hazards.</param>
        /// <param name="nodeName">The chance-node display name used in diagnostics.</param>
        /// <returns>Deterministically ordered validation messages.</returns>
        internal List<string> Validate(IReadOnlyList<double> hazards, string nodeName)
        {
            var messages = new List<string>();
            if (Kind == ProbabilitySourceKind.DeterministicScalar)
            {
                double value = ScalarProbability!.Value;
                if (!double.IsFinite(value) || value < 0d || value > 1d)
                    messages.Add($"Error: Chance node '{nodeName}' has a scalar probability outside [0, 1].");
                return messages;
            }

            if (Kind == ProbabilitySourceKind.UncertainTabular)
            {
                if (ReferenceEquals(Table, null) || !Table.IsValid || Table.Count != hazards.Count)
                {
                    messages.Add($"Error: Chance node '{nodeName}' must have one valid probability-table ordinate per event-tree hazard level.");
                    return messages;
                }
                for (int i = 0; i < hazards.Count; i++)
                {
                    if (Table[i].X != hazards[i])
                    {
                        messages.Add($"Error: Chance node '{nodeName}' probability-table hazards are not aligned to the event tree.");
                        break;
                    }
                    if (ReferenceEquals(Table[i].Y, null) || Table[i].Y!.Minimum < 0d || Table[i].Y!.Maximum > 1d)
                    {
                        messages.Add($"Error: Chance node '{nodeName}' probability distributions must remain within [0, 1].");
                        break;
                    }
                }
                return messages;
            }

            foreach (string reference in _unresolvedReferences)
                messages.Add($"Error: Chance node '{nodeName}' references {reference}, which was not found.");
            if (ResponseFunction == null && _unresolvedReferences.Count == 0)
                messages.Add($"Error: Chance node '{nodeName}' has no referenced response function.");
            else if (ResponseFunction is EventTreeResponse)
                messages.Add($"Error: Chance node '{nodeName}' references another event-tree response; recursive tree-response sources are deferred beyond the first Phase 10A implementation slice.");
            else if (ResponseFunction != null && !ResponseFunction.Validate().IsValid)
                messages.Add($"Error: Chance node '{nodeName}' references an invalid response function '{ResponseFunction.Name}'.");
            return messages;
        }

        /// <summary>Evaluates the source's mean probability.</summary>
        /// <param name="hazard">The current hazard value.</param>
        /// <param name="hazardIndex">The aligned tree-hazard index.</param>
        /// <returns>The raw conditional probability.</returns>
        internal double EvaluateMean(double hazard, int hazardIndex)
        {
            return Kind switch
            {
                ProbabilitySourceKind.DeterministicScalar => ScalarProbability!.Value,
                ProbabilitySourceKind.UncertainTabular => Table!.CurveSample()[hazardIndex].Y,
                ProbabilitySourceKind.ResponseFunctionReference => ResponseFunction!.SampleFunction().CDF(hazard),
                _ => throw new InvalidOperationException($"Unsupported probability source kind '{Kind}'."),
            };
        }

        /// <summary>Evaluates the source at one co-monotonic percentile.</summary>
        /// <param name="hazard">The current hazard value.</param>
        /// <param name="hazardIndex">The aligned tree-hazard index.</param>
        /// <param name="percentile">The knowledge percentile.</param>
        /// <returns>The raw conditional probability.</returns>
        internal double EvaluatePercentile(double hazard, int hazardIndex, double percentile)
        {
            return Kind switch
            {
                ProbabilitySourceKind.DeterministicScalar => ScalarProbability!.Value,
                ProbabilitySourceKind.UncertainTabular => Table!.CurveSample(percentile)[hazardIndex].Y,
                ProbabilitySourceKind.ResponseFunctionReference => ResponseFunction!.SampleFunction(percentile).CDF(hazard),
                _ => throw new InvalidOperationException($"Unsupported probability source kind '{Kind}'."),
            };
        }

        /// <summary>Evaluates the source for one sampler realization.</summary>
        /// <param name="hazard">The current hazard value.</param>
        /// <param name="hazardIndex">The aligned tree-hazard index.</param>
        /// <param name="realizationIndex">The realization index.</param>
        /// <param name="localPercentile">The local table percentile, or zero for a nonlocal source.</param>
        /// <returns>The raw conditional probability.</returns>
        internal double EvaluateRealization(double hazard, int hazardIndex, int realizationIndex, double localPercentile)
        {
            return Kind switch
            {
                ProbabilitySourceKind.DeterministicScalar => ScalarProbability!.Value,
                ProbabilitySourceKind.UncertainTabular => Table!.CurveSample(localPercentile)[hazardIndex].Y,
                ProbabilitySourceKind.ResponseFunctionReference => ResponseFunction!.SampleFunction(realizationIndex).CDF(hazard),
                _ => throw new InvalidOperationException($"Unsupported probability source kind '{Kind}'."),
            };
        }
        /// <summary>Evaluates the mean source at a caller hazard not aligned to this source's owner axis.</summary>
        internal double EvaluateMeanAtHazard(double hazard)
        {
            return Kind switch
            {
                ProbabilitySourceKind.DeterministicScalar => ScalarProbability!.Value,
                ProbabilitySourceKind.UncertainTabular => Table!.CurveSample().GetYFromX(hazard),
                ProbabilitySourceKind.ResponseFunctionReference => ResponseFunction!.SampleFunction().CDF(hazard),
                _ => throw new InvalidOperationException($"Unsupported probability source kind '{Kind}'."),
            };
        }

        /// <summary>Evaluates a percentile source at a caller hazard not aligned to this source's owner axis.</summary>
        internal double EvaluatePercentileAtHazard(double hazard, double percentile)
        {
            return Kind switch
            {
                ProbabilitySourceKind.DeterministicScalar => ScalarProbability!.Value,
                ProbabilitySourceKind.UncertainTabular => Table!.CurveSample(percentile).GetYFromX(hazard),
                ProbabilitySourceKind.ResponseFunctionReference => ResponseFunction!.SampleFunction(percentile).CDF(hazard),
                _ => throw new InvalidOperationException($"Unsupported probability source kind '{Kind}'."),
            };
        }

        /// <summary>Evaluates a realization at a caller hazard not aligned to this source's owner axis.</summary>
        internal double EvaluateRealizationAtHazard(double hazard, int realizationIndex, double localPercentile)
        {
            return Kind switch
            {
                ProbabilitySourceKind.DeterministicScalar => ScalarProbability!.Value,
                ProbabilitySourceKind.UncertainTabular => Table!.CurveSample(localPercentile).GetYFromX(hazard),
                ProbabilitySourceKind.ResponseFunctionReference => ResponseFunction!.SampleFunction(realizationIndex).CDF(hazard),
                _ => throw new InvalidOperationException($"Unsupported probability source kind '{Kind}'."),
            };
        }

        /// <summary>Serializes the discriminated source in the requested function-reference mode.</summary>
        /// <param name="mode">The serialization mode.</param>
        /// <returns>The serialized source.</returns>
        internal XElement ToXElement(RiskSerializationMode mode)
        {
            var element = new XElement(nameof(ProbabilitySource));
            element.SetAttributeValue(nameof(Kind), Kind.ToString());
            switch (Kind)
            {
                case ProbabilitySourceKind.DeterministicScalar:
                    element.SetAttributeValue(nameof(ScalarProbability), SerializationUtilities.FormatDouble(ScalarProbability!.Value));
                    break;
                case ProbabilitySourceKind.UncertainTabular:
                    if (!ReferenceEquals(Table, null)) element.Add(Table.SaveToXElement());
                    break;
                case ProbabilitySourceKind.ResponseFunctionReference:
                    if (ResponseFunction != null)
                        element.Add(new XElement("Function", FunctionEntry.Write(ResponseFunction, mode)));
                    break;
            }
            return element;
        }

        /// <summary>Builds the metadata-free projected identity of this source.</summary>
        /// <returns>The canonical identity element.</returns>
        internal XElement ToIdentityXElement()
        {
            var element = new XElement(nameof(ProbabilitySource));
            element.SetAttributeValue(nameof(Kind), Kind.ToString());
            switch (Kind)
            {
                case ProbabilitySourceKind.DeterministicScalar:
                    element.SetAttributeValue(nameof(ScalarProbability), SerializationUtilities.FormatDouble(ScalarProbability!.Value));
                    break;
                case ProbabilitySourceKind.UncertainTabular:
                    if (!ReferenceEquals(Table, null)) element.Add(Table.SaveToXElement());
                    break;
                case ProbabilitySourceKind.ResponseFunctionReference:
                    element.SetAttributeValue("FunctionHash", ReferenceEquals(ResponseFunction, null)
                        ? string.Empty
                        : ResponseFunction is EventTreeResponse
                            ? "UnsupportedRecursiveEventTreeReference"
                            : CanonicalContentHasher.ToTokenHex(ResponseFunction.CanonicalHash()));
                    break;
            }
            return element;
        }

        /// <summary>Returns a stable token for sampler ordering and occurrence seeding.</summary>
        /// <returns>The uppercase canonical SHA-256 token.</returns>
        internal string CanonicalToken()
        {
            return CanonicalContentHasher.ToTokenHex(
                CanonicalContentHasher.Hash(ToIdentityXElement(), CanonicalizationRules.ModelRules));
        }
    }
}
