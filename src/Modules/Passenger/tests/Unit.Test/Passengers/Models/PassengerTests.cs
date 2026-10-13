namespace Unit.Test.Passengers.Models;

using FluentAssertions;
using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using global::Passenger.Passengers.ValueObjects;
using Xunit;

public class PassengerTests
{
    [Fact]
    public void create_should_assign_owner_user_id()
    {
        var userId = Guid.CreateVersion7();

        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            userId,
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        passenger.UserId.Should().Be(userId);
    }

    [Fact]
    public void create_should_raise_passenger_created_event_carrying_owner_user_id()
    {
        var passengerId = PassengerId.Of(Guid.CreateVersion7());
        var userId = Guid.CreateVersion7();

        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            passengerId,
            userId,
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        var @event = passenger
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<PassengerCreatedDomainEvent>()
            .Subject;

        @event.Id.Should().Be(passengerId.Value);
        @event.UserId.Should().Be(userId);
        @event.Name.Should().Be("Sam");
        @event.PassportNumber.Should().Be("123456789");
        @event.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void complete_registration_should_preserve_owner_user_id()
    {
        var userId = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            userId,
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            Name.Of("Samuel"),
            PassportNumber.Of("987654321"),
            PassengerType.Male,
            Age.Of(30)
        );

        passenger.UserId.Should().Be(userId);
    }

    [Fact]
    public void complete_registration_should_raise_registration_completed_event_carrying_owner_user_id()
    {
        var userId = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            userId,
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            Name.Of("Samuel"),
            PassportNumber.Of("987654321"),
            PassengerType.Male,
            Age.Of(30)
        );

        var @event = passenger
            .DomainEvents.OfType<PassengerRegistrationCompletedDomainEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        @event.Id.Should().Be(passenger.Id.Value);
        @event.UserId.Should().Be(userId);
        @event.Name.Should().Be("Samuel");
        @event.PassportNumber.Should().Be("987654321");
        @event.PassengerType.Should().Be(PassengerType.Male);
        @event.Age.Should().Be(30);
    }
}
