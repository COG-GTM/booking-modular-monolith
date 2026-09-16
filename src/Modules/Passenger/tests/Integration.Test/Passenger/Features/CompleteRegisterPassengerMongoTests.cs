using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class CompleteRegisterPassengerMongoTests : PassengerIntegrationTestBase
{
    public CompleteRegisterPassengerMongoTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_write_passenger_read_model_to_mongo()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        // Act
        await Fixture.SendAsync(command);

        // Assert
        var readModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Passenger.AsQueryable().SingleOrDefaultAsync(x => x.PassengerId == command.Id));

        readModel.Should().NotBeNull();
        readModel!.Name.Should().Be(command.Name);
        readModel.PassportNumber.Should().Be(command.PassportNumber);
        readModel.Age.Should().Be(command.Age);
        readModel.PassengerType.Should().Be(command.PassengerType);
    }
}
