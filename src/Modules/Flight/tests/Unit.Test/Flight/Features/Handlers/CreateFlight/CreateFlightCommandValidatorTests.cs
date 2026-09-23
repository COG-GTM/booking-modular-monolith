namespace Unit.Test.Flight.Features.Handlers.CreateFlight;

using System;
using FluentAssertions;
using FluentValidation.TestHelper;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.CreatingFlight.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class CreateFlightCommandValidatorTests
{
    private static CreateFlight ValidCommand() =>
        new FakeCreateFlightCommand().Generate() with
        {
            Price = 100,
            Status = FlightStatus.Flying,
            DurationMinutes = 120,
            FlightDate = new DateTime(2022, 1, 31)
        };

    [Fact]
    public void is_valid_should_be_false_when_have_invalid_parameter()
    {
        // Arrange
        var command = new FakeValidateCreateFlightCommand().Generate();
        var validator = new CreateFlightValidator();

        // Act
        var result = validator.TestValidate(command);

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

    [Fact]
    public void is_valid_should_be_true_when_have_valid_parameter()
    {
        // Arrange
        var command = ValidCommand();
        var validator = new CreateFlightValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void is_valid_should_be_false_when_price_is_zero()
    {
        // Arrange
        var command = ValidCommand() with { Price = 0 };
        var validator = new CreateFlightValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Price)
            .WithErrorMessage("Price must be greater than 0");
        result.ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Theory]
    [InlineData(FlightStatus.Flying)]
    [InlineData(FlightStatus.Delay)]
    [InlineData(FlightStatus.Canceled)]
    [InlineData(FlightStatus.Completed)]
    public void is_valid_should_be_true_when_status_is_accepted_value(FlightStatus status)
    {
        // Arrange
        var command = ValidCommand() with { Status = status };
        var validator = new CreateFlightValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Theory]
    [InlineData(FlightStatus.Unknown)]
    [InlineData((FlightStatus)10)]
    public void is_valid_should_be_false_when_status_is_not_accepted_value(FlightStatus status)
    {
        // Arrange
        var command = ValidCommand() with { Status = status };
        var validator = new CreateFlightValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Status)
            .WithErrorMessage("Status must be Flying, Delay, Canceled or Completed");
        result.ShouldNotHaveValidationErrorFor(x => x.Price);
    }
}
