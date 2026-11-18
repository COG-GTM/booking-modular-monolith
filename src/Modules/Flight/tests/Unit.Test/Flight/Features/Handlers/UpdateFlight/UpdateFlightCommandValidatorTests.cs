namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using FluentAssertions;
using FluentValidation;
using FluentValidation.TestHelper;
using global::Flight;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Microsoft.Extensions.DependencyInjection;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class UpdateFlightCommandValidatorTests
{
    private readonly UpdateFlightValidator _validator = new();

    [Fact]
    public void validator_should_be_registered_for_update_flight_command()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddValidatorsFromAssembly(typeof(FlightRoot).Assembly);
        using var provider = services.BuildServiceProvider();

        // Act
        var validator = provider.GetService<IValidator<UpdateFlight>>();

        // Assert
        validator.Should().NotBeNull();
        validator.Should().BeOfType<UpdateFlightValidator>();
    }

    [Fact]
    public void is_valid_should_be_true_when_have_valid_parameter()
    {
        var command = new FakeUpdateFlightCommand().Generate();

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
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

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void should_have_error_when_status_is_not_defined(int status)
    {
        var command = new FakeUpdateFlightCommand().Generate() with { Status = (FlightStatus)status };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Status)
            .WithErrorMessage("Status must be Flying, Delay, Canceled or Completed");
    }

    [Fact]
    public void should_have_error_when_duration_minutes_is_zero()
    {
        var command = new FakeUpdateFlightCommand().Generate() with { DurationMinutes = 0 };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes)
            .WithErrorMessage("DurationMinutes must be greater than 0");
    }

    [Fact]
    public void should_have_error_when_id_is_empty()
    {
        var command = new FakeUpdateFlightCommand().Generate() with { Id = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Id must be not empty");
    }

    [Fact]
    public void is_valid_should_be_false_when_have_invalid_parameter()
    {
        var command = new FakeUpdateFlightCommand().Generate() with
        {
            Price = 0,
            Status = FlightStatus.Unknown,
            AircraftId = Guid.Empty,
            DepartureAirportId = Guid.Empty,
            ArriveAirportId = Guid.Empty,
            DurationMinutes = 0,
            FlightDate = default
        };

        var result = _validator.TestValidate(command);

        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Price);
        result.ShouldHaveValidationErrorFor(x => x.Status);
        result.ShouldHaveValidationErrorFor(x => x.AircraftId);
        result.ShouldHaveValidationErrorFor(x => x.DepartureAirportId);
        result.ShouldHaveValidationErrorFor(x => x.ArriveAirportId);
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
        result.ShouldHaveValidationErrorFor(x => x.FlightDate);
    }
}
