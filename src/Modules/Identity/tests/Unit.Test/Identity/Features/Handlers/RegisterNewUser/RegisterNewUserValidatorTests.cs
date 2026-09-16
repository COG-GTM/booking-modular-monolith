using FluentAssertions;
using FluentValidation.TestHelper;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Identity.Features.Handlers.RegisterNewUser;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

[Collection(nameof(UnitTestFixture))]
public class RegisterNewUserValidatorTests
{
    private readonly RegisterNewUserValidator _validator = new();

    [Fact]
    public void is_valid_should_be_true_when_have_valid_parameters()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void is_valid_should_be_false_when_required_fields_are_empty()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            FirstName = "",
            LastName = "",
            Username = "",
            Email = "",
            Password = "",
            ConfirmPassword = "",
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.FirstName);
        result.ShouldHaveValidationErrorFor(x => x.LastName);
        result.ShouldHaveValidationErrorFor(x => x.Username);
        result.ShouldHaveValidationErrorFor(x => x.Email);
        result.ShouldHaveValidationErrorFor(x => x.Password);
        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword);
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
        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("A valid email is required");
    }

    [Fact]
    public void is_valid_should_be_false_when_passwords_do_not_match()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            ConfirmPassword = "Different@123",
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Passwords should match");
    }
}
