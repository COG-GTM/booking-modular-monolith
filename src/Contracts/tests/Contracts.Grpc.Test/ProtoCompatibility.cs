namespace Contracts.Grpc.Test;

internal static class ProtoCompatibility
{
    public static IReadOnlyList<string> Check(
        IEnumerable<ProtoFileSnapshot> baselineFiles,
        IEnumerable<ProtoFileSnapshot> currentFiles
    )
    {
        var baselineSnapshots = baselineFiles.ToArray();
        var currentSnapshots = currentFiles.ToArray();
        var baselineServices = GetServices(baselineSnapshots);
        var currentServices = GetServices(currentSnapshots);
        var baselineMessages = GetMessages(baselineSnapshots);
        var currentMessages = GetMessages(currentSnapshots);
        var baselineEnums = GetEnums(baselineSnapshots);
        var currentEnums = GetEnums(currentSnapshots);
        var violations = new List<string>();

        CheckServices(baselineServices, currentServices, violations);
        CheckMessages(baselineMessages, currentMessages, violations);
        CheckEnums(baselineEnums, currentEnums, violations);
        return violations;
    }

    private static Dictionary<string, ProtoServiceSnapshot> GetServices(IEnumerable<ProtoFileSnapshot> files) =>
        files
            .SelectMany(file =>
                file.Services.Select(service => (FullName: Qualify(file.Package, service.Name), Service: service))
            )
            .ToDictionary(item => item.FullName, item => item.Service, StringComparer.Ordinal);

    private static Dictionary<string, ProtoMessageSnapshot> GetMessages(IEnumerable<ProtoFileSnapshot> files) =>
        files.SelectMany(file => file.Messages).ToDictionary(message => message.FullName, StringComparer.Ordinal);

    private static Dictionary<string, ProtoEnumSnapshot> GetEnums(IEnumerable<ProtoFileSnapshot> files) =>
        files.SelectMany(file => file.Enums).ToDictionary(value => value.FullName, StringComparer.Ordinal);

    private static void CheckServices(
        IReadOnlyDictionary<string, ProtoServiceSnapshot> baselineServices,
        IReadOnlyDictionary<string, ProtoServiceSnapshot> currentServices,
        List<string> violations
    )
    {
        foreach (var (serviceName, service) in baselineServices)
        {
            if (!currentServices.TryGetValue(serviceName, out var actual))
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

    private static void CheckMessages(
        IReadOnlyDictionary<string, ProtoMessageSnapshot> baselineMessages,
        IReadOnlyDictionary<string, ProtoMessageSnapshot> currentMessages,
        List<string> violations
    )
    {
        foreach (var (messageName, message) in baselineMessages)
        {
            if (!currentMessages.TryGetValue(messageName, out var actual))
            {
                violations.Add($"Message removed: {messageName}");
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
                        violations.Add($"{messageName}.{field.Name}: field renamed to {renamedField.Name}");
                        CheckFieldShape(messageName, field, renamedField, violations);
                    }
                    else if (!IsReserved(actual, field.Number, field.Name))
                    {
                        violations.Add(
                            $"Field removed without reserving its number and name: {messageName}.{field.Name}"
                        );
                    }

                    continue;
                }

                CheckFieldShape(messageName, field, currentField, violations);
            }

            foreach (var field in actual.Fields)
            {
                if (
                    message.ReservedNames.Contains(field.Name, StringComparer.Ordinal)
                    || message.ReservedRanges.Any(range => ContainsMessageReservedRange(range, field.Number))
                )
                    violations.Add($"{messageName}.{field.Name}: reuses a baseline-reserved number or name");
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
        && message.ReservedRanges.Any(range => ContainsMessageReservedRange(range, number));

    private static void CheckEnums(
        IReadOnlyDictionary<string, ProtoEnumSnapshot> baselineEnums,
        IReadOnlyDictionary<string, ProtoEnumSnapshot> currentEnums,
        List<string> violations
    )
    {
        foreach (var (enumName, value) in baselineEnums)
        {
            if (!currentEnums.TryGetValue(enumName, out var actual))
            {
                violations.Add($"Enum removed: {enumName}");
                continue;
            }

            var actualValues = actual.Values.ToDictionary(item => item.Name, StringComparer.Ordinal);
            foreach (var oldValue in value.Values)
            {
                if (actualValues.TryGetValue(oldValue.Name, out var currentValue))
                {
                    if (oldValue.Number != currentValue.Number)
                        violations.Add(
                            $"{enumName}.{oldValue.Name}: enum number changed {oldValue.Number} -> {currentValue.Number}"
                        );
                }
                else if (
                    !actual.ReservedNames.Contains(oldValue.Name, StringComparer.Ordinal)
                    || !actual.ReservedRanges.Any(range => ContainsEnumReservedRange(range, oldValue.Number))
                )
                {
                    violations.Add(
                        $"Enum value removed without reserving its number and name: {enumName}.{oldValue.Name}"
                    );
                }
            }

            foreach (var enumValue in actual.Values)
            {
                if (
                    value.ReservedNames.Contains(enumValue.Name, StringComparer.Ordinal)
                    || value.ReservedRanges.Any(range => ContainsEnumReservedRange(range, enumValue.Number))
                )
                    violations.Add($"{enumName}.{enumValue.Name}: reuses a baseline-reserved number or name");
            }
        }
    }

    private static string Qualify(string package, string name) =>
        string.IsNullOrEmpty(package) ? name : $"{package}.{name}";

    private static bool ContainsMessageReservedRange(ProtoReservedRange range, int number) =>
        number >= range.Start && number < range.End;

    private static bool ContainsEnumReservedRange(ProtoReservedRange range, int number) =>
        number >= range.Start && number <= range.End;
}
