using Booking.Booking.Features.CreatingBook.V1;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Unit.Test.Features.CreatingBook.V1;

public class CreateBookingValidatorTests
{
    [Fact]
    public void valid_command_passes_validation()
    {
        var command = new CreateBooking(Guid.NewGuid(), Guid.NewGuid(), "Window seat");

        new CreateBookingValidator().TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }
}
