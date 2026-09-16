using FluentAssertions;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test;

using global::Passenger.Passengers.Dtos;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using global::Passenger.Passengers.Models;
using global::Passenger.Passengers.ValueObjects;

[Collection(nameof(UnitTestFixture))]
public class PassengerMappingTests
{
    private readonly UnitTestFixture _fixture;

    public PassengerMappingTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void should_map_complete_register_passenger_request_dto_to_command()
    {
        // Arrange
        var request = new FakeCompleteRegisterPassengerRequestDto().Generate();

        // Act
        var command = _fixture.Mapper.Map<CompleteRegisterPassenger>(request);

        // Assert
        command.PassportNumber.Should().Be(request.PassportNumber);
        command.PassengerType.Should().Be(request.PassengerType);
        command.Age.Should().Be(request.Age);
        command.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void should_map_mongo_command_to_read_model()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        // Act
        var readModel = _fixture.Mapper.Map<PassengerReadModel>(command);

        // Assert
        readModel.Id.Should().NotBeEmpty();
        readModel.Id.Should().NotBe(command.Id);
        readModel.PassengerId.Should().Be(command.Id);
        readModel.Name.Should().Be(command.Name);
        readModel.PassportNumber.Should().Be(command.PassportNumber);
        readModel.PassengerType.Should().Be(command.PassengerType);
        readModel.Age.Should().Be(command.Age);
        readModel.IsDeleted.Should().Be(command.IsDeleted);
    }

    [Fact]
    public void should_map_read_model_to_passenger_dto()
    {
        // Arrange
        var readModel = FakePassengerReadModel.Generate();

        // Act
        var dto = _fixture.Mapper.Map<PassengerDto>(readModel);

        // Assert
        dto.Id.Should().Be(readModel.PassengerId);
        dto.Name.Should().Be(readModel.Name);
        dto.PassportNumber.Should().Be(readModel.PassportNumber);
        dto.PassengerType.Should().Be(readModel.PassengerType);
        dto.Age.Should().Be(readModel.Age);
    }

    [Fact]
    public void should_map_passenger_aggregate_to_passenger_dto()
    {
        // Arrange
        var passenger = FakePassengerCreate.Generate();
        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Baby,
            Age.Of(1)
        );

        // Act
        var dto = _fixture.Mapper.Map<PassengerDto>(passenger);

        // Assert
        dto.Id.Should().Be(passenger.Id.Value);
        dto.Name.Should().Be(passenger.Name.Value);
        dto.PassportNumber.Should().Be(passenger.PassportNumber.Value);
        dto.PassengerType.Should().Be(PassengerType.Baby);
        dto.Age.Should().Be(1);
    }
}
