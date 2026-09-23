namespace Unit.Test.Passenger.Features.Domains;

using System.Linq;
using FluentAssertions;
using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.ValueObjects;
using MassTransit;
using Unit.Test.Fakes;
using Xunit;

public class CreatePassengerTests
{
    [Fact]
    public void can_create_valid_passenger()
    {
        // Arrange
        var id = PassengerId.Of(NewId.NextGuid());

        // Act
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            id,
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        // Assert
        passenger.Should().NotBeNull();
        passenger.Id.Should().Be(id);
        passenger.Name.Value.Should().Be("Sam");
        passenger.PassportNumber.Value.Should().Be("123456789");
        passenger.PassengerType.Should().Be(PassengerType.Unknown);
        passenger.Age.Should().BeNull();
        passenger.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void can_create_passenger_with_is_deleted()
    {
        // Arrange + Act
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(NewId.NextGuid()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            isDeleted: true
        );

        // Assert
        passenger.IsDeleted.Should().BeTrue();
        passenger.DomainEvents.Single().As<PassengerCreatedDomainEvent>().IsDeleted.Should().BeTrue();
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
    public void created_domain_event_should_carry_passenger_state()
    {
        // Arrange + Act
        var passenger = FakePassengerCreate.Generate();

        // Assert
        var @event = passenger.DomainEvents.Single().Should().BeOfType<PassengerCreatedDomainEvent>().Subject;
        @event.Id.Should().Be(passenger.Id.Value);
        @event.Name.Should().Be(FakePassengerCreate.DefaultName);
        @event.PassportNumber.Should().Be(FakePassengerCreate.DefaultPassportNumber);
        @event.IsDeleted.Should().BeFalse();
    }
}
