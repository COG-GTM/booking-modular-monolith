namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using global::Flight.Flights.ValueObjects;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class UpdateFlightCommandHandlerTests
{
    private readonly UnitTestFixture _fixture;
    private readonly UpdateFlightHandler _handler;

    public Task<UpdateFlightResult> Act(
        global::Flight.Flights.Features.UpdatingFlight.V1.UpdateFlight command,
        CancellationToken cancellationToken
    ) => _handler.Handle(command, cancellationToken);

    public UpdateFlightCommandHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _handler = new UpdateFlightHandler(fixture.DbContext);
    }

    [Fact]
    public async Task handler_with_valid_command_should_update_existing_flight_and_persist_all_fields()
    {
        // Arrange
        var flight = await _fixture.DbContext.Flights.FirstAsync();
        var command = new FakeUpdateFlightCommand(flight).Generate();

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be(flight.Id);

        var entity = await _fixture.DbContext.Flights.FindAsync(FlightId.Of(command.Id));

        entity.Should().NotBeNull();
        entity!.FlightNumber.Value.Should().Be(command.FlightNumber);
        entity.AircraftId.Value.Should().Be(command.AircraftId);
        entity.DepartureAirportId.Value.Should().Be(command.DepartureAirportId);
        entity.ArriveAirportId.Value.Should().Be(command.ArriveAirportId);
        entity.DepartureDate.Value.Should().Be(command.DepartureDate);
        entity.ArriveDate.Value.Should().Be(command.ArriveDate);
        entity.DurationMinutes.Value.Should().Be(command.DurationMinutes);
        entity.FlightDate.Value.Should().Be(command.FlightDate);
        entity.Status.Should().Be(command.Status);
        entity.Price.Value.Should().Be(command.Price);
        entity.IsDeleted.Should().Be(command.IsDeleted);
    }

    [Fact]
    public async Task handler_with_unknown_flight_id_should_throw_flight_not_found_exception()
    {
        // Arrange
        var flight = await _fixture.DbContext.Flights.FirstAsync();
        var command = new FakeUpdateFlightCommand(flight).Generate() with { Id = NewId.NextGuid() };
        var flightNumberBefore = flight.FlightNumber.Value;

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();

        var entities = await _fixture.DbContext.Flights.ToListAsync();
        entities.Should().NotContain(x => x.Id == FlightId.Of(command.Id));
        entities.Single(x => x.Id == flight.Id).FlightNumber.Value.Should().Be(flightNumberBefore);
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        global::Flight.Flights.Features.UpdatingFlight.V1.UpdateFlight command = null;

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
