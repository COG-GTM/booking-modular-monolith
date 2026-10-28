using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Contracts.Grpc.Test;

internal sealed record ProtoFileSnapshot(
    string FileName,
    string Package,
    ProtoServiceSnapshot[] Services,
    ProtoMessageSnapshot[] Messages,
    ProtoEnumSnapshot[] Enums
)
{
    public static ProtoFileSnapshot FromDescriptor(ByteString serializedData)
    {
        var file = FileDescriptorProto.Parser.ParseFrom(serializedData);
        var messages = new List<ProtoMessageSnapshot>();
        var enums = new List<ProtoEnumSnapshot>();

        foreach (var message in file.MessageType)
            AddMessage(message, file.Package, messages, enums);
        foreach (var value in file.EnumType)
            enums.Add(ToEnum(value, Join(file.Package, value.Name)));

        return new ProtoFileSnapshot(
            file.Name,
            file.Package,
            file.Service.Select(service => new ProtoServiceSnapshot(
                    service.Name,
                    service
                        .Method.Select(method => new ProtoMethodSnapshot(
                            method.Name,
                            method.InputType,
                            method.OutputType,
                            method.ClientStreaming,
                            method.ServerStreaming
                        ))
                        .ToArray()
                ))
                .ToArray(),
            messages.ToArray(),
            enums.ToArray()
        );
    }

    private static void AddMessage(
        DescriptorProto message,
        string parentName,
        List<ProtoMessageSnapshot> messages,
        List<ProtoEnumSnapshot> enums
    )
    {
        var fullName = Join(parentName, message.Name);
        messages.Add(
            new ProtoMessageSnapshot(
                fullName,
                message
                    .Field.Select(field => new ProtoFieldSnapshot(
                        field.Name,
                        field.Number,
                        field.Type.ToString(),
                        field.TypeName,
                        field.Label.ToString(),
                        field.Proto3Optional,
                        field.HasOneofIndex ? field.OneofIndex : null
                    ))
                    .ToArray(),
                message.ReservedRange.Select(range => new ProtoReservedRange(range.Start, range.End)).ToArray(),
                message.ReservedName.ToArray()
            )
        );

        foreach (var nested in message.NestedType)
            AddMessage(nested, fullName, messages, enums);
        foreach (var value in message.EnumType)
            enums.Add(ToEnum(value, Join(fullName, value.Name)));
    }

    private static ProtoEnumSnapshot ToEnum(EnumDescriptorProto value, string fullName) =>
        new(
            fullName,
            value.Value.Select(item => new ProtoEnumValueSnapshot(item.Name, item.Number)).ToArray(),
            value.ReservedRange.Select(range => new ProtoReservedRange(range.Start, range.End)).ToArray(),
            value.ReservedName.ToArray()
        );

    private static string Join(string parent, string child) =>
        string.IsNullOrEmpty(parent) ? child : $"{parent}.{child}";
}

internal sealed record ProtoServiceSnapshot(string Name, ProtoMethodSnapshot[] Methods);

internal sealed record ProtoMethodSnapshot(
    string Name,
    string InputType,
    string OutputType,
    bool ClientStreaming,
    bool ServerStreaming
);

internal sealed record ProtoMessageSnapshot(
    string FullName,
    ProtoFieldSnapshot[] Fields,
    ProtoReservedRange[] ReservedRanges,
    string[] ReservedNames
);

internal sealed record ProtoFieldSnapshot(
    string Name,
    int Number,
    string Type,
    string TypeName,
    string Label,
    bool Proto3Optional,
    int? OneofIndex
);

internal sealed record ProtoReservedRange(int Start, int End);

internal sealed record ProtoEnumSnapshot(
    string FullName,
    ProtoEnumValueSnapshot[] Values,
    ProtoReservedRange[] ReservedRanges,
    string[] ReservedNames
);

internal sealed record ProtoEnumValueSnapshot(string Name, int Number);
