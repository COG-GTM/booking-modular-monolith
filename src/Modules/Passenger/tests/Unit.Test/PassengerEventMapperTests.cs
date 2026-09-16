using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test;

using global::Passenger;
using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using global::Passenger.Passengers.ValueObjects;
using PassengerType = global::Passenger.Passengers.Enums.PassengerType;

[Collection(nameof(UnitTestFixture))]
public class PassengerEventMapperTests
{
    private readonly PassengerEventMapper _mapper = new();

    [Fact]
    public void passenger_created_should_map_to_integration_event_and_internal_command()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();
        var domainEvent = (PassengerCreatedDomainEvent)passenger.DomainEvents.Single();

        // Act
        var integrationEvent = _mapper.MapToIntegrationEvent(domainEvent);
        var internalCommand = _mapper.MapToInternalCommand(domainEvent);

        // Assert
        integrationEvent.Should().BeOfType<PassengerCreated>().Which.Id.Should().Be(domainEvent.Id);
        var command = internalCommand.Should().BeOfType<CompleteRegisterPassengerMongoCommand>().Subject;
        command.Id.Should().Be(domainEvent.Id);
        command.PassportNumber.Should().Be(domainEvent.PassportNumber);
        command.Name.Should().Be(domainEvent.Name);
        command.PassengerType.Should().Be(PassengerType.Unknown);
        command.Age.Should().Be(0);
    }

    [Fact]
    public void passenger_registration_completed_should_map_to_integration_event_and_internal_command()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();
        passenger.ClearDomainEvents();
        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Female,
            Age.Of(40)
        );
        var domainEvent = (PassengerRegistrationCompletedDomainEvent)passenger.DomainEvents.Single();

        // Act
        var integrationEvent = _mapper.MapToIntegrationEvent(domainEvent);
        var internalCommand = _mapper.MapToInternalCommand(domainEvent);

        // Assert
        integrationEvent.Should().BeOfType<PassengerRegistrationCompleted>().Which.Id.Should().Be(domainEvent.Id);
        var command = internalCommand.Should().BeOfType<CompleteRegisterPassengerMongoCommand>().Subject;
        command.Id.Should().Be(domainEvent.Id);
        command.PassengerType.Should().Be(PassengerType.Female);
        command.Age.Should().Be(40);
    }
}
