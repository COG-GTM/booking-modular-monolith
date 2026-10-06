namespace Contracts.Test.ContractModel;

public static class CompatibilityChecker
{
    public static IReadOnlyList<string> Compare(GrpcSnapshot baseline, GrpcSnapshot current)
    {
        var changes = new List<string>();
        if (baseline.Package != current.Package)
        {
            changes.Add($"gRPC package changed from {baseline.Package} to {current.Package}");
        }

        if (baseline.FileName != current.FileName)
        {
            changes.Add($"gRPC file removed or renamed: {baseline.FileName}");
        }

        foreach (var service in baseline.Services)
        {
            if (!current.Services.TryGetValue(service.Key, out var currentService))
            {
                changes.Add($"gRPC service removed: {service.Key}");
                continue;
            }

            foreach (var method in service.Value.Methods)
            {
                if (!currentService.Methods.TryGetValue(method.Key, out var currentMethod))
                {
                    changes.Add($"gRPC method removed: {service.Key}.{method.Key}");
                    continue;
                }

                if (method.Value.InputType != currentMethod.InputType)
                {
                    changes.Add($"gRPC method input type changed: {service.Key}.{method.Key}");
                }

                if (method.Value.OutputType != currentMethod.OutputType)
                {
                    changes.Add($"gRPC method output type changed: {service.Key}.{method.Key}");
                }

                if (
                    method.Value.ClientStreaming != currentMethod.ClientStreaming
                    || method.Value.ServerStreaming != currentMethod.ServerStreaming
                )
                {
                    changes.Add($"gRPC method streaming changed: {service.Key}.{method.Key}");
                }
            }
        }

        foreach (var message in baseline.Messages)
        {
            if (!current.Messages.TryGetValue(message.Key, out var currentMessage))
            {
                changes.Add($"gRPC message removed: {message.Key}");
                continue;
            }

            foreach (var field in message.Value.Fields)
            {
                if (!currentMessage.Fields.TryGetValue(field.Key, out var currentField))
                {
                    if (
                        !currentMessage.ReservedNames.Contains(field.Value.Name)
                        || !IsReserved(currentMessage.ReservedRanges, field.Key)
                    )
                    {
                        changes.Add(
                            $"gRPC field removed without reserving number and name: {message.Key}.{field.Value.Name} ({field.Key})"
                        );
                    }

                    continue;
                }

                if (field.Value.Name != currentField.Name)
                {
                    changes.Add(
                        $"gRPC field renamed: {message.Key} field {field.Key} from {field.Value.Name} to {currentField.Name}"
                    );
                }

                if (field.Value.Type != currentField.Type)
                {
                    changes.Add($"gRPC field type changed: {message.Key}.{field.Value.Name}");
                }

                if (field.Value.TypeName != currentField.TypeName)
                {
                    changes.Add($"gRPC field type_name changed: {message.Key}.{field.Value.Name}");
                }

                if (field.Value.Label != currentField.Label)
                {
                    changes.Add($"gRPC field label changed: {message.Key}.{field.Value.Name}");
                }

                if (field.Value.JsonName != currentField.JsonName)
                {
                    changes.Add($"gRPC field json_name changed: {message.Key}.{field.Value.Name}");
                }
            }
        }

        foreach (var protoEnum in baseline.Enums)
        {
            if (!current.Enums.TryGetValue(protoEnum.Key, out var currentEnum))
            {
                changes.Add($"gRPC enum removed: {protoEnum.Key}");
                continue;
            }

            foreach (var value in protoEnum.Value.Values)
            {
                if (!currentEnum.Values.TryGetValue(value.Key, out var number))
                {
                    if (
                        !currentEnum.ReservedNames.Contains(value.Key)
                        && !IsReserved(currentEnum.ReservedRanges, value.Value)
                    )
                    {
                        changes.Add(
                            $"gRPC enum value removed without reserving name or number: {protoEnum.Key}.{value.Key}"
                        );
                    }

                    continue;
                }

                if (number != value.Value)
                {
                    changes.Add($"gRPC enum value renumbered: {protoEnum.Key}.{value.Key}");
                }
            }
        }

        return changes;
    }

    public static IReadOnlyList<string> Compare(MessagesSnapshot baseline, MessagesSnapshot current)
    {
        var changes = new List<string>();
        foreach (var message in baseline.Messages)
        {
            if (!current.Messages.TryGetValue(message.Key, out var currentMessage))
            {
                changes.Add($"Message type removed: {message.Key}");
                continue;
            }

            if (message.Value.Urn != currentMessage.Urn)
            {
                changes.Add($"Message URN changed: {message.Key}");
            }

            foreach (var property in message.Value.Properties)
            {
                if (!currentMessage.Properties.TryGetValue(property.Key, out var currentProperty))
                {
                    changes.Add($"Message property removed: {message.Key}.{property.Key}");
                }
                else if (property.Value.Type != currentProperty.Type)
                {
                    changes.Add($"Message property type changed: {message.Key}.{property.Key}");
                }
                else if (property.Value.Nullability != currentProperty.Nullability)
                {
                    changes.Add($"Message property nullability changed: {message.Key}.{property.Key}");
                }
            }
        }

        return changes;
    }

    public static bool IsReserved(IEnumerable<ProtoReservedRange> ranges, int number) =>
        ranges.Any(range => number >= range.Start && number < range.End);
}
