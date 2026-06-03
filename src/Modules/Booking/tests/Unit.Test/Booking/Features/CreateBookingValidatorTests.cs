using Booking.Booking.Features.CreatingBook.V1;
using FluentValidation.TestHelper;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Booking.Features;

[Collection(nameof(Common.UnitTestFixture))]
public class CreateBookingValidatorTests
{
    private readonly CreateBookingValidator _validator;

    public CreateBookingValidatorTests()
    {
        _validator = new CreateBookingValidator();
    }

    [Fact]
    public void valid_command_has_no_validation_errors()
    {
        var command = FakeCreateBookingCommand.Generate();

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void validator_has_rule_for_flight_id()
    {
        var command = FakeCreateBookingCommand.Generate();

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.FlightId);
    }

    [Fact]
    public void validator_has_rule_for_passenger_id()
    {
        var command = FakeCreateBookingCommand.Generate();

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.PassengerId);
    }
}
