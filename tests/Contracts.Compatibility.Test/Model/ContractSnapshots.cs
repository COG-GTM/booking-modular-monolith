namespace Contracts.Compatibility.Test.Model;

using System.Reflection;
using BuildingBlocks.Core.Event;
using Google.Protobuf.Reflection;
using MassTransit;

// Wire-relevant shape of the integration events in Contracts.Messages.
public sealed class MessageContractsSnapshot
{
    public SortedDictionary<string, MessageContract> Messages { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MessageContract
{
    // MassTransit message URN (urn:message:<namespace>:<type>): exchange/queue binding identity.
    public string Urn { get; set; } = string.Empty;
    public SortedDictionary<string, MessageProperty> Properties { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MessageProperty
{
    public string Type { get; set; } = string.Empty;
    public bool Nullable { get; set; }

    // Optional constructor parameter: older payloads that omit the member still deserialize.
    public bool? HasDefault { get; set; }
}

// Wire-relevant shape of one .proto file (what `buf breaking` would compare).
public sealed class ProtoFileSnapshot
{
    public string File { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public string CSharpNamespace { get; set; } = string.Empty;
    public SortedDictionary<string, ProtoService> Services { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, ProtoMessage> Messages { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, ProtoEnum> Enums { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ProtoService
{
    public SortedDictionary<string, ProtoMethod> Methods { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ProtoMethod
{
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public bool ClientStreaming { get; set; }
    public bool ServerStreaming { get; set; }
}

public sealed class ProtoMessage
{
    public SortedDictionary<int, ProtoField> Fields { get; set; } = [];
    public SortedSet<string> ReservedNames { get; set; } = new(StringComparer.Ordinal);
    public List<ProtoRange> ReservedRanges { get; set; } = [];
}

public sealed class ProtoField
{
    public string Name { get; set; } = string.Empty;
    public string JsonName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Oneof { get; set; }

    // proto3 `optional` (explicit presence); null when absent so existing baselines stay unchanged.
    public bool? Optional { get; set; }
}

public sealed class ProtoEnum
{
    public SortedDictionary<string, int> Values { get; set; } = new(StringComparer.Ordinal);
    public SortedSet<string> ReservedNames { get; set; } = new(StringComparer.Ordinal);
    public List<ProtoRange> ReservedRanges { get; set; } = [];
}

// Inclusive start, exclusive end (descriptor semantics for message ranges).
public sealed record ProtoRange(int Start, int End)
{
    public bool Contains(int number) => number >= Start && number < End;
}

public static class ContractSnapshots
{
    public static Assembly MessagesAssembly => typeof(IIntegrationEvent).Assembly;

    public static Assembly GrpcAssembly => typeof(global::Contracts.Grpc.Flight.FlightReflection).Assembly;

    public static IReadOnlyList<Type> IntegrationEventTypes(Assembly assembly) =>
        assembly
            .GetExportedTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(type)
            )
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    public static MessageContractsSnapshot ForMessages(Assembly assembly)
    {
        var snapshot = new MessageContractsSnapshot();
        foreach (var type in IntegrationEventTypes(assembly))
        {
            snapshot.Messages[type.FullName!] = ForMessage(type);
        }

        return snapshot;
    }

    // Mirrors System.Text.Json: [JsonConstructor], else a public parameterless constructor, else the only public one.
    private static ConstructorInfo? DeserializationConstructor(Type type)
    {
        var constructors = type.GetConstructors();
        return type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(ctor => ctor.IsDefined(typeof(System.Text.Json.Serialization.JsonConstructorAttribute)))
            ?? (
                constructors.Any(ctor => ctor.GetParameters().Length == 0) ? null
                : constructors.Length == 1 ? constructors[0]
                : null
            );
    }

    private static bool HasInitializer(PropertyInfo property, object? prototype)
    {
        // System.Text.Json rejects payloads that omit required members, initializer or not.
        if (
            prototype is null
            || property.SetMethod is null
            || property.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute))
            || property.IsDefined(typeof(System.Text.Json.Serialization.JsonRequiredAttribute))
        )
        {
            return false;
        }

        var value = property.GetValue(prototype);
        return value is not null
            && (!property.PropertyType.IsValueType || !value.Equals(Activator.CreateInstance(property.PropertyType)));
    }

    public static MessageContract ForMessage(Type type)
    {
        var nullability = new NullabilityInfoContext();
        var contract = new MessageContract { Urn = MessageUrn.ForTypeString(type) };
        var constructor = DeserializationConstructor(type);
        var parameters = constructor?.GetParameters() ?? [];
        // Parameterless deserialization keeps property initializers for members missing from the payload.
        var prototype =
            constructor is null && type.GetConstructor(Type.EmptyTypes) is { } parameterless
                ? parameterless.Invoke(null)
                : null;
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            contract.Properties[property.Name] = new MessageProperty
            {
                Type = TypeName(property.PropertyType),
                Nullable =
                    System.Nullable.GetUnderlyingType(property.PropertyType) is not null
                    || nullability.Create(property).ReadState == NullabilityState.Nullable,
                HasDefault =
                    parameters.Any(parameter =>
                        parameter.HasDefaultValue
                        && string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)
                    ) || HasInitializer(property, prototype)
                        ? true
                        : null,
            };
        }

        return contract;
    }

    public static IReadOnlyList<FileDescriptor> ProtoFiles(Assembly assembly) =>
        assembly
            .GetExportedTypes()
            .Where(type =>
                type.IsAbstract && type.IsSealed && type.Name.EndsWith("Reflection", StringComparison.Ordinal)
            )
            .Select(type => type.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)?.GetValue(null))
            .OfType<FileDescriptor>()
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();

    public static ProtoFileSnapshot ForProto(FileDescriptor file) => ForProto(file.ToProto());

    public static ProtoFileSnapshot ForProto(FileDescriptorProto file)
    {
        var snapshot = new ProtoFileSnapshot
        {
            File = file.Name,
            Package = file.Package,
            CSharpNamespace = file.Options?.CsharpNamespace ?? string.Empty,
        };
        var scope = string.IsNullOrEmpty(file.Package) ? string.Empty : $".{file.Package}";

        foreach (var service in file.Service)
        {
            var item = new ProtoService();
            foreach (var method in service.Method)
            {
                item.Methods[method.Name] = new ProtoMethod
                {
                    Input = method.InputType,
                    Output = method.OutputType,
                    ClientStreaming = method.ClientStreaming,
                    ServerStreaming = method.ServerStreaming,
                };
            }

            snapshot.Services[$"{scope}.{service.Name}"] = item;
        }

        foreach (var message in file.MessageType)
        {
            AddMessage(snapshot, message, scope);
        }

        foreach (var protoEnum in file.EnumType)
        {
            AddEnum(snapshot, protoEnum, scope);
        }

        return snapshot;
    }

    public static string TypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var name = type.GetGenericTypeDefinition().FullName!;
        return $"{name[..name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(",", type.GetGenericArguments().Select(TypeName))}>";
    }

    private static void AddMessage(ProtoFileSnapshot snapshot, DescriptorProto message, string scope)
    {
        var fullName = $"{scope}.{message.Name}";
        var item = new ProtoMessage
        {
            ReservedNames = new SortedSet<string>(message.ReservedName, StringComparer.Ordinal),
            ReservedRanges = message.ReservedRange.Select(range => new ProtoRange(range.Start, range.End)).ToList(),
        };

        foreach (var field in message.Field)
        {
            item.Fields[field.Number] = new ProtoField
            {
                Name = field.Name,
                JsonName = field.JsonName,
                Type = field.Type.ToString(),
                TypeName = field.TypeName,
                Label = field.Label.ToString(),
                Oneof = field.HasOneofIndex && !field.Proto3Optional ? message.OneofDecl[field.OneofIndex].Name : null,
                Optional = field.Proto3Optional ? true : null,
            };
        }

        snapshot.Messages[fullName] = item;

        foreach (var nested in message.NestedType)
        {
            AddMessage(snapshot, nested, fullName);
        }

        foreach (var nestedEnum in message.EnumType)
        {
            AddEnum(snapshot, nestedEnum, fullName);
        }
    }

    private static void AddEnum(ProtoFileSnapshot snapshot, EnumDescriptorProto protoEnum, string scope)
    {
        snapshot.Enums[$"{scope}.{protoEnum.Name}"] = new ProtoEnum
        {
            Values = new SortedDictionary<string, int>(
                protoEnum.Value.ToDictionary(value => value.Name, value => value.Number, StringComparer.Ordinal),
                StringComparer.Ordinal
            ),
            ReservedNames = new SortedSet<string>(protoEnum.ReservedName, StringComparer.Ordinal),
            // Enum reserved ranges are inclusive on both ends; store them half-open like message ranges.
            ReservedRanges = protoEnum
                .ReservedRange.Select(range => new ProtoRange(range.Start, range.End + 1))
                .ToList(),
        };
    }
}
