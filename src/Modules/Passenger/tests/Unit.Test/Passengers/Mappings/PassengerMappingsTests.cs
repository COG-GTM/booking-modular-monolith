using FluentAssertions;
using Mapster;
using Passenger;
using Passenger.Passengers.Dtos;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.Models;
using Passenger.Passengers.ValueObjects;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Passengers.Mappings;

public class PassengerMappingsTests
{
    [Fact]
    public void Mongo_command_maps_to_read_model_with_a_new_document_id()
    {
        var command = new CompleteRegisterPassengerMongoCommand(
            Guid.NewGuid(),
            "123456789",
            "Sam Passenger",
            PassengerType.Male,
            35
        );

        var result = MapsterMapper.Map<PassengerReadModel>(command);

        result.PassengerId.Should().Be(command.Id);
        result.Id.Should().NotBeEmpty().And.NotBe(command.Id);
        result.Name.Should().Be(command.Name);
        result.PassportNumber.Should().Be(command.PassportNumber);
        result.PassengerType.Should().Be(command.PassengerType);
        result.Age.Should().Be(command.Age);
        result.IsDeleted.Should().Be(command.IsDeleted);
    }

    [Fact]
    public void Request_dto_maps_to_completion_command()
    {
        var request = new CompleteRegisterPassengerRequestDto("123456789", PassengerType.Baby, 2);

        var result = MapsterMapper.Map<CompleteRegisterPassenger>(request);

        result.PassportNumber.Should().Be("123456789");
        result.PassengerType.Should().Be(PassengerType.Baby);
        result.Age.Should().Be(2);
    }

    [Fact]
    public void Read_model_maps_to_passenger_dto()
    {
        var model = new PassengerReadModel
        {
            Id = Guid.NewGuid(),
            PassengerId = Guid.NewGuid(),
            Name = "Sam",
            PassportNumber = "123456789",
            PassengerType = PassengerType.Female,
            Age = 29,
            IsDeleted = false,
        };

        MapsterMapper
            .Map<PassengerDto>(model)
            .Should()
            .Be(new PassengerDto(model.PassengerId, model.Name, model.PassportNumber, model.PassengerType, model.Age));
    }

    [Fact]
    public void Aggregate_maps_to_passenger_dto()
    {
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.NewGuid()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );
        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Male,
            Age.Of(38)
        );

        MapsterMapper
            .Map<PassengerDto>(passenger)
            .Should()
            .Be(new PassengerDto(passenger.Id, "Sam", "123456789", PassengerType.Male, 38));
    }

    [Fact]
    public void Passenger_mapping_configuration_compiles()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(PassengerRoot).Assembly);

        config.Compile();
    }

    private static class MapsterMapper
    {
        public static TDestination Map<TDestination>(object source) =>
            source.Adapt<TDestination>(MapperFactory.Create().Config);
    }
}
