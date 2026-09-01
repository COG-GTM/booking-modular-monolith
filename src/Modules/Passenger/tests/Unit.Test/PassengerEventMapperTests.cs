namespace Unit.Test;

using global::Passenger;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Xunit;

public class PassengerEventMapperTests
{
    private readonly PassengerEventMapper _mapper = new();

    [Fact]
    public void map_to_integration_event_passenger_registration_completed_returns_correct_type()
    {
        var domainEvent = new PassengerRegistrationCompletedDomainEvent(
            Guid.NewGuid(),
            "John",
            "AB123",
            global::Passenger.Passengers.Enums.PassengerType.Male,
            30
        );

        var result = _mapper.MapToIntegrationEvent(domainEvent);

        result.Should().BeOfType<PassengerRegistrationCompleted>();
    }

    [Fact]
    public void map_to_integration_event_passenger_created_returns_correct_type()
    {
        var domainEvent = new PassengerCreatedDomainEvent(Guid.NewGuid(), "Jane", "CD456");

        var result = _mapper.MapToIntegrationEvent(domainEvent);

        result.Should().BeOfType<PassengerCreated>();
    }

    [Fact]
    public void map_to_internal_command_passenger_registration_completed_returns_correct_type()
    {
        var domainEvent = new PassengerRegistrationCompletedDomainEvent(
            Guid.NewGuid(),
            "John",
            "AB123",
            global::Passenger.Passengers.Enums.PassengerType.Male,
            30
        );

        var result = _mapper.MapToInternalCommand(domainEvent);

        result.Should().BeOfType<CompleteRegisterPassengerMongoCommand>();
    }

    [Fact]
    public void map_to_internal_command_passenger_created_returns_correct_type()
    {
        var domainEvent = new PassengerCreatedDomainEvent(Guid.NewGuid(), "Jane", "CD456");

        var result = _mapper.MapToInternalCommand(domainEvent);

        result.Should().BeOfType<CompleteRegisterPassengerMongoCommand>();
    }

    [Fact]
    public void map_to_integration_event_unknown_event_returns_null()
    {
        var unknownEvent = new FakeUnknownDomainEvent();

        var result = _mapper.MapToIntegrationEvent(unknownEvent);

        result.Should().BeNull();
    }

    [Fact]
    public void map_to_internal_command_unknown_event_returns_null()
    {
        var unknownEvent = new FakeUnknownDomainEvent();

        var result = _mapper.MapToInternalCommand(unknownEvent);

        result.Should().BeNull();
    }

    private record FakeUnknownDomainEvent : IDomainEvent;
}
