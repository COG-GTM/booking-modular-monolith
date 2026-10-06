namespace Contracts.Test.ContractModel;

using System.Reflection;
using System.Text.Json;

public sealed class GrpcPact
{
    public string Consumer { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public List<GrpcPactService> Services { get; set; } = [];
    public List<GrpcPactMessage> Messages { get; set; } = [];
}

public sealed class GrpcPactService
{
    public string Name { get; set; } = string.Empty;
    public List<GrpcPactMethod> Methods { get; set; } = [];
}

public sealed class GrpcPactMethod
{
    public string Name { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public bool ClientStreaming { get; set; }
    public bool ServerStreaming { get; set; }
}

public sealed class GrpcPactMessage
{
    public string Name { get; set; } = string.Empty;
    public List<GrpcPactField> Fields { get; set; } = [];
}

public sealed class GrpcPactField
{
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Type { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class MessagePact
{
    public string Consumer { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public List<MessagePactProperty> Properties { get; set; } = [];
}

public sealed class MessagePactProperty
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public static class PactContracts
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<GrpcPact> ReadGrpcPacts() =>
        Directory
            .GetFiles(Path.Combine(AppContext.BaseDirectory, "Pacts"), "*.grpc.json")
            .Select(path => JsonSerializer.Deserialize<GrpcPact>(File.ReadAllText(path), Options)!)
            .ToList();

    public static IReadOnlyList<MessagePact> ReadMessagePacts() =>
        JsonSerializer.Deserialize<List<MessagePact>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pacts", "messages.json")),
            Options
        )!;

    public static IReadOnlyList<string> Validate(GrpcPact pact, GrpcSnapshot snapshot)
    {
        var failures = new List<string>();
        foreach (var servicePact in pact.Services)
        {
            if (!snapshot.Services.TryGetValue(servicePact.Name, out var service))
            {
                failures.Add($"missing service {servicePact.Name}");
                continue;
            }

            foreach (var methodPact in servicePact.Methods)
            {
                if (!service.Methods.TryGetValue(methodPact.Name, out var method))
                {
                    failures.Add($"missing method {servicePact.Name}.{methodPact.Name}");
                }
                else if (
                    method.InputType != methodPact.Input
                    || method.OutputType != methodPact.Output
                    || method.ClientStreaming != methodPact.ClientStreaming
                    || method.ServerStreaming != methodPact.ServerStreaming
                )
                {
                    failures.Add($"method signature mismatch {servicePact.Name}.{methodPact.Name}");
                }
            }
        }

        foreach (var messagePact in pact.Messages)
        {
            var fullName = snapshot.Messages.Keys.SingleOrDefault(name =>
                name.EndsWith($".{messagePact.Name}", StringComparison.Ordinal)
            );
            if (fullName is null)
            {
                failures.Add($"missing message {messagePact.Name}");
                continue;
            }

            var message = snapshot.Messages[fullName];
            foreach (var fieldPact in messagePact.Fields)
            {
                if (
                    !message.Fields.TryGetValue(fieldPact.Number, out var field)
                    || field.Name != fieldPact.Name
                    || field.Type != fieldPact.Type
                    || field.TypeName != fieldPact.TypeName
                    || field.Label != fieldPact.Label
                )
                {
                    failures.Add($"field pact mismatch {messagePact.Name}.{fieldPact.Name} ({fieldPact.Number})");
                }
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> Validate(MessagePact pact, MessagesSnapshot snapshot)
    {
        var failures = new List<string>();
        if (!snapshot.Messages.TryGetValue(pact.MessageType, out var message))
        {
            return [$"missing message type {pact.MessageType}"];
        }

        foreach (var propertyPact in pact.Properties)
        {
            if (
                !message.Properties.TryGetValue(propertyPact.Name, out var property)
                || property.Type != propertyPact.Type
            )
            {
                failures.Add($"property pact mismatch {pact.MessageType}.{propertyPact.Name}");
            }
        }

        return failures;
    }

    public static IReadOnlySet<string> DiscoverMessageConsumers(
        IEnumerable<Assembly> assemblies,
        IReadOnlySet<string> contractNames
    )
    {
        return assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type =>
                type.GetInterfaces()
                    .Where(@interface =>
                        @interface.IsGenericType
                        && @interface.GetGenericTypeDefinition().FullName == "MassTransit.IConsumer`1"
                    )
                    .Select(@interface => (Consumer: type, Message: @interface.GetGenericArguments()[0]))
            )
            .Where(pair => pair.Message.FullName is not null && contractNames.Contains(pair.Message.FullName))
            .Select(pair => $"{pair.Consumer.FullName}|{pair.Message.FullName}")
            .ToHashSet(StringComparer.Ordinal);
    }
}
