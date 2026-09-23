using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Xunit;

namespace Integration.Test.Airport.Features;

using global::Flight.Airports.Exceptions;

public class CreateAirportMongoTests : FlightIntegrationTestBase
{
    public CreateAirportMongoTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_insert_airport_read_model_to_mongo()
    {
        // Arrange
        var command = new FakeCreateAirportMongoCommand().Generate();

        // Act
        await Fixture.SendAsync(command);

        // Assert
        var airportReadModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Airport.AsQueryable().SingleOrDefaultAsync(x => x.AirportId == command.Id)
        );

        airportReadModel.Should().NotBeNull();
        airportReadModel!.Id.Should().NotBe(Guid.Empty);
        airportReadModel.Id.Should().NotBe(command.Id);
        airportReadModel.Name.Should().Be(command.Name);
        airportReadModel.Address.Should().Be(command.Address);
        airportReadModel.Code.Should().Be(command.Code);
        airportReadModel.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_throw_airport_already_exist_exception_when_airport_read_model_exists()
    {
        // Arrange
        var command = new FakeCreateAirportMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<AirportAlreadyExistException>();

        var count = await Fixture.ExecuteReadContextAsync(db =>
            db.Airport.CountDocumentsAsync(x => x.AirportId == command.Id)
        );

        count.Should().Be(1);
    }
}
