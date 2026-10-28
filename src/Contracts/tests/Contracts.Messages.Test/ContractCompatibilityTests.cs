using System.Reflection;
using System.Text.Json;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using MassTransit;
using Xunit;

namespace Contracts.Messages.Test;

public class ContractCompatibilityTests
{
    private const string EventUrnPrefix = "urn:message:BuildingBlocks.Contracts.EventBus.Messages:";
    private const string BaselinePolicy =
        "Additive contract changes require an updated baseline and a minor package bump; breaking changes require a V2 type.";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IEnumerable<object[]> EventTypes =>
        [
            [typeof(FlightCreated), $"{EventUrnPrefix}FlightCreated"],
            [typeof(FlightUpdated), $"{EventUrnPrefix}FlightUpdated"],
            [typeof(FlightDeleted), $"{EventUrnPrefix}FlightDeleted"],
            [typeof(AircraftCreated), $"{EventUrnPrefix}AircraftCreated"],
            [typeof(AirportCreated), $"{EventUrnPrefix}AirportCreated"],
            [typeof(SeatCreated), $"{EventUrnPrefix}SeatCreated"],
            [typeof(SeatReserved), $"{EventUrnPrefix}SeatReserved"],
            [typeof(UserCreated), $"{EventUrnPrefix}UserCreated"],
            [typeof(PassengerRegistrationCompleted), $"{EventUrnPrefix}PassengerRegistrationCompleted"],
            [typeof(PassengerCreated), $"{EventUrnPrefix}PassengerCreated"],
            [typeof(BookingCreated), $"{EventUrnPrefix}BookingCreated"],
        ];

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void Integration_event_urn_is_pinned(Type type, string expectedUrn)
    {
        Assert.Equal(expectedUrn, MessageUrn.ForTypeString(type));
    }

    [Fact]
    public void Every_public_integration_event_has_a_pinned_urn()
    {
        var discovered = typeof(UserCreated)
            .Assembly.GetExportedTypes()
            .Where(type => !type.IsAbstract && typeof(IIntegrationEvent).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var pinned = EventTypes
            .Select(row => (Type)row[0])
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(pinned, discovered);
    }

    [Fact]
    public void Every_baseline_type_and_member_remains_compatible()
    {
        var baseline = ReadBaseline();
        var current = GetContractShapes().ToDictionary(shape => shape.FullName, StringComparer.Ordinal);
        var violations = MessageCompatibility.Check(baseline, current.Values);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations) + $" {BaselinePolicy}");
    }

    [Fact]
    public void Contract_shape_matches_the_committed_baseline()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CONTRACT_BASELINE_DIR")))
            return;

        var actualJson = JsonSerializer.Serialize(GetContractShapes(), JsonOptions);
        if (
            string.Equals(
                Environment.GetEnvironmentVariable("UPDATE_CONTRACT_BASELINES"),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            var baselinePath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../Baselines/messages.json")
            );
            File.WriteAllText(baselinePath, actualJson + Environment.NewLine);
            return;
        }

        var expectedJson = JsonSerializer.Serialize(ReadBaseline(), JsonOptions);
        Assert.True(
            string.Equals(expectedJson, actualJson, StringComparison.Ordinal),
            $"{BaselinePolicy} Regenerate with UPDATE_CONTRACT_BASELINES=true after reviewing the change."
        );
    }

    [Fact]
    public void Contracts_assembly_has_only_allowed_dependencies()
    {
        var assembly = typeof(UserCreated).Assembly;
        var referencedNames = assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
        var unexpected = referencedNames
            .Where(name =>
                name != "netstandard"
                && name != "MassTransit.Abstractions"
                && name != "MediatR.Contracts"
                && name != "System"
                && !name.StartsWith("System.", StringComparison.Ordinal)
            )
            .ToArray();

        Assert.NotEqual("BuildingBlocks.Core", assembly.GetName().Name);
        Assert.Empty(unexpected);
    }

    [Fact]
    public void BuildingBlocks_core_forwards_contract_types()
    {
        Assert.Same(
            typeof(UserCreated),
            Type.GetType(
                "BuildingBlocks.Contracts.EventBus.Messages.UserCreated, BuildingBlocks.Core",
                throwOnError: true
            )
        );
        Assert.Same(
            typeof(IIntegrationEvent),
            Type.GetType("BuildingBlocks.Core.Event.IIntegrationEvent, BuildingBlocks.Core", throwOnError: true)
        );
    }

    private static ContractShape[] GetContractShapes()
    {
        var assembly = typeof(UserCreated).Assembly;
        return assembly
            .GetExportedTypes()
            .Where(type =>
                type == typeof(IEvent)
                || type == typeof(IIntegrationEvent)
                || typeof(IIntegrationEvent).IsAssignableFrom(type)
            )
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new ContractShape(
                type.FullName!,
                MessageUrn.ForTypeString(type),
                GetProperties(type)
                    .Select(property => new PropertyShape(
                        property.Name,
                        property.PropertyType.FullName ?? property.PropertyType.Name
                    ))
                    .ToArray()
            ))
            .ToArray();
    }

    private static PropertyInfo[] GetProperties(Type type)
    {
        IEnumerable<Type> interfaces = type.IsInterface ? type.GetInterfaces().Append(type) : new[] { type };
        return interfaces
            .SelectMany(contract => contract.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .DistinctBy(property => property.Name)
            .OrderBy(property => property.MetadataToken)
            .ToArray();
    }

    internal static ContractShape[] ReadBaseline()
    {
        var baselineDirectory = Environment.GetEnvironmentVariable("CONTRACT_BASELINE_DIR");
        var baselinePath = string.IsNullOrWhiteSpace(baselineDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Baselines", "messages.json")
            : Path.Combine(baselineDirectory, "messages.json");
        var baselineJson = File.ReadAllText(baselinePath);
        return JsonSerializer.Deserialize<ContractShape[]>(baselineJson)
            ?? throw new InvalidOperationException("The contract baseline is empty or invalid.");
    }
}
