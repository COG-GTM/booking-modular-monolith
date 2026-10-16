using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Passenger;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Unit.Test.Passengers.Mappings;

public class PassengerEventMapperTests
{
    private readonly global::Passenger.PassengerEventMapper _mapper = new();

    [Fact]
    public void Registration_completed_event_maps_to_integration_event()
    {
        var @event = new PassengerRegistrationCompletedDomainEvent(
            Guid.NewGuid(),
            "Sam",
            "123456789",
            PassengerType.Male,
            31,
            true
        );

        _mapper.MapToIntegrationEvent(@event).Should().Be(new PassengerRegistrationCompleted(@event.Id));
    }

    [Fact]
    public void Passenger_created_event_maps_to_integration_event()
    {
        var @event = new global::Passenger.Identity.Consumers.RegisteringNewUser.V1.PassengerCreatedDomainEvent(
            Guid.NewGuid(),
            "Sam",
            "123456789",
            true
        );

        _mapper.MapToIntegrationEvent(@event).Should().Be(new PassengerCreated(@event.Id));
    }

    [Fact]
    public void Registration_completed_event_maps_to_mongo_command_with_all_fields()
    {
        var @event = new PassengerRegistrationCompletedDomainEvent(
            Guid.NewGuid(),
            "Sam",
            "123456789",
            PassengerType.Female,
            29,
            true
        );

        _mapper
            .MapToInternalCommand(@event)
            .Should()
            .Be(
                new CompleteRegisterPassengerMongoCommand(@event.Id, "123456789", "Sam", PassengerType.Female, 29, true)
            );
    }

    [Fact]
    public void Passenger_created_event_maps_to_mongo_command_with_initial_values()
    {
        var @event = new global::Passenger.Identity.Consumers.RegisteringNewUser.V1.PassengerCreatedDomainEvent(
            Guid.NewGuid(),
            "Sam",
            "123456789"
        );

        _mapper
            .MapToInternalCommand(@event)
            .Should()
            .Be(
                new CompleteRegisterPassengerMongoCommand(
                    @event.Id,
                    "123456789",
                    "Sam",
                    PassengerType.Unknown,
                    0,
                    false
                )
            );
    }

    [Fact]
    public void Unknown_domain_event_has_no_mapping()
    {
        var @event = new UnmappedDomainEvent();

        _mapper.MapToIntegrationEvent(@event).Should().BeNull();
        _mapper.MapToInternalCommand(@event).Should().BeNull();
    }

    private sealed record UnmappedDomainEvent : IDomainEvent;
}
