using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Exception;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Flight.Features;

using System.Linq;
using global::Flight.Data.Seed;
using global::Flight.Flights.Models;
using global::Flight.Flights.ValueObjects;

public class UpdateFlightTests : FlightIntegrationTestBase
{
    public UpdateFlightTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

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

        (await Fixture.WaitForPublishing<FlightUpdated>()).Should().Be(true);
    }

    [Fact]
    public async Task should_throw_validation_exception_when_update_flight_command_is_invalid()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var command = new FakeUpdateFlightCommand(flightEntity).Generate() with { Price = -1 };

        // Act
        Func<Task> act = () => Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("Price must be greater than 0");
    }

    [Fact]
    public async Task should_throw_validation_exception_when_update_flight_aircraft_id_is_empty()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var command = new FakeUpdateFlightCommand(flightEntity).Generate() with { AircraftId = Guid.Empty };

        // Act
        Func<Task> act = () => Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("AircraftId must be not empty");
    }
}
