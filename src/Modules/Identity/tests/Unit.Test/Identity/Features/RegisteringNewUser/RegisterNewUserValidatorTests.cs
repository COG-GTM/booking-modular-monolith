namespace Unit.Test.Identity.Features.RegisteringNewUser;

using FluentAssertions;
using FluentValidation.TestHelper;
using global::Identity.Identity.Features.RegisteringNewUser.V1;
using Unit.Test.Fakes;
using Xunit;

public class RegisterNewUserValidatorTests
{
    private readonly RegisterNewUserValidator _validator = new();

    [Fact]
    public void should_not_have_error_when_command_is_valid()
    {
        var command = new FakeRegisterNewUser().Generate();

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_have_error_when_password_is_empty()
    {
        var command = new FakeRegisterNewUser().Generate() with { Password = string.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Please enter the password");
    }

    [Fact]
    public void should_have_error_when_confirm_password_is_empty()
    {
        var command = new FakeRegisterNewUser().Generate() with { ConfirmPassword = string.Empty };

        var result = _validator.TestValidate(command);

        result
            .ShouldHaveValidationErrorFor(x => x.ConfirmPassword)
            .WithErrorMessage("Please enter the confirmation password");
    }

    [Fact]
    public void should_have_error_when_passwords_do_not_match()
    {
        var command = new FakeRegisterNewUser().Generate() with { ConfirmPassword = "Different@123" };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Passwords should match");
    }

    [Theory]
    [InlineData(nameof(RegisterNewUser.Username), "Please enter the username")]
    [InlineData(nameof(RegisterNewUser.FirstName), "Please enter the first name")]
    [InlineData(nameof(RegisterNewUser.LastName), "Please enter the last name")]
    public void should_have_error_when_name_field_is_empty(string propertyName, string expectedMessage)
    {
        var command = new FakeRegisterNewUser().Generate();
        command = propertyName switch
        {
            nameof(RegisterNewUser.Username) => command with { Username = string.Empty },
            nameof(RegisterNewUser.FirstName) => command with { FirstName = string.Empty },
            nameof(RegisterNewUser.LastName) => command with { LastName = string.Empty },
            _ => command,
        };

        var result = _validator.TestValidate(command);

        result.Errors.Should().Contain(e => e.PropertyName == propertyName && e.ErrorMessage == expectedMessage);
    }

    [Fact]
    public void should_have_error_when_email_is_empty()
    {
        var command = new FakeRegisterNewUser().Generate() with { Email = string.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("Please enter the last email");
    }

    [Fact]
    public void should_have_error_when_email_is_malformed()
    {
        var command = new FakeRegisterNewUser().Generate() with { Email = "not-an-email" };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("A valid email is required");
    }
}
