namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using FluentAssertions;
using FluentValidation.TestHelper;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Xunit;

public class UpdateFlightValidatorTests
{
    [Fact]
    public void rejects_a_zero_price()
    {
        var result = new UpdateFlightValidator().TestValidate(ValidCommand(price: 0));

        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void accepts_a_valid_command()
    {
        var result = new UpdateFlightValidator().TestValidate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    private static UpdateFlight ValidCommand(decimal price = 800)
    {
        var now = DateTime.UtcNow;

        return new UpdateFlight(
            Guid.NewGuid(),
            "FL123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            now.AddHours(2),
            Guid.NewGuid(),
            120,
            now.AddDays(1),
            FlightStatus.Flying,
            price
        );
    }
}
