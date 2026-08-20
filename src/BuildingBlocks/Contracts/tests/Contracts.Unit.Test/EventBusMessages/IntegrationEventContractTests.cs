using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using Xunit;

namespace Contracts.Unit.Test.EventBusMessages;

public class IntegrationEventContractTests
{
    public static IEnumerable<object[]> IntegrationEventTypes
    {
        get
        {
            yield return new object[] { typeof(FlightCreated) };
            yield return new object[] { typeof(FlightUpdated) };
            yield return new object[] { typeof(FlightDeleted) };
            yield return new object[] { typeof(AircraftCreated) };
            yield return new object[] { typeof(AirportCreated) };
            yield return new object[] { typeof(SeatCreated) };
            yield return new object[] { typeof(SeatReserved) };
            yield return new object[] { typeof(BookingCreated) };
            yield return new object[] { typeof(UserCreated) };
            yield return new object[] { typeof(PassengerRegistrationCompleted) };
            yield return new object[] { typeof(PassengerCreated) };
        }
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void message_type_should_implement_integration_event(Type messageType)
    {
        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(messageType));
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void message_type_should_keep_stable_namespace_for_transport_routing(Type messageType)
    {
        Assert.Equal("BuildingBlocks.Contracts.EventBus.Messages", messageType.Namespace);
    }

    [Theory]
    [MemberData(nameof(IntegrationEventTypes))]
    public void message_type_should_live_in_standalone_eventbus_messages_assembly(Type messageType)
    {
        Assert.Equal("EventBus.Messages", messageType.Assembly.GetName().Name);
    }

    [Fact]
    public void user_created_should_preserve_constructor_values()
    {
        var id = Guid.NewGuid();

        var message = new UserCreated(id, "John Doe", "P1234567");

        Assert.Equal(id, message.Id);
        Assert.Equal("John Doe", message.Name);
        Assert.Equal("P1234567", message.PassportNumber);
    }

    [Fact]
    public void messages_with_same_values_should_be_equal()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new FlightCreated(id), new FlightCreated(id));
        Assert.Equal(new BookingCreated(id), new BookingCreated(id));
    }
}
