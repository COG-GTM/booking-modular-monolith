using System.Reflection;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Utils;
using Xunit;

namespace Unit.Test.ContractsPackage;

public class ContractTypeForwardsTests
{
    private static readonly string[] AllContractTypeNames =
    [
        "BuildingBlocks.Core.Event.IEvent",
        "BuildingBlocks.Core.Event.IIntegrationEvent",
        "BuildingBlocks.Contracts.EventBus.Messages.FlightCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.FlightUpdated",
        "BuildingBlocks.Contracts.EventBus.Messages.FlightDeleted",
        "BuildingBlocks.Contracts.EventBus.Messages.AircraftCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.AirportCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.SeatCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.SeatReserved",
        "BuildingBlocks.Contracts.EventBus.Messages.UserCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.PassengerRegistrationCompleted",
        "BuildingBlocks.Contracts.EventBus.Messages.PassengerCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.BookingCreated",
    ];

    private static readonly string[] AllMessageTypeNames =
    [
        "BuildingBlocks.Contracts.EventBus.Messages.FlightCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.FlightUpdated",
        "BuildingBlocks.Contracts.EventBus.Messages.FlightDeleted",
        "BuildingBlocks.Contracts.EventBus.Messages.AircraftCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.AirportCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.SeatCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.SeatReserved",
        "BuildingBlocks.Contracts.EventBus.Messages.UserCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.PassengerRegistrationCompleted",
        "BuildingBlocks.Contracts.EventBus.Messages.PassengerCreated",
        "BuildingBlocks.Contracts.EventBus.Messages.BookingCreated",
    ];

    [Fact]
    public void BuildingBlocks_forwards_exactly_the_packaged_contract_types()
    {
        var forwardedTypes = typeof(TypeProvider)
            .Assembly.GetForwardedTypes()
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
        var packagedTypes = typeof(UserCreated)
            .Assembly.GetExportedTypes()
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(packagedTypes, forwardedTypes);
    }

    [Fact]
    public void BuildingBlocks_no_longer_defines_contract_types()
    {
        var buildingBlocksTypes = typeof(TypeProvider).Assembly.GetTypes();

        Assert.DoesNotContain(
            buildingBlocksTypes,
            type => type.Namespace == "BuildingBlocks.Contracts.EventBus.Messages"
        );
        Assert.DoesNotContain(
            buildingBlocksTypes,
            type => type.FullName is "BuildingBlocks.Core.Event.IEvent" or "BuildingBlocks.Core.Event.IIntegrationEvent"
        );
    }

    [Theory]
    [MemberData(nameof(ContractTypeNames))]
    public void BuildingBlocks_type_names_resolve_to_packaged_types(string name)
    {
        var type = Type.GetType($"{name}, BuildingBlocks", throwOnError: true)!;

        Assert.Equal("Contracts.Messages", type.Assembly.GetName().Name);
        Assert.Equal(name, type.FullName);
    }

    [Theory]
    [MemberData(nameof(MessageTypeNames))]
    public void Outbox_data_type_resolves_to_one_packaged_type(string name)
    {
        _ = typeof(UserCreated);
        var type = TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(name);
        var matchingTypeCount = AppDomain
            .CurrentDomain.GetAssemblies()
            .SelectMany(GetTypesSafely)
            .Count(candidate => candidate.FullName == name);

        Assert.Equal("Contracts.Messages", type?.Assembly.GetName().Name);
        Assert.Equal(name, type?.FullName);
        Assert.Equal(1, matchingTypeCount);
    }

    public static IEnumerable<object[]> ContractTypeNames => AllContractTypeNames.Select(name => new object[] { name });

    public static IEnumerable<object[]> MessageTypeNames => AllMessageTypeNames.Select(name => new object[] { name });

    private static IEnumerable<Type> GetTypesSafely(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
