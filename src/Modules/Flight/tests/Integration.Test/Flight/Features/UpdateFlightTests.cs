using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Test.Flight.Features;

using System.Linq;
using global::Flight.Data.Seed;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Models;
using global::Flight.Flights.ValueObjects;
using MassTransit;

public class UpdateFlightTests : FlightIntegrationTestBase
{
    public UpdateFlightTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_update_flight_to_db_and_publish_message_to_broker()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var command = new FakeUpdateFlightCommand(flightEntity).Generate();

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        response.Should().NotBeNull();
        response?.Id.Should().Be(flightEntity.Id);

        var updatedFlight = await Fixture.ExecuteDbContextAsync(db =>
            db.Flights.Where(x => x.Id == FlightId.Of(command.Id)).IgnoreQueryFilters().SingleOrDefaultAsync()
        );

        updatedFlight.Should().NotBeNull();
        updatedFlight!.FlightNumber.Value.Should().Be(command.FlightNumber);
        updatedFlight.Price.Value.Should().Be(command.Price);
        updatedFlight.Status.Should().Be(command.Status);
        updatedFlight.IsDeleted.Should().Be(command.IsDeleted);
        updatedFlight.AircraftId.Value.Should().Be(command.AircraftId);
        updatedFlight.DepartureAirportId.Value.Should().Be(command.DepartureAirportId);
        updatedFlight.ArriveAirportId.Value.Should().Be(command.ArriveAirportId);

        (await Fixture.WaitForPublishing<FlightUpdated>()).Should().Be(true);
    }

    [Fact]
    public async Task should_throw_flight_not_found_exception_when_flight_does_not_exist()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var command = new FakeUpdateFlightCommand(flightEntity).Generate() with { Id = NewId.NextGuid() };

        // Act
        Func<Task> act = async () =>
        {
            await Fixture.SendAsync(command);
        };

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }
}
