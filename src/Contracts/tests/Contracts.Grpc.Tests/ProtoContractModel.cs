using Google.Protobuf.Reflection;

namespace Contracts.Grpc.Tests;

internal sealed record ProtoContractModel(List<ProtoFileContract> Files)
{
    public static ProtoContractModel FromDescriptors(params FileDescriptor[] descriptors) =>
        new(
            descriptors
                .Select(file =>
                {
                    var proto = file.ToProto();
                    return new ProtoFileContract(
                        file.Name,
                        file.Package,
                        file.Services.OrderBy(service => service.FullName, StringComparer.Ordinal)
                            .Select(service => new ProtoServiceContract(
                                service.FullName,
                                service
                                    .Methods.OrderBy(method => method.Name, StringComparer.Ordinal)
                                    .Select(method => new ProtoMethodContract(
                                        method.Name,
                                        method.InputType.FullName,
                                        method.OutputType.FullName,
                                        method.IsClientStreaming,
                                        method.IsServerStreaming
                                    ))
                                    .ToList()
                            ))
                            .ToList(),
                        FlattenMessages(file.MessageTypes)
                            .OrderBy(message => message.FullName, StringComparer.Ordinal)
                            .Select(message =>
                            {
                                var messageProto = message.ToProto();
                                return new ProtoMessageContract(
                                    message.FullName,
                                    message
                                        .Fields.InFieldNumberOrder()
                                        .Select(field => new ProtoFieldContract(
                                            field.FieldNumber,
                                            field.Name,
                                            FieldTypeName(field),
                                            FieldLabel(field),
                                            field.JsonName
                                        ))
                                        .ToList(),
                                    messageProto
                                        .ReservedRange.Select(range => new ProtoNumberRange(range.Start, range.End))
                                        .ToList(),
                                    messageProto.ReservedName.Order(StringComparer.Ordinal).ToList()
                                );
                            })
                            .ToList(),
                        FlattenEnums(file.MessageTypes, file.EnumTypes)
                            .Concat(file.EnumTypes)
                            .DistinctBy(enumType => enumType.FullName)
                            .OrderBy(enumType => enumType.FullName, StringComparer.Ordinal)
                            .Select(enumType =>
                            {
                                var enumProto = enumType.ToProto();
                                return new ProtoEnumContract(
                                    enumType.FullName,
                                    enumType
                                        .Values.OrderBy(value => value.Number)
                                        .ThenBy(value => value.Name, StringComparer.Ordinal)
                                        .Select(value => new ProtoEnumValueContract(value.Number, value.Name))
                                        .ToList(),
                                    enumProto
                                        .ReservedRange.Select(range => new ProtoNumberRange(
                                            range.Start,
                                            (long)range.End + 1
                                        ))
                                        .ToList(),
                                    enumProto.ReservedName.Order(StringComparer.Ordinal).ToList()
                                );
                            })
                            .ToList()
                    );
                })
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToList()
        );

