using FluentAssertions;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.Models;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Unit.Test.Passengers.Models;

public class PassengerAggregateTests
{
    [Fact]
    public void Create_sets_values_and_raises_one_matching_created_event()
    {
        var id = PassengerId.Of(Guid.NewGuid());
        var name = Name.Of("Sam Passenger");
        var passport = PassportNumber.Of("123456789");

        var passenger = global::Passenger.Passengers.Models.Passenger.Create(id, name, passport, true);

        passenger.Id.Should().Be(id);
        passenger.Name.Should().Be(name);
        passenger.PassportNumber.Should().Be(passport);
        passenger.IsDeleted.Should().BeTrue();
        passenger
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new global::Passenger.Identity.Consumers.RegisteringNewUser.V1.PassengerCreatedDomainEvent(
                    id,
                    name,
                    passport,
                    true
                )
            );
    }

    [Fact]
    public void Completing_registration_updates_state_and_appends_matching_event_in_order()
    {
        var id = PassengerId.Of(Guid.NewGuid());
        var name = Name.Of("Sam Passenger");
        var passport = PassportNumber.Of("123456789");
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(id, name, passport);

        passenger.CompleteRegistrationPassenger(id, name, passport, PassengerType.Female, Age.Of(37), true);

        passenger.PassengerType.Should().Be(PassengerType.Female);
        passenger.Age.Should().Be(Age.Of(37));
        passenger.IsDeleted.Should().BeTrue();
        passenger.DomainEvents.Should().HaveCount(2);
        passenger
            .DomainEvents[0]
            .Should()
            .Be(
                new global::Passenger.Identity.Consumers.RegisteringNewUser.V1.PassengerCreatedDomainEvent(
                    id,
                    name,
                    passport
                )
            );
        passenger
            .DomainEvents[1]
            .Should()
            .Be(new PassengerRegistrationCompletedDomainEvent(id, name, passport, PassengerType.Female, 37, true));
    }

    [Fact]
    public void Clear_domain_events_returns_events_and_resets_the_aggregate_event_list()
    {
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.NewGuid()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        var events = passenger.ClearDomainEvents();

        events.Should().ContainSingle();
        passenger.DomainEvents.Should().BeEmpty();
    }
}
