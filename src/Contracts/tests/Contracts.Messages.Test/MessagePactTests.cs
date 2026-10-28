using System.Reflection;
using System.Text.Json;
using MassTransit;
using MassTransit.Serialization;
using Xunit;

namespace Contracts.Messages.Test;

public sealed class MessagePactTests
{
    private static readonly JsonSerializerOptions PactJsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Theory]
    [MemberData(nameof(Pacts))]
    public void Message_pact_matches_the_current_contract(string pactPath)
    {
        var pact =
            JsonSerializer.Deserialize<MessagePact>(File.ReadAllText(pactPath), PactJsonOptions)
            ?? throw new InvalidOperationException($"Invalid message pact: {pactPath}");
        Assert.False(string.IsNullOrWhiteSpace(pact.Queue));
        var messageType = typeof(BuildingBlocks.Contracts.EventBus.Messages.UserCreated)
            .Assembly.GetExportedTypes()
            .SingleOrDefault(type =>
                string.Equals(MessageUrn.ForTypeString(type), pact.MessageUrn, StringComparison.Ordinal)
            );

        Assert.NotNull(messageType);
        Assert.Equal(pact.MessageUrn, MessageUrn.ForTypeString(messageType));

        var producer = Activator.CreateInstance(messageType!, Guid.NewGuid(), "Pact User", "AB5045678");
        Assert.NotNull(producer);
        using var serialized = JsonDocument.Parse(
            JsonSerializer.Serialize(producer, messageType!, SystemTextJsonMessageSerializer.Options)
        );

        foreach (var field in pact.Fields)
        {
            Assert.True(
                serialized.RootElement.TryGetProperty(field.Name, out var value),
                $"Missing JSON field {field.Name}"
            );
            Assert.Equal(Enum.Parse<JsonValueKind>(field.JsonType, ignoreCase: true), value.ValueKind);
            if (string.Equals(field.Format, "uuid", StringComparison.OrdinalIgnoreCase))
                Assert.True(Guid.TryParse(value.GetString(), out _), $"Field {field.Name} is not a UUID.");
        }

        var samplePayload = JsonSerializer.Deserialize(
            pact.SamplePayload.GetRawText(),
            messageType!,
            SystemTextJsonMessageSerializer.Options
        );
        Assert.NotNull(samplePayload);
        foreach (var field in pact.Fields)
        {
            var propertyName = char.ToUpperInvariant(field.Name[0]) + field.Name[1..];
            var property = messageType!.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            var value = property!.GetValue(samplePayload);
            Assert.NotNull(value);
            if (property.PropertyType == typeof(string))
                Assert.False(string.IsNullOrWhiteSpace((string)value));
            else
                Assert.NotEqual(Activator.CreateInstance(property.PropertyType), value);
        }
    }

    public static IEnumerable<object[]> Pacts =>
        Directory
            .EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Pacts"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => new object[] { path });

    private sealed record MessagePact(
        string Consumer,
        string Provider,
        string MessageUrn,
        string Queue,
        PactField[] Fields,
        JsonElement SamplePayload
    );

    private sealed record PactField(string Name, string JsonType, string? Format);
}
