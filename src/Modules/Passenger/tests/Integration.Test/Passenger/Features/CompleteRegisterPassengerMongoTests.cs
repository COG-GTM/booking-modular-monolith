using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.GettingPassengerById.V1;

public class CompleteRegisterPassengerMongoTests : PassengerIntegrationTestBase
{
    public CompleteRegisterPassengerMongoTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_insert_passenger_read_model_to_mongo_when_passenger_does_not_exist()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        // Act
        await Fixture.SendAsync(command);

        // Assert
        var passengerReadModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Passenger.AsQueryable().SingleOrDefaultAsync(x => x.PassengerId == command.Id)
        );

        passengerReadModel.Should().NotBeNull();
        passengerReadModel!.Id.Should().NotBe(Guid.Empty);
        passengerReadModel.Id.Should().NotBe(command.Id);
        passengerReadModel.Name.Should().Be(command.Name);
        passengerReadModel.PassportNumber.Should().Be(command.PassportNumber);
        passengerReadModel.PassengerType.Should().Be(command.PassengerType);
        passengerReadModel.Age.Should().Be(command.Age);
        passengerReadModel.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_update_existing_passenger_read_model_in_mongo_instead_of_inserting_duplicate()
    {
        // Arrange
        var insertCommand = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(insertCommand);

        var original = await Fixture.ExecuteReadContextAsync(db =>
            db.Passenger.AsQueryable().SingleAsync(x => x.PassengerId == insertCommand.Id)
        );

        var updateCommand = new FakeCompleteRegisterPassengerMongoCommand()
            .RuleFor(r => r.Id, _ => insertCommand.Id)
            .RuleFor(r => r.Name, _ => "Alex")
            .RuleFor(r => r.PassportNumber, _ => "987654321")
            .RuleFor(r => r.Age, _ => 41)
            .RuleFor(r => r.PassengerType, _ => PassengerType.Female)
            .Generate();

        // Act
        await Fixture.SendAsync(updateCommand);

        // Assert
        var passengerReadModels = await Fixture.ExecuteReadContextAsync(db =>
            db.Passenger.AsQueryable().Where(x => x.PassengerId == insertCommand.Id).ToListAsync()
        );

        passengerReadModels.Should().ContainSingle();

        var updated = passengerReadModels[0];

        updated.Id.Should().Be(original.Id);
        updated.PassengerId.Should().Be(insertCommand.Id);
        updated.Name.Should().Be(updateCommand.Name);
        updated.PassportNumber.Should().Be(updateCommand.PassportNumber);
        updated.Age.Should().Be(updateCommand.Age);
        updated.PassengerType.Should().Be(updateCommand.PassengerType);
        updated.IsDeleted.Should().BeFalse();

        var response = await Fixture.SendAsync(new GetPassengerById(insertCommand.Id));

        response?.PassengerDto?.Name.Should().Be(updateCommand.Name);
        response?.PassengerDto?.Age.Should().Be(updateCommand.Age);
        response?.PassengerDto?.PassengerType.Should().Be(updateCommand.PassengerType);
    }

    [Fact]
    public async Task should_not_update_other_passengers_read_models_in_mongo()
    {
        // Arrange
        var firstCommand = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        var secondCommand = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(firstCommand);
        await Fixture.SendAsync(secondCommand);

        var updateCommand = new FakeCompleteRegisterPassengerMongoCommand()
            .RuleFor(r => r.Id, _ => firstCommand.Id)
            .RuleFor(r => r.Name, _ => "Alex")
            .Generate();

        // Act
        await Fixture.SendAsync(updateCommand);

        // Assert
        var second = await Fixture.ExecuteReadContextAsync(db =>
            db.Passenger.AsQueryable().SingleAsync(x => x.PassengerId == secondCommand.Id)
        );

        second.Name.Should().Be(secondCommand.Name);
        second.Age.Should().Be(secondCommand.Age);
    }
}
