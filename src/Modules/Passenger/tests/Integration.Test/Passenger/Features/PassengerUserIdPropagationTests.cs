using global::Passenger;
using FluentAssertions;
using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Features;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using global::Passenger.Passengers.Models;
using global::Passenger.Passengers.ValueObjects;
using Mapster;
using Xunit;
using PassengerType = global::Passenger.Passengers.Enums.PassengerType;

namespace Integration.Test.Passenger.Features;

public class PassengerUserIdPropagationTests
{
    private static global::Passenger.Passengers.Models.Passenger CreatePassenger(Guid? userId) =>
        global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.NewGuid()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            userId
        );

    [Fact]
    public void create_should_store_user_id_and_raise_created_event_with_it()
    {
        var userId = Guid.NewGuid();

        var passenger = CreatePassenger(userId);

        passenger.UserId.Should().Be(userId);
        passenger
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<PassengerCreatedDomainEvent>()
            .Which.UserId.Should()
            .Be(userId);
    }

    [Fact]
    public void create_without_user_id_should_leave_owner_empty()
    {
        var passenger = CreatePassenger(null);

        passenger.UserId.Should().BeNull();
        passenger.DomainEvents.OfType<PassengerCreatedDomainEvent>().Single().UserId.Should().BeNull();
    }

    [Fact]
    public void complete_registration_should_keep_owner_and_raise_completed_event_with_it()
    {
        var userId = Guid.NewGuid();
        var passenger = CreatePassenger(userId);

        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Male,
            Age.Of(30)
        );

        passenger.UserId.Should().Be(userId);
        passenger.DomainEvents.OfType<PassengerRegistrationCompletedDomainEvent>().Single().UserId.Should().Be(userId);
    }

    [Fact]
    public void event_mapper_should_pass_user_id_from_created_event_to_mongo_command()
    {
        var userId = Guid.NewGuid();
        var @event = new PassengerCreatedDomainEvent(Guid.NewGuid(), "Sam", "123456789", UserId: userId);

        var command = new PassengerEventMapper().MapToInternalCommand(@event);

        command.Should().BeOfType<CompleteRegisterPassengerMongoCommand>().Which.UserId.Should().Be(userId);
    }

    [Fact]
    public void event_mapper_should_pass_user_id_from_registration_completed_event_to_mongo_command()
    {
        var userId = Guid.NewGuid();
        var @event = new PassengerRegistrationCompletedDomainEvent(
            Guid.NewGuid(),
            "Sam",
            "123456789",
            PassengerType.Female,
            25,
            UserId: userId
        );

        var command = new PassengerEventMapper().MapToInternalCommand(@event);

        var mongoCommand = command.Should().BeOfType<CompleteRegisterPassengerMongoCommand>().Subject;
        mongoCommand.UserId.Should().Be(userId);
        mongoCommand.PassengerType.Should().Be(PassengerType.Female);
        mongoCommand.Age.Should().Be(25);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void mapping_should_copy_user_id_to_read_model(bool hasOwner)
    {
        var config = new TypeAdapterConfig();
        new PassengerMappings().Register(config);
        Guid? userId = hasOwner ? Guid.NewGuid() : null;
        var command = new CompleteRegisterPassengerMongoCommand(
            Guid.NewGuid(),
            "123456789",
            "Sam",
            PassengerType.Male,
            30,
            UserId: userId
        );

        var readModel = command.Adapt<PassengerReadModel>(config);

        readModel.PassengerId.Should().Be(command.Id);
        readModel.UserId.Should().Be(userId);
    }
}
