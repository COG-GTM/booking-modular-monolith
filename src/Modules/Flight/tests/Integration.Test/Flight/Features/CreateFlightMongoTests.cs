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

namespace Integration.Test.Flight.Features;

using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.GettingFlightById.V1;

public class CreateFlightMongoTests : FlightIntegrationTestBase
{
    public CreateFlightMongoTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_insert_flight_read_model_to_mongo()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        // Act
        await Fixture.SendAsync(command);

        // Assert
        var flightReadModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Flight.AsQueryable().SingleOrDefaultAsync(x => x.FlightId == command.Id)
        );

        flightReadModel.Should().NotBeNull();
        flightReadModel!.Id.Should().NotBe(Guid.Empty);
        flightReadModel.Id.Should().NotBe(command.Id);
        flightReadModel.FlightNumber.Should().Be(command.FlightNumber);
        flightReadModel.AircraftId.Should().Be(command.AircraftId);
        flightReadModel.DepartureAirportId.Should().Be(command.DepartureAirportId);
        flightReadModel.ArriveAirportId.Should().Be(command.ArriveAirportId);
        flightReadModel.DurationMinutes.Should().Be(command.DurationMinutes);
        flightReadModel.Status.Should().Be(command.Status);
        flightReadModel.Price.Should().Be(command.Price);
        flightReadModel.IsDeleted.Should().BeFalse();

        var response = await Fixture.SendAsync(new GetFlightById(command.Id));
        response?.FlightDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_throw_flight_already_exist_exception_when_flight_read_model_exists()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<FlightAlreadyExistException>();

        var count = await Fixture.ExecuteReadContextAsync(db =>
            db.Flight.CountDocumentsAsync(x => x.FlightId == command.Id)
        );

        count.Should().Be(1);
    }

    [Fact]
    public async Task should_insert_flight_read_model_when_existing_one_is_deleted()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);
        await Fixture.SendAsync(new FakeDeleteFlightMongoCommand(command.Id).Generate());

        // Act
        await Fixture.SendAsync(command);

        // Assert
        var flightReadModels = await Fixture.ExecuteReadContextAsync(db =>
            db.Flight.AsQueryable().Where(x => x.FlightId == command.Id).ToListAsync()
        );

        flightReadModels.Should().HaveCount(2);
        flightReadModels.Should().ContainSingle(x => !x.IsDeleted);
    }
}
