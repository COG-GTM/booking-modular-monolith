namespace Contracts.Grpc.Test;

internal static class ProtoCompatibility
{
    public static IReadOnlyList<string> Check(
        IEnumerable<ProtoFileSnapshot> baselineFiles,
        IEnumerable<ProtoFileSnapshot> currentFiles
    )
    {
        var current = currentFiles.ToDictionary(file => file.FileName, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var baseline in baselineFiles)
        {
            if (!current.TryGetValue(baseline.FileName, out var actual))
            {
                violations.Add($"Proto file removed: {baseline.FileName}");
                continue;
            }

            if (!string.Equals(baseline.Package, actual.Package, StringComparison.Ordinal))
                violations.Add($"{baseline.FileName}: package changed {baseline.Package} -> {actual.Package}");

            CheckServices(baseline, actual, violations);
            CheckMessages(baseline, actual, violations);
            CheckEnums(baseline, actual, violations);
        }

        return violations;
    }

    private static void CheckServices(ProtoFileSnapshot baseline, ProtoFileSnapshot current, List<string> violations)
    {
        var currentServices = current.Services.ToDictionary(service => service.Name, StringComparer.Ordinal);
        foreach (var service in baseline.Services)
        {
            var serviceName = $"{baseline.Package}.{service.Name}";
            if (!currentServices.TryGetValue(service.Name, out var actual))
            {
                violations.Add($"Service removed or renamed: {serviceName}");
                continue;
            }

            var currentMethods = actual.Methods.ToDictionary(method => method.Name, StringComparer.Ordinal);
            foreach (var method in service.Methods)
            {
                var rpcName = $"{serviceName}.{method.Name}";
                if (!currentMethods.TryGetValue(method.Name, out var currentMethod))
                {
                    violations.Add($"RPC removed or renamed: {rpcName}");
                    continue;
                }

                if (!string.Equals(method.InputType, currentMethod.InputType, StringComparison.Ordinal))
                    violations.Add($"{rpcName}: input type changed {method.InputType} -> {currentMethod.InputType}");
                if (!string.Equals(method.OutputType, currentMethod.OutputType, StringComparison.Ordinal))
                    violations.Add($"{rpcName}: output type changed {method.OutputType} -> {currentMethod.OutputType}");
                if (method.ClientStreaming != currentMethod.ClientStreaming)
                    violations.Add(
                        $"{rpcName}: client streaming changed {method.ClientStreaming} -> {currentMethod.ClientStreaming}"
                    );
                if (method.ServerStreaming != currentMethod.ServerStreaming)
                    violations.Add(
                        $"{rpcName}: server streaming changed {method.ServerStreaming} -> {currentMethod.ServerStreaming}"
                    );
            }
        }
    }

    private static void CheckMessages(ProtoFileSnapshot baseline, ProtoFileSnapshot current, List<string> violations)
    {
        var currentMessages = current.Messages.ToDictionary(message => message.FullName, StringComparer.Ordinal);
        foreach (var message in baseline.Messages)
        {
            if (!currentMessages.TryGetValue(message.FullName, out var actual))
            {
                violations.Add($"Message removed: {message.FullName}");
                continue;
            }

            var fieldsByName = actual.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
            var fieldsByNumber = actual.Fields.ToDictionary(field => field.Number);
            foreach (var field in message.Fields)
            {
                if (!fieldsByName.TryGetValue(field.Name, out var currentField))
                {
                    if (fieldsByNumber.TryGetValue(field.Number, out var renamedField))
                    {
                        violations.Add($"{message.FullName}.{field.Name}: field renamed to {renamedField.Name}");
                        CheckFieldShape(message.FullName, field, renamedField, violations);
                    }
                    else if (!IsReserved(actual, field.Number, field.Name))
                    {
                        violations.Add(
                            $"Field removed without reserving its number and name: {message.FullName}.{field.Name}"
                        );
                    }

                    continue;
                }

                CheckFieldShape(message.FullName, field, currentField, violations);
            }

            foreach (var field in actual.Fields)
            {
                if (
                    message.ReservedNames.Contains(field.Name, StringComparer.Ordinal)
                    || message.ReservedRanges.Any(range => Contains(range, field.Number))
                )
                    violations.Add($"{message.FullName}.{field.Name}: reuses a baseline-reserved number or name");
            }
        }
    }

    private static void CheckFieldShape(
        string messageName,
        ProtoFieldSnapshot baseline,
        ProtoFieldSnapshot current,
        List<string> violations
    )
    {
        var fieldName = $"{messageName}.{baseline.Name}";
        if (baseline.Number != current.Number)
            violations.Add($"{fieldName}: field number changed {baseline.Number} -> {current.Number}");
        if (!string.Equals(baseline.Type, current.Type, StringComparison.Ordinal))
            violations.Add($"{fieldName}: field type changed {baseline.Type} -> {current.Type}");
        if (!string.Equals(baseline.TypeName, current.TypeName, StringComparison.Ordinal))
            violations.Add($"{fieldName}: field type_name changed {baseline.TypeName} -> {current.TypeName}");
        if (!string.Equals(baseline.Label, current.Label, StringComparison.Ordinal))
            violations.Add($"{fieldName}: field label changed {baseline.Label} -> {current.Label}");
        if (baseline.Proto3Optional != current.Proto3Optional)
            violations.Add(
                $"{fieldName}: proto3_optional changed {baseline.Proto3Optional} -> {current.Proto3Optional}"
            );
        if (baseline.OneofIndex != current.OneofIndex)
            violations.Add($"{fieldName}: oneof index changed {baseline.OneofIndex} -> {current.OneofIndex}");
    }

    private static bool IsReserved(ProtoMessageSnapshot message, int number, string name) =>
        message.ReservedNames.Contains(name, StringComparer.Ordinal)
        && message.ReservedRanges.Any(range => Contains(range, number));

    private static void CheckEnums(ProtoFileSnapshot baseline, ProtoFileSnapshot current, List<string> violations)
    {
        var currentEnums = current.Enums.ToDictionary(value => value.FullName, StringComparer.Ordinal);
        foreach (var value in baseline.Enums)
        {
            if (!currentEnums.TryGetValue(value.FullName, out var actual))
            {
                violations.Add($"Enum removed: {value.FullName}");
                continue;
            }

            var actualValues = actual.Values.ToDictionary(item => item.Name, StringComparer.Ordinal);
            foreach (var oldValue in value.Values)
            {
                if (actualValues.TryGetValue(oldValue.Name, out var currentValue))
                {
                    if (oldValue.Number != currentValue.Number)
                        violations.Add(
                            $"{value.FullName}.{oldValue.Name}: enum number changed {oldValue.Number} -> {currentValue.Number}"
                        );
                }
                else if (
                    !actual.ReservedNames.Contains(oldValue.Name, StringComparer.Ordinal)
                    || !actual.ReservedRanges.Any(range => Contains(range, oldValue.Number))
                )
                {
                    violations.Add(
                        $"Enum value removed without reserving its number and name: {value.FullName}.{oldValue.Name}"
                    );
                }
            }

            foreach (var enumValue in actual.Values)
            {
                if (
                    value.ReservedNames.Contains(enumValue.Name, StringComparer.Ordinal)
                    || value.ReservedRanges.Any(range => Contains(range, enumValue.Number))
                )
                    violations.Add($"{value.FullName}.{enumValue.Name}: reuses a baseline-reserved number or name");
            }
        }
    }

    private static bool Contains(ProtoReservedRange range, int number) => number >= range.Start && number < range.End;
}
