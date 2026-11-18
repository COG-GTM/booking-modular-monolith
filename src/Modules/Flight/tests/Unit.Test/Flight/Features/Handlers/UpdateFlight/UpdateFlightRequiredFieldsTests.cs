namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using FluentValidation.TestHelper;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Xunit;

public class UpdateFlightRequiredFieldsTests
{
    [Fact]
    public void rejects_an_empty_id()
    {
        var result = new UpdateFlightValidator().TestValidate(ValidCommand() with { Id = Guid.Empty });

        result.ShouldHaveValidationErrorFor(x => x.Id).Only();
    }

    [Fact]
    public void rejects_an_empty_flight_number()
    {
        var result = new UpdateFlightValidator().TestValidate(ValidCommand() with { FlightNumber = "" });

        result.ShouldHaveValidationErrorFor(x => x.FlightNumber).Only();
    }

    private static UpdateFlight ValidCommand()
    {
        var departure = new DateTime(2030, 6, 1, 8, 0, 0, DateTimeKind.Utc);

        return new UpdateFlight(
            Guid.NewGuid(),
            "FL123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            departure,
            departure.AddHours(2),
            Guid.NewGuid(),
            120,
            departure.Date,
            FlightStatus.Flying,
            800
        );
    }
}
