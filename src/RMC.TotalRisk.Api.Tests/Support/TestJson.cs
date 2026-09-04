using System.Text.Json;
using System.Text.Json.Serialization;

namespace RMC.TotalRisk.Api.Tests.Support;

/// <summary>
/// The test mirror of the API's wire serializer options (camelCase, string enums, ignore-null,
/// named floating-point literals) plus round-trip helpers. <c>McpJsonTests</c> asserts this
/// mirror stays in parity with the production options.
/// </summary>
public static class TestJson
{
    /// <summary>The mirrored wire options.</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Builds the mirrored options.</summary>
    /// <returns>The configured options.</returns>
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Serializes a value with the wire options.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserializes JSON with the wire options.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="json">The JSON text.</param>
    /// <returns>The value.</returns>
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Serializes then deserializes a value with the wire options.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The round-tripped value.</returns>
    public static T? Roundtrip<T>(T value) => Deserialize<T>(Serialize(value));
}
