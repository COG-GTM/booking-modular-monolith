namespace Unit.Test;

using global::Passenger;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Xunit;
using PassengerType = global::Passenger.Passengers.Enums.PassengerType;

public class PassengerEventMapperTests
{
    private readonly PassengerEventMapper _mapper = new();

    [Fact]
    public void should_map_passenger_created_event_to_mongo_command_with_owner_user_id()
    {
        var passengerId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var @event = new PassengerCreatedDomainEvent(passengerId, userId, "Sam", "123456789");

        var command = _mapper.MapToInternalCommand(@event);

        var mongoCommand = command.Should().BeOfType<CompleteRegisterPassengerMongoCommand>().Subject;
        mongoCommand.Id.Should().Be(passengerId);
        mongoCommand.UserId.Should().Be(userId);
        mongoCommand.Name.Should().Be("Sam");
        mongoCommand.PassportNumber.Should().Be("123456789");
        mongoCommand.PassengerType.Should().Be(PassengerType.Unknown);
        mongoCommand.Age.Should().Be(0);
        mongoCommand.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void should_map_registration_completed_event_to_mongo_command_with_owner_user_id()
    {
        var passengerId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var @event = new PassengerRegistrationCompletedDomainEvent(
            passengerId,
            userId,
            "Sam",
            "123456789",
            PassengerType.Female,
            30
        );

        var command = _mapper.MapToInternalCommand(@event);

        var mongoCommand = command.Should().BeOfType<CompleteRegisterPassengerMongoCommand>().Subject;
        mongoCommand.Id.Should().Be(passengerId);
        mongoCommand.UserId.Should().Be(userId);
        mongoCommand.Name.Should().Be("Sam");
        mongoCommand.PassportNumber.Should().Be("123456789");
        mongoCommand.PassengerType.Should().Be(PassengerType.Female);
        mongoCommand.Age.Should().Be(30);
        mongoCommand.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void should_not_map_unknown_domain_event_to_internal_command()
    {
        _mapper.MapToInternalCommand(new UnknownDomainEvent()).Should().BeNull();
    }

    private sealed record UnknownDomainEvent : IDomainEvent;
}
