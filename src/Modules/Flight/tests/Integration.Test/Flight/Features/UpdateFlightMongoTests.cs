using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Xunit;

namespace Integration.Test.Flight.Features;

using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.GettingFlightById.V1;

public class UpdateFlightMongoTests : FlightIntegrationTestBase
{
    public UpdateFlightMongoTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_update_flight_read_model_fields_in_mongo()
    {
        // Arrange
        var createCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(createCommand);

        var original = await Fixture.ExecuteReadContextAsync(db =>
            db.Flight.AsQueryable().SingleAsync(x => x.FlightId == createCommand.Id)
        );

        var updateCommand = new FakeUpdateFlightMongoCommand(createCommand.Id).Generate();

        // Act
        await Fixture.SendAsync(updateCommand);

        // Assert
        var updated = await Fixture.ExecuteReadContextAsync(db =>
            db.Flight.AsQueryable().SingleAsync(x => x.FlightId == createCommand.Id)
        );

        updated.Id.Should().Be(original.Id);
        updated.FlightId.Should().Be(createCommand.Id);
        updated.FlightNumber.Should().Be(updateCommand.FlightNumber);
        updated.AircraftId.Should().Be(updateCommand.AircraftId);
        updated.DepartureAirportId.Should().Be(updateCommand.DepartureAirportId);
        updated.ArriveAirportId.Should().Be(updateCommand.ArriveAirportId);
        updated.DepartureDate.Should().Be(updateCommand.DepartureDate);
        updated.ArriveDate.Should().Be(updateCommand.ArriveDate);
        updated.FlightDate.Should().Be(updateCommand.FlightDate);
        updated.DurationMinutes.Should().Be(updateCommand.DurationMinutes);
        updated.Status.Should().Be(updateCommand.Status);
        updated.Price.Should().Be(updateCommand.Price);
        updated.IsDeleted.Should().BeFalse();

        var response = await Fixture.SendAsync(new GetFlightById(createCommand.Id));
        response?.FlightDto?.FlightNumber.Should().Be(updateCommand.FlightNumber);
        response?.FlightDto?.Price.Should().Be(updateCommand.Price);
    }

    [Fact]
    public async Task should_throw_flight_not_found_exception_when_flight_read_model_does_not_exist()
    {
        // Arrange
        var updateCommand = new FakeUpdateFlightMongoCommand(NewId.NextGuid()).Generate();

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(updateCommand);

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task should_throw_flight_not_found_exception_when_flight_read_model_is_deleted()
    {
        // Arrange
        var createCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(createCommand);
        await Fixture.SendAsync(new FakeDeleteFlightMongoCommand(createCommand.Id).Generate());

        var updateCommand = new FakeUpdateFlightMongoCommand(createCommand.Id).Generate();

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(updateCommand);

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();

        var flightReadModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Flight.AsQueryable().SingleAsync(x => x.FlightId == createCommand.Id)
        );

        flightReadModel.FlightNumber.Should().Be(createCommand.FlightNumber);
        flightReadModel.IsDeleted.Should().BeTrue();
    }
}
