namespace Unit.Test.Passenger.Features.Domains;

using System.Linq;
using FluentAssertions;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using global::Passenger.Passengers.ValueObjects;
using Unit.Test.Fakes;
using Xunit;

public class CompleteRegistrationPassengerTests
{
    [Fact]
    public void can_complete_registration_of_valid_passenger()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();
        var id = passenger.Id;

        // Act
        FakePassengerCompleteRegistration.Generate(passenger);

        // Assert
        passenger.Id.Should().Be(id);
        passenger.Name.Value.Should().Be(FakePassengerCreate.DefaultName);
        passenger.PassportNumber.Value.Should().Be(FakePassengerCreate.DefaultPassportNumber);
        passenger.PassengerType.Should().Be(FakePassengerCompleteRegistration.DefaultPassengerType);
        passenger.Age.Should().NotBeNull();
        passenger.Age!.Value.Should().Be(FakePassengerCompleteRegistration.DefaultAge);
        passenger.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void complete_registration_should_apply_is_deleted()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();

        // Act
        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Female,
            Age.Of(25),
            isDeleted: true
        );

        // Assert
        passenger.IsDeleted.Should().BeTrue();
        passenger.DomainEvents.OfType<PassengerRegistrationCompletedDomainEvent>().Single().IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void queue_domain_event_on_complete_registration()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();
        passenger.ClearDomainEvents();

        // Act
        FakePassengerCompleteRegistration.Generate(passenger);

        // Assert
        passenger.DomainEvents.Count.Should().Be(1);
        passenger.DomainEvents.FirstOrDefault().Should().BeOfType(typeof(PassengerRegistrationCompletedDomainEvent));
    }

    [Fact]
    public void registration_completed_domain_event_should_carry_passenger_state()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();
        passenger.ClearDomainEvents();

        // Act
        FakePassengerCompleteRegistration.Generate(passenger);

        // Assert
        var @event = passenger
            .DomainEvents.Single()
            .Should()
            .BeOfType<PassengerRegistrationCompletedDomainEvent>()
            .Subject;
        @event.Id.Should().Be(passenger.Id.Value);
        @event.Name.Should().Be(FakePassengerCreate.DefaultName);
        @event.PassportNumber.Should().Be(FakePassengerCreate.DefaultPassportNumber);
        @event.PassengerType.Should().Be(FakePassengerCompleteRegistration.DefaultPassengerType);
        @event.Age.Should().Be(FakePassengerCompleteRegistration.DefaultAge);
        @event.IsDeleted.Should().BeFalse();
    }
}
