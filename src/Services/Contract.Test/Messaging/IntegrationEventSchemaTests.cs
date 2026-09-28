using System;
using System.Linq;
using System.Reflection;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using MassTransit;
using Xunit;

namespace Contract.Test.Messaging;

/// <summary>
/// Frozen wire schema of every integration event exchanged over RabbitMQ. MassTransit derives exchange names and
/// bindings from the message URN (namespace + type name) and deserializes by property name, so any difference here
/// breaks consumers in other services: add a new versioned contract instead of editing an existing one.
/// </summary>
public class IntegrationEventSchemaTests
{
    private const string Namespace = "BuildingBlocks.Contracts.EventBus.Messages";

    public static TheoryData<Type, string[]> Contracts =>
        new()
        {
            { typeof(UserCreated), ["Id:Guid", "Name:String", "PassportNumber:String"] },
            { typeof(PassengerCreated), ["Id:Guid"] },
            { typeof(PassengerRegistrationCompleted), ["Id:Guid"] },
            { typeof(FlightCreated), ["Id:Guid"] },
            { typeof(FlightUpdated), ["Id:Guid"] },
            { typeof(FlightDeleted), ["Id:Guid"] },
            { typeof(AircraftCreated), ["Id:Guid"] },
            { typeof(AirportCreated), ["Id:Guid"] },
            { typeof(SeatCreated), ["Id:Guid"] },
            { typeof(SeatReserved), ["Id:Guid"] },
            { typeof(BookingCreated), ["Id:Guid"] },
        };

    [Theory]
    [MemberData(nameof(Contracts))]
    public void integration_event_schema_is_unchanged(Type eventType, string[] expectedProperties)
    {
        eventType.Namespace.Should().Be(Namespace);
        eventType.Should().BeAssignableTo<IIntegrationEvent>();

        MessageUrn.ForTypeString(eventType).Should().Be($"urn:message:{Namespace}:{eventType.Name}");

        eventType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .Should()
            .BeEquivalentTo(expectedProperties);
    }

    [Fact]
    public void every_integration_event_contract_is_pinned()
    {
        var contracts = typeof(UserCreated)
            .Assembly.GetTypes()
            .Where(t => t.Namespace == Namespace && !t.IsAbstract && typeof(IIntegrationEvent).IsAssignableFrom(t));

        contracts.Should().BeEquivalentTo(Contracts.Select(row => (Type)row[0]));
    }

    [Fact]
    public void user_created_is_consumed_by_passenger_with_the_shared_contract_type()
    {
        // Identity publishes UserCreated; Passenger.Api consumes it in its own process now, so both must bind to the
        // same shared type from BuildingBlocks rather than to a module-local copy.
        var consumer = typeof(Passenger.PassengerRoot)
            .Assembly.GetTypes()
            .Single(t => typeof(IConsumer<UserCreated>).IsAssignableFrom(t) && !t.IsAbstract);

        consumer.Namespace.Should().StartWith("Passenger.");
    }
}