    public static IReadOnlyList<string> BreakingChanges(ProtoContractModel baseline, ProtoContractModel current)
    {
        var changes = new List<string>();
        var currentFiles = current.Files.ToDictionary(file => file.Name, StringComparer.Ordinal);

        foreach (var oldFile in baseline.Files)
        {
            if (!currentFiles.TryGetValue(oldFile.Name, out var newFile))
            {
                changes.Add($"{oldFile.Name}: file removed");
                changes.AddRange(oldFile.Services.Select(service => $"{service.FullName}: service removed"));
                changes.AddRange(
                    oldFile.Services.SelectMany(service =>
                        service.Methods.Select(method => $"{service.FullName}/{method.Name}: method removed")
                    )
                );
                changes.AddRange(oldFile.Messages.Select(message => $"{message.FullName}: message removed"));
                changes.AddRange(oldFile.Enums.Select(enumType => $"{enumType.FullName}: enum removed"));
                continue;
            }

            if (oldFile.Package != newFile.Package)
                changes.Add($"{oldFile.Name}: package changed {oldFile.Package} -> {newFile.Package}");

            CompareServices(oldFile, newFile, changes);
            CompareMessages(oldFile, newFile, changes);
            CompareEnums(oldFile, newFile, changes);
        }

        return changes.Order(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<MessageDescriptor> FlattenMessages(IEnumerable<MessageDescriptor> messages)
    {
        foreach (var message in messages)
        {
            yield return message;

            foreach (var nested in FlattenMessages(message.NestedTypes))
                yield return nested;
        }
    }

    private static IEnumerable<EnumDescriptor> FlattenEnums(
        IEnumerable<MessageDescriptor> messages,
        IEnumerable<EnumDescriptor> enums
    )
    {
        foreach (var enumType in enums)
            yield return enumType;

        foreach (var message in messages)
        {
            foreach (var nestedEnum in message.EnumTypes)
                yield return nestedEnum;

            foreach (var nested in FlattenEnums(message.NestedTypes, []))
                yield return nested;
        }
    }

    private static string FieldTypeName(FieldDescriptor field) =>
        field.FieldType switch
        {
            FieldType.Message => field.MessageType.FullName,
            FieldType.Enum => field.EnumType.FullName,
            _ => field.FieldType.ToString().ToLowerInvariant(),
        };

    private static string FieldLabel(FieldDescriptor field) =>
        field.IsRepeated ? "repeated"
        : field.IsRequired ? "required"
        : "optional";

    private static void CompareServices(
        ProtoFileContract baseline,
        ProtoFileContract current,
        ICollection<string> changes
    )
    {
        var currentServices = current.Services.ToDictionary(service => service.FullName, StringComparer.Ordinal);
        foreach (var oldService in baseline.Services)
        {
            if (!currentServices.TryGetValue(oldService.FullName, out var newService))
            {
                changes.Add($"{oldService.FullName}: service removed");
                continue;
            }

            var currentMethods = newService.Methods.ToDictionary(method => method.Name, StringComparer.Ordinal);
            foreach (var oldMethod in oldService.Methods)
            {
                if (!currentMethods.TryGetValue(oldMethod.Name, out var newMethod))
                {
                    changes.Add($"{oldService.FullName}/{oldMethod.Name}: method removed");
                    continue;
                }

                if (oldMethod.InputType != newMethod.InputType)
                {
                    changes.Add(
                        $"{oldService.FullName}/{oldMethod.Name}: input type changed {oldMethod.InputType} -> {newMethod.InputType}"
                    );
                }

                if (oldMethod.OutputType != newMethod.OutputType)
                {
                    changes.Add(
                        $"{oldService.FullName}/{oldMethod.Name}: output type changed {oldMethod.OutputType} -> {newMethod.OutputType}"
                    );
                }

                if (
                    oldMethod.ClientStreaming != newMethod.ClientStreaming
                    || oldMethod.ServerStreaming != newMethod.ServerStreaming
                )
                {
                    changes.Add($"{oldService.FullName}/{oldMethod.Name}: streaming changed");
                }
            }
        }
    }

    private static void CompareMessages(
        ProtoFileContract baseline,
        ProtoFileContract current,
        ICollection<string> changes
    )
    {
        var currentMessages = current.Messages.ToDictionary(message => message.FullName, StringComparer.Ordinal);
        foreach (var oldMessage in baseline.Messages)
        {
            if (!currentMessages.TryGetValue(oldMessage.FullName, out var newMessage))
            {
                changes.Add($"{oldMessage.FullName}: message removed");
                continue;
            }

            var currentByNumber = newMessage.Fields.ToDictionary(field => field.Number);
            var currentByName = newMessage.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);

            foreach (var oldField in oldMessage.Fields)
            {
                currentByName.TryGetValue(oldField.Name, out var sameName);
                currentByNumber.TryGetValue(oldField.Number, out var sameNumber);

                if (sameName is not null && sameName.Number != oldField.Number)
                {
                    changes.Add(
                        $"{oldMessage.FullName}.{oldField.Name}: field moved from number {oldField.Number} to {sameName.Number}"
                    );
                }

                if (
                    sameNumber is not null
                    && (
                        sameNumber.Name != oldField.Name
                        || sameNumber.Type != oldField.Type
                        || sameNumber.Label != oldField.Label
                        || sameNumber.JsonName != oldField.JsonName
                    )
                )
                {
                    changes.Add(
                        $"{oldMessage.FullName}: field number {oldField.Number} now used with different name, type, or label"
                    );
                }

                if (
                    sameName is null
                    && sameNumber is null
                    && !newMessage.ReservedNumbers.Any(range => range.Contains(oldField.Number))
                )
                {
                    changes.Add(
                        $"{oldMessage.FullName}.{oldField.Name}: field {oldField.Number} removed without reserving its number"
                    );
                }
            }

            foreach (var oldReservedNumber in oldMessage.ReservedNumbers)
            {
                var reusedNumber = currentByNumber.Keys.FirstOrDefault(oldReservedNumber.Contains, -1);
                if (reusedNumber >= 0)
                    changes.Add($"{oldMessage.FullName}: reserved field number {reusedNumber} reused");
            }

            foreach (var oldReservedName in oldMessage.ReservedNames)
            {
                if (currentByName.ContainsKey(oldReservedName))
                    changes.Add($"{oldMessage.FullName}: reserved field name {oldReservedName} reused");
            }
        }
    }

    private static void CompareEnums(ProtoFileContract baseline, ProtoFileContract current, ICollection<string> changes)
    {
        var currentEnums = current.Enums.ToDictionary(enumType => enumType.FullName, StringComparer.Ordinal);
        foreach (var oldEnum in baseline.Enums)
        {
            if (!currentEnums.TryGetValue(oldEnum.FullName, out var newEnum))
            {
                changes.Add($"{oldEnum.FullName}: enum removed");
                continue;
            }

            var currentByNumber = newEnum.Values.ToDictionary(value => value.Number);
            var currentByName = newEnum.Values.ToDictionary(value => value.Name, StringComparer.Ordinal);

            foreach (var oldValue in oldEnum.Values)
            {
                currentByName.TryGetValue(oldValue.Name, out var sameName);
                currentByNumber.TryGetValue(oldValue.Number, out var sameNumber);
                if (sameNumber is not null && sameNumber.Name != oldValue.Name)
                {
                    changes.Add(
                        $"{oldEnum.FullName}.{oldValue.Name}: enum number {oldValue.Number} reused by {sameNumber.Name}"
                    );
                }

                if (sameName is not null && sameName.Number != oldValue.Number)
                {
                    changes.Add(
                        $"{oldEnum.FullName}.{oldValue.Name}: enum value renumbered {oldValue.Number} -> {sameName.Number}"
                    );
                }
                else if (
                    sameName is null
                    && !newEnum.ReservedNumbers.Any(range => range.Contains(oldValue.Number))
                    && !newEnum.ReservedNames.Contains(oldValue.Name, StringComparer.Ordinal)
                )
                {
                    changes.Add(
                        $"{oldEnum.FullName}.{oldValue.Name}: enum value {oldValue.Number} removed without reserving its number or name"
                    );
                }
            }

            foreach (var oldReservedNumber in oldEnum.ReservedNumbers)
            {
                var reusedNumber = currentByNumber.Keys.FirstOrDefault(oldReservedNumber.Contains, -1);
                if (reusedNumber >= 0)
                    changes.Add($"{oldEnum.FullName}: reserved enum number {reusedNumber} reused");
            }

            foreach (var oldReservedName in oldEnum.ReservedNames)
            {
                if (currentByName.ContainsKey(oldReservedName))
                    changes.Add($"{oldEnum.FullName}: reserved enum name {oldReservedName} reused");
            }
        }
    }
}

internal sealed record ProtoFileContract(
    string Name,
    string Package,
    List<ProtoServiceContract> Services,
    List<ProtoMessageContract> Messages,
    List<ProtoEnumContract> Enums
);

internal sealed record ProtoServiceContract(string FullName, List<ProtoMethodContract> Methods);

internal sealed record ProtoMethodContract(
    string Name,
    string InputType,
    string OutputType,
    bool ClientStreaming,
    bool ServerStreaming
);

internal sealed record ProtoMessageContract(
    string FullName,
    List<ProtoFieldContract> Fields,
    List<ProtoNumberRange> ReservedNumbers,
    List<string> ReservedNames
);

internal sealed record ProtoFieldContract(int Number, string Name, string Type, string Label, string JsonName);

internal sealed record ProtoEnumContract(
    string FullName,
    List<ProtoEnumValueContract> Values,
    List<ProtoNumberRange> ReservedNumbers,
    List<string> ReservedNames
);

internal sealed record ProtoEnumValueContract(int Number, string Name);

internal sealed record ProtoNumberRange(int Start, long EndExclusive)
{
    public bool Contains(int number) => number >= Start && number < EndExclusive;
}
