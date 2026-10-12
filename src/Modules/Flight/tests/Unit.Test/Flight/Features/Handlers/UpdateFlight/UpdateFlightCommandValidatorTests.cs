namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using FluentAssertions;
using FluentValidation;
using FluentValidation.TestHelper;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class UpdateFlightCommandValidatorTests
{
    [Fact]
    public void is_valid_should_be_false_when_have_invalid_parameter()
    {
        // Arrange
        var command = new FakeValidateUpdateFlightCommand().Generate();
        var validator = new UpdateFlightValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Id);
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
        var command = new FakeUpdateFlightCommand().Generate();
        var validator = new UpdateFlightValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void validator_should_be_bound_to_update_flight_command()
    {
        // Arrange & Act
        var validator = new UpdateFlightValidator();

        // Assert
        validator.Should().BeAssignableTo<IValidator<global::Flight.Flights.Features.UpdatingFlight.V1.UpdateFlight>>();
        validator.Should().NotBeAssignableTo<IValidator<global::Flight.Flights.Features.CreatingFlight.V1.CreateFlight>>();
    }
}
