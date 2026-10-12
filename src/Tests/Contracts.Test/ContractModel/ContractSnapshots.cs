namespace Contracts.Test.ContractModel;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using Google.Protobuf.Reflection;
using MassTransit;

public sealed class GrpcSnapshot
{
    public string Package { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public Dictionary<string, GrpcServiceSnapshot> Services { get; set; } = [];
    public Dictionary<string, ProtoMessageSnapshot> Messages { get; set; } = [];
    public Dictionary<string, ProtoEnumSnapshot> Enums { get; set; } = [];
}

public sealed class GrpcServiceSnapshot
{
    public Dictionary<string, GrpcMethodSnapshot> Methods { get; set; } = [];
}

public sealed class GrpcMethodSnapshot
{
    public string InputType { get; set; } = string.Empty;
    public string OutputType { get; set; } = string.Empty;
    public bool ClientStreaming { get; set; }
    public bool ServerStreaming { get; set; }
}

public sealed class ProtoMessageSnapshot
{
    public Dictionary<int, ProtoFieldSnapshot> Fields { get; set; } = [];
    public HashSet<string> ReservedNames { get; set; } = [];
    public List<ProtoReservedRange> ReservedRanges { get; set; } = [];
}

public sealed class ProtoFieldSnapshot
{
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Type { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string JsonName { get; set; } = string.Empty;
}

public sealed class ProtoEnumSnapshot
{
    public Dictionary<string, int> Values { get; set; } = [];
    public HashSet<string> ReservedNames { get; set; } = [];
    public List<ProtoReservedRange> ReservedRanges { get; set; } = [];
}

public sealed class ProtoReservedRange(int start, int end)
{
    public int Start { get; set; } = start;
    public int End { get; set; } = end;
}

public sealed class MessagesSnapshot
{
    public Dictionary<string, MessageSnapshot> Messages { get; set; } = [];
}

public sealed class MessageSnapshot
{
    public string Urn { get; set; } = string.Empty;
    public Dictionary<string, MessagePropertySnapshot> Properties { get; set; } = [];
}

public sealed class MessagePropertySnapshot
{
    public string Type { get; set; } = string.Empty;
    public string Nullability { get; set; } = string.Empty;
}

public static class ContractSnapshotFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyDictionary<string, GrpcSnapshot> CreateGrpcSnapshots(Assembly assembly)
    {
        var snapshots = new Dictionary<string, GrpcSnapshot>(StringComparer.Ordinal);
        foreach (
            var reflectionType in assembly
                .GetTypes()
                .Where(type => type.Name.EndsWith("Reflection", StringComparison.Ordinal))
        )
        {
            if (
                reflectionType.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                is not FileDescriptor descriptor
            )
            {
                continue;
            }

            var file = FileDescriptorProto.Parser.ParseFrom(descriptor.SerializedData);
            var package = file.Package;
            var snapshot = new GrpcSnapshot { Package = package, FileName = file.Name };

            foreach (var service in file.Service)
            {
                var serviceSnapshot = new GrpcServiceSnapshot();
                foreach (var method in service.Method)
                {
                    serviceSnapshot.Methods[method.Name] = new GrpcMethodSnapshot
                    {
                        InputType = method.InputType,
                        OutputType = method.OutputType,
                        ClientStreaming = method.ClientStreaming,
                        ServerStreaming = method.ServerStreaming,
                    };
                }

                snapshot.Services[service.Name] = serviceSnapshot;
            }

            foreach (var message in file.MessageType)
            {
                AddMessage(snapshot, message, package);
            }

            foreach (var protoEnum in file.EnumType)
            {
                AddEnum(snapshot, protoEnum, package);
            }

            snapshots[package] = snapshot;
        }

        return snapshots;
    }

    public static MessagesSnapshot CreateMessagesSnapshot(Assembly assembly)
    {
        var nullability = new NullabilityInfoContext();
        var snapshot = new MessagesSnapshot();
        foreach (
            var type in assembly
                .GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract && typeof(IIntegrationEvent).IsAssignableFrom(type))
        )
        {
            var message = new MessageSnapshot { Urn = MessageUrn.ForTypeString(type) };

            foreach (
                var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property => property.GetMethod is { IsPublic: true })
            )
            {
                message.Properties[property.Name] = new MessagePropertySnapshot
                {
                    Type = TypeName(property.PropertyType),
                    Nullability = nullability.Create(property).ReadState.ToString(),
                };
            }

            snapshot.Messages[type.FullName!] = message;
        }

        return snapshot;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static string TypeName(Type type) => type.FullName ?? type.Name;

    private static void AddMessage(
        GrpcSnapshot snapshot,
        DescriptorProto message,
        string package,
        string parentName = ""
    )
    {
        var fullName = string.IsNullOrEmpty(parentName) ? $".{package}.{message.Name}" : $"{parentName}.{message.Name}";
        var item = new ProtoMessageSnapshot
        {
            ReservedNames = [.. message.ReservedName],
            ReservedRanges = message
                .ReservedRange.Select(range => new ProtoReservedRange(range.Start, range.End))
                .ToList(),
        };
        foreach (var field in message.Field)
        {
            item.Fields[field.Number] = new ProtoFieldSnapshot
            {
                Name = field.Name,
                Number = field.Number,
                Type = $"TYPE_{field.Type.ToString().ToUpperInvariant()}",
                TypeName = field.TypeName,
                Label = $"LABEL_{field.Label.ToString().ToUpperInvariant()}",
                JsonName = string.IsNullOrEmpty(field.JsonName) ? DefaultJsonName(field.Name) : field.JsonName,
            };
        }

        snapshot.Messages[fullName] = item;
        foreach (var nested in message.NestedType)
        {
            AddMessage(snapshot, nested, package, fullName);
        }

        foreach (var nestedEnum in message.EnumType)
        {
            AddEnum(snapshot, nestedEnum, package, fullName);
        }
    }

    private static string DefaultJsonName(string fieldName)
    {
        var jsonName = new System.Text.StringBuilder(fieldName.Length);
        var uppercaseNext = false;
        var firstCharacter = true;
        foreach (var character in fieldName)
        {
            if (character == '_')
            {
                uppercaseNext = true;
                continue;
            }

            jsonName.Append(
                uppercaseNext ? char.ToUpperInvariant(character)
                : firstCharacter ? char.ToLowerInvariant(character)
                : character
            );
            uppercaseNext = false;
            firstCharacter = false;
        }

        return jsonName.ToString();
    }

    private static void AddEnum(
        GrpcSnapshot snapshot,
        EnumDescriptorProto protoEnum,
        string package,
        string parentName = ""
    )
    {
        var fullName = string.IsNullOrEmpty(parentName)
            ? $".{package}.{protoEnum.Name}"
            : $"{parentName}.{protoEnum.Name}";
        var item = new ProtoEnumSnapshot
        {
            Values = protoEnum.Value.ToDictionary(value => value.Name, value => value.Number, StringComparer.Ordinal),
            ReservedNames = [.. protoEnum.ReservedName],
            ReservedRanges = protoEnum
                .ReservedRange.Select(range => new ProtoReservedRange(range.Start, range.End))
                .ToList(),
        };
        snapshot.Enums[fullName] = item;
    }
}
