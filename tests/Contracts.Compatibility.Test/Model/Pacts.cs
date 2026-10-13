namespace Contracts.Compatibility.Test.Model;

using System.Reflection;
using System.Text.Json;

// Consumer-driven contracts: each consumer declares the subset of a provider contract it actually
// reads. Unlike baselines (which can be reset on a major version), a pact can only be dropped by the
// consumer team, so a provider can never remove something a live consumer depends on.
public sealed class GrpcPact
{
    public string Consumer { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProtoFile { get; set; } = string.Empty;
    public List<GrpcPactRpc> Rpcs { get; set; } = [];
    public List<GrpcPactField> Fields { get; set; } = [];
}

public sealed class GrpcPactRpc
{
    public string Service { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
}

public sealed class GrpcPactField
{
    public string Message { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? TypeName { get; set; }
    public string Label { get; set; } = "Optional";
}

public sealed class MessagePact
{
    public string Consumer { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Urn { get; set; } = string.Empty;
    public List<MessagePactProperty> Properties { get; set; } = [];
}

public sealed class MessagePactProperty
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public static class Pacts
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> GrpcPactFiles() => Files("grpc");

    public static IReadOnlyList<string> MessagePactFiles() => Files("messages");

    public static T Read<T>(string relativePath) =>
        JsonSerializer.Deserialize<T>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pacts", relativePath)),
            Options
        )!;

    public static IReadOnlyList<string> Verify(GrpcPact pact, ProtoFileSnapshot provider)
    {
        var failures = new List<string>();
        foreach (var rpc in pact.Rpcs)
        {
            if (
                !provider.Services.TryGetValue(rpc.Service, out var service)
                || !service.Methods.TryGetValue(rpc.Method, out var method)
            )
            {
                failures.Add(
                    $"{pact.Consumer} calls {rpc.Service}/{rpc.Method}, which {pact.Provider} no longer serves"
                );
                continue;
            }

            if (
                !string.Equals(method.Input, rpc.Input, StringComparison.Ordinal)
                || !string.Equals(method.Output, rpc.Output, StringComparison.Ordinal)
                || method.ClientStreaming
                || method.ServerStreaming
            )
            {
                failures.Add($"{pact.Consumer} expects unary {rpc.Service}/{rpc.Method}({rpc.Input}) -> {rpc.Output}");
            }
        }

        foreach (var expected in pact.Fields)
        {
            var name = $"{expected.Message}.{expected.Name} = {expected.Number}";
            if (
                !provider.Messages.TryGetValue(expected.Message, out var message)
                || !message.Fields.TryGetValue(expected.Number, out var field)
            )
            {
                failures.Add($"{pact.Consumer} reads {name}, which {pact.Provider} no longer defines");
                continue;
            }

            if (
                !string.Equals(field.Name, expected.Name, StringComparison.Ordinal)
                || !string.Equals(field.Type, expected.Type, StringComparison.Ordinal)
                || !string.Equals(field.TypeName, expected.TypeName ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(field.Label, expected.Label, StringComparison.Ordinal)
            )
            {
                failures.Add(
                    $"{pact.Consumer} reads {name} as {expected.Label} {expected.Type} {expected.TypeName}, provider has {field.Label} {field.Type} {field.TypeName} {field.Name}"
                );
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> Verify(MessagePact pact, MessageContractsSnapshot provider)
    {
        if (!provider.Messages.TryGetValue(pact.MessageType, out var message))
        {
            return [$"{pact.Consumer} consumes {pact.MessageType}, which is no longer published"];
        }

        var failures = new List<string>();
        if (!string.Equals(message.Urn, pact.Urn, StringComparison.Ordinal))
        {
            failures.Add($"{pact.Consumer} binds to {pact.Urn}, provider publishes {message.Urn}");
        }

        foreach (var expected in pact.Properties)
        {
            if (
                !message.Properties.TryGetValue(expected.Name, out var property)
                || !string.Equals(property.Type, expected.Type, StringComparison.Ordinal)
            )
            {
                failures.Add($"{pact.Consumer} reads {pact.MessageType}.{expected.Name} as {expected.Type}");
            }
        }

        return failures;
    }

    // "<consumer type>|<message type>" for every MassTransit IConsumer<T> of a shared contract.
    public static ISet<string> DiscoverConsumers(IEnumerable<Assembly> assemblies, ICollection<string> contractTypes) =>
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type =>
                type.GetInterfaces()
                    .Where(@interface =>
                        @interface.IsGenericType
                        && @interface.GetGenericTypeDefinition() == typeof(MassTransit.IConsumer<>)
                    )
                    .Select(@interface => (Consumer: type, Message: @interface.GetGenericArguments()[0]))
            )
            .Where(pair => contractTypes.Contains(pair.Message.FullName!))
            .Select(pair => $"{pair.Consumer.FullName}|{pair.Message.FullName}")
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<string> Files(string folder)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Pacts", folder);
        return Directory
            .GetFiles(directory, "*.json")
            .Select(path => $"{folder}/{Path.GetFileName(path)}")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }
}
