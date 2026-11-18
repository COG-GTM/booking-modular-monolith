using FluentAssertions;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using PassengerEventMapper = global::Passenger.PassengerEventMapper;

public class PassengerEventMapperTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_map_created_event_owner_to_mongo_command(bool hasOwner)
    {
        Guid? userId = hasOwner ? Guid.NewGuid() : null;
        var domainEvent = new PassengerCreatedDomainEvent(Guid.NewGuid(), "Sam", "123456789", UserId: userId);

        var command = new PassengerEventMapper().MapToInternalCommand(domainEvent);

        command.Should().BeOfType<CompleteRegisterPassengerMongoCommand>().Which.UserId.Should().Be(userId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_map_registration_completed_event_owner_to_mongo_command(bool hasOwner)
    {
        Guid? userId = hasOwner ? Guid.NewGuid() : null;
        var domainEvent = new PassengerRegistrationCompletedDomainEvent(
            Guid.NewGuid(),
            "Sam",
            "123456789",
            PassengerType.Male,
            30,
            UserId: userId
        );

        var command = new PassengerEventMapper().MapToInternalCommand(domainEvent);

        command.Should().BeOfType<CompleteRegisterPassengerMongoCommand>().Which.UserId.Should().Be(userId);
    }
}
