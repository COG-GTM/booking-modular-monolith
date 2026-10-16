namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using FluentAssertions;
using FluentValidation.TestHelper;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.CreatingFlight.V1;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class UpdateFlightValidatorTests
{
    [Fact]
    public void validator_rejects_nonpositive_price_unknown_status_empty_ids_nonpositive_duration_and_default_date()
    {
        var command = new FakeCreateFlightCommand().Generate() with
        {
            Price = 0,
            Status = FlightStatus.Unknown,
            AircraftId = Guid.Empty,
            DepartureAirportId = Guid.Empty,
            ArriveAirportId = Guid.Empty,
            DurationMinutes = 0,
            FlightDate = default,
        };
        var validator = new UpdateFlightValidator();

        var result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Price);
        result.ShouldHaveValidationErrorFor(x => x.Status);
        result.ShouldHaveValidationErrorFor(x => x.AircraftId);
        result.ShouldHaveValidationErrorFor(x => x.DepartureAirportId);
        result.ShouldHaveValidationErrorFor(x => x.ArriveAirportId);
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
        result.ShouldHaveValidationErrorFor(x => x.FlightDate);
    }

    [Theory]
    [InlineData(FlightStatus.Flying)]
    [InlineData(FlightStatus.Delay)]
    [InlineData(FlightStatus.Canceled)]
    [InlineData(FlightStatus.Completed)]
    public void validator_accepts_supported_statuses(FlightStatus status)
    {
        var command = new FakeCreateFlightCommand().Generate() with { Status = status, Price = 1, DurationMinutes = 1 };

        new UpdateFlightValidator().TestValidate(command).ShouldNotHaveValidationErrorFor(x => x.Status);
    }
}
