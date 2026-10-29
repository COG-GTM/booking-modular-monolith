using System.Text.Json;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Utils;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Tests;

public class ContractsOutboxResolutionTests
{
    public static TheoryData<IIntegrationEvent> Events =>
        new()
        {
            new FlightCreated(Guid.NewGuid()),
            new FlightUpdated(Guid.NewGuid()),
            new FlightDeleted(Guid.NewGuid()),
            new AircraftCreated(Guid.NewGuid()),
            new AirportCreated(Guid.NewGuid()),
            new SeatCreated(Guid.NewGuid()),
            new SeatReserved(Guid.NewGuid()),
            new UserCreated(Guid.NewGuid(), "Sam Hamed", "1234567890"),
            new PassengerRegistrationCompleted(Guid.NewGuid()),
            new PassengerCreated(Guid.NewGuid()),
            new BookingCreated(Guid.NewGuid()),
        };

    [Theory]
    [MemberData(nameof(Events))]
    public void persisted_full_names_resolve_and_deserialize_after_extraction(IIntegrationEvent message)
    {
        var envelope = new MessageEnvelope(message);
        var persisted = JsonSerializer.Serialize(envelope);
        var restored = JsonSerializer.Deserialize<MessageEnvelope>(persisted)!;
        var dataType = message.GetType().ToString();

        var resolved = TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(dataType);

        resolved.Should().Be(message.GetType());
        resolved!.Assembly.GetName().Name.Should().Be("Contracts.Messages");
        var deserialized = JsonSerializer.Deserialize(restored.Message.ToString()!, resolved);
        deserialized.Should().BeAssignableTo<IEvent>();
        deserialized.Should().Be(message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BuildingBlocks.Contracts.EventBus.Messages.UnknownEvent")]
    public void unknown_or_empty_persisted_type_names_do_not_resolve(string dataType)
    {
        TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(dataType).Should().BeNull();
    }

    [Theory]
    [InlineData("BuildingBlocks.Core.Event.IEvent", typeof(IEvent))]
    [InlineData("BuildingBlocks.Core.Event.IIntegrationEvent", typeof(IIntegrationEvent))]
    public void old_assembly_qualified_interface_names_follow_core_type_forwards(string name, Type expected)
    {
        Type.GetType($"{name}, BuildingBlocks.Core", throwOnError: true).Should().Be(expected);
    }
}
