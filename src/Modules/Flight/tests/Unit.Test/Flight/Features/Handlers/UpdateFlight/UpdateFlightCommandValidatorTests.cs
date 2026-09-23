namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using FluentAssertions;
using FluentValidation.TestHelper;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class UpdateFlightCommandValidatorTests
{
    private readonly UpdateFlightValidator _validator = new();

    [Fact]
    public void is_valid_should_be_true_when_have_valid_parameter()
    {
        // Arrange
        var command = new FakeUpdateFlightCommand().Generate();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void is_valid_should_be_false_when_have_invalid_parameter()
    {
        // Arrange
        var command = new FakeValidateUpdateFlightCommand().Generate();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Price);
        result.ShouldHaveValidationErrorFor(x => x.Status);
        result.ShouldHaveValidationErrorFor(x => x.AircraftId);
        result.ShouldHaveValidationErrorFor(x => x.DepartureAirportId);
        result.ShouldHaveValidationErrorFor(x => x.ArriveAirportId);
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
        result.ShouldHaveValidationErrorFor(x => x.FlightDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_have_error_when_price_is_not_greater_than_zero(decimal price)
    {
        var command = new FakeUpdateFlightCommand().Generate() with { Price = price };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Price)
            .WithErrorMessage("Price must be greater than 0");
    }

    [Fact]
    public void should_have_error_when_status_is_not_a_defined_flight_status()
    {
        var command = new FakeUpdateFlightCommand().Generate() with { Status = (FlightStatus)10 };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Status)
            .WithErrorMessage("Status must be Flying, Delay, Canceled or Completed");
    }

    [Theory]
    [InlineData(FlightStatus.Flying)]
    [InlineData(FlightStatus.Delay)]
    [InlineData(FlightStatus.Canceled)]
    [InlineData(FlightStatus.Completed)]
    public void should_not_have_error_when_status_is_a_defined_flight_status(FlightStatus status)
    {
        var command = new FakeUpdateFlightCommand().Generate() with { Status = status };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void should_have_error_when_aircraft_id_is_empty()
    {
        var command = new FakeUpdateFlightCommand().Generate() with { AircraftId = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AircraftId)
            .WithErrorMessage("AircraftId must be not empty");
    }

    [Fact]
    public void should_have_error_when_departure_airport_id_is_empty()
    {
        var command = new FakeUpdateFlightCommand().Generate() with { DepartureAirportId = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DepartureAirportId)
            .WithErrorMessage("DepartureAirportId must be not empty");
    }

    [Fact]
    public void should_have_error_when_arrive_airport_id_is_empty()
    {
        var command = new FakeUpdateFlightCommand().Generate() with { ArriveAirportId = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ArriveAirportId)
            .WithErrorMessage("ArriveAirportId must be not empty");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void should_have_error_when_duration_minutes_is_not_greater_than_zero(decimal durationMinutes)
    {
        var command = new FakeUpdateFlightCommand().Generate() with { DurationMinutes = durationMinutes };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes)
            .WithErrorMessage("DurationMinutes must be greater than 0");
    }

    [Fact]
    public void should_have_error_when_flight_date_is_default()
    {
        var command = new FakeUpdateFlightCommand().Generate() with { FlightDate = default };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.FlightDate)
            .WithErrorMessage("FlightDate must be not empty");
    }
}
