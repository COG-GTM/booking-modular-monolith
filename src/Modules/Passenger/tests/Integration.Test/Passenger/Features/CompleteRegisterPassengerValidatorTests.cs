using FluentAssertions;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class CompleteRegisterPassengerValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_empty_or_whitespace_passport_number(string passportNumber)
    {
        var command = new CompleteRegisterPassenger(passportNumber, PassengerType.Male, 30);

        var result = new CompleteRegisterPassengerValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CompleteRegisterPassenger.PassportNumber));
    }

    [Fact]
    public void should_accept_valid_passport_number()
    {
        var command = new CompleteRegisterPassenger("AB1234567", PassengerType.Male, 30);

        var result = new CompleteRegisterPassengerValidator().Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
