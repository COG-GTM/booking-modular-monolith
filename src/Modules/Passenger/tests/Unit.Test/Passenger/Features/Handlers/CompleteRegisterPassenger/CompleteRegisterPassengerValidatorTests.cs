using FluentAssertions;
using FluentValidation.TestHelper;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Passenger.Features.Handlers.CompleteRegisterPassenger;

using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

[Collection(nameof(UnitTestFixture))]
public class CompleteRegisterPassengerValidatorTests
{
    private readonly CompleteRegisterPassengerValidator _validator = new();

    [Fact]
    public void is_valid_should_be_true_when_have_valid_parameters()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand().Generate();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void is_valid_should_be_false_when_age_is_not_positive()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand().Generate() with
        {
            Age = 0,
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Age);
    }

    [Fact]
    public void is_valid_should_be_false_when_passport_number_is_null()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand().Generate() with
        {
            PassportNumber = null,
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PassportNumber);
    }

    [Fact]
    public void is_valid_should_be_false_when_passenger_type_is_undefined()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand().Generate() with
        {
            PassengerType = (PassengerType)99,
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PassengerType);
    }
}
