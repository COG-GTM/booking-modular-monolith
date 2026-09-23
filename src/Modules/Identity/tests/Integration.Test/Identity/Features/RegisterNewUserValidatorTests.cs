using FluentAssertions;
using FluentValidation.TestHelper;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Identity.Features;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class RegisterNewUserValidatorTests
{
    private readonly RegisterNewUserValidator _validator = new();

    [Fact]
    public void is_valid_should_be_true_when_command_is_well_formed()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void is_valid_should_be_false_when_have_invalid_parameter()
    {
        // Arrange
        var command = new FakeValidateRegisterNewUserCommand().Generate();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Username).WithErrorMessage("Please enter the username");
        result.ShouldHaveValidationErrorFor(x => x.FirstName).WithErrorMessage("Please enter the first name");
        result.ShouldHaveValidationErrorFor(x => x.LastName).WithErrorMessage("Please enter the last name");
        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("A valid email is required");
        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Passwords should match");
    }

    [Fact]
    public void is_valid_should_be_false_when_password_and_confirm_password_are_empty()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Password = string.Empty,
            ConfirmPassword = string.Empty,
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Please enter the password");
        result
            .ShouldHaveValidationErrorFor(x => x.ConfirmPassword)
            .WithErrorMessage("Please enter the confirmation password");
        result.ShouldNotHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void is_valid_should_be_false_when_passwords_do_not_match()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            ConfirmPassword = "Mismatch@123",
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Passwords should match");
        result.ShouldNotHaveValidationErrorFor(x => x.ConfirmPassword);
    }

    [Fact]
    public void is_valid_should_be_false_when_email_is_empty()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Email = string.Empty,
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("Please enter the last email");
    }

    [Fact]
    public void is_valid_should_be_false_when_email_is_malformed()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Email = "not-an-email",
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("A valid email is required");
    }
}
