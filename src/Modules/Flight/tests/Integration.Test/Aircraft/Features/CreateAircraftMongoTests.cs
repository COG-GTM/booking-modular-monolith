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

namespace Integration.Test.Aircraft.Features;

using global::Flight.Aircrafts.Exceptions;

public class CreateAircraftMongoTests : FlightIntegrationTestBase
{
    public CreateAircraftMongoTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_insert_aircraft_read_model_to_mongo()
    {
        // Arrange
        var command = new FakeCreateAircraftMongoCommand().Generate();

        // Act
        await Fixture.SendAsync(command);

        // Assert
        var aircraftReadModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Aircraft.AsQueryable().SingleOrDefaultAsync(x => x.AircraftId == command.Id)
        );

        aircraftReadModel.Should().NotBeNull();
        aircraftReadModel!.Id.Should().NotBe(Guid.Empty);
        aircraftReadModel.Id.Should().NotBe(command.Id);
        aircraftReadModel.Name.Should().Be(command.Name);
        aircraftReadModel.Model.Should().Be(command.Model);
        aircraftReadModel.ManufacturingYear.Should().Be(command.ManufacturingYear);
        aircraftReadModel.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_throw_aircraft_already_exist_exception_when_aircraft_read_model_exists()
    {
        // Arrange
        var command = new FakeCreateAircraftMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<AircraftAlreadyExistException>();

        var count = await Fixture.ExecuteReadContextAsync(db =>
            db.Aircraft.CountDocumentsAsync(x => x.AircraftId == command.Id)
        );

        count.Should().Be(1);
    }
}
