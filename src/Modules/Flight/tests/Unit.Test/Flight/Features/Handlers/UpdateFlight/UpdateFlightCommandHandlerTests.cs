namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class UpdateFlightCommandHandlerTests
{
    private readonly UnitTestFixture _fixture;
    private readonly UpdateFlightHandler _handler;

    public Task<UpdateFlightResult> Act(UpdateFlight command, CancellationToken cancellationToken) =>
        _handler.Handle(command, cancellationToken);

    public UpdateFlightCommandHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _handler = new UpdateFlightHandler(fixture.DbContext);
    }

    [Fact]
    public async Task handler_with_valid_command_should_update_flight_and_return_correct_id()
    {
        // Arrange
        var existingFlight = await _fixture.DbContext.Flights.FirstOrDefaultAsync();
        existingFlight.Should().NotBeNull();

        var command = new UpdateFlight(
            existingFlight!.Id,
            existingFlight.FlightNumber,
            existingFlight.AircraftId,
            existingFlight.DepartureAirportId,
            existingFlight.DepartureDate,
            existingFlight.ArriveDate,
            existingFlight.ArriveAirportId,
            existingFlight.DurationMinutes,
            existingFlight.FlightDate,
            existingFlight.Status,
            existingFlight.IsDeleted,
            1000);

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be(existingFlight.Id);
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        UpdateFlight command = null;

        // Act
        Func<Task> act = async () => { await Act(command, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
