using FluentAssertions;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Passenger.Features.Domains;

using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using global::Passenger.Passengers.ValueObjects;

[Collection(nameof(UnitTestFixture))]
public class PassengerTests
{
    [Fact]
    public void can_create_valid_passenger()
    {
        // Arrange + Act
        var passenger = FakePassengerCreate.Generate();

        // Assert
        passenger.Should().NotBeNull();
        passenger.Name.Value.Should().Be("Sam");
        passenger.PassportNumber.Value.Should().Be("987654321");
        passenger.PassengerType.Should().Be(PassengerType.Unknown);
        passenger.Age.Should().BeNull();
        passenger.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void queue_domain_event_on_create()
    {
        // Arrange + Act
        var passenger = FakePassengerCreate.Generate();

        // Assert
        passenger.DomainEvents.Count.Should().Be(1);
        passenger.DomainEvents.FirstOrDefault().Should().BeOfType(typeof(PassengerCreatedDomainEvent));
    }

    [Fact]
    public void complete_registration_should_update_type_and_age()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();

        // Act
        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Female,
            Age.Of(42)
        );

        // Assert
        passenger.PassengerType.Should().Be(PassengerType.Female);
        passenger.Age!.Value.Should().Be(42);
    }

    [Fact]
    public void queue_domain_event_on_complete_registration()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();
        passenger.ClearDomainEvents();

        // Act
        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Male,
            Age.Of(30)
        );

        // Assert
        passenger.DomainEvents.Count.Should().Be(1);
        var @event = passenger
            .DomainEvents.Single()
            .Should()
            .BeOfType<PassengerRegistrationCompletedDomainEvent>()
            .Subject;
        @event.Id.Should().Be(passenger.Id.Value);
        @event.PassengerType.Should().Be(PassengerType.Male);
        @event.Age.Should().Be(30);
    }
}
