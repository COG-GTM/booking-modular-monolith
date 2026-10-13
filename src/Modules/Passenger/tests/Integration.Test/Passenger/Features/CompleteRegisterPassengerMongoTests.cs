using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Passenger.Data;
using Passenger.Passengers.Models;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class CompleteRegisterPassengerMongoTests : PassengerIntegrationTestBase
{
    public CompleteRegisterPassengerMongoTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_insert_read_model_with_owner_user_id()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        // Act
        await Fixture.SendAsync(command);

        // Assert
        var readModel = await FindReadModel(command.Id);
        readModel.Should().NotBeNull();
        readModel!.UserId.Should().Be(command.UserId);
        readModel.Name.Should().Be(command.Name);
    }

    [Fact]
    public async Task should_preserve_existing_owner_when_legacy_command_has_empty_user_id()
    {
        // Arrange
        var create = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(create);

        // Commands queued before UserId existed deserialize with Guid.Empty.
        var legacyUpdate = create with
        {
            UserId = Guid.Empty,
            Age = create.Age + 1,
            PassportNumber = "987654321",
        };

        // Act
        await Fixture.SendAsync(legacyUpdate);

        // Assert
        var readModel = await FindReadModel(create.Id);
        readModel.Should().NotBeNull();
        readModel!.UserId.Should().Be(create.UserId);
        readModel.Age.Should().Be(legacyUpdate.Age);
        readModel.PassportNumber.Should().Be(legacyUpdate.PassportNumber);
    }

    [Fact]
    public async Task should_update_owner_when_command_carries_non_empty_user_id()
    {
        // Arrange
        var create = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(create);

        var newOwner = Guid.CreateVersion7();
        var update = create with { UserId = newOwner };

        // Act
        await Fixture.SendAsync(update);

        // Assert
        var readModel = await FindReadModel(create.Id);
        readModel.Should().NotBeNull();
        readModel!.UserId.Should().Be(newOwner);
    }

    private Task<PassengerReadModel?> FindReadModel(Guid passengerId) =>
        Fixture.ExecuteReadContextAsync(db =>
            db.Passenger.AsQueryable().SingleOrDefaultAsync(x => x.PassengerId == passengerId && !x.IsDeleted)
        );
}
