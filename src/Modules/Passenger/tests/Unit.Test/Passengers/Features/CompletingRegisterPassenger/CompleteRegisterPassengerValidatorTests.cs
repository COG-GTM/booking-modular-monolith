using FluentAssertions;
using FluentValidation.TestHelper;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Passengers.Features.CompletingRegisterPassenger;

public class CompleteRegisterPassengerValidatorTests
{
    private readonly CompleteRegisterPassengerValidator _validator = new();

    [Fact]
    public void Valid_request_passes_validation()
    {
        _validator.TestValidate(new FakeCompleteRegisterPassenger().Generate()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Null_passport_number_fails_with_the_required_message()
    {
        var request = new CompleteRegisterPassenger(null!, PassengerType.Male, 25);

        _validator
            .TestValidate(request)
            .ShouldHaveValidationErrorFor(x => x.PassportNumber)
            .WithErrorMessage("The PassportNumber is required!");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_age_fails(int age)
    {
        _validator
            .TestValidate(new CompleteRegisterPassenger("123456789", PassengerType.Male, age))
            .ShouldHaveValidationErrorFor(x => x.Age)
            .WithErrorMessage("The Age must be greater than 0!");
    }

    [Theory]
    [InlineData(PassengerType.Unknown)]
    [InlineData(PassengerType.Male)]
    [InlineData(PassengerType.Female)]
    [InlineData(PassengerType.Baby)]
    public void Defined_passenger_types_pass(PassengerType passengerType)
    {
        _validator
            .TestValidate(new CompleteRegisterPassenger("123456789", passengerType, 25))
            .ShouldNotHaveValidationErrorFor(x => x.PassengerType);
    }

    [Fact]
    public void Undefined_passenger_type_fails()
    {
        _validator
            .TestValidate(new CompleteRegisterPassenger("123456789", (PassengerType)99, 25))
            .ShouldHaveValidationErrorFor(x => x.PassengerType);
    }
}
