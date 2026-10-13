namespace Contracts.Compatibility.Test.Model;

// Backward-compatibility rules from ADR 0006: additive changes pass, anything that can break a
// consumer that has not been redeployed is reported. Mirrors buf's WIRE_JSON breaking category.
public static class CompatibilityRules
{
    public static IReadOnlyList<string> Compare(MessageContractsSnapshot baseline, MessageContractsSnapshot current)
    {
        var violations = new List<string>();
        foreach (var (name, previous) in baseline.Messages)
        {
            if (!current.Messages.TryGetValue(name, out var now))
            {
                violations.Add($"message {name}: removed or renamed (publish a new versioned type instead)");
                continue;
            }

            if (!string.Equals(previous.Urn, now.Urn, StringComparison.Ordinal))
            {
                violations.Add($"message {name}: MassTransit URN changed from {previous.Urn} to {now.Urn}");
            }

            foreach (var (propertyName, property) in previous.Properties)
            {
                if (!now.Properties.TryGetValue(propertyName, out var currentProperty))
                {
                    violations.Add($"message {name}: property {propertyName} removed or renamed");
                    continue;
                }

                if (!string.Equals(property.Type, currentProperty.Type, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"message {name}: property {propertyName} type changed from {property.Type} to {currentProperty.Type}"
                    );
                }

                if (!property.Nullable && currentProperty.Nullable)
                {
                    violations.Add($"message {name}: property {propertyName} became nullable");
                }

                // Producers on the older contract can still omit it.
                if (property.Nullable && !currentProperty.Nullable)
                {
                    violations.Add($"message {name}: property {propertyName} became required");
                }
            }

            foreach (var (propertyName, property) in now.Properties)
            {
                if (!previous.Properties.ContainsKey(propertyName) && !property.Nullable && property.HasDefault != true)
                {
                    violations.Add(
                        $"message {name}: property {propertyName} added as required (new members must be nullable or have a default)"
                    );
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> Compare(ProtoFileSnapshot baseline, ProtoFileSnapshot current)
    {
        var violations = new List<string>();
        var file = baseline.File;

        if (!string.Equals(baseline.Package, current.Package, StringComparison.Ordinal))
        {
            violations.Add($"{file}: package changed from '{baseline.Package}' to '{current.Package}'");
        }

        if (!string.Equals(baseline.CSharpNamespace, current.CSharpNamespace, StringComparison.Ordinal))
        {
            violations.Add(
                $"{file}: csharp_namespace changed from '{baseline.CSharpNamespace}' to '{current.CSharpNamespace}'"
            );
        }

        foreach (var (serviceName, service) in baseline.Services)
        {
            if (!current.Services.TryGetValue(serviceName, out var currentService))
            {
                violations.Add($"{file}: service {serviceName} removed or renamed");
                continue;
            }

            foreach (var (methodName, method) in service.Methods)
            {
                var rpc = $"{serviceName}/{methodName}";
                if (!currentService.Methods.TryGetValue(methodName, out var currentMethod))
                {
                    violations.Add($"{file}: rpc {rpc} removed or renamed");
                    continue;
                }

                if (!string.Equals(method.Input, currentMethod.Input, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{file}: rpc {rpc} request type changed from {method.Input} to {currentMethod.Input}"
                    );
                }

                if (!string.Equals(method.Output, currentMethod.Output, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{file}: rpc {rpc} response type changed from {method.Output} to {currentMethod.Output}"
                    );
                }

                if (
                    method.ClientStreaming != currentMethod.ClientStreaming
                    || method.ServerStreaming != currentMethod.ServerStreaming
                )
                {
                    violations.Add($"{file}: rpc {rpc} streaming mode changed");
                }
            }
        }

        foreach (var (messageName, message) in baseline.Messages)
        {
            if (!current.Messages.TryGetValue(messageName, out var currentMessage))
            {
                violations.Add($"{file}: message {messageName} removed or renamed");
                continue;
            }

            foreach (var (number, field) in message.Fields)
            {
                var label = $"{file}: field {messageName}.{field.Name} = {number}";
                if (!currentMessage.Fields.TryGetValue(number, out var currentField))
                {
                    // Reserving the tag only prevents reuse; clients that still read the field get a default.
                    violations.Add($"{label}: removed (breaking even when reserved; publish a new version instead)");

                    continue;
                }

                if (!string.Equals(field.Name, currentField.Name, StringComparison.Ordinal))
                {
                    violations.Add($"{label}: renamed to {currentField.Name} (breaks JSON transcoding)");
                }
                else if (!string.Equals(field.JsonName, currentField.JsonName, StringComparison.Ordinal))
                {
                    violations.Add($"{label}: json_name changed from {field.JsonName} to {currentField.JsonName}");
                }

                if (
                    !string.Equals(field.Type, currentField.Type, StringComparison.Ordinal)
                    || !string.Equals(field.TypeName, currentField.TypeName, StringComparison.Ordinal)
                )
                {
                    violations.Add($"{label}: type changed from {Describe(field)} to {Describe(currentField)}");
                }

                if (!string.Equals(field.Label, currentField.Label, StringComparison.Ordinal))
                {
                    violations.Add($"{label}: cardinality changed from {field.Label} to {currentField.Label}");
                }

                if (!string.Equals(field.Oneof, currentField.Oneof, StringComparison.Ordinal))
                {
                    violations.Add($"{label}: moved into or out of a oneof");
                }

                if ((field.Optional ?? false) != (currentField.Optional ?? false))
                {
                    violations.Add($"{label}: explicit presence (proto3 optional) changed");
                }
            }

            foreach (var (number, currentField) in currentMessage.Fields)
            {
                if (
                    !message.Fields.ContainsKey(number)
                    && message.ReservedRanges.Exists(range => range.Contains(number))
                )
                {
                    violations.Add($"{file}: field {messageName}.{currentField.Name} reuses reserved number {number}");
                }

                if (!message.Fields.ContainsKey(number) && message.ReservedNames.Contains(currentField.Name))
                {
                    violations.Add($"{file}: field {messageName}.{currentField.Name} reuses a reserved name");
                }
            }
        }

        foreach (var (enumName, protoEnum) in baseline.Enums)
        {
            if (!current.Enums.TryGetValue(enumName, out var currentEnum))
            {
                violations.Add($"{file}: enum {enumName} removed or renamed");
                continue;
            }

            foreach (var (valueName, number) in protoEnum.Values)
            {
                if (currentEnum.Values.TryGetValue(valueName, out var currentNumber))
                {
                    if (currentNumber != number)
                    {
                        violations.Add(
                            $"{file}: enum value {enumName}.{valueName} renumbered from {number} to {currentNumber}"
                        );
                    }

                    continue;
                }

                violations.Add(
                    $"{file}: enum value {enumName}.{valueName} = {number} removed (breaking even when reserved)"
                );
            }
        }

        return violations;
    }

    private static string Describe(ProtoField field) =>
        string.IsNullOrEmpty(field.TypeName) ? field.Type : $"{field.Type} {field.TypeName}";
}
