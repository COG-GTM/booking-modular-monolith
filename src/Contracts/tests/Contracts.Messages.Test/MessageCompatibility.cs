namespace Contracts.Messages.Test;

internal static class MessageCompatibility
{
    public static IReadOnlyList<string> Check(
        IEnumerable<ContractShape> baselineShapes,
        IEnumerable<ContractShape> currentShapes
    )
    {
        var current = currentShapes.ToDictionary(shape => shape.FullName, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var previous in baselineShapes)
        {
            if (!current.TryGetValue(previous.FullName, out var actual))
            {
                violations.Add($"Type removed: {previous.FullName}");
                continue;
            }

            if (!string.Equals(previous.Urn, actual.Urn, StringComparison.Ordinal))
                violations.Add($"{previous.FullName}: URN changed {previous.Urn} -> {actual.Urn}");

            foreach (var property in previous.Properties)
            {
                var currentProperty = actual.Properties.SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, property.Name, StringComparison.Ordinal)
                );
                if (currentProperty is null)
                    violations.Add($"Property removed: {previous.FullName}.{property.Name}");
                else if (!string.Equals(property.Type, currentProperty.Type, StringComparison.Ordinal))
                    violations.Add(
                        $"{previous.FullName}.{property.Name}: property type changed {property.Type} -> {currentProperty.Type}"
                    );
            }
        }

        return violations;
    }
}

internal sealed record ContractShape(string FullName, string Urn, PropertyShape[] Properties);

internal sealed record PropertyShape(string Name, string Type);
