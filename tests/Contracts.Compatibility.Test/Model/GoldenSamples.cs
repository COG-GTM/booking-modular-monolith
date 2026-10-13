namespace Contracts.Compatibility.Test.Model;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MassTransit.Serialization;

// Golden payloads are the serialized bodies a producer emitted when the baseline was taken. They are
// never regenerated, so a consumer built against the current contracts must still read them.
public static class GoldenSamples
{
    private static readonly JsonSerializerOptions WireOptions = SystemTextJsonMessageSerializer.Options;

    public static string Create(Type messageType)
    {
        var constructor = messageType
            .GetConstructors()
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .First();
        var arguments = constructor
            .GetParameters()
            .Select(parameter => SampleValue(parameter.ParameterType, parameter.Name!))
            .ToArray();
        var instance = constructor.Invoke(arguments);
        var json = JsonSerializer.SerializeToNode(instance, messageType, WireOptions)!.AsObject();
        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    // Every member recorded in the baseline must survive a round trip from the old payload into the
    // current CLR type with the same value.
    public static IReadOnlyList<string> Verify(
        string messageName,
        MessageContract baseline,
        Type currentType,
        string sample
    )
    {
        var expected = JsonNode.Parse(sample)!.AsObject();
        object? message;
        try
        {
            message = JsonSerializer.Deserialize(sample, currentType, WireOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return [$"{messageName}: golden sample no longer deserializes: {exception.Message}"];
        }

        var actual = JsonSerializer.SerializeToNode(message, currentType, WireOptions)!.AsObject();
        var failures = new List<string>();
        foreach (var property in baseline.Properties.Keys)
        {
            var key = JsonNamingPolicy.CamelCase.ConvertName(property);
            if (!expected.ContainsKey(key))
            {
                continue;
            }

            if (!actual.TryGetPropertyValue(key, out var value) || !JsonNode.DeepEquals(expected[key], value))
            {
                failures.Add($"{messageName}.{property}: value from golden sample was lost on deserialization");
            }
        }

        return failures;
    }

    private static object? SampleValue(Type type, string name)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var seed = Math.Abs(StringComparer.Ordinal.GetHashCode(name)) % 1000;
        return underlying switch
        {
            _ when underlying == typeof(Guid) => Guid.Parse($"00000000-0000-0000-0000-{seed + 1:D12}"),
            _ when underlying == typeof(string) => $"sample-{name}",
            _ when underlying == typeof(int) => seed + 1,
            _ when underlying == typeof(long) => (long)seed + 1,
            _ when underlying == typeof(decimal) => seed + 0.5m,
            _ when underlying == typeof(double) => seed + 0.5d,
            _ when underlying == typeof(bool) => true,
            _ when underlying == typeof(DateTime) => new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            _ when underlying == typeof(DateTimeOffset) => new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            _ when underlying.IsEnum => Enum.GetValues(underlying)
                .GetValue(Math.Min(1, Enum.GetValues(underlying).Length - 1)),
            _ => throw new NotSupportedException(
                $"No golden sample generator for {underlying} ({name}); extend {nameof(GoldenSamples)}.{nameof(SampleValue)}."
            ),
        };
    }
}
