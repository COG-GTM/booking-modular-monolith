using FluentAssertions;
using FluentValidation.TestHelper;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Booking.Features.Handlers.CreateBooking;

using global::Booking.Booking.Features.CreatingBook.V1;

[Collection(nameof(UnitTestFixture))]
public class CreateBookingCommandValidatorTests
{
    [Fact]
    public void is_valid_should_be_true_when_have_valid_parameters()
    {
        // Arrange
        var command = new FakeCreateBookingCommand().Generate();
        var validator = new CreateBookingValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveValidationErrorFor(x => x.FlightId);
        result.ShouldNotHaveValidationErrorFor(x => x.PassengerId);
    }
}
