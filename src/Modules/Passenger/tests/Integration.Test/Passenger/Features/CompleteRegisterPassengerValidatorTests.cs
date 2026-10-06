using FluentAssertions;
using FluentValidation.TestHelper;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public class CompleteRegisterPassengerValidatorTests
{
    [Fact]
    public void should_have_validation_error_when_user_id_is_empty()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand(
            "123456789",
            Guid.CreateVersion7(),
            Guid.Empty
        ).Generate();
        var validator = new CompleteRegisterPassengerValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.UserId).WithErrorMessage("The UserId is required!");
    }

    [Fact]
    public void should_not_have_validation_error_when_user_id_is_provided()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand(
            "123456789",
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        ).Generate();
        var validator = new CompleteRegisterPassengerValidator();

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveValidationErrorFor(x => x.UserId);
    }
}
