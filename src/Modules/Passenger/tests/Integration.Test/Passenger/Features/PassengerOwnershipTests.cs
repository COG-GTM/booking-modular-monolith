using FluentAssertions;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using global::Passenger.Passengers.ValueObjects;
using PassengerModel = global::Passenger.Passengers.Models.Passenger;

public class PassengerOwnershipTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_set_owner_on_passenger_and_created_event(bool hasOwner)
    {
        Guid? userId = hasOwner ? Guid.NewGuid() : null;

        var passenger = PassengerModel.Create(
            PassengerId.Of(Guid.NewGuid()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            userId: userId
        );

        passenger.UserId.Should().Be(userId);
        passenger
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<PassengerCreatedDomainEvent>()
            .Which.UserId.Should()
            .Be(userId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_preserve_owner_on_registration_and_completed_event(bool hasOwner)
    {
        Guid? userId = hasOwner ? Guid.NewGuid() : null;
        var passenger = PassengerModel.Create(
            PassengerId.Of(Guid.NewGuid()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            userId: userId
        );
        passenger.ClearDomainEvents();

        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Male,
            Age.Of(30)
        );

        passenger.UserId.Should().Be(userId);
        passenger
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<PassengerRegistrationCompletedDomainEvent>()
            .Which.UserId.Should()
            .Be(userId);
    }
}
