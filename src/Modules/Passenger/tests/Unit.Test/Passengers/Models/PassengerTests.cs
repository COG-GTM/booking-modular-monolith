namespace Unit.Test.Passengers.Models;

using System.Linq;
using FluentAssertions;
using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using global::Passenger.Passengers.ValueObjects;
using MassTransit;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class PassengerTests
{
    [Fact]
    public void can_create_valid_passenger()
    {
        var passenger = FakePassengerCreate.Generate();

        passenger.Should().NotBeNull();
        passenger.Name.Should().NotBeNull();
        passenger.PassportNumber.Should().NotBeNull();
    }

    [Fact]
    public void create_should_enqueue_passenger_created_domain_event()
    {
        var passenger = FakePassengerCreate.Generate();

        passenger.DomainEvents.Count.Should().Be(1);
        passenger.DomainEvents.FirstOrDefault().Should().BeOfType<PassengerCreatedDomainEvent>();
    }

    [Fact]
    public void complete_registration_should_update_properties_and_enqueue_event()
    {
        var passenger = FakePassengerCreate.Generate();
        passenger.ClearDomainEvents();

        var newId = PassengerId.Of(NewId.NextGuid());
        var newName = Name.Of("Updated Name");
        var newPassport = PassportNumber.Of("XY999999");
        var age = Age.Of(25);

        passenger.CompleteRegistrationPassenger(
            newId,
            newName,
            newPassport,
            global::Passenger.Passengers.Enums.PassengerType.Male,
            age
        );

        passenger.Id.Should().Be(newId);
        passenger.Name.Should().Be(newName);
        passenger.PassportNumber.Should().Be(newPassport);
        passenger.Age.Should().Be(age);
        passenger.PassengerType.Should().Be(global::Passenger.Passengers.Enums.PassengerType.Male);
        passenger.DomainEvents.Count.Should().Be(1);
        passenger.DomainEvents.FirstOrDefault().Should().BeOfType<PassengerRegistrationCompletedDomainEvent>();
    }
}
