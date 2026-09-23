using FluentAssertions;
using FluentValidation.TestHelper;
using Passenger.Passengers.Enums;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public class CompleteRegisterPassengerValidatorTests
{
    private readonly CompleteRegisterPassengerValidator _validator = new();

    [Theory]
    [InlineData(PassengerType.Unknown)]
    [InlineData(PassengerType.Male)]
    [InlineData(PassengerType.Female)]
    [InlineData(PassengerType.Baby)]
    public void is_valid_should_be_true_when_all_parameters_are_valid(PassengerType passengerType)
    {
        // Arrange
        var command = new CompleteRegisterPassenger("123456789", passengerType, 30);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void is_valid_should_be_false_when_passport_number_is_null()
    {
        // Arrange
        var command = new CompleteRegisterPassenger(null!, PassengerType.Male, 30);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.PassportNumber).WithErrorMessage("The PassportNumber is required!");
        result.ShouldNotHaveValidationErrorFor(x => x.Age);
        result.ShouldNotHaveValidationErrorFor(x => x.PassengerType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void is_valid_should_be_false_when_age_is_not_greater_than_zero(int age)
    {
        // Arrange
        var command = new CompleteRegisterPassenger("123456789", PassengerType.Male, age);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Age).WithErrorMessage("The Age must be greater than 0!");
        result.ShouldNotHaveValidationErrorFor(x => x.PassportNumber);
        result.ShouldNotHaveValidationErrorFor(x => x.PassengerType);
    }

    [Fact]
    public void is_valid_should_be_false_when_passenger_type_is_not_a_defined_enum_value()
    {
        // Arrange
        var command = new CompleteRegisterPassenger("123456789", (PassengerType)99, 30);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result
            .ShouldHaveValidationErrorFor(x => x.PassengerType)
            .WithErrorMessage("PassengerType must be Male, Female, Baby or Unknown");
        result.ShouldNotHaveValidationErrorFor(x => x.PassportNumber);
        result.ShouldNotHaveValidationErrorFor(x => x.Age);
    }

    [Fact]
    public void is_valid_should_be_false_and_report_every_rule_when_all_parameters_are_invalid()
    {
        // Arrange
        var command = new CompleteRegisterPassenger(null!, (PassengerType)99, 0);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
        result.ShouldHaveValidationErrorFor(x => x.PassportNumber);
        result.ShouldHaveValidationErrorFor(x => x.Age);
        result.ShouldHaveValidationErrorFor(x => x.PassengerType);
    }
}
