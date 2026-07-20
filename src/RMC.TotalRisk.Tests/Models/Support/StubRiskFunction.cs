using System.Collections.Generic;
using System.Xml.Linq;
using Numerics.Distributions;
using RMC.TotalRisk.Models.Support;

namespace RMC.TotalRisk.Tests.Models.Support;

/// <summary>
/// Minimal concrete <see cref="RiskFunctionBase"/> used to exercise the kernel base class and the
/// kitchen-sink hash-invariance scaffold: one compute-relevant scalar (<see cref="Value"/>) plus a
/// configurable sampling dimension, with pass-throughs exposing the protected sampler surface.
/// </summary>
public sealed class StubRiskFunction : RiskFunctionBase
{
    /// <summary>Backing field for <see cref="Value"/>.</summary>
    private double _value = 1.0;

    /// <summary>The single compute-relevant scalar; editing it must move the canonical hash.</summary>
    public double Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                RaisePropertyChange(nameof(Value));
            }
        }
    }

    /// <summary>The sampling dimension the stub reports; settable per test.</summary>
    public int Dimensions { get; set; } = 1;

    /// <inheritdoc/>
    public override bool IsDeterministic => Dimensions == 0;

    /// <inheritdoc/>
    public override int SamplingDimensions => Dimensions;

    /// <summary>Exposes the protected <see cref="RiskFunctionBase.Percentile(int, int)"/> for asserts.</summary>
    /// <param name="realizationIndex">The realization row.</param>
    /// <param name="dimension">The sampling dimension column.</param>
    /// <returns>The pre-allocated percentile.</returns>
    public double PercentileAt(int realizationIndex, int dimension) => Percentile(realizationIndex, dimension);

    /// <inheritdoc/>
    public override UncertaintyAnalysisResults? ComputeUncertaintyResults(double confidenceIntervalWidth = 0.9) => null;

    /// <inheritdoc/>
    public override (bool IsValid, List<string> ValidationMessages) Validate() => (true, new List<string>());

    /// <inheritdoc/>
    public override XElement ToXElement()
    {
        var element = new XElement(nameof(StubRiskFunction));
        element.SetAttributeValue(nameof(Name), Name);
        element.SetAttributeValue(nameof(Description), Description);
        element.SetAttributeValue(nameof(SpecifiedHazard), SpecifiedHazard);
        element.SetAttributeValue(nameof(HazardUnit), HazardUnit);
        element.SetAttributeValue(nameof(Value), SerializationUtilities.FormatDouble(Value));
        return element;
    }
}
